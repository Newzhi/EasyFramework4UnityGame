using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AssetBundle 打包编排：RuleResolver → SharedBundlePlanner → BuildPipeline → Catalogue/Manifest。
/// </summary>
public static class BundleBuildPipeline
{
    public static bool Execute(BuildSetting setting, BundleBuildExecutionMode executionMode = BundleBuildExecutionMode.Incremental)
    {
        if (setting == null)
        {
            Debug.LogError("BuildSetting 为空");
            return false;
        }

        if (!BundleBuilder.Validate(setting))
            return false;

        BuildTarget target = BundleBuilder.ToBuildTarget(setting.platform);
        if (!BundleBuilder.EnsureActiveBuildTarget(target))
            return false;

        StreamingAssetsPlatformIsolation.WarnIfMultiplePlatformFoldersPresent();

        BuildAssetBundleOptions options = BuildAssetBundleOptionsFactory.Resolve(setting, executionMode);

        if (setting.packingRule == PackingRule.Custom)
            return ExecuteCustomGrouped(setting, target, options, executionMode);

        List<AssetBundleBuild> builds = RuleResolver.Resolve(setting);
        if (builds.Count == 0)
        {
            Debug.LogError("没有可打包的内容");
            return false;
        }

        if (!ExecuteSingleMode(setting.buildMode, builds, setting, target, options, executionMode))
            return false;

        AssetDatabase.Refresh();
        Debug.Log("打包完成，bundle 数量: " + builds.Count
            + "，模式: " + executionMode
            + "，输出: " + BundleBuilder.ResolveBundleRoot(setting.buildMode, setting));
        return true;
    }

    static bool ExecuteCustomGrouped(
        BuildSetting setting,
        BuildTarget target,
        BuildAssetBundleOptions options,
        BundleBuildExecutionMode executionMode)
    {
        Dictionary<BuildMode, List<AssetBundleBuild>> grouped =
            RuleResolver.ResolveCustomGrouped(setting.customItems);

        int totalCount = 0;
        bool anyBuild = false;
        bool allSucceeded = true;

        foreach (KeyValuePair<BuildMode, List<AssetBundleBuild>> pair in grouped)
        {
            if (pair.Value.Count == 0)
                continue;

            anyBuild = true;
            totalCount += pair.Value.Count;
            if (!ExecuteSingleMode(pair.Key, pair.Value, setting, target, options, executionMode))
                allSucceeded = false;
        }

        if (!anyBuild)
        {
            Debug.LogError("没有可打包的内容");
            return false;
        }

        AssetDatabase.Refresh();
        if (allSucceeded)
            Debug.Log("自定义打包完成，bundle 数量: " + totalCount + "，模式: " + executionMode);
        else
            Debug.LogError("自定义打包部分失败，bundle 数量: " + totalCount);

        return allSucceeded;
    }

