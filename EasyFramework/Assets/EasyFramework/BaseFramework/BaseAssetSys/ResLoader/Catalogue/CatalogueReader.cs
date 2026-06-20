using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class CatalogueReader
{
    public const string RuntimeCatalogueFileName = BundlePlatformPaths.CatalogFileName;
    const string DefaultResourceRoot = "Assets/AssetBundle";

    #region 变量定义

    AssetCatalog catalog;
    Dictionary<string, AssetCatalogEntry> entryMap = new Dictionary<string, AssetCatalogEntry>();
    Dictionary<string, AssetCatalogEntry> loadPathMap = new Dictionary<string, AssetCatalogEntry>();
    Dictionary<string, string[]> dependencyMap = new Dictionary<string, string[]>();

    readonly Dictionary<string, Dictionary<string, AssetCatalogEntry>> overlayEntryMaps =
        new Dictionary<string, Dictionary<string, AssetCatalogEntry>>();

    readonly Dictionary<string, Dictionary<string, AssetCatalogEntry>> overlayLoadPathMaps =
        new Dictionary<string, Dictionary<string, AssetCatalogEntry>>();

    readonly Dictionary<string, Dictionary<string, string[]>> overlayDependencyMaps =
        new Dictionary<string, Dictionary<string, string[]>>();

    readonly HashSet<string> mountedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region 属性

    public bool IsLoaded => catalog != null;

    public AssetCatalog Catalog => catalog;

    #endregion

    #region 加载

    public bool LoadFromFile(string cataloguePath)
    {
        Clear();

        if (string.IsNullOrEmpty(cataloguePath))
        {
            Debug.LogError("Catalogue path is empty");
            return false;
        }

        try
        {
            if (!AssetCatalogBinaryCodec.TryLoadFromPath(cataloguePath, out catalog))
            {
                Debug.LogError("Catalogue parse failed: " + cataloguePath);
                return false;
            }
        }
        catch (IOException ex)
        {
            Debug.LogError("Catalogue read failed: " + cataloguePath + " | " + ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError("Catalogue read failed: " + cataloguePath + " | " + ex.Message);
            return false;
        }

        BuildLookupTables();
        return true;
    }

    public bool LoadFromBundleRoot(string bundleRoot)
    {
        if (string.IsNullOrEmpty(bundleRoot))
            bundleRoot = Application.streamingAssetsPath;

        if (!BundlePlatformPaths.TryResolveRuntimeCatalogPath(bundleRoot, out string cataloguePath, out _))
            return false;

        return LoadFromFile(cataloguePath);
    }

#if UNITY_EDITOR
    public bool LoadFromProjectCatalogue(string relativeAssetPath = null)
    {
        if (string.IsNullOrEmpty(relativeAssetPath))
            relativeAssetPath = BundlePlatformPaths.ProjectCatalogueRelativePath;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string fullPath = Path.GetFullPath(Path.Combine(projectRoot, relativeAssetPath.Replace("/", Path.DirectorySeparatorChar.ToString())));
        return LoadFromFile(fullPath);
    }
#endif

    #endregion

    #region 查询

    /// <summary>Unity 工程完整路径，如 Assets/AssetBundle/Atlas/Role/Hog.png</summary>
    public bool TryGetEntry(string assetPath, out AssetCatalogEntry entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(assetPath))
            return false;

        return entryMap.TryGetValue(NormalizePath(assetPath), out entry);
    }

    /// <summary>业务简路径，相对 resourceRoot、无扩展名，如 Atlas/Role/Hog_Attack_000</summary>
    public bool TryGetEntryByLoadPath(string loadPath, out AssetCatalogEntry entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(loadPath))
            return false;

        return loadPathMap.TryGetValue(NormalizeLoadPath(loadPath), out entry);
    }

    public bool IsPackageMounted(string packageId)
    {
        return !string.IsNullOrEmpty(packageId)
            && mountedPackageIds.Contains(packageId);
    }

    /// <summary>合并 DLC/Mod catalog 片段（同 AssetCatalog 结构）。</summary>
    public bool Merge(AssetCatalog fragment, string packageId)
    {
        if (fragment == null || string.IsNullOrEmpty(packageId))
            return false;

        if (!IsLoaded)
        {
            Debug.LogError("CatalogueReader.Merge failed: base catalogue not loaded");
            return false;
        }

        if (mountedPackageIds.Contains(packageId))
            return true;

        var entryOverlay = new Dictionary<string, AssetCatalogEntry>();
        var loadPathOverlay = new Dictionary<string, AssetCatalogEntry>();
        string resourceRoot = string.IsNullOrEmpty(catalog.resourceRoot)
            ? DefaultResourceRoot
            : catalog.resourceRoot;

        if (fragment.entries != null)
        {
            foreach (AssetCatalogEntry entry in fragment.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.assetPath))
                    continue;

                string loadKey = ToLoadPath(entry.assetPath, resourceRoot);
                if (!string.IsNullOrEmpty(loadKey) && loadPathMap.ContainsKey(loadKey))
                {
                    Debug.LogError("Catalogue merge conflict loadPath=" + loadKey + " package=" + packageId);
                    return false;
                }

                if (!string.IsNullOrEmpty(loadKey))
                    loadPathOverlay[loadKey] = entry;

                entryOverlay[NormalizePath(entry.assetPath)] = entry;
            }
        }

        var depOverlay = new Dictionary<string, string[]>();
        if (fragment.bundles != null)
        {
            foreach (BundleCatalogInfo info in fragment.bundles)
            {
                if (info == null || string.IsNullOrEmpty(info.bundleName))
                    continue;

                string[] deps = info.dependenciesAll != null && info.dependenciesAll.Length > 0
                    ? info.dependenciesAll
                    : info.dependencies ?? new string[0];
                depOverlay[info.bundleName] = deps;
            }
        }

        foreach (KeyValuePair<string, AssetCatalogEntry> pair in entryOverlay)
            entryMap[pair.Key] = pair.Value;

        foreach (KeyValuePair<string, AssetCatalogEntry> pair in loadPathOverlay)
            loadPathMap[pair.Key] = pair.Value;

        foreach (KeyValuePair<string, string[]> pair in depOverlay)
            dependencyMap[pair.Key] = pair.Value;

        overlayEntryMaps[packageId] = entryOverlay;
        overlayLoadPathMaps[packageId] = loadPathOverlay;
        overlayDependencyMaps[packageId] = depOverlay;
        mountedPackageIds.Add(packageId);
        return true;
    }

    public bool Unmerge(string packageId)
    {
        if (string.IsNullOrEmpty(packageId) || !mountedPackageIds.Contains(packageId))
            return false;

        if (overlayEntryMaps.TryGetValue(packageId, out Dictionary<string, AssetCatalogEntry> entryOverlay))
        {
            foreach (string key in entryOverlay.Keys)
                entryMap.Remove(key);
        }

        if (overlayLoadPathMaps.TryGetValue(packageId, out Dictionary<string, AssetCatalogEntry> loadPathOverlay))
        {
            foreach (string key in loadPathOverlay.Keys)
                loadPathMap.Remove(key);
        }

        if (overlayDependencyMaps.TryGetValue(packageId, out Dictionary<string, string[]> depOverlay))
        {
            foreach (string key in depOverlay.Keys)
                dependencyMap.Remove(key);
        }

        overlayEntryMaps.Remove(packageId);
        overlayLoadPathMaps.Remove(packageId);
        overlayDependencyMaps.Remove(packageId);
        mountedPackageIds.Remove(packageId);
        return true;
    }

    public string[] GetBundleDependencies(string bundleName)
    {
        if (string.IsNullOrEmpty(bundleName))
            return new string[0];

        if (dependencyMap.TryGetValue(bundleName, out string[] deps))
            return deps ?? new string[0];

        return new string[0];
    }

    /// <summary>读取 bundles[] 中的 resourcePriority；无清单或未配置时返回 Normal。</summary>
    public int GetBundleResourcePriority(string bundleName)
    {
        if (string.IsNullOrEmpty(bundleName) || catalog?.bundles == null)
            return (int)ResourcePriority.Normal;

        string normalized = BundlePlatformPaths.NormalizeBundleName(bundleName);
        foreach (BundleCatalogInfo info in catalog.bundles)
        {
            if (info == null || string.IsNullOrEmpty(info.bundleName))
                continue;

            if (string.Equals(
                    BundlePlatformPaths.NormalizeBundleName(info.bundleName),
                    normalized,
                    System.StringComparison.OrdinalIgnoreCase))
                return info.resourcePriority;
        }

        return (int)ResourcePriority.Normal;
    }

    #endregion

    #region 辅助函数

    void Clear()
    {
        catalog = null;
        entryMap.Clear();
        loadPathMap.Clear();
        dependencyMap.Clear();
        overlayEntryMaps.Clear();
        overlayLoadPathMaps.Clear();
        overlayDependencyMaps.Clear();
        mountedPackageIds.Clear();
    }

    void BuildLookupTables()
    {
        entryMap.Clear();
        loadPathMap.Clear();
        dependencyMap.Clear();

        string resourceRoot = string.IsNullOrEmpty(catalog.resourceRoot)
            ? DefaultResourceRoot
            : catalog.resourceRoot;

        if (catalog.entries != null)
        {
            foreach (AssetCatalogEntry entry in catalog.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.assetPath))
                    continue;

                string assetKey = NormalizePath(entry.assetPath);
                entryMap[assetKey] = entry;

                string loadKey = ToLoadPath(entry.assetPath, resourceRoot);
                if (!string.IsNullOrEmpty(loadKey))
                    loadPathMap[loadKey] = entry;
            }
        }

        if (catalog.bundles != null)
        {
            foreach (BundleCatalogInfo info in catalog.bundles)
            {
                if (info == null || string.IsNullOrEmpty(info.bundleName))
                    continue;

                string[] deps = info.dependenciesAll != null && info.dependenciesAll.Length > 0
                    ? info.dependenciesAll
                    : info.dependencies ?? new string[0];
                if (deps.Length > 1)
                {
                    deps = BundleDependencyTopology.SortUsingCatalogAllDeps(
                        catalog.bundles,
                        info.bundleName,
                        deps);
                }

                dependencyMap[info.bundleName] = deps;
            }
        }
    }

    public static string NormalizePath(string path)
    {
        return string.IsNullOrEmpty(path) ? path : path.Replace("\\", "/");
    }

    public static string NormalizeLoadPath(string loadPath)
    {
        if (string.IsNullOrEmpty(loadPath))
            return loadPath;

        string normalized = NormalizePath(loadPath).Trim('/');
        int lastSlash = normalized.LastIndexOf('/');
        int lastDot = normalized.LastIndexOf('.');
        if (lastDot > lastSlash)
            normalized = normalized.Substring(0, lastDot);

        return normalized;
    }

    public static string ToLoadPath(string assetPath, string resourceRoot)
    {
        if (string.IsNullOrEmpty(assetPath))
            return null;

        string normalized = NormalizePath(assetPath);
        string root = NormalizePath(resourceRoot).TrimEnd('/');

        if (!string.IsNullOrEmpty(root))
        {
            if (normalized.StartsWith(root + "/"))
                normalized = normalized.Substring(root.Length + 1);
            else if (normalized == root)
                normalized = Path.GetFileNameWithoutExtension(normalized);
        }

        return NormalizeLoadPath(normalized);
    }

    #endregion
}
