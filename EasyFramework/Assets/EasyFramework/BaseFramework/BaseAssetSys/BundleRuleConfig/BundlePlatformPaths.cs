using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 各平台 AB 输出/加载子目录名。构建与运行时共用，便于 Win/Android 等产物并存。
/// </summary>
public static class BundlePlatformPaths
{
    /// <summary>工程内 AB 资源系统根目录（BaseAssetSys，原 ABSystem_Beta）。</summary>
    public const string SystemRoot = "Assets/vFramework/BaseFramework/BaseAssetSys";

    public const string ProjectCatalogueRelativePath = SystemRoot + "/BundleRuleConfig/Catalogue/AssetCatalog.bytes";

    public const string DefaultBuildSettingRelativePath = SystemRoot + "/BundleRuleConfig/Setting/DefaultBuildSetting.asset";

    public const string ReportEditorBlueprintRelativePath = SystemRoot + "/Docs/ReportEditorBlueprint.html";

    public const string BundleSuffix = ".bundle";

    public const string WindowsFolder = "StandaloneWindows64";
    public const string AndroidFolder = "Android";
    public const string IOSFolder = "iOS";
    public const string MacFolder = "StandaloneOSX";
    public const string WebGLFolder = "WebGL";

    public const string BasePackageId = "Base";
    public const string VersionFolder = "Version";
    public const string BundlesFolder = "Bundles";
    public const string ConfigFolder = "Config";
    public const string ModsFolder = "Mods";
    public const string DlcPackagePrefix = "DLC_";

    public const string CatalogFileName = "catalog.bytes";
    public const string ManifestFileName = "manifest.json";
    public const string VersionFileName = "version.json";
    public const string CatalogFragmentFileName = "catalog.fragment.bytes";

    public static string GetFolderName(BuildPlatform platform)
    {
        switch (platform)
        {
            case BuildPlatform.Android:
                return AndroidFolder;
            case BuildPlatform.iOS:
                return IOSFolder;
            case BuildPlatform.macOS:
                return MacFolder;
            case BuildPlatform.WebGL:
                return WebGLFolder;
            default:
                return WindowsFolder;
        }
    }

