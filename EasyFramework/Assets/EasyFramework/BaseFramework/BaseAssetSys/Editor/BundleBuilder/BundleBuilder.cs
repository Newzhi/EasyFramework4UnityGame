using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class BundleBuilder
{
    #region 变量定义

    public const string SystemRoot = BundlePlatformPaths.SystemRoot;
    public const string DefaultSettingPath = BundlePlatformPaths.DefaultBuildSettingRelativePath;

    #endregion

    #region 打包

    public static bool Build(BuildSetting setting)
    {
        return Build(setting, BundleBuildExecutionMode.Incremental);
    }

    public static bool Build(BuildSetting setting, BundleBuildExecutionMode executionMode)
    {
        return BundleBuildPipeline.Execute(setting, executionMode);
    }

    public static void EnsureOutputDirectoryPublic(string outputPath)
    {
        EnsureOutputDirectory(outputPath);
    }

    #endregion

    #region 清理

    public static void Clean(BuildSetting setting)
    {
        if (setting != null)
        {
            CleanOutputPath(setting.deviceOutputPath, setting);
            CleanOutputPath(setting.cdnOutputPath, setting);
            BuildManifestService.DeleteCache(BundleBuilder.ResolveBundleRoot(BuildMode.DeviceDebug, setting));
            BuildManifestService.DeleteCache(BundleBuilder.ResolveBundleRoot(BuildMode.CdnHotUpdate, setting));
        }

        string cataloguePath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            CatalogueWriter.CatalogueAssetPath);

        if (File.Exists(cataloguePath))
        {
            string catalogueRelative = CatalogueWriter.CatalogueAssetPath;
            if (AssetDatabase.LoadAssetAtPath<Object>(catalogueRelative) != null)
                AssetDatabase.DeleteAsset(catalogueRelative);
            else
                DeleteFileAndMeta(cataloguePath);
        }

        string catalogueDir = Path.GetDirectoryName(cataloguePath);
        if (!string.IsNullOrEmpty(catalogueDir))
        {
            foreach (string legacyName in new[] { "AssetCatalog.json", "catalog.json", "catalog.fragment.json" })
            {
                string legacyPath = Path.Combine(catalogueDir, legacyName);
                if (File.Exists(legacyPath))
                    DeleteFileAndMeta(legacyPath);
            }
        }

        AssetDatabase.Refresh();
        Debug.Log("已清理打包输出、清单与 BuildCache");
    }

    #endregion

    #region 辅助函数

    public static bool Validate(BuildSetting setting)
    {
        if (setting.packingRule != PackingRule.Custom)
        {
            if (!AssetDatabase.IsValidFolder(setting.targetDirectory))
            {
                Debug.LogError("目标资源目录不存在: " + setting.targetDirectory);
                return false;
            }

            if (setting.buildMode == BuildMode.DeviceDebug && string.IsNullOrEmpty(setting.deviceOutputPath))
            {
                Debug.LogError("真机环境输出路径不能为空");
                return false;
            }

            if (setting.buildMode == BuildMode.CdnHotUpdate && string.IsNullOrEmpty(setting.cdnOutputPath))
            {
                Debug.LogError("联网 CDN 输出路径不能为空");
                return false;
            }

            // TODO: DlcPackage 校验 dlcPackageId
            if (setting.buildMode == BuildMode.DlcPackage && string.IsNullOrEmpty(setting.dlcPackageId))
            {
                Debug.LogError("DLC分包模式：dlcPackageId 不能为空（如 Desert）");
                return false;
            }
        }
        else
        {
            if (setting.customItems == null || setting.customItems.Count == 0)
            {
                Debug.LogError("自定义打包模式下至少需要一个配置项");
                return false;
            }

            if (string.IsNullOrEmpty(setting.deviceOutputPath))
            {
                Debug.LogError("真机环境输出路径不能为空");
                return false;
            }

            bool needsCdnPath = false;
            bool needsDlcPath = false;
            foreach (BundleConfigItem item in setting.customItems)
            {
                if (item.buildMode == BuildMode.CdnHotUpdate)
                    needsCdnPath = true;
                if (item.buildMode == BuildMode.DlcPackage)
                    needsDlcPath = true;
            }

            if (needsCdnPath && string.IsNullOrEmpty(setting.cdnOutputPath))
            {
                Debug.LogError("联网 CDN 输出路径不能为空");
                return false;
            }

            // TODO: DlcPackage Custom 项校验 dlcOutputPath
            if (needsDlcPath && string.IsNullOrEmpty(setting.cdnOutputPath))
            {
                Debug.LogError("DLC分包配置项：输出路径占位不能为空（TODO：改为 dlcOutputPath）");
                return false;
            }
        }

        return true;
    }

    public static string[] CollectAssetPaths(string folder)
    {
        string[] guids = AssetDatabase.FindAssets("", new[] { folder });
        List<string> paths = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path))
                continue;
            if (path.EndsWith(".cs"))
                continue;

            paths.Add(path);
        }

        return paths.ToArray();
    }

    public static string ResolveBundleRoot(BuildMode mode, BuildSetting setting)
    {
        string basePath;
        switch (mode)
        {
            case BuildMode.CdnHotUpdate:
                basePath = setting.cdnOutputPath;
                break;
            case BuildMode.DlcPackage:
                basePath = setting.deviceOutputPath;
                break;
            case BuildMode.DeviceDebug:
                basePath = setting.deviceOutputPath;
                break;
            default:
                basePath = setting.deviceOutputPath;
                break;
        }

        string platformRoot = ResolvePlatformOutputPath(basePath, setting.platform, setting.usePlatformSubfolders);
        string packageId = BundlePlatformPaths.ResolvePackageId(mode, setting);
        return BundlePlatformPaths.ResolvePackageRoot(platformRoot, packageId);
    }

    public static string ResolveBundlesBuildRoot(BuildMode mode, BuildSetting setting)
    {
        return BundlePlatformPaths.ResolveBundlesRoot(ResolveBundleRoot(mode, setting));
    }

    public static string ResolvePlatformOutputPath(
        string outputPath,
        BuildPlatform platform,
        bool usePlatformSubfolders)
    {
        return BundlePlatformPaths.ResolvePlatformOutputPath(outputPath, platform, usePlatformSubfolders);
    }

    public static string ResolveOutputPath(string outputPath)
    {
        return BundlePlatformPaths.ResolveBaseOutputPath(outputPath);
    }

    static void EnsureOutputDirectory(string outputPath)
    {
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);
    }

    static void CleanOutputPath(string configPath, BuildSetting setting)
    {
        if (setting.usePlatformSubfolders)
        {
            string baseAbs = ResolveOutputPath(configPath);
            foreach (string platformFolder in StreamingAssetsPlatformIsolation.KnownPlatformFolders)
            {
                string platformPath = Path.Combine(baseAbs, platformFolder);
                if (Directory.Exists(platformPath))
                {
                    CleanPackageTree(platformPath);
                    TryDeletePlatformFolderAsset(platformPath);
                }
            }

            return;
        }

        string outputPath = ResolvePlatformOutputPath(
            configPath,
            setting.platform,
            setting.usePlatformSubfolders);

        if (!Directory.Exists(outputPath))
            return;

        CleanOutputDirectory(outputPath);
        TryDeletePlatformFolderAsset(outputPath);
    }

    static void TryDeletePlatformFolderAsset(string absolutePath)
    {
        string relative = ToAssetsRelativePath(absolutePath);
        if (string.IsNullOrEmpty(relative) || !relative.StartsWith("Assets/"))
            return;

        if (AssetDatabase.IsValidFolder(relative))
            AssetDatabase.DeleteAsset(relative);
    }

    static void CleanPackageTree(string platformPath)
    {
        string basePath = Path.Combine(platformPath, BundlePlatformPaths.BasePackageId);
        if (Directory.Exists(basePath))
            CleanOutputDirectory(basePath);

        if (!Directory.Exists(platformPath))
            return;

        foreach (string dir in Directory.GetDirectories(platformPath))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith(BundlePlatformPaths.DlcPackagePrefix, System.StringComparison.OrdinalIgnoreCase))
                CleanOutputDirectory(dir);
        }

        string modsRoot = Path.Combine(platformPath, BundlePlatformPaths.ModsFolder);
        if (Directory.Exists(modsRoot))
        {
            foreach (string modDir in Directory.GetDirectories(modsRoot))
                CleanOutputDirectory(modDir);
        }

        CleanOutputDirectory(platformPath);
    }

    static void CleanOutputDirectory(string outputPath)
    {
        if (!Directory.Exists(outputPath))
            return;

        foreach (string file in Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories))
        {
            if (ShouldDeleteBuildArtifact(file))
                DeleteOutputFile(file);
        }

        DeleteReportsArtifacts(outputPath);

        foreach (string file in Directory.GetFiles(outputPath, "*", SearchOption.TopDirectoryOnly))
        {
            if (ShouldDeleteExtensionlessManifest(file))
                DeleteOutputFile(file);
        }

        string runtimeCatalogueDir = Path.Combine(outputPath, BundlePlatformPaths.VersionFolder);
        if (Directory.Exists(runtimeCatalogueDir))
        {
            foreach (string file in Directory.GetFiles(runtimeCatalogueDir, "*", SearchOption.AllDirectories))
                DeleteOutputFile(file);
        }

        string bundlesDir = Path.Combine(outputPath, BundlePlatformPaths.BundlesFolder);
        if (Directory.Exists(bundlesDir))
        {
            foreach (string file in Directory.GetFiles(bundlesDir, "*", SearchOption.AllDirectories))
            {
                if (ShouldDeleteBuildArtifact(file))
                    DeleteOutputFile(file);
            }
        }

        string configDir = Path.Combine(outputPath, BundlePlatformPaths.ConfigFolder);
        if (Directory.Exists(configDir))
        {
            foreach (string file in Directory.GetFiles(configDir, "*", SearchOption.AllDirectories))
                DeleteOutputFile(file);
        }

        string runtimeCatalogueDirLegacy = Path.Combine(outputPath, "Catalogue");
        if (Directory.Exists(runtimeCatalogueDirLegacy))
        {
            foreach (string file in Directory.GetFiles(runtimeCatalogueDirLegacy, "*", SearchOption.AllDirectories))
                DeleteOutputFile(file);

            string relativeCatalogueDir = ToAssetsRelativePath(runtimeCatalogueDirLegacy);
            if (!string.IsNullOrEmpty(relativeCatalogueDir) && AssetDatabase.IsValidFolder(relativeCatalogueDir))
                AssetDatabase.DeleteAsset(relativeCatalogueDir);
            else if (Directory.Exists(runtimeCatalogueDirLegacy) && Directory.GetFiles(runtimeCatalogueDirLegacy).Length == 0)
                Directory.Delete(runtimeCatalogueDirLegacy, true);
        }
    }

    static void DeleteReportsArtifacts(string outputPath)
    {
        string reportsDir = Path.Combine(outputPath, BundleBuildAnalyzer.ReportsFolderName);
        if (!Directory.Exists(reportsDir))
            return;

        foreach (string file in Directory.GetFiles(reportsDir, "*", SearchOption.TopDirectoryOnly))
            DeleteOutputFile(file);
    }

    static bool ShouldDeleteBuildArtifact(string filePath)
    {
        string name = Path.GetFileName(filePath);
        return name.EndsWith(RuleResolver.BundleSuffix)
            || name.EndsWith(".manifest")
            || name == RuntimeCatalogueFileName
            || name == BundlePlatformPaths.CatalogFragmentFileName
            || name == "AssetCatalog.json"
            || name == "catalog.json"
            || name == "catalog.fragment.json";
    }

    static bool ShouldDeleteExtensionlessManifest(string filePath)
    {
        if (Directory.Exists(filePath))
            return false;

        string name = Path.GetFileName(filePath);
        return !string.IsNullOrEmpty(name) && name.IndexOf('.') < 0;
    }

    static void DeleteOutputFile(string filePath)
    {
        string relative = ToAssetsRelativePath(filePath);
        if (!string.IsNullOrEmpty(relative) && relative.StartsWith("Assets/"))
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(relative) != null
                || File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, relative)))
            {
                AssetDatabase.DeleteAsset(relative);
                return;
            }
        }

        DeleteFileAndMeta(filePath);
    }

    static void DeleteFileAndMeta(string filePath)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);

        string metaPath = filePath + ".meta";
        if (File.Exists(metaPath))
            File.Delete(metaPath);
    }

    public static BuildTarget ToBuildTarget(BuildPlatform platform)
    {
        switch (platform)
        {
            case BuildPlatform.iOS:
                return BuildTarget.iOS;
            case BuildPlatform.Android:
                return BuildTarget.Android;
            case BuildPlatform.macOS:
                return BuildTarget.StandaloneOSX;
            case BuildPlatform.WebGL:
                return BuildTarget.WebGL;
            default:
                return BuildTarget.StandaloneWindows64;
        }
    }

    /// <summary>AB 打包前切换到 BuildSetting 目标平台，避免在错误 Active Target 下产出其它平台格式 bundle。</summary>
    public static bool EnsureActiveBuildTarget(BuildTarget target)
    {
        if (EditorUserBuildSettings.activeBuildTarget == target)
            return true;

        BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
        {
            Debug.LogError(
                "无法切换 Active Build Target 到 " + target +
                "（当前 " + EditorUserBuildSettings.activeBuildTarget +
                "）。请在 Build Settings 中切换平台后重试。");
            return false;
        }

        Debug.Log("[BundleBuilder] 已切换 Active Build Target → " + target);
        return true;
    }

    public static string ToAssetsRelativePath(string absPath)
    {
        if (string.IsNullOrEmpty(absPath))
            return null;

        absPath = absPath.Replace("\\", "/");
        string dataPath = Application.dataPath.Replace("\\", "/");

        if (!absPath.StartsWith(dataPath))
            return null;

        return "Assets" + absPath.Substring(dataPath.Length);
    }

    public static string ToAbsoluteAssetsPath(string assetsRelativePath)
    {
        string assetsRoot = Application.dataPath;

        if (string.IsNullOrEmpty(assetsRelativePath))
            return assetsRoot;

        string normalized = assetsRelativePath.Replace("\\", "/");
        string absPath;

        if (normalized == "Assets")
            absPath = assetsRoot;
        else if (normalized.StartsWith("Assets/"))
            absPath = Path.GetFullPath(Path.Combine(assetsRoot, normalized.Substring("Assets/".Length)));
        else if (Path.IsPathRooted(assetsRelativePath))
            absPath = Path.GetFullPath(assetsRelativePath);
        else
            absPath = Path.GetFullPath(Path.Combine(assetsRoot, assetsRelativePath));

        if (Directory.Exists(absPath))
            return absPath;

        string parent = Path.GetDirectoryName(absPath);
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            return parent;

        return assetsRoot;
    }

    const string RuntimeCatalogueFileName = CatalogueWriter.RuntimeCatalogueFileName;

    #endregion
}
