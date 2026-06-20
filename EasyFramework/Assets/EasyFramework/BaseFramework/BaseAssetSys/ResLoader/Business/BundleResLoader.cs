using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 从 AssetBundle / Resources 加载抽象资源，管理 Resource 层缓存与引用计数。
/// 对象池见 <see cref="PrefabPoolManager"/>。
/// </summary>
public class BundleResLoader
{
    #region 单例

    /// <summary>单例实例（懒创建）。</summary>
    static volatile BundleResLoader instance;

    /// <summary>保护 <see cref="instance"/> 初始化的锁对象。</summary>
    static readonly object instanceLock = new object();

    public static BundleResLoader Instance
    {
        get
        {
            if (instance == null)
            {
                lock (instanceLock)
                {
                    if (instance == null)
                        instance = new BundleResLoader();
                }
            }
            return instance;
        }
    }

    #endregion

    #region 字段定义

    /// <summary>懒 Init 时默认在 Bundle 根下追加当前平台子目录（如 StreamingAssets/Windows）。</summary>
    const bool DefaultUsePlatformSubfolder = true;

    /// <summary>保护 <see cref="Init"/> / <see cref="EnsureInitialized"/> 的互斥锁。</summary>
    readonly object initLock = new object();

    /// <summary>是否已完成 Catalogue + BundleManager + AssetRouter 初始化。</summary>
    bool initialized;

    /// <summary>预热持有的 Bundle 引用（UnloadAll 时对称 Release）。</summary>
    readonly List<string> preloadedBundleRefs = new List<string>();

    /// <summary>资源清单读取器；Load 前解析 loadPath → bundle / asset。</summary>
    readonly CatalogueReader catalogue = new CatalogueReader();

    /// <summary>异步加载协调器（inFlight 合并 + 真异步 I/O）。</summary>
    readonly ResourceLoadCoordinator loadCoordinator;

    /// <summary>
    /// Resource 层缓存：key 多为 bundleName/assetName，Resources 路径为 loadPath。
    /// 命中时 <see cref="AbstractResource.AddReference"/>，归零时 onUnLoad 移除项。
    /// </summary>
    Dictionary<string, AbstractResource> resourceDic = new Dictionary<string, AbstractResource>();

    BundleResLoader()
    {
        loadCoordinator = new ResourceLoadCoordinator(EnsureInitialized, resourceDic, catalogue);
    }

    #endregion

    #region 初始化

    public bool Init(string bundleRootPath, bool usePlatformSubfolder = true)
    {
        lock (initLock)
        {
            if (initialized && catalogue.IsLoaded)
            {
                Debug.LogWarning("BundleResLoader already initialized; ignoring repeated Init.");
                return true;
            }

            bundleRootPath = BundlePlatformPaths.ResolveRuntimeBundleRoot(bundleRootPath, usePlatformSubfolder);

            if (resourceDic.Count > 0)
            {
                foreach (AbstractResource res in resourceDic.Values)
                {
                    if (res == null)
                        continue;

                    res.onUnLoad = null;
                    res.UnLoad();
                }
            }
            resourceDic.Clear();
            preloadedBundleRefs.Clear();
            loadCoordinator.ResetInflight();

            string bundlesRoot = bundleRootPath;
            bool catalogueLoaded;
            if (BundlePlatformPaths.TryResolveRuntimeCatalogPath(bundleRootPath, out string cataloguePath, out string resolvedBundlesRoot))
            {
                catalogueLoaded = catalogue.LoadFromFile(cataloguePath);
                if (!string.IsNullOrEmpty(resolvedBundlesRoot))
                    bundlesRoot = resolvedBundlesRoot;
            }
            else
            {
                catalogueLoaded = catalogue.LoadFromBundleRoot(bundleRootPath);
            }

#if UNITY_EDITOR
            if (!catalogueLoaded)
                catalogueLoaded = catalogue.LoadFromProjectCatalogue();
#endif

            if (!catalogueLoaded)
            {
                initialized = false;
                Debug.LogError("BundleResLoader Init failed: catalogue not loaded from " + bundleRootPath);
                return false;
            }

            DefaultBundlePathResolver resolver = DefaultBundlePathResolver.Create(bundlesRoot);
            string cacheRoot = resolver.CacheRoot;

            CdnRuntimeBootstrap.SyncCatalogueIfNeeded(catalogue, cacheRoot);

            BundleManager.Init(bundlesRoot, catalogue);
            IRemoteBundleProvider remoteProvider = CdnRuntimeBootstrap.CreateRemoteProvider(catalogue, cacheRoot);
            AssetRouter.Instance.Init(catalogue, resolver, remoteProvider);

            if (catalogue.Catalog == null || catalogue.Catalog.bundles == null || catalogue.Catalog.bundles.Length == 0)
            {
                Debug.LogWarning("Catalogue loaded but bundle dependency map is empty. Cross-bundle dependencies will not be preloaded (EditorTest may produce this).");
            }

            initialized = true;
            return true;
        }
    }

