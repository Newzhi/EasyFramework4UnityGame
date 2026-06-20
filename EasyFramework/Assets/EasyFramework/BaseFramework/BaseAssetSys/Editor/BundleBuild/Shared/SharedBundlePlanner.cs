using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 全自动公共依赖包规划：将跨包引用 ≥ 阈值的资产抽到 shared_auto.bundle，构建后恢复 Importer 标签。
/// </summary>
public static class SharedBundlePlanner
{
    /// <summary>被移入公共包前的 Importer 状态，用于 finally 恢复。</summary>
    public sealed class ImporterRestoreEntry
    {
        /// <summary>Unity 资产路径</summary>
        public string assetPath;

        /// <summary>原 assetBundleName</summary>
        public string bundleName;

        /// <summary>原 assetBundleVariant</summary>
        public string variant;
    }

    /// <summary>
    /// 分析并改写 builds：候选资产改 Importer 标签、从原包移除并注入公共包条目。
    /// </summary>
    /// <param name="restoreList">输出待恢复的 Importer 列表，由 Pipeline 在 finally 中恢复。</param>
    /// <returns>是否注入了公共包。</returns>
    public static bool TryInjectSharedBundle(
        BuildSetting setting,
        List<AssetBundleBuild> builds,
        out string[] sharedAssetPaths,
        out List<ImporterRestoreEntry> restoreList)
    {
        sharedAssetPaths = Array.Empty<string>();
        restoreList = new List<ImporterRestoreEntry>();

        if (setting == null || builds == null || builds.Count == 0 || !setting.enableAutoSharedBundle)
            return false;

        string sharedBundleName = BundlePlatformPaths.NormalizeBundleName(
            string.IsNullOrEmpty(setting.sharedBundleName)
                ? "shared_auto.bundle"
                : setting.sharedBundleName);

        int minRef = Mathf.Max(2, setting.sharedBundleMinRefCount);
        HashSet<string> candidates = CollectSharedCandidates(builds, minRef);
        if (candidates.Count == 0)
            return false;

        sharedAssetPaths = candidates.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (string assetPath in sharedAssetPaths)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
                continue;

            restoreList.Add(new ImporterRestoreEntry
            {
                assetPath = assetPath,
                bundleName = importer.assetBundleName,
                variant = importer.assetBundleVariant
            });

            importer.assetBundleName = sharedBundleName;
            importer.assetBundleVariant = string.Empty;
            importer.SaveAndReimport();
        }

        RemoveAssetsFromBuilds(builds, candidates);
        builds.Add(new AssetBundleBuild
        {
            assetBundleName = sharedBundleName,
            assetNames = sharedAssetPaths
        });

        Debug.Log("[SharedBundlePlanner] Injected " + sharedAssetPaths.Length
            + " assets into " + sharedBundleName);
        return true;
    }

    /// <summary>构建结束后恢复 Importer 上的 assetBundleName。</summary>
    public static void RestoreImporters(IReadOnlyList<ImporterRestoreEntry> restoreList)
    {
        if (restoreList == null)
            return;

        foreach (ImporterRestoreEntry entry in restoreList)
        {
            if (entry == null || string.IsNullOrEmpty(entry.assetPath))
                continue;

            AssetImporter importer = AssetImporter.GetAtPath(entry.assetPath);
            if (importer == null)
                continue;

            importer.assetBundleName = entry.bundleName ?? string.Empty;
            importer.assetBundleVariant = entry.variant ?? string.Empty;
            importer.SaveAndReimport();
        }
    }

    static HashSet<string> CollectSharedCandidates(List<AssetBundleBuild> builds, int minRefCount)
    {
        var assetToBundle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetBundleBuild build in builds)
        {
            string bundleName = BundlePlatformPaths.NormalizeBundleName(build.assetBundleName);
            if (build.assetNames == null)
                continue;

            foreach (string assetPath in build.assetNames)
            {
                if (!string.IsNullOrEmpty(assetPath))
                    assetToBundle[CatalogueReader.NormalizePath(assetPath)] = bundleName;
            }
        }

        var consumerByDependency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (AssetBundleBuild build in builds)
        {
            string consumerBundle = BundlePlatformPaths.NormalizeBundleName(build.assetBundleName);
            if (build.assetNames == null)
                continue;

            foreach (string primaryAsset in build.assetNames)
            {
                string[] deps = AssetDatabase.GetDependencies(primaryAsset, true);
                foreach (string depPath in deps)
                {
                    if (!IsSharedCandidateDependency(depPath))
                        continue;

                    string normalizedDep = CatalogueReader.NormalizePath(depPath);
                    if (string.Equals(normalizedDep, CatalogueReader.NormalizePath(primaryAsset), StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!assetToBundle.TryGetValue(normalizedDep, out string providerBundle))
                        continue;

                    if (string.Equals(providerBundle, consumerBundle, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!consumerByDependency.TryGetValue(normalizedDep, out HashSet<string> consumers))
                    {
                        consumers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        consumerByDependency[normalizedDep] = consumers;
                    }

                    consumers.Add(consumerBundle);
                }
            }
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, HashSet<string>> pair in consumerByDependency)
        {
            if (pair.Value.Count >= minRefCount)
                candidates.Add(pair.Key);
        }

        return candidates;
    }

    static bool IsSharedCandidateDependency(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;

        if (assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return false;

        if (assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            return false;

        return assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
    }

    static void RemoveAssetsFromBuilds(List<AssetBundleBuild> builds, HashSet<string> removeSet)
    {
        for (int i = 0; i < builds.Count; i++)
        {
            AssetBundleBuild build = builds[i];
            if (build.assetNames == null || build.assetNames.Length == 0)
                continue;

            List<string> kept = build.assetNames
                .Where(p => !removeSet.Contains(CatalogueReader.NormalizePath(p)))
                .ToList();

            if (kept.Count == build.assetNames.Length)
                continue;

            if (kept.Count == 0)
            {
                builds.RemoveAt(i);
                i--;
                continue;
            }

            build.assetNames = kept.ToArray();
            builds[i] = build;
        }
    }
}
