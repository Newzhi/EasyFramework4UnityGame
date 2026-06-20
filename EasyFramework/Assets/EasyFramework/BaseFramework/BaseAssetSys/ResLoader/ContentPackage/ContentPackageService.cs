using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// DLC / Mod 内容包挂载与卸载。是否允许加载由第三方 SDK 门控（见 <see cref="IContentPackageGate"/>）。
/// 不修改 <see cref="BundleResLoader"/> 主加载 API；挂载后合并 catalog，卸载时卸包。
/// </summary>
public sealed class ContentPackageService
{
    static ContentPackageService instance;

    public static ContentPackageService Instance => instance ?? (instance = new ContentPackageService());

    readonly Dictionary<string, string> mountedPackageRoots = new Dictionary<string, string>();
    readonly Dictionary<string, List<string>> mountedPackageBundles = new Dictionary<string, List<string>>();

    IContentPackageGate gate;

    /// <summary>注入第三方 SDK 门控；null 时除 Base 外默认拒绝 DLC/Mod。</summary>
    public void SetGate(IContentPackageGate packageGate)
    {
        gate = packageGate;
    }

    public bool IsMounted(string packageId)
    {
        return !string.IsNullOrEmpty(packageId) && mountedPackageRoots.ContainsKey(packageId);
    }

    /// <summary>
    /// 挂载内容包。packageRoot 为空时从 StreamingAssets/{platform}/{packageId} 解析。
    /// </summary>
    public bool TryMount(string packageId, string packageRoot = null)
    {
        if (string.IsNullOrEmpty(packageId))
            return false;

        if (packageId == BundlePlatformPaths.BasePackageId)
            return true;

        if (IsMounted(packageId))
            return true;

        if (!IsEnabledByGate(packageId))
        {
            Debug.LogWarning("[ContentPackageService] Package denied by gate: " + packageId);
            return false;
        }

        CatalogueReader catalogue = BundleResLoader.Instance.GetCatalogue();
        if (catalogue == null || !catalogue.IsLoaded)
        {
            Debug.LogError("[ContentPackageService] Mount failed: base catalogue not loaded");
            return false;
        }

        string platformRoot = BundleResLoader.GetDefaultRuntimeBundleRoot();
        if (string.IsNullOrEmpty(packageRoot))
            packageRoot = BundlePlatformPaths.ResolvePackageRoot(platformRoot, packageId);

        string bundlesRoot = BundlePlatformPaths.ResolveBundlesRoot(packageRoot);
        if (!TryLoadCatalogFragment(packageRoot, out AssetCatalog fragment))
        {
            Debug.LogError("[ContentPackageService] Mount failed: catalog fragment missing for " + packageId);
            return false;
        }

        if (!catalogue.Merge(fragment, packageId))
            return false;

        DefaultBundlePathResolver resolver = BundleManager.GetPathResolver() as DefaultBundlePathResolver;
        resolver?.RegisterPackageBundlesRoot(bundlesRoot);

        mountedPackageRoots[packageId] = packageRoot;
        mountedPackageBundles[packageId] = CollectBundleNames(fragment);
        Debug.Log("[ContentPackageService] Mounted " + packageId + " @ " + packageRoot);
        return true;
    }

    public bool TryUnmount(string packageId)
    {
        if (string.IsNullOrEmpty(packageId) || packageId == BundlePlatformPaths.BasePackageId)
            return false;

        if (!IsMounted(packageId))
            return false;

        if (mountedPackageBundles.TryGetValue(packageId, out List<string> bundleNames))
            BundleManager.UnloadPackageBundles(bundleNames);

        CatalogueReader catalogue = BundleResLoader.Instance.GetCatalogue();
        catalogue?.Unmerge(packageId);

        if (mountedPackageRoots.TryGetValue(packageId, out string packageRoot))
        {
            DefaultBundlePathResolver resolver = BundleManager.GetPathResolver() as DefaultBundlePathResolver;
            string bundlesRoot = BundlePlatformPaths.ResolveBundlesRoot(packageRoot);
            resolver?.UnregisterPackageBundlesRoot(bundlesRoot);
        }

        mountedPackageRoots.Remove(packageId);
        mountedPackageBundles.Remove(packageId);
        Debug.Log("[ContentPackageService] Unmounted " + packageId);
        return true;
    }

    bool IsEnabledByGate(string packageId)
    {
        if (gate == null)
            return false;

        return gate.IsPackageEnabled(packageId);
    }

    static bool TryLoadCatalogFragment(string packageRoot, out AssetCatalog fragment)
    {
        fragment = null;
        string versionDir = BundlePlatformPaths.ResolveVersionDir(packageRoot);
        string fragmentPath = StreamingAssetsIO.CombinePath(versionDir, BundlePlatformPaths.CatalogFragmentFileName);
        if (TryLoadCatalogAtPath(fragmentPath, out fragment))
            return true;

        string catalogPath = StreamingAssetsIO.CombinePath(versionDir, BundlePlatformPaths.CatalogFileName);
        return TryLoadCatalogAtPath(catalogPath, out fragment);
    }

    static bool TryLoadCatalogAtPath(string path, out AssetCatalog catalog)
    {
        catalog = null;
        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            if (!StreamingAssetsIO.IsNonFileProtocolPath(path) && !File.Exists(path))
                return false;

            return AssetCatalogBinaryCodec.TryLoadFromPath(path, out catalog);
        }
        catch
        {
            catalog = null;
            return false;
        }
    }

    static List<string> CollectBundleNames(AssetCatalog catalog)
    {
        var names = new List<string>();
        if (catalog?.bundles == null)
            return names;

        foreach (BundleCatalogInfo info in catalog.bundles)
        {
            if (info != null && !string.IsNullOrEmpty(info.bundleName))
                names.Add(info.bundleName);
        }

        return names;
    }
}

/// <summary>
/// 第三方 SDK 门控：判定 DLC/Mod 是否已购买、已下载或已启用。
/// </summary>
public interface IContentPackageGate
{
    bool IsPackageEnabled(string packageId);
}

/// <summary>测试用：除 Base 外全部启用。</summary>
public sealed class AllowAllContentPackageGate : IContentPackageGate
{
    public bool IsPackageEnabled(string packageId)
    {
        return packageId != BundlePlatformPaths.BasePackageId;
    }
}

// TODO(Steam SDK): 实现 SteamContentPackageGate
//   - 使用 Steamworks.NET / Facepunch.Steamworks
//   - packageId → Steam DLC AppId 映射表（ScriptableObject 或配置表）
//   - IsPackageEnabled: SteamApps.BIsDlcInstalled(dlcAppId) 或 SteamApps.BIsSubscribedApp
//   - ShouldDownload（若扩展接口）: 结合 SteamApps.GetDlcDownloadProgress
//
// TODO(TapTap / 渠道 SDK): 按渠道包配置 DLC 开关，与 Steam 门控同形实现 IContentPackageGate
//
// TODO(配置): ContentPackageGateConfig.asset — packageId, storeSku, steamAppId, defaultEnabled(Editor)
