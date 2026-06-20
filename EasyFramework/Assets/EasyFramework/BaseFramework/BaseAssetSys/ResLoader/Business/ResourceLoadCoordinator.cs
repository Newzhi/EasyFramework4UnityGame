using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 异步加载协调：同 resource key inFlight 合并；加载完成时 Ref==0 丢弃。
/// </summary>
internal sealed class ResourceLoadCoordinator
{
    readonly object inflightGate = new object();
    readonly Dictionary<string, UniTask<IAssetHandle>> loadInFlight =
        new Dictionary<string, UniTask<IAssetHandle>>(StringComparer.Ordinal);

    readonly Func<bool> ensureInitialized;
    readonly Dictionary<string, AbstractResource> resourceDic;
    readonly CatalogueReader catalogue;

    public ResourceLoadCoordinator(
        Func<bool> ensureInitialized,
        Dictionary<string, AbstractResource> resourceDic,
        CatalogueReader catalogue)
    {
        this.ensureInitialized = ensureInitialized ?? throw new ArgumentNullException(nameof(ensureInitialized));
        this.resourceDic = resourceDic ?? throw new ArgumentNullException(nameof(resourceDic));
        this.catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    }

    public void ResetInflight()
    {
        lock (inflightGate)
            loadInFlight.Clear();
    }

    public int GetCachedResourceCount()
    {
        return resourceDic.Count;
    }

    public async UniTask<IAssetHandle> LoadAsync<T>(string loadPath) where T : Object
    {
        if (string.IsNullOrEmpty(loadPath))
        {
            Debug.LogError("Load path is null or empty.");
            return null;
        }

        if (!ensureInitialized())
        {
            Debug.LogError("BundleResLoader not initialized; cannot load: " + loadPath);
            return null;
        }

#if DEVELOPMENT_BUILD || VF_POOL_LOAD_LINT
        if (PrefabPoolManager.Instance.TryGetPool(loadPath, out _))
            Debug.LogWarning("[BundleResLoader] Load on pooled path; prefer PrefabPoolManager: " + loadPath);
#endif

        if (ResourcesAssetProvider.IsResourcesLoadPath(loadPath))
            return await LoadResourcesAsync<T>(loadPath);

        if (!catalogue.TryGetEntryByLoadPath(loadPath, out AssetCatalogEntry entry))
        {
            Debug.LogError("Load path not found in catalogue: " + loadPath);
            return null;
        }

        return await LoadByBundleAsync<T>(
            entry.bundleName,
            entry.assetName,
            entry.assetPath,
            loadPath);
    }

