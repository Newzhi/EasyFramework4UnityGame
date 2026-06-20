using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class BundleBuildAnalyzer
{
    public const string ReportFileName = "BundleBuildReport.json";
    public const string ReportsFolderName = "Reports";

    public static string GetReportPath(string bundleRoot)
    {
        return Path.Combine(bundleRoot, ReportsFolderName, ReportFileName);
    }

    public static bool TryLoadReport(string bundleRoot, out BundleBuildReport report)
    {
        report = null;
        if (string.IsNullOrEmpty(bundleRoot))
            return false;

        string path = GetReportPath(bundleRoot);
        if (!File.Exists(path))
            return false;

        try
        {
            report = JsonUtility.FromJson<BundleBuildReport>(File.ReadAllText(path));
            return report != null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BundleBuildReport read failed: " + path + " | " + ex.Message);
            return false;
        }
    }

    public static void AnalyzeAndWriteReport(
        BuildSetting setting,
        AssetBundleBuild[] builds,
        string bundleRoot,
        AssetBundleManifest manifest = null)
    {
        if (setting == null || string.IsNullOrEmpty(bundleRoot))
            return;

        var stopwatch = Stopwatch.StartNew();
        BundleBuildReport report = Analyze(setting, builds, bundleRoot, manifest);
        stopwatch.Stop();
        report.buildTimeSeconds = stopwatch.Elapsed.TotalSeconds;

        string reportsDir = Path.Combine(bundleRoot, ReportsFolderName);
        if (!Directory.Exists(reportsDir))
            Directory.CreateDirectory(reportsDir);

        string json = JsonUtility.ToJson(report, true);
        File.WriteAllText(GetReportPath(bundleRoot), json);
        Debug.Log("Bundle build report written: " + GetReportPath(bundleRoot));
    }

    public static BundleBuildReport Analyze(
        BuildSetting setting,
        AssetBundleBuild[] builds,
        string bundleRoot,
        AssetBundleManifest manifest = null)
    {
        var report = new BundleBuildReport
        {
            platform = setting.platform.ToString(),
            buildMode = setting.buildMode.ToString(),
            bundleSizes = Array.Empty<BundleSizeEntry>(),
            redundantAssets = Array.Empty<RedundantAssetEntry>(),
            crossBundleEdges = Array.Empty<CrossBundleEdgeEntry>(),
            loadPathDuplicates = Array.Empty<LoadPathDuplicateEntry>()
        };

        if (builds == null || builds.Length == 0)
            return report;

        report.bundleCount = builds.Length;
        report.bundleSizes = CollectBundleSizes(builds, bundleRoot);

        Dictionary<string, string> assetToBundle = BuildAssetToBundleMap(builds);
        List<AssetCatalogEntry> entries = BuildEntriesFromBuilds(builds);
        CatalogueValidator.ValidationResult validation = CatalogueValidator.ValidateEntries(
            entries,
            setting.targetDirectory,
            setting.loadPathDuplicateAsError);
        report.loadPathDuplicates = validation.loadPathDuplicates
            .Select(d => new LoadPathDuplicateEntry
            {
                loadPath = d.loadPath,
                firstAssetPath = d.firstAssetPath,
                secondAssetPath = d.secondAssetPath
            })
            .ToArray();

        AnalyzeCrossBundleReferences(builds, assetToBundle, out RedundantAssetEntry[] redundant, out CrossBundleEdgeEntry[] edges);
        report.redundantAssets = redundant;
        report.crossBundleEdges = edges;

        return report;
    }

    static List<AssetCatalogEntry> BuildEntriesFromBuilds(AssetBundleBuild[] builds)
    {
        var entries = new List<AssetCatalogEntry>();
        foreach (AssetBundleBuild build in builds)
        {
            string bundleName = BundlePlatformPaths.NormalizeBundleName(build.assetBundleName);
            foreach (string assetPath in build.assetNames)
            {
                entries.Add(new AssetCatalogEntry
                {
                    assetPath = assetPath,
                    bundleName = bundleName,
                    assetName = Path.GetFileNameWithoutExtension(assetPath)
                });
            }
        }

        return entries;
    }

    static Dictionary<string, string> BuildAssetToBundleMap(AssetBundleBuild[] builds)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetBundleBuild build in builds)
        {
            string bundleName = BundlePlatformPaths.NormalizeBundleName(build.assetBundleName);
            foreach (string assetPath in build.assetNames)
            {
                string normalized = CatalogueReader.NormalizePath(assetPath);
                map[normalized] = bundleName;
            }
        }

        return map;
    }

    static void AnalyzeCrossBundleReferences(
        AssetBundleBuild[] builds,
        Dictionary<string, string> assetToBundle,
        out RedundantAssetEntry[] redundantAssets,
        out CrossBundleEdgeEntry[] crossBundleEdges)
    {
        var consumerByDependency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var edgeList = new List<CrossBundleEdgeEntry>();

        foreach (AssetBundleBuild build in builds)
        {
            string consumerBundle = BundlePlatformPaths.NormalizeBundleName(build.assetBundleName);
            foreach (string primaryAsset in build.assetNames)
            {
                string[] deps = AssetDatabase.GetDependencies(primaryAsset, true);
                foreach (string depPath in deps)
                {
                    if (!IsAnalyzableDependency(depPath))
                        continue;

                    string normalizedDep = CatalogueReader.NormalizePath(depPath);
                    if (string.Equals(normalizedDep, CatalogueReader.NormalizePath(primaryAsset), StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!assetToBundle.TryGetValue(normalizedDep, out string providerBundle))
                        continue;

                    if (string.Equals(providerBundle, consumerBundle, StringComparison.OrdinalIgnoreCase))
                        continue;

                    edgeList.Add(new CrossBundleEdgeEntry
                    {
                        consumerBundle = consumerBundle,
                        providerBundle = providerBundle,
                        dependencyAssetPath = normalizedDep
                    });

                    if (!consumerByDependency.TryGetValue(normalizedDep, out HashSet<string> consumers))
                    {
                        consumers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        consumerByDependency[normalizedDep] = consumers;
                    }

                    consumers.Add(consumerBundle);
                }
            }
        }

        var redundantList = new List<RedundantAssetEntry>();
        foreach (KeyValuePair<string, HashSet<string>> pair in consumerByDependency)
        {
            if (pair.Value.Count < 2)
                continue;

            redundantList.Add(new RedundantAssetEntry
            {
                assetPath = pair.Key,
                referencedByBundles = pair.Value.OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToArray(),
                suggestion = "Consider shared bundle or move asset to common folder rule"
            });
        }

        redundantAssets = redundantList
            .OrderByDescending(r => r.referencedByBundles?.Length ?? 0)
            .ThenBy(r => r.assetPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        crossBundleEdges = edgeList
            .GroupBy(e => e.consumerBundle + "|" + e.providerBundle + "|" + e.dependencyAssetPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.consumerBundle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.providerBundle, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static bool IsAnalyzableDependency(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;

        if (assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return false;

        if (assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (assetPath.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
            return false;

        return assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
    }

    static BundleSizeEntry[] CollectBundleSizes(AssetBundleBuild[] builds, string bundleRoot)
    {
        var sizes = new List<BundleSizeEntry>();
        foreach (AssetBundleBuild build in builds)
        {
            string bundleName = BundlePlatformPaths.NormalizeBundleName(build.assetBundleName);
            long bytes = 0;
            string path = Path.Combine(bundleRoot, bundleName);
            if (File.Exists(path))
            {
                bytes = new FileInfo(path).Length;
            }
            else if (Directory.Exists(bundleRoot))
            {
                foreach (string file in Directory.GetFiles(bundleRoot, "*.bundle"))
                {
                    if (string.Equals(Path.GetFileName(file), bundleName, StringComparison.OrdinalIgnoreCase))
                    {
                        bytes = new FileInfo(file).Length;
                        break;
                    }
                }
            }

            sizes.Add(new BundleSizeEntry
            {
                bundleName = bundleName,
                bytes = bytes
            });
        }

        return sizes
            .OrderByDescending(s => s.bytes)
            .ThenBy(s => s.bundleName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