    static bool ExecuteSingleMode(
        BuildMode mode,
        List<AssetBundleBuild> builds,
        BuildSetting setting,
        BuildTarget target,
        BuildAssetBundleOptions options,
        BundleBuildExecutionMode executionMode)
    {
        string packageRoot = BundleBuilder.ResolveBundleRoot(mode, setting);
        string bundlesRoot = BundlePlatformPaths.ResolveBundlesRoot(packageRoot);
        BundleBuilder.EnsureOutputDirectoryPublic(bundlesRoot);
        BundleBuilder.EnsureOutputDirectoryPublic(packageRoot);

        List<SharedBundlePlanner.ImporterRestoreEntry> sharedRestore = null;
        try
        {
            if (SharedBundlePlanner.TryInjectSharedBundle(setting, builds, out _, out sharedRestore)
                && sharedRestore != null && sharedRestore.Count > 0)
            {
                // Importer 已改标签，builds 已注入 shared 包。
            }

            AssetBundleBuild[] buildArray = builds.ToArray();
            bool skipUnityBuild = ShouldSkipUnityBuild(mode, executionMode, buildArray, packageRoot, out string skipReason);
            string buildId = ResolveBuildId(packageRoot, skipUnityBuild);
            Dictionary<string, int> bundlePriorities = BundlePriorityResolver.ResolveBundlePriorities(setting, buildArray);

            bool skipUnityBuildResolved = skipUnityBuild;
            AssetBundleManifest manifest = null;

            if (mode != BuildMode.EditorTest && !skipUnityBuildResolved)
            {
                manifest = BuildPipeline.BuildAssetBundles(bundlesRoot, buildArray, options, target);
                if (manifest == null)
                {
                    Debug.LogError("BuildPipeline.BuildAssetBundles 失败: " + bundlesRoot);
                    return false;
                }

                CatalogueWriter.FinalizeBundlesLayout(bundlesRoot, buildArray);
            }
            else if (skipUnityBuildResolved)
            {
                Debug.Log("[BundleBuildPipeline] 跳过 Unity 构建: " + skipReason);
                manifest = TryLoadManifestFromDisk(bundlesRoot);
            }

            string packageId = BundlePlatformPaths.ResolvePackageId(mode, setting);
            if (!CatalogueWriter.Write(
                    setting,
                    buildArray,
                    packageRoot,
                    bundlesRoot,
                    manifest,
                    buildId,
                    bundlePriorities,
                    mode,
                    packageId))
                return false;

            AssetCatalog catalog = CatalogueWriter.LoadLastBuiltCatalog();
            BuildManifest manifestModel = BuildManifestService.CreateManifest(
                setting, buildId, mode, catalog?.bundles, bundlesRoot);

            BuildManifestService.WriteManifestAndDiff(packageRoot, manifestModel);
            BuildManifestService.WriteVersionPackageFiles(packageRoot, packageId, setting, catalog, manifestModel);

            if (executionMode != BundleBuildExecutionMode.Incremental || !skipUnityBuildResolved)
                BuildManifestService.UpdateCache(packageRoot, buildId, buildArray, catalog?.bundles);

            CopyConfigBytesIfNeeded(setting, packageRoot);

            if (setting.runBuildAnalyzer)
            {
                BundleBuildAnalyzer.AnalyzeAndWriteReport(
                    setting,
                    buildArray,
                    packageRoot,
                    manifest);
            }

            AssetCatalog graphCatalog = CatalogueWriter.LoadLastBuiltCatalog();
            if (graphCatalog != null)
                DependencyGraphWriter.Write(packageRoot, graphCatalog);

            return true;
        }
        finally
        {
            if (sharedRestore != null && sharedRestore.Count > 0)
                SharedBundlePlanner.RestoreImporters(sharedRestore);
        }
    }

    static bool ShouldSkipUnityBuild(
        BuildMode mode,
        BundleBuildExecutionMode executionMode,
        AssetBundleBuild[] builds,
        string bundleRoot,
        out string reason)
    {
        reason = null;

        if (mode == BuildMode.EditorTest)
        {
            reason = "EditorTest 不生成 .bundle";
            return true;
        }

        if (executionMode == BundleBuildExecutionMode.FullOverwrite)
            return false;

        if (BuildManifestService.HasSourceChanges(builds, bundleRoot, out bool cacheMissing))
        {
            if (cacheMissing)
                return false;

            reason = "源资源无变更";
            return true;
        }

        return false;
    }

    static string ResolveBuildId(string bundleRoot, bool skipUnityBuild)
    {
        if (skipUnityBuild)
        {
            string cachePath = BuildManifestService.GetCachePath(bundleRoot);
            if (File.Exists(cachePath))
            {
                try
                {
                    var cache = JsonUtility.FromJson<BuildCacheData>(File.ReadAllText(cachePath));
                    if (cache != null && !string.IsNullOrEmpty(cache.lastBuildId))
                        return cache.lastBuildId;
                }
                catch
                {
                    // fall through
                }
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    static AssetBundleManifest TryLoadManifestFromDisk(string bundlesRoot)
    {
        if (string.IsNullOrEmpty(bundlesRoot) || !Directory.Exists(bundlesRoot))
            return null;

        foreach (string file in Directory.GetFiles(bundlesRoot, "*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file).Length > 0)
                continue;

            AssetBundle manifestBundle = null;
            try
            {
                manifestBundle = AssetBundle.LoadFromFile(file);
                if (manifestBundle == null)
                    continue;

                AssetBundleManifest manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                return manifest;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (manifestBundle != null)
                    manifestBundle.Unload(true);
            }
        }

        return null;
    }

    static void CopyConfigBytesIfNeeded(BuildSetting setting, string packageRoot)
    {
        if (setting == null || string.IsNullOrEmpty(setting.configSourceDirectory))
            return;

        string sourceAbs = BundleBuilder.ToAbsoluteAssetsPath(setting.configSourceDirectory);
        if (!Directory.Exists(sourceAbs))
            return;

        string configDir = BundlePlatformPaths.ResolveConfigDir(packageRoot);
        Directory.CreateDirectory(configDir);

        foreach (string file in Directory.GetFiles(sourceAbs, "*.bytes", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(configDir, Path.GetFileName(file));
            File.Copy(file, dest, true);
        }
    }
}