    bool EnsureInitialized()
    {
        if (initialized && catalogue.IsLoaded)
            return true;

        lock (initLock)
        {
            if (initialized && catalogue.IsLoaded)
                return true;

            return Init(null, DefaultUsePlatformSubfolder);
        }
    }

    /// <summary>懒 Init 预热：加载 Catalogue 与 Bundle 根目录，供启动或测试在首次 Load 前调用。</summary>
    public bool EnsureReady()
    {
        return EnsureInitialized();
    }

    /// <summary>运行时默认首包根目录（StreamingAssets + 当前平台子目录）。</summary>
    public static string GetDefaultRuntimeBundleRoot(bool usePlatformSubfolder = true)
    {
        return BundlePlatformPaths.ResolveRuntimeBundleRoot(null, usePlatformSubfolder);
    }

    public CatalogueReader GetCatalogue()
    {
        return catalogue;
    }

    public bool IsCatalogueLoaded => catalogue.IsLoaded;

    #endregion

    #region 加载

    #region 同步加载

    // TODO：业务侧预先加载对应模块；见 Docs/BusinessApiAndCdnPlanning.md §1 需求4
    public IAssetHandle PreLoad<T>(string loadPath) where T : Object
    {
        if (string.IsNullOrEmpty(loadPath))
            return null;

        if (!EnsureInitialized())
            return null;

        if (!catalogue.TryGetEntryByLoadPath(loadPath, out AssetCatalogEntry entry))
            return null;

        PreLoadBundles(new[] { entry.bundleName });
        return Load<T>(loadPath);
    }

    /// <summary>
    /// 包级预热：Acquire 依赖链并保持 Bundle 引用，不创建 AbstractResource。
    /// </summary>
    public void PreLoadBundles(IReadOnlyList<string> bundleNames)
    {
        if (!EnsureInitialized() || bundleNames == null)
            return;

        foreach (string rawName in bundleNames)
        {
            if (string.IsNullOrEmpty(rawName))
                continue;

            string bundleName = BundlePlatformPaths.NormalizeBundleName(rawName);
            var acquired = new List<string>();
            if (BundleManager.AcquireBundleWithDependencies(bundleName, acquired) == null)
                continue;

            foreach (string name in acquired)
            {
                if (!preloadedBundleRefs.Contains(name))
                    preloadedBundleRefs.Add(name);
            }
        }
    }

