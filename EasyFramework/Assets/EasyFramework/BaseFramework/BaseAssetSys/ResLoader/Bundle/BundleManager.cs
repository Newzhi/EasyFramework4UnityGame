using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class BundleManager
{
    #region 变量定义

    private static string bundleRootPath;
    private static CatalogueReader catalogue;
    private static IBundlePathResolver pathResolver;
    private static Dictionary<string, BundleEntry> loadedBundles = new Dictionary<string, BundleEntry>();
    private static readonly object bundleInflightGate = new object();
    private static readonly Dictionary<string, UniTask<AssetBundle>> bundleInflight =
        new Dictionary<string, UniTask<AssetBundle>>(StringComparer.OrdinalIgnoreCase);

    private class BundleEntry
    {
        public AssetBundle Bundle;
        public int Ref;
        public float LastUsedTime;
        public int ResourcePriority;
        public string PackageId;
    }

    #endregion

    #region 初始化

    public static void Init(string rootPath, CatalogueReader reader = null)
    {
        if (loadedBundles.Count > 0)
        {
            foreach (BundleEntry entry in loadedBundles.Values)
            {
                if (entry?.Bundle != null)
                    entry.Bundle.Unload(true);
            }
        }

        bundleRootPath = rootPath;
        catalogue = reader;
        loadedBundles.Clear();
        ClearBundleInflight();
    }

    public static void SetCatalogue(CatalogueReader reader)
    {
        catalogue = reader;
    }

    public static void SetPathResolver(IBundlePathResolver resolver)
    {
        pathResolver = resolver;
    }

    public static IBundlePathResolver GetPathResolver()
    {
        return pathResolver;
    }

    /// <summary>卸载指定内容包拥有的 bundle（ref&gt;0 时强制卸载并打 Warning）。</summary>
    public static void UnloadPackageBundles(IReadOnlyList<string> bundleNames)
    {
        if (bundleNames == null)
            return;

        foreach (string rawName in bundleNames)
        {
            string bundleName = BundlePlatformPaths.NormalizeBundleName(rawName);
            if (string.IsNullOrEmpty(bundleName))
                continue;

            if (!loadedBundles.TryGetValue(bundleName, out BundleEntry entry))
                continue;

            if (entry.Ref > 0)
            {
                Debug.LogWarning(
                    "UnloadPackageBundles force unload with ref>0: " + bundleName + " ref=" + entry.Ref);
            }

            ForceUnloadBundle(bundleName, entry, "UnloadPackage");
        }
    }

    #endregion

    #region 加载/卸载

    /// <summary>
    /// 按清单顺序 Acquire 依赖包再 Acquire 主包。
    /// catalogue.bundles[].dependencies 已为拓扑序（叶→根），与 Unity Manifest 一致。
    /// </summary>
    public static AssetBundle AcquireBundleWithDependencies(string bundleName, List<string> acquiredBundles = null)
    {
        bundleName = BundlePlatformPaths.NormalizeBundleName(bundleName);

        string[] deps = null;
        if (catalogue != null && catalogue.IsLoaded)
        {
            deps = catalogue.GetBundleDependencies(bundleName);
#if DEVELOPMENT_BUILD
            ValidateDependencyOrder(bundleName, deps);
#endif
        }

        if (deps != null)
        {
            foreach (string dep in deps)
            {
                if (string.IsNullOrEmpty(dep))
                    continue;

                AssetBundle depBundle = AcquireBundle(dep, "Dep", bundleName);
                if (depBundle != null)
                    acquiredBundles?.Add(dep);
            }
        }

        AssetBundle bundle = AcquireBundle(bundleName, "Main", bundleName);
        if (bundle != null)
            acquiredBundles?.Add(bundleName);

        return bundle;
    }

    public static AssetBundle AcquireBundle(string bundleName)
    {
        return AcquireBundle(bundleName, null, null);
    }

    static AssetBundle AcquireBundle(string bundleName, string role, string mainBundle)
    {
        bundleName = BundlePlatformPaths.NormalizeBundleName(bundleName);
        TryEvictIdleBundles();

        if (loadedBundles.TryGetValue(bundleName, out BundleEntry entry))
        {
            entry.Ref++;
            entry.LastUsedTime = Time.realtimeSinceStartup;
            return entry.Bundle;
        }

        string path = ResolveBundleFilePath(bundleName);
        AssetBundle bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null)
        {
            Debug.LogError("Bundle load failed: " + path);
            return null;
        }

        loadedBundles[bundleName] = new BundleEntry
        {
            Bundle = bundle,
            Ref = 1,
            LastUsedTime = Time.realtimeSinceStartup,
            ResourcePriority = ResolveResourcePriority(bundleName)
        };
        return bundle;
    }

    /// <summary>
    /// 异步版 <see cref="AcquireBundleWithDependencies"/>；同 bundle in-flight 合并 <see cref="LoadFromFileAsync"/>。
    /// </summary>
    public static async UniTask<AssetBundle> AcquireBundleWithDependenciesAsync(
        string bundleName,
        List<string> acquiredBundles = null)
    {
        bundleName = BundlePlatformPaths.NormalizeBundleName(bundleName);

        string[] deps = null;
        if (catalogue != null && catalogue.IsLoaded)
        {
            deps = catalogue.GetBundleDependencies(bundleName);
#if DEVELOPMENT_BUILD
            ValidateDependencyOrder(bundleName, deps);
#endif
        }

        if (deps != null)
        {
            foreach (string dep in deps)
            {
                if (string.IsNullOrEmpty(dep))
                    continue;

                AssetBundle depBundle = await AcquireBundleAsync(dep, "Dep", bundleName);
                if (depBundle != null)
                    acquiredBundles?.Add(dep);
            }
        }

        AssetBundle bundle = await AcquireBundleAsync(bundleName, "Main", bundleName);
        if (bundle != null)
            acquiredBundles?.Add(bundleName);

        return bundle;
    }

    public static UniTask<AssetBundle> AcquireBundleAsync(string bundleName)
    {
        return AcquireBundleAsync(bundleName, null, null);
    }

    static async UniTask<AssetBundle> AcquireBundleAsync(string bundleName, string role, string mainBundle)
    {
        bundleName = BundlePlatformPaths.NormalizeBundleName(bundleName);
        TryEvictIdleBundles();

        if (loadedBundles.TryGetValue(bundleName, out BundleEntry entry))
        {
            entry.Ref++;
            entry.LastUsedTime = Time.realtimeSinceStartup;
            return entry.Bundle;
        }

        UniTask<AssetBundle> inflightTask;
        bool isFollower;

        lock (bundleInflightGate)
        {
            if (bundleInflight.TryGetValue(bundleName, out inflightTask))
            {
                isFollower = true;
            }
            else
            {
                var tcs = new UniTaskCompletionSource<AssetBundle>();
                inflightTask = tcs.Task;
                bundleInflight[bundleName] = inflightTask;
                isFollower = false;
                RunBundleLoadAsync(bundleName, role, mainBundle, tcs).Forget();
            }
        }

        try
        {
            AssetBundle bundle = await inflightTask;
            if (isFollower && bundle != null && loadedBundles.TryGetValue(bundleName, out BundleEntry loaded))
            {
                loaded.Ref++;
                loaded.LastUsedTime = Time.realtimeSinceStartup;
            }

            return bundle;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static async UniTaskVoid RunBundleLoadAsync(
        string bundleName,
        string role,
        string mainBundle,
        UniTaskCompletionSource<AssetBundle> tcs)
    {
        try
        {
            string path = ResolveBundleFilePath(bundleName);
            AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(path);
            if (request == null)
            {
                Debug.LogError("Bundle load failed: " + path);
                tcs.TrySetResult(null);
                return;
            }

            await request;

            AssetBundle bundle = request.assetBundle;
            if (bundle == null)
            {
                Debug.LogError("Bundle load failed: " + path);
                tcs.TrySetResult(null);
                return;
            }

            if (loadedBundles.TryGetValue(bundleName, out BundleEntry existing))
            {
                bundle.Unload(true);
                existing.Ref++;
                existing.LastUsedTime = Time.realtimeSinceStartup;
                tcs.TrySetResult(existing.Bundle);
                return;
            }

            loadedBundles[bundleName] = new BundleEntry
            {
                Bundle = bundle,
                Ref = 1,
                LastUsedTime = Time.realtimeSinceStartup,
                ResourcePriority = ResolveResourcePriority(bundleName)
            };
            tcs.TrySetResult(bundle);
        }
        catch (Exception ex)
        {
            Debug.LogError("Bundle async load failed: " + bundleName + " | " + ex.Message);
            tcs.TrySetException(ex);
        }
        finally
        {
            lock (bundleInflightGate)
                bundleInflight.Remove(bundleName);
        }
    }

    static void ClearBundleInflight()
    {
        lock (bundleInflightGate)
            bundleInflight.Clear();
    }

    /// <summary>
    /// 释放包。Ref 归零时进入 LRU 空闲队列，延迟卸载而非立即 Unload。
    /// </summary>
    public static void ReleaseBundle(string bundleName)
    {
        bundleName = BundlePlatformPaths.NormalizeBundleName(bundleName);

        if (!loadedBundles.TryGetValue(bundleName, out BundleEntry entry))
        {
            Debug.LogError("ReleaseBundle failed, bundle not loaded: " + bundleName);
            return;
        }

        if (entry.Ref <= 0)
        {
            Debug.LogError("ReleaseBundle failed, ref already 0: " + bundleName);
            return;
        }

        entry.Ref--;
        if (entry.Ref <= 0)
        {
            entry.LastUsedTime = Time.realtimeSinceStartup;
            TryEvictIdleBundles();
        }
    }

    /// <summary>
    /// 主动驱动 LRU 淘汰（可选；Acquire/Release 已会触发）。建议在低负载帧调用。
    /// </summary>
    public static void TickLruUnload()
    {
        TryEvictIdleBundles();
    }

    /// <summary>
    /// 关闭游戏之前或者调试的方法；立即卸载全部包（含 LRU 空闲队列）。
    /// </summary>
    public static void UnloadAll()
    {
        TracePositiveBundleRefs();
        int count = loadedBundles.Count;

        foreach (BundleEntry entry in loadedBundles.Values)
            entry.Bundle.Unload(true);

        loadedBundles.Clear();
        ClearBundleInflight();
    }

    /// <summary>UnloadAll 前输出仍为正引用的 Bundle 摘要。</summary>
    public static void TracePositiveBundleRefs()
    {
        foreach (KeyValuePair<string, BundleEntry> pair in loadedBundles)
        {
            if (pair.Value != null && pair.Value.Ref > 0)
            {
              
            }
        }
    }

    #endregion

    #region LRU

    static int ResolveResourcePriority(string bundleName)
    {
        if (catalogue != null && catalogue.IsLoaded)
            return catalogue.GetBundleResourcePriority(bundleName);

        return (int)ResourcePriority.Normal;
    }

    static void TryEvictIdleBundles()
    {
        if (loadedBundles.Count == 0)
            return;

        float now = Time.realtimeSinceStartup;
        var idleCandidates = new List<KeyValuePair<string, BundleEntry>>();

        foreach (KeyValuePair<string, BundleEntry> pair in loadedBundles)
        {
            if (pair.Value != null && pair.Value.Ref <= 0)
                idleCandidates.Add(pair);
        }

        if (idleCandidates.Count == 0)
            return;

        idleCandidates.Sort(CompareEvictionOrder);

        int idleOverCap = idleCandidates.Count - BundleLruUnloadPolicy.MaxIdleBundles;
        int forcedEvictRemaining = idleOverCap > 0 ? idleOverCap : 0;

        foreach (KeyValuePair<string, BundleEntry> pair in idleCandidates)
        {
            string bundleName = pair.Key;
            BundleEntry entry = pair.Value;

            if (BundleLruUnloadPolicy.IsNeverUnload(entry.ResourcePriority))
                continue;

            float elapsed = now - entry.LastUsedTime;
            float grace = BundleLruUnloadPolicy.GetGraceSeconds(entry.ResourcePriority);
            bool pastGrace = elapsed >= grace;

            if (!pastGrace && forcedEvictRemaining <= 0)
                continue;

            if (!pastGrace)
                forcedEvictRemaining--;

            ForceUnloadBundle(bundleName, entry, pastGrace ? "LruEvict" : "LruEvictCap");
        }
    }

    static int CompareEvictionOrder(KeyValuePair<string, BundleEntry> a, KeyValuePair<string, BundleEntry> b)
    {
        int priorityCompare = b.Value.ResourcePriority.CompareTo(a.Value.ResourcePriority);
        if (priorityCompare != 0)
            return priorityCompare;

        return a.Value.LastUsedTime.CompareTo(b.Value.LastUsedTime);
    }

    static void ForceUnloadBundle(string bundleName, BundleEntry entry, string reason)
    {
        if (entry?.Bundle == null)
        {
            loadedBundles.Remove(bundleName);
            return;
        }

        entry.Bundle.Unload(true);
        loadedBundles.Remove(bundleName);
    }

    #endregion

    #region 辅助函数

    static string ResolveBundleFilePath(string bundleName)
    {
        if (pathResolver != null && pathResolver.TryResolveLocalPath(bundleName, out string resolvedPath))
            return resolvedPath;

        string root = string.IsNullOrEmpty(bundleRootPath)
            ? Application.streamingAssetsPath
            : bundleRootPath;

        return ResolveBundleFilePath(root, bundleName);
    }

    static string ResolveBundleFilePath(string root, string bundleName)
    {
        if (BundlePlatformPaths.TryResolveBundleFilePath(root, bundleName, out string path))
            return path;

        return StreamingAssetsIO.CombinePath(root, bundleName);
    }

#if DEVELOPMENT_BUILD
    static void ValidateDependencyOrder(string bundleName, string[] deps)
    {
        if (deps == null || deps.Length <= 1 || catalogue?.Catalog?.bundles == null)
            return;

        var graph = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (BundleCatalogInfo info in catalogue.Catalog.bundles)
        {
            if (info == null || string.IsNullOrEmpty(info.bundleName))
                continue;

            string key = BundlePlatformPaths.NormalizeBundleName(info.bundleName);
            var list = new List<string>();
            if (info.dependencies != null)
            {
                foreach (string dep in info.dependencies)
                {
                    string normalizedDep = BundlePlatformPaths.NormalizeBundleName(dep);
                    if (!string.IsNullOrEmpty(normalizedDep) && !list.Contains(normalizedDep))
                        list.Add(normalizedDep);
                }
            }

            graph[key] = list;
        }

        var closure = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (string dep in deps)
        {
            string normalized = BundlePlatformPaths.NormalizeBundleName(dep);
            if (!string.IsNullOrEmpty(normalized))
                closure.Add(normalized);
        }

        if (!BundleDependencyTopology.TryTopologicalSort(closure, graph, out _, out string cycleHint))
            Debug.LogWarning("Bundle dependency order may be invalid for " + bundleName + ": cycle near " + cycleHint);
    }
#endif

    #endregion
}
