using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CatalogueWriter
{
    public const string CatalogueAssetPath = BundlePlatformPaths.ProjectCatalogueRelativePath;
    public const string RuntimeCatalogueFileName = BundlePlatformPaths.CatalogFileName;

    static AssetCatalog lastBuiltCatalog;

    /// <summary>供 Pipeline 写 Manifest 时读取刚构建的 bundles[] 完整性字段。</summary>
    public static AssetCatalog LoadLastBuiltCatalog()
    {
        return lastBuiltCatalog;
    }

    public static bool Write(
        BuildSetting setting,
        AssetBundleBuild[] builds,
        string bundleRoot,
        AssetBundleManifest manifest = null)
    {
        return Write(setting, builds, bundleRoot, manifest, Guid.NewGuid().ToString("N"), null, setting.buildMode);
    }

    public static bool Write(
        BuildSetting setting,
        AssetBundleBuild[] builds,
        string packageRoot,
        string bundlesRoot,
        AssetBundleManifest manifest,
        string buildId,
        Dictionary<string, int> bundlePriorities,
        BuildMode modeOverride,
        string packageId)
    {
        if (!TryBuildCatalog(
                setting,
                builds,
                bundlesRoot,
                manifest,
                buildId,
                bundlePriorities,
                modeOverride,
                out AssetCatalog catalog,
                out string errorMessage))
        {
            Debug.LogError("Catalogue write failed: " + errorMessage);
            return false;
        }

        lastBuiltCatalog = catalog;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string catalogueFullPath = Path.GetFullPath(Path.Combine(projectRoot, CatalogueAssetPath));
        if (!AssetCatalogBinaryCodec.WriteToFile(catalogueFullPath, catalog))
        {
            Debug.LogError("Catalogue write failed: project catalogue");
            return false;
        }

        string versionDir = BundlePlatformPaths.ResolveVersionDir(packageRoot);
        if (!Directory.Exists(versionDir))
            Directory.CreateDirectory(versionDir);

        string runtimeCatalogPath = Path.Combine(versionDir, BundlePlatformPaths.CatalogFileName);
        if (!AssetCatalogBinaryCodec.WriteToFile(runtimeCatalogPath, catalog))
        {
            Debug.LogError("Catalogue write failed: runtime catalogue");
            return false;
        }

        if (modeOverride == BuildMode.DlcPackage)
        {
            string fragmentPath = Path.Combine(versionDir, BundlePlatformPaths.CatalogFragmentFileName);
            if (!AssetCatalogBinaryCodec.WriteToFile(fragmentPath, catalog))
            {
                Debug.LogError("Catalogue write failed: DLC fragment");
                return false;
            }
        }

        AssetDatabase.Refresh();
        return true;
    }

    public static bool Write(
        BuildSetting setting,
        AssetBundleBuild[] builds,
        string bundleRoot,
        AssetBundleManifest manifest,
        string buildId,
        Dictionary<string, int> bundlePriorities,
        BuildMode modeOverride)
    {
        string bundlesRoot = BundlePlatformPaths.ResolveBundlesRoot(bundleRoot);
        string packageId = BundlePlatformPaths.ResolvePackageId(modeOverride, setting);
        return Write(setting, builds, bundleRoot, bundlesRoot, manifest, buildId, bundlePriorities, modeOverride, packageId);
    }

    /// <summary>将 Bundles 根目录下 flat 产物按 catalog 相对路径归位到子文件夹。</summary>
    public static void FinalizeBundlesLayout(string bundlesRoot, AssetBundleBuild[] builds)
    {
        if (string.IsNullOrEmpty(bundlesRoot) || builds == null || !Directory.Exists(bundlesRoot))
            return;

        foreach (AssetBundleBuild build in builds)
        {
            string bundleName = BundlePlatformPaths.NormalizeBundleName(build.assetBundleName);
            if (string.IsNullOrEmpty(bundleName) || bundleName.IndexOf('/') < 0)
                continue;

            string dest = Path.Combine(
                bundlesRoot,
                bundleName.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(dest))
                continue;

            string flatName = Path.GetFileName(bundleName);
            string flatPath = Path.Combine(bundlesRoot, flatName);
            if (!File.Exists(flatPath))
                continue;

            string destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Move(flatPath, dest);
            MoveSiblingIfExists(flatPath + ".manifest", dest + ".manifest");
            MoveSiblingIfExists(flatPath + ".meta", dest + ".meta");
        }
    }

    static void MoveSiblingIfExists(string source, string dest)
    {
        if (!File.Exists(source))
            return;

        string destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        if (File.Exists(dest))
            File.Delete(dest);

        File.Move(source, dest);
    }

    public static bool TryBuildCatalog(
        BuildSetting setting,
        AssetBundleBuild[] builds,
        string bundleRoot,
        AssetBundleManifest manifest,
        out AssetCatalog catalog,
        out string errorMessage)
    {
        return TryBuildCatalog(
            setting,
            builds,
            bundleRoot,
            manifest,
            Guid.NewGuid().ToString("N"),
            null,
            setting != null ? setting.buildMode : BuildMode.DeviceDebug,
            out catalog,
            out errorMessage);
    }

    public static bool TryBuildCatalog(
        BuildSetting setting,
        AssetBundleBuild[] builds,
        string bundleRoot,
        AssetBundleManifest manifest,
        string buildId,
        Dictionary<string, int> bundlePriorities,
        BuildMode modeOverride,
        out AssetCatalog catalog,
        out string errorMessage)
    {
        catalog = null;
        errorMessage = null;

        if (setting == null)
        {
            errorMessage = "BuildSetting is null.";
            return false;
        }

        List<AssetCatalogEntry> entries = new List<AssetCatalogEntry>();

        if (builds != null)
        {
            foreach (AssetBundleBuild build in builds)
            {
                foreach (string assetPath in build.assetNames)
                {
                    entries.Add(new AssetCatalogEntry
                    {
                        assetPath = assetPath,
                        bundleName = BundlePlatformPaths.NormalizeBundleName(build.assetBundleName),
                        assetName = Path.GetFileNameWithoutExtension(assetPath)
                    });
                }
            }
        }

        CatalogueValidator.ValidationResult validation = CatalogueValidator.ValidateEntries(
            entries,
            setting.targetDirectory,
            setting.loadPathDuplicateAsError);

        foreach (CatalogueValidator.LoadPathDuplicate duplicate in validation.loadPathDuplicates)
        {
            string message = "Duplicate loadPath '" + duplicate.loadPath + "' for assets: "
                + duplicate.firstAssetPath + " and " + duplicate.secondAssetPath;

            if (setting.loadPathDuplicateAsError)
                Debug.LogError(message);
            else
                Debug.LogWarning(message);
        }

        if (validation.hasErrors)
        {
            errorMessage = "Catalogue validation failed due to duplicate loadPath entries.";
            return false;
        }

        if (!TryBuildBundleDependencies(
                setting,
                manifest,
                builds,
                setting.useTopologicalSort,
                bundlePriorities,
                out BundleCatalogInfo[] bundles,
                out errorMessage))
            return false;

        foreach (BundleCatalogInfo info in bundles)
            BuildManifestService.FillBundleIntegrity(bundleRoot, info);

        catalog = new AssetCatalog
        {
            version = setting.version,
            buildNumber = setting.buildNumber,
            platform = setting.platform.ToString(),
            buildMode = modeOverride.ToString(),
            packingRule = setting.packingRule.ToString(),
            bundleRoot = bundleRoot,
            resourceRoot = setting.targetDirectory,
            buildId = buildId,
            compressionMode = setting.compressionMode.ToString(),
            cdnBaseUrl = setting.cdnBaseUrl ?? string.Empty,
            entries = entries.ToArray(),
            bundles = bundles
        };

        catalog.catalogueHash = AssetCatalogBinaryCodec.ComputeCatalogueHash(catalog);

        return true;
    }

    static bool TryBuildBundleDependencies(
        BuildSetting setting,
        AssetBundleManifest manifest,
        AssetBundleBuild[] builds,
        bool useTopologicalSort,
        Dictionary<string, int> bundlePriorities,
        out BundleCatalogInfo[] bundles,
        out string errorMessage)
    {
        bundles = new BundleCatalogInfo[0];
        errorMessage = null;

        if (manifest == null || builds == null || builds.Length == 0)
            return true;

        Dictionary<string, List<string>> directGraph = BuildDirectDependencyGraph(manifest, builds);
        List<BundleCatalogInfo> bundleList = new List<BundleCatalogInfo>();

        bool directOnly = setting != null && setting.useDirectDependenciesOnly;

        foreach (AssetBundleBuild build in builds)
        {
            string bundleName = BundlePlatformPaths.NormalizeBundleName(build.assetBundleName);
            HashSet<string> allDepSet = CollectAllDependencies(manifest, build.assetBundleName, bundleName);
            HashSet<string> directDepSet = directOnly
                ? CollectDirectDependencies(manifest, build.assetBundleName, bundleName)
                : allDepSet;

            string[] depNames;
            string[] depAllNames = null;

            if (useTopologicalSort)
            {
                var closure = new HashSet<string>(directDepSet, StringComparer.OrdinalIgnoreCase);
                if (!BundleDependencyTopology.TryTopologicalSort(closure, directGraph, out depNames, out string cycleHint))
                {
                    errorMessage = "Dependency cycle detected for bundle " + bundleName
                        + (string.IsNullOrEmpty(cycleHint) ? "" : " near " + cycleHint);
                    return false;
                }

                if (!directOnly && !BundleDependencyTopology.SetsEqual(depNames, allDepSet))
                {
                    errorMessage = "Topological sort changed dependency set for bundle: " + bundleName;
                    return false;
                }

                if (directOnly)
                {
                    var allClosure = new HashSet<string>(allDepSet, StringComparer.OrdinalIgnoreCase);
                    if (!BundleDependencyTopology.TryTopologicalSort(allClosure, directGraph, out depAllNames, out cycleHint))
                        depAllNames = allDepSet.ToArray();
                }
            }
            else
            {
                depNames = directDepSet.ToArray();
                if (directOnly)
                    depAllNames = allDepSet.ToArray();
            }

            int priority = (int)ResourcePriority.Normal;
            if (bundlePriorities != null && bundlePriorities.TryGetValue(bundleName, out int resolved))
                priority = resolved;

            var info = new BundleCatalogInfo
            {
                bundleName = bundleName,
                dependencies = depNames,
                resourcePriority = priority
            };

            if (directOnly && depAllNames != null)
                info.dependenciesAll = depAllNames;

            bundleList.Add(info);
        }

        bundles = bundleList.ToArray();
        return true;
    }

    static Dictionary<string, List<string>> BuildDirectDependencyGraph(AssetBundleManifest manifest, AssetBundleBuild[] builds)
    {
        List<(string bundleName, string[] directDependencies)> rows =
            new List<(string, string[])>();

        foreach (AssetBundleBuild build in builds)
        {
            string[] direct = manifest.GetDirectDependencies(build.assetBundleName);
            rows.Add((build.assetBundleName, direct));
        }

        return BundleDependencyTopology.CreateDirectDependencyGraph(rows);
    }

    static HashSet<string> CollectAllDependencies(AssetBundleManifest manifest, string rawBundleName, string normalizedBundleName)
    {
        return CollectDependencies(manifest, rawBundleName, normalizedBundleName, allDependencies: true);
    }

    static HashSet<string> CollectDirectDependencies(AssetBundleManifest manifest, string rawBundleName, string normalizedBundleName)
    {
        return CollectDependencies(manifest, rawBundleName, normalizedBundleName, allDependencies: false);
    }

    static HashSet<string> CollectDependencies(
        AssetBundleManifest manifest,
        string rawBundleName,
        string normalizedBundleName,
        bool allDependencies)
    {
        HashSet<string> depSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] deps = allDependencies
            ? manifest.GetAllDependencies(rawBundleName)
            : manifest.GetDirectDependencies(rawBundleName);

        foreach (string dep in deps)
        {
            if (dep == rawBundleName)
                continue;

            string depFileName = BundlePlatformPaths.NormalizeBundleName(dep.Replace("\\", "/"));
            if (string.IsNullOrEmpty(depFileName))
                continue;

            if (string.Equals(depFileName, normalizedBundleName, StringComparison.OrdinalIgnoreCase))
                continue;

            depSet.Add(depFileName);
        }

        return depSet;
    }
}