    /// <summary>分帧预热 bundle（Loading 进度用）。</summary>
    public async UniTask PreLoadBundlesAsync(
        IReadOnlyList<string> bundleNames,
        IProgress<float> progress = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (!EnsureInitialized() || bundleNames == null)
            return;

        int count = bundleNames.Count;
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreLoadBundles(new[] { bundleNames[i] });
            progress?.Report((i + 1f) / count);
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }
    }

    /// <summary>同步加载。loadPath 为相对打包根目录的简路径，无扩展名。
    /// 例：Default 规则下 targetDirectory=Assets/AssetBundle → Load&lt;Sprite&gt;("Atlas/Role/Hog_Attack_000")
    /// </summary>
    public IAssetHandle Load<T>(string loadPath) where T : Object
    {
        if (string.IsNullOrEmpty(loadPath))
        {
            Debug.LogError("Load path is null or empty.");
            return null;
        }

        if (!EnsureInitialized())
        {
            Debug.LogError("BundleResLoader not initialized; cannot load: " + loadPath);
            return null;
        }

#if DEVELOPMENT_BUILD || VF_POOL_LOAD_LINT
        if (PrefabPoolManager.Instance.TryGetPool(loadPath, out _))
            Debug.LogWarning("[BundleResLoader] Load on pooled path; prefer PrefabPoolManager: " + loadPath);
#endif

        if (ResourcesAssetProvider.IsResourcesLoadPath(loadPath))
            return LoadResources<T>(loadPath);

        if (!catalogue.TryGetEntryByLoadPath(loadPath, out AssetCatalogEntry entry))
        {
            Debug.LogError("Load path not found in catalogue: " + loadPath);
            return null;
        }

        return LoadByBundle<T>(entry.bundleName, entry.assetName, entry.assetPath, loadPath);
    }

    /// <summary>按 bundle 名 + 包内 asset 名加载，Resource 层与 BundleManager 的桥接。</summary>
    public IAssetHandle LoadByBundle<T>(string bundleName, string assetName, string assetPath = null, string loadPath = null) where T : Object
    {
        if (string.IsNullOrEmpty(bundleName) || string.IsNullOrEmpty(assetName))
        {
            Debug.LogError("LoadByBundle failed, bundleName or assetName is null/empty.");
            return null;
        }

        if (!EnsureInitialized())
        {
            Debug.LogError("BundleResLoader not initialized; cannot load bundle: " + bundleName + "/" + assetName);
            return null;
        }

        string key = bundleName + "/" + assetName;

        if (resourceDic.TryGetValue(key, out AbstractResource res))
        {
            res.AddReference();
            if (res.GetAsset<T>() == null)
            {
                res.Release();
                Debug.LogError("LoadByBundle type mismatch for cached resource: " + key + ", requested type: " + typeof(T).Name);
                return null;
            }
            return res;
        }

        res = new AbstractResource(key, bundleName, assetName, assetPath, loadPath);
        res.onUnLoad = () => resourceDic.Remove(key);
        resourceDic.Add(key, res);
        res.AddReference();
        res.LoadAsset(typeof(T), assetPath, loadPath);

        if (res.GetAsset<T>() == null)
        {
            res.Release();
            return null;
        }

        return res;
    }

    /// <summary>按 Unity 工程完整 assetPath 加载，如 Assets/AssetBundle/Atlas/Role/Hog.png</summary>
    public IAssetHandle LoadByAssetPath<T>(string assetPath) where T : Object
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogError("Asset path is null or empty.");
            return null;
        }

        if (!EnsureInitialized())
        {
            Debug.LogError("BundleResLoader not initialized; cannot load asset path: " + assetPath);
            return null;
        }

        if (!catalogue.TryGetEntry(assetPath, out AssetCatalogEntry entry))
        {
            Debug.LogError("Asset path not found in catalogue: " + assetPath);
            return null;
        }

        return LoadByBundle<T>(
            entry.bundleName,
            entry.assetName,
            entry.assetPath,
            CatalogueReader.ToLoadPath(entry.assetPath, catalogue.Catalog?.resourceRoot));
    }

    /// <summary>从 Unity Resources 目录加载（loadPath 以 Resources/ 前缀标识）。</summary>
    IAssetHandle LoadResources<T>(string loadPath) where T : Object
    {
        if (resourceDic.TryGetValue(loadPath, out AbstractResource res))
        {
            res.AddReference();
            if (res.GetAsset<T>() == null)
            {
                res.Release();
                Debug.LogError("LoadResources type mismatch for: " + loadPath + ", requested type: " + typeof(T).Name);
                return null;
            }
            return res;
        }

        res = new AbstractResource(loadPath, null, null, null, loadPath);
        res.onUnLoad = () => resourceDic.Remove(loadPath);
        resourceDic.Add(loadPath, res);
        res.AddReference();
        res.LoadAsset(typeof(T), null, loadPath);

        if (res.GetAsset<T>() == null)
        {
            res.Release();
            return null;
        }

        return res;
    }

    #endregion

    #region LoadGameObject（自动句柄）

    /// <summary>
    /// 加载 Prefab 并实例化，句柄由 <see cref="AssetReference"/> 绑定到实例；<see cref="Object.Destroy"/> 时自动 Release。
    /// 高频复用请用 <see cref="PrefabPoolManager"/>，勿与本 API 混用同路径。
    /// </summary>
    public GameObject LoadGameObject(string loadPath)
    {
        return LoadGameObject(loadPath, Vector3.zero, Quaternion.identity, null);
    }

    /// <summary>
    /// 加载 Prefab 并实例化到指定位姿；Destroy 实例时自动 Release 本次 Load 的引用。
    /// </summary>
    public GameObject LoadGameObject(string loadPath, Vector3 worldPosition, Quaternion worldRotation, Transform parent = null)
    {
        if (string.IsNullOrEmpty(loadPath))
        {
            Debug.LogError("LoadGameObject: loadPath is null or empty.");
            return null;
        }

        IAssetHandle handle = Load<GameObject>(loadPath);
        if (handle == null)
            return null;

        GameObject instance = handle.InstantiateAt(worldPosition, worldRotation, parent);
        if (instance == null)
        {
            handle.Release();
            Debug.LogError("LoadGameObject: Instantiate failed, path=" + loadPath);
            return null;
        }

        AssetReference.Bind(instance, handle, loadPath);
        return instance;
    }

    #endregion

    #region 自动卸载门面

    /// <summary>加载并实例化，句柄挂 <see cref="AssetReference"/>；Destroy 时自动 Release（低频非池用法）。</summary>
    public GameObject LoadWithAutoUnLoad(string loadPath)
    {
        return LoadGameObject(loadPath);
    }

    /// <summary>泛型：仅返回句柄，不自动绑定实例。GameObject 请用 <see cref="LoadGameObject"/>。</summary>
    public IAssetHandle LoadWithAutoUnLoadGeneric<T>(string loadPath) where T : Object
    {
        return Load<T>(loadPath);
    }

    #endregion

    #region UniTask 加载

    /// <summary>UniTask 版 <see cref="LoadGameObject"/>（当前 Yield 后同步 Load）。</summary>
    public async UniTask<GameObject> LoadGameObjectAsync(string loadPath)
    {
        return await LoadGameObjectAsync(loadPath, Vector3.zero, Quaternion.identity, null);
    }

    /// <summary>UniTask 版 <see cref="LoadGameObject"/> 带位姿。</summary>
    public async UniTask<GameObject> LoadGameObjectAsync(
        string loadPath,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Transform parent = null)
    {
        if (string.IsNullOrEmpty(loadPath))
        {
            Debug.LogError("LoadGameObject: loadPath is null or empty.");
            return null;
        }

        IAssetHandle handle = await loadCoordinator.LoadAsync<GameObject>(loadPath);
        if (handle == null)
            return null;

        GameObject instance = handle.InstantiateAt(worldPosition, worldRotation, parent);
        if (instance == null)
        {
            handle.Release();
            Debug.LogError("LoadGameObject: Instantiate failed, path=" + loadPath);
            return null;
        }

        AssetReference.Bind(instance, handle, loadPath);
        return instance;
    }

    /// <summary>异步版 <see cref="LoadWithAutoUnLoad"/>。</summary>
    public async UniTask<GameObject> LoadUniTaskAsynWithAutoUnLoad(string loadPath)
    {
        return await LoadGameObjectAsync(loadPath);
    }

    /// <summary>
    /// UniTask 异步加载默认入口；同 path inFlight 合并，底层真异步 I/O。
    /// </summary>
    public UniTask<IAssetHandle> LoadUniTaskAsync<T>(string loadPath) where T : Object
    {
        return loadCoordinator.LoadAsync<T>(loadPath);
    }

    /// <summary>UniTask 带回调加载；useUniTask=false 时走同步 Load 并立即回调。</summary>
    public void LoadUniTaskWithCallback<T>(string loadPath, Action<IAssetHandle> onComplete, Action<string> onFailed = null, bool useUniTask = true) where T : Object
    {
        if (!useUniTask)
        {
            InvokeSyncLoadWithCallback(
                () => Load<T>(loadPath),
                onComplete,
                onFailed,
                "LoadUniTaskWithCallback failed, loadPath=" + loadPath);
            return;
        }

        InvokeUniTaskLoadWithCallback(
            () => LoadUniTaskAsync<T>(loadPath),
            onComplete,
            onFailed,
            "LoadUniTaskWithCallback failed, loadPath=" + loadPath);
    }

    /// <summary>按 Unity 完整 assetPath 的 UniTask 回调加载。</summary>
    public void LoadByAssetPathUniTaskWithCallback<T>(string assetPath, Action<IAssetHandle> onComplete, Action<string> onFailed = null, bool useUniTask = true) where T : Object
    {
        if (!useUniTask)
        {
            InvokeSyncLoadWithCallback(
                () => LoadByAssetPath<T>(assetPath),
                onComplete,
                onFailed,
                "LoadByAssetPathUniTaskWithCallback failed, assetPath=" + assetPath);
            return;
        }

        InvokeUniTaskLoadWithCallback(
            () => LoadByAssetPathUniTaskAsync<T>(assetPath),
            onComplete,
            onFailed,
            "LoadByAssetPathUniTaskWithCallback failed, assetPath=" + assetPath);
    }

    /// <summary>按 bundle+asset 的 UniTask 回调加载。</summary>
    public void LoadByBundleUniTaskWithCallback<T>(string bundleName, string assetName, Action<IAssetHandle> onComplete, Action<string> onFailed = null, bool useUniTask = true, string assetPath = null) where T : Object
    {
        if (!useUniTask)
        {
            InvokeSyncLoadWithCallback(
                () => LoadByBundle<T>(bundleName, assetName, assetPath),
                onComplete,
                onFailed,
                "LoadByBundleUniTaskWithCallback failed, key=" + bundleName + "/" + assetName);
            return;
        }

        InvokeUniTaskLoadWithCallback(
            async () => await LoadByBundleUniTaskAsync<T>(bundleName, assetName, assetPath),
            onComplete,
            onFailed,
            "LoadByBundleUniTaskWithCallback failed, key=" + bundleName + "/" + assetName);
    }

    async UniTask<IAssetHandle> LoadByAssetPathUniTaskAsync<T>(string assetPath) where T : Object
    {
        return await loadCoordinator.LoadByAssetPathAsync<T>(assetPath);
    }

    async UniTask<IAssetHandle> LoadByBundleUniTaskAsync<T>(string bundleName, string assetName, string assetPath = null) where T : Object
    {
        return await loadCoordinator.LoadByBundleAsync<T>(bundleName, assetName, assetPath);
    }

    /// <summary>B-2 验收：加载中 Release 后完成不入缓存。</summary>
    //TODO 确认这个方法是干什么的？
    public UniTask<bool> VerifyInflightAbandonAsync<T>(string loadPath) where T : Object
    {
        return loadCoordinator.VerifyInflightAbandonAsync<T>(loadPath);
    }

    /// <summary>当前 Resource 层缓存条目数（测试用）。</summary>
    /// TODO 后续不允许有测试代码在框架层
    internal int GetCachedResourceCountForTest()
    {
        return loadCoordinator.GetCachedResourceCount();
    }

    #endregion

    #endregion

    #region 卸载

    #region 单资源卸载

    /// <summary>
    /// 卸载资源：可选直接销毁实例，并减少资源引用计数。
    /// </summary>
    /// <param name="resource">由 Load / LoadUniTaskAsync 返回的资源句柄，可为 null。</param>
    /// <param name="instance">业务侧实例对象，可为 null；不为 null 时会直接 Destroy。</param>
    /// <param name="onComplete">卸载完成回调，参数表示是否执行了至少一个有效卸载动作。</param>
    public void Unload(IAssetHandle resource, GameObject instance = null, Action<bool> onComplete = null)
    {
        bool unloaded = false;

        if (instance != null)
        {
            Object.Destroy(instance);
            unloaded = true;
        }

        if (resource != null)
        {
            resource.Release();
            unloaded = true;
        }

        if (!unloaded)
            Debug.LogWarning("Unload called with null resource and null instance.");

        onComplete?.Invoke(unloaded);
    }

    #endregion

    #region 全部卸载

    /// <summary>
    /// 进程级收尾：销毁全部对象池 → 清空 Resource 缓存 → BundleManager.UnloadAll。
    /// 切场景 / 关游戏时调用；之后勿再使用旧句柄。
    /// </summary>
    public void UnloadAll()
    {
        PrefabPoolManager.Instance.DeleteAllPools();
        loadCoordinator.ResetInflight();

        foreach (string bundleName in preloadedBundleRefs)
            BundleManager.ReleaseBundle(bundleName);
        preloadedBundleRefs.Clear();

        AbstractResource[] resources = new AbstractResource[resourceDic.Count];
        resourceDic.Values.CopyTo(resources, 0);
        resourceDic.Clear();

        foreach (AbstractResource res in resources)
        {
            res.onUnLoad = null;
            res.UnLoad();
        }

        BundleManager.UnloadAll();
    }

    #endregion

    #endregion

    #region 辅助函数

    void TracePositiveResourceRefsBeforeUnloadAll()
    {
        foreach (KeyValuePair<string, AbstractResource> pair in resourceDic)
        {
            if (pair.Value != null && pair.Value.CurrentRef > 0)
            {
                
            }
        }
    }

    void InvokeSyncLoadWithCallback(Func<IAssetHandle> loader, Action<IAssetHandle> onComplete, Action<string> onFailed, string failMessage)
    {
        try
        {
            IAssetHandle handle = loader.Invoke();
            if (handle != null)
            {
                onComplete?.Invoke(handle);
                return;
            }

            if (onFailed != null)
                onFailed.Invoke(failMessage);
            else
                Debug.LogError(failMessage);
        }
        catch (Exception ex)
        {
            string msg = failMessage + ", exception=" + ex.Message;
            if (onFailed != null)
                onFailed.Invoke(msg);
            else
                Debug.LogError(msg);
        }
    }

    void InvokeUniTaskLoadWithCallback(Func<UniTask<IAssetHandle>> loader, Action<IAssetHandle> onComplete, Action<string> onFailed, string failMessage)
    {
        UniTask.Void(async () =>
        {
            try
            {
                IAssetHandle handle = await loader.Invoke();
                if (handle != null)
                {
                    onComplete?.Invoke(handle);
                    return;
                }

                if (onFailed != null)
                    onFailed.Invoke(failMessage);
                else
                    Debug.LogError(failMessage);
            }
            catch (Exception ex)
            {
                string msg = failMessage + ", exception=" + ex.Message;
                if (onFailed != null)
                    onFailed.Invoke(msg);
                else
                    Debug.LogError(msg);
            }
        });
    }

    #endregion
}
