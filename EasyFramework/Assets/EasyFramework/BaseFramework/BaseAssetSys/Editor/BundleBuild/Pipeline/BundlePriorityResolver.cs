using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 将资产路径映射为 Bundle 级 <see cref="ResourcePriority"/>（取包内最高优先级 = 最小整型值）。
/// </summary>
public static class BundlePriorityResolver
{
    public static Dictionary<string, int> ResolveBundlePriorities(BuildSetting setting, AssetBundleBuild[] builds)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (builds == null || builds.Length == 0)
            return result;

        Dictionary<string, int> assetPriority = BuildAssetPriorityMap(setting);

        foreach (AssetBundleBuild build in builds)
        {
            string bundleName = BundlePlatformPaths.NormalizeBundleName(build.assetBundleName);
            int bundlePriority = (int)ResourcePriority.Normal;

            if (build.assetNames != null)
            {
                foreach (string assetPath in build.assetNames)
                {
                    if (string.IsNullOrEmpty(assetPath))
                        continue;

                    int priority = assetPriority.TryGetValue(assetPath, out int p)
                        ? p
                        : setting.packingRule == PackingRule.Custom
                            ? (int)ResourcePriority.Normal
                            : (int)setting.defaultBundlePriority;

                    if (priority < bundlePriority)
                        bundlePriority = priority;
                }
            }

            result[bundleName] = bundlePriority;
        }

        return result;
    }

    static Dictionary<string, int> BuildAssetPriorityMap(BuildSetting setting)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (setting == null)
            return map;

        int defaultPriority = (int)setting.defaultBundlePriority;

        if (setting.packingRule == PackingRule.Custom && setting.customItems != null)
        {
            foreach (BundleConfigItem item in setting.customItems)
            {
                if (item == null || string.IsNullOrEmpty(item.assetPath))
                    continue;

                int itemPriority = (int)item.resourcePriority;

                if (AssetDatabase.IsValidFolder(item.assetPath))
                {
                    foreach (string path in BundleBuilder.CollectAssetPaths(item.assetPath))
                        map[path] = itemPriority;
                }
                else if (AssetDatabase.LoadMainAssetAtPath(item.assetPath) != null)
                {
                    map[item.assetPath] = itemPriority;
                }
            }

            return map;
        }

        if (!string.IsNullOrEmpty(setting.targetDirectory) && AssetDatabase.IsValidFolder(setting.targetDirectory))
        {
            foreach (string path in BundleBuilder.CollectAssetPaths(setting.targetDirectory))
                map[path] = defaultPriority;
        }

        return map;
    }
}