    /// <summary>当前运行环境对应的平台子目录（Editor 下 Windows 编辑器等 → StandaloneWindows64）。</summary>
    public static string GetRuntimeFolderName()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.Android:
                return AndroidFolder;
            case RuntimePlatform.IPhonePlayer:
                return IOSFolder;
            case RuntimePlatform.OSXPlayer:
            case RuntimePlatform.OSXEditor:
                return MacFolder;
            case RuntimePlatform.WebGLPlayer:
                return WebGLFolder;
            default:
                return WindowsFolder;
        }
    }

    public static string AppendPlatformFolder(string basePath, string platformFolder)
    {
        if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(platformFolder))
            return basePath;

        if (StreamingAssetsIO.IsNonFileProtocolPath(basePath))
            return StreamingAssetsIO.CombinePath(basePath, platformFolder);

        return Path.Combine(basePath, platformFolder);
    }

    /// <summary>将配置中的输出路径（如 Assets/StreamingAssets、Bundles/CDN）解析为绝对路径。</summary>
    public static string ResolveBaseOutputPath(string outputPath)
    {
        if (string.IsNullOrEmpty(outputPath))
            return Application.streamingAssetsPath;

        string normalized = outputPath.Replace("\\", "/");
        if (normalized == "Assets/StreamingAssets")
            return Application.streamingAssetsPath;

#if UNITY_EDITOR
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.GetFullPath(Path.Combine(projectRoot, normalized));
#else
        if (Path.IsPathRooted(outputPath))
            return Path.GetFullPath(outputPath);

        return Path.Combine(Application.streamingAssetsPath, normalized);
#endif
    }

    public static string ResolvePlatformOutputPath(
        string outputPath,
        BuildPlatform platform,
        bool usePlatformSubfolders)
    {
        string baseAbs = ResolveBaseOutputPath(outputPath);
        if (!usePlatformSubfolders)
            return baseAbs;

        return Path.GetFullPath(AppendPlatformFolder(baseAbs, GetFolderName(platform)));
    }

    /// <summary>运行时默认 bundle 根：首包 base + 当前平台子目录。</summary>
    public static string ResolveRuntimeBundleRoot(string baseOutputPath, bool usePlatformSubfolders)
    {
        string baseAbs = string.IsNullOrEmpty(baseOutputPath)
            ? Application.streamingAssetsPath
            : ResolveBaseOutputPath(baseOutputPath);

        if (!usePlatformSubfolders)
            return baseAbs;

        string withPlatform = AppendPlatformFolder(baseAbs, GetRuntimeFolderName());
        if (StreamingAssetsIO.IsNonFileProtocolPath(baseAbs))
            return withPlatform;

        return Path.GetFullPath(withPlatform);
    }

    public static string ResolvePackageId(BuildMode mode, BuildSetting setting)
    {
        if (mode == BuildMode.DlcPackage)
        {
            if (setting != null && !string.IsNullOrEmpty(setting.dlcPackageId))
                return NormalizeDlcPackageId(setting.dlcPackageId);
            return DlcPackagePrefix + "Default";
        }

        return BasePackageId;
    }

    public static string NormalizeDlcPackageId(string dlcPackageId)
    {
        if (string.IsNullOrEmpty(dlcPackageId))
            return DlcPackagePrefix + "Default";

        string id = dlcPackageId.Replace("\\", "/").Trim('/');
        if (id.StartsWith(DlcPackagePrefix, StringComparison.OrdinalIgnoreCase))
            return id;

        return DlcPackagePrefix + id;
    }

    public static string ResolvePackageRoot(string platformRoot, string packageId)
    {
        if (string.IsNullOrEmpty(platformRoot))
            return platformRoot;

        if (string.IsNullOrEmpty(packageId) || packageId == BasePackageId)
        {
            if (StreamingAssetsIO.IsNonFileProtocolPath(platformRoot))
                return StreamingAssetsIO.CombinePath(platformRoot, BasePackageId);
            return Path.GetFullPath(Path.Combine(platformRoot, BasePackageId));
        }

        if (packageId.StartsWith(ModsFolder + "/", StringComparison.OrdinalIgnoreCase))
        {
            if (StreamingAssetsIO.IsNonFileProtocolPath(platformRoot))
                return StreamingAssetsIO.CombinePath(platformRoot, packageId);
            return Path.GetFullPath(Path.Combine(platformRoot, packageId.Replace("/", Path.DirectorySeparatorChar.ToString())));
        }

        if (StreamingAssetsIO.IsNonFileProtocolPath(platformRoot))
            return StreamingAssetsIO.CombinePath(platformRoot, packageId);

        return Path.GetFullPath(Path.Combine(platformRoot, packageId));
    }

    public static string ResolveBundlesRoot(string packageRoot)
    {
        if (string.IsNullOrEmpty(packageRoot))
            return packageRoot;

        if (StreamingAssetsIO.IsNonFileProtocolPath(packageRoot))
            return StreamingAssetsIO.CombinePath(packageRoot, BundlesFolder);

        return Path.GetFullPath(Path.Combine(packageRoot, BundlesFolder));
    }

    public static string ResolveVersionDir(string packageRoot)
    {
        if (string.IsNullOrEmpty(packageRoot))
            return packageRoot;

        if (StreamingAssetsIO.IsNonFileProtocolPath(packageRoot))
            return StreamingAssetsIO.CombinePath(packageRoot, VersionFolder);

        return Path.GetFullPath(Path.Combine(packageRoot, VersionFolder));
    }

    public static string ResolveConfigDir(string packageRoot)
    {
        if (string.IsNullOrEmpty(packageRoot))
            return packageRoot;

        if (StreamingAssetsIO.IsNonFileProtocolPath(packageRoot))
            return StreamingAssetsIO.CombinePath(packageRoot, ConfigFolder);

        return Path.GetFullPath(Path.Combine(packageRoot, ConfigFolder));
    }

    public static string ResolveVersionCatalogPath(string packageRoot)
    {
        return StreamingAssetsIO.CombinePath(ResolveVersionDir(packageRoot), CatalogFileName);
    }

    /// <summary>运行时解析首包：Base/Version/catalog.bytes。</summary>
    public static bool TryResolveRuntimeCatalogPath(string platformRoot, out string cataloguePath, out string bundlesRoot)
    {
        cataloguePath = null;
        bundlesRoot = null;

        if (string.IsNullOrEmpty(platformRoot))
            platformRoot = Application.streamingAssetsPath;

        string basePackageRoot = ResolvePackageRoot(platformRoot, BasePackageId);
        string catalogPath = ResolveVersionCatalogPath(basePackageRoot);
        if (!CatalogFileExists(catalogPath))
            return false;

        cataloguePath = catalogPath;
        bundlesRoot = ResolveBundlesRoot(basePackageRoot);
        return true;
    }

    static bool CatalogFileExists(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (StreamingAssetsIO.IsNonFileProtocolPath(path))
            return true;

        return File.Exists(path);
    }

    public static bool TryResolveBundleFilePath(string bundlesRoot, string bundleName, out string fullPath)
    {
        fullPath = null;
        if (string.IsNullOrEmpty(bundlesRoot) || string.IsNullOrEmpty(bundleName))
            return false;

        bundleName = NormalizeBundleName(bundleName);
        string direct = StreamingAssetsIO.CombinePath(bundlesRoot, bundleName);
        if (!StreamingAssetsIO.IsNonFileProtocolPath(bundlesRoot) && File.Exists(direct))
        {
            fullPath = direct;
            return true;
        }

        if (StreamingAssetsIO.IsNonFileProtocolPath(bundlesRoot))
        {
            fullPath = direct;
            return true;
        }

        if (!Directory.Exists(bundlesRoot))
            return false;

        string fileName = Path.GetFileName(bundleName);
        foreach (string file in Directory.GetFiles(bundlesRoot, "*.bundle", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
            {
                fullPath = file;
                return true;
            }
        }

        return false;
    }

    /// <summary>统一 bundle 名为小写 + .bundle；保留分类子路径（如 core/ui.bundle）。</summary>
    public static string NormalizeBundleName(string bundleName)
    {
        if (string.IsNullOrEmpty(bundleName))
            return bundleName;

        string name = bundleName.Replace("\\", "/").Trim();
        string[] segments = name.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (string.IsNullOrEmpty(segments[i]))
                continue;

            string segment = segments[i];
            if (!segment.EndsWith(BundleSuffix, StringComparison.OrdinalIgnoreCase))
                segment += BundleSuffix;

            segments[i] = segment.ToLowerInvariant();
        }

        return string.Join("/", segments);
    }
}