    public async UniTask<IAssetHandle> LoadByAssetPathAsync<T>(string assetPath) where T : Object
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogError("Asset path is null or empty.");
            return null;
        }

        if (!ensureInitialized())
        {
            Debug.LogError("BundleResLoader not initialized; cannot load asset path: " + assetPath);
            return null;
        }

        if (!catalogue.TryGetEntry(assetPath, out AssetCatalogEntry entry))
        {
            Debug.LogError("Asset path not found in catalogue: " + assetPath);
            return null;
        }

        return await LoadByBundleAsync<T>(
            entry.bundleName,
            entry.assetName,
            entry.assetPath,
            CatalogueReader.ToLoadPath(entry.assetPath, catalogue.Catalog?.resourceRoot));
    }

    public async UniTask<IAssetHandle> LoadByBundleAsync<T>(
        string bundleName,
        string assetName,
        string assetPath = null,
        string loadPath = null) where T : Object
    {
        if (string.IsNullOrEmpty(bundleName) || string.IsNullOrEmpty(assetName))
        {
            Debug.LogError("LoadByBundle failed, bundleName or assetName is null/empty.");
            return null;
        }

        if (!ensureInitialized())
        {
            Debug.LogError("BundleResLoader not initialized; cannot load bundle: " + bundleName + "/" + assetName);
            return null;
        }

        string key = bundleName + "/" + assetName;
        return await LoadWithInflightAsync<T>(
            key,
            () => new AbstractResource(key, bundleName, assetName, assetPath, loadPath),
            res => res.LoadAssetAsync(typeof(T), assetPath, loadPath));
    }

    async UniTask<IAssetHandle> LoadResourcesAsync<T>(string loadPath) where T : Object
    {
        if (resourceDic.TryGetValue(loadPath, out AbstractResource cached) && !cached.IsLoading)
            return TryAddRefAndValidateType<T>(cached, loadPath);

        return await LoadWithInflightAsync<T>(
            loadPath,
            () => new AbstractResource(loadPath, null, null, null, loadPath),
            res => res.LoadAssetAsync(typeof(T), null, loadPath));
    }

    async UniTask<IAssetHandle> LoadWithInflightAsync<T>(
        string key,
        Func<AbstractResource> createResource,
        Func<AbstractResource, UniTask<bool>> loadAsync) where T : Object
    {
        if (resourceDic.TryGetValue(key, out AbstractResource cached) && !cached.IsLoading)
            return TryAddRefAndValidateType<T>(cached, key);

        UniTask<IAssetHandle> inflightTask;
        bool isFollower;

        lock (inflightGate)
        {
            if (loadInFlight.TryGetValue(key, out inflightTask))
            {
                isFollower = true;
            }
            else
            {
                var tcs = new UniTaskCompletionSource<IAssetHandle>();
                inflightTask = tcs.Task;
                loadInFlight[key] = inflightTask;
                isFollower = false;
                RunLeaderLoadAsync(key, createResource, loadAsync, tcs).Forget();
            }
        }

        IAssetHandle handle = await inflightTask;
        if (handle == null)
            return null;

        if (isFollower)
            ((AbstractResource)handle).AddReference();

        return TryAddRefAndValidateType<T>((AbstractResource)handle, key, skipAddRef: true);
    }

    async UniTaskVoid RunLeaderLoadAsync(
        string key,
        Func<AbstractResource> createResource,
        Func<AbstractResource, UniTask<bool>> loadAsync,
        UniTaskCompletionSource<IAssetHandle> tcs)
    {
        AbstractResource res = null;
        try
        {
            res = createResource();
            res.onUnLoad = () => resourceDic.Remove(key);
            resourceDic[key] = res;
            res.AddReference();

            bool ok = await loadAsync(res);
            if (!ok || res.CurrentRef <= 0)
            {
                CleanupFailedLoad(key, res);
                tcs.TrySetResult(null);
                return;
            }

            tcs.TrySetResult(res);
        }
        catch (Exception ex)
        {
            Debug.LogError("Async load failed for key=" + key + ": " + ex.Message);
            if (res != null)
                CleanupFailedLoad(key, res);
            tcs.TrySetException(ex);
        }
        finally
        {
            lock (inflightGate)
                loadInFlight.Remove(key);
        }
    }

    IAssetHandle TryAddRefAndValidateType<T>(AbstractResource res, string key, bool skipAddRef = false) where T : Object
    {
        if (!skipAddRef)
            res.AddReference();

        if (res.GetAsset<T>() == null)
        {
            res.Release();
            Debug.LogError("Async load type mismatch for: " + key + ", requested type: " + typeof(T).Name);
            return null;
        }

        return res;
    }

    void CleanupFailedLoad(string key, AbstractResource res)
    {
        resourceDic.Remove(key);
        res.onUnLoad = null;
        res.ForceUnloadFromCoordinator();
    }

    /// <summary>验证加载中 Release 后完成时资源不入缓存（B-2 ref==0 丢弃）。</summary>
    public async UniTask<bool> VerifyInflightAbandonAsync<T>(string loadPath) where T : Object
    {
        if (string.IsNullOrEmpty(loadPath) || !ensureInitialized())
            return false;

        if (!catalogue.TryGetEntryByLoadPath(loadPath, out AssetCatalogEntry entry))
            return false;

        string key = entry.bundleName + "/" + entry.assetName;
        if (resourceDic.ContainsKey(key))
            return false;

        UniTask<IAssetHandle> loadTask = LoadAsync<T>(loadPath);

        AbstractResource inflightRes = null;
        for (int i = 0; i < 60 && inflightRes == null; i++)
        {
            resourceDic.TryGetValue(key, out inflightRes);
            if (inflightRes == null)
                await UniTask.Yield(PlayerLoopTiming.Update);
        }

        if (inflightRes == null)
        {
            Debug.LogWarning(
                "[ResourceLoadCoordinator] VerifyInflightAbandon: resource not registered within 60 frames for " + loadPath);
            await loadTask;
            return false;
        }

        bool releasedDuringLoad = false;
        for (int i = 0; i < 60 && !releasedDuringLoad; i++)
        {
            if (inflightRes.IsLoading)
            {
                inflightRes.Release();
                releasedDuringLoad = true;
                break;
            }

            if (inflightRes.GetAsset<T>() != null)
            {
                Debug.LogWarning(
                    "[ResourceLoadCoordinator] VerifyInflightAbandon: load finished before Release for " + loadPath);
                await loadTask;
                return false;
            }

            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        if (!releasedDuringLoad)
        {
            Debug.LogWarning(
                "[ResourceLoadCoordinator] VerifyInflightAbandon: IsLoading not observed within 60 frames for " + loadPath);
            await loadTask;
            return false;
        }

        IAssetHandle result = await loadTask;
        return result == null && !resourceDic.ContainsKey(key);
    }
}
