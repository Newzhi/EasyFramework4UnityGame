using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class RuleResolver
{
    public const string BundleSuffix = ".bundle";

    public static List<AssetBundleBuild> Resolve(BuildSetting setting)
    {
        switch (setting.packingRule)
        {
            case PackingRule.Detailed:
                return ResolveDetailed(setting.targetDirectory);
            case PackingRule.Custom:
                return ResolveCustom(setting.customItems);
            default:
                return ResolveDefault(setting);
        }
    }

    public static Dictionary<BuildMode, List<AssetBundleBuild>> ResolveCustomGrouped(List<BundleConfigItem> items)
    {
        Dictionary<BuildMode, List<AssetBundleBuild>> grouped = new Dictionary<BuildMode, List<AssetBundleBuild>>
        {
            { BuildMode.EditorTest, new List<AssetBundleBuild>() },
            { BuildMode.DeviceDebug, new List<AssetBundleBuild>() },
            { BuildMode.CdnHotUpdate, new List<AssetBundleBuild>() },
            { BuildMode.DlcPackage, new List<AssetBundleBuild>() },
        };

        if (items == null)
            return grouped;

        foreach (BundleConfigItem item in items)
        {
            List<AssetBundleBuild> targetList = grouped[item.buildMode];
            AddCustomItemBuilds(item, targetList);
        }

        return grouped;
    }

    public static List<AssetBundleBuild> ResolveDefault(BuildSetting setting)
    {
        List<AssetBundleBuild> builds = new List<AssetBundleBuild>();
        AddFirstLevelSubfolderBuilds(setting?.targetDirectory, builds, setting);
        return builds;
    }

    public static List<AssetBundleBuild> ResolveDetailed(string targetFolder)
    {
        List<AssetBundleBuild> builds = new List<AssetBundleBuild>();
        AddAllSubfolderBuilds(targetFolder, builds);
        return builds;
    }

    public static List<AssetBundleBuild> ResolveCustom(List<BundleConfigItem> items)
    {
        List<AssetBundleBuild> builds = new List<AssetBundleBuild>();

        if (items == null)
            return builds;

        foreach (BundleConfigItem item in items)
            AddCustomItemBuilds(item, builds);

        return builds;
    }

    static void AddCustomItemBuilds(BundleConfigItem item, List<AssetBundleBuild> builds)
    {
        if (string.IsNullOrEmpty(item.assetPath))
            return;

        if (AssetDatabase.IsValidFolder(item.assetPath))
        {
            switch (item.folderPackingRule)
            {
                case BundleFolderRule.FirstLevelSubfolders:
                    AddFirstLevelSubfolderBuilds(item.assetPath, builds, null);
                    return;
                case BundleFolderRule.AllSubfolders:
                    AddAllSubfolderBuilds(item.assetPath, builds);
                    return;
            }

            if (string.IsNullOrEmpty(item.bundleName))
                return;

            string entireBundleName = NormalizeBundleName(item.bundleName);
            TryAddFolderBuild(item.assetPath, entireBundleName, builds);
            return;
        }

        if (string.IsNullOrEmpty(item.bundleName))
            return;

        if (AssetDatabase.LoadMainAssetAtPath(item.assetPath) != null)
        {
            builds.Add(new AssetBundleBuild
            {
                assetBundleName = NormalizeBundleName(item.bundleName),
                assetNames = new[] { item.assetPath }
            });
        }
    }

    static void AddFirstLevelSubfolderBuilds(string targetFolder, List<AssetBundleBuild> builds, BuildSetting setting)
    {
        if (!AssetDatabase.IsValidFolder(targetFolder))
            return;

        foreach (string subFolder in AssetDatabase.GetSubFolders(targetFolder))
        {
            string folderName = Path.GetFileName(subFolder);
            string category = ResolveCategoryFolder(setting, folderName);
            string shortName = folderName.ToLowerInvariant() + BundleSuffix;
            string bundleName = string.IsNullOrEmpty(category)
                ? shortName
                : category.ToLowerInvariant() + "/" + shortName;
            TryAddFolderBuild(subFolder, bundleName, builds);
        }
    }

    static string ResolveCategoryFolder(BuildSetting setting, string sourceFolderName)
    {
        if (setting?.bundleCategoryMappings == null || string.IsNullOrEmpty(sourceFolderName))
            return sourceFolderName.ToLowerInvariant();

        foreach (BundleCategoryMapping map in setting.bundleCategoryMappings)
        {
            if (map == null || string.IsNullOrEmpty(map.sourceFolderName))
                continue;

            if (string.Equals(map.sourceFolderName, sourceFolderName, System.StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(map.categoryFolder))
                return map.categoryFolder;
        }

        return sourceFolderName.ToLowerInvariant();
    }

    static void AddAllSubfolderBuilds(string targetFolder, List<AssetBundleBuild> builds)
    {
        if (!AssetDatabase.IsValidFolder(targetFolder))
            return;

        List<string> folders = new List<string>();
        CollectAllSubFolders(targetFolder, folders);

        foreach (string folder in folders)
        {
            string relative = folder.Substring(targetFolder.Length).TrimStart('/');
            string bundleName = BundlePlatformPaths.NormalizeBundleName(
                string.IsNullOrEmpty(relative)
                    ? Path.GetFileName(folder) + BundleSuffix
                    : relative.Replace("/", "_") + BundleSuffix);

            TryAddFolderBuild(folder, bundleName, builds);
        }
    }

    static string NormalizeBundleName(string bundleName)
    {
        return BundlePlatformPaths.NormalizeBundleName(
            bundleName.EndsWith(BundleSuffix)
                ? bundleName
                : bundleName + BundleSuffix);
    }

    static void CollectAllSubFolders(string folder, List<string> result)
    {
        result.Add(folder);
        foreach (string sub in AssetDatabase.GetSubFolders(folder))
            CollectAllSubFolders(sub, result);
    }

    static void TryAddFolderBuild(string folder, string bundleName, List<AssetBundleBuild> builds)
    {
        string[] assetPaths = BundleBuilder.CollectAssetPaths(folder);
        if (assetPaths.Length == 0)
            return;

        builds.Add(new AssetBundleBuild
        {
            assetBundleName = BundlePlatformPaths.NormalizeBundleName(bundleName),
            assetNames = assetPaths
        });
    }
}
