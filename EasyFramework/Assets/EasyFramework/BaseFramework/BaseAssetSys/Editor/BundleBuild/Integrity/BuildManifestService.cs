using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 写入 BuildManifest、diff，并维护 BuildCache 以支持增量打包判断。
/// </summary>
public static class BuildManifestService
{
    public const string ManifestFileName = "BuildManifest.json";
    public const string DiffFileName = "BuildManifest.diff.json";
    public const string CacheFileName = "BuildCache.json";

    public static string GetReportsDir(string bundleRoot)
    {
        return Path.Combine(bundleRoot, BundleBuildAnalyzer.ReportsFolderName);
    }

    public static string GetManifestPath(string bundleRoot)
    {
        return Path.Combine(GetReportsDir(bundleRoot), ManifestFileName);
    }

    public static string GetDiffPath(string bundleRoot)
    {
        return Path.Combine(GetReportsDir(bundleRoot), DiffFileName);
    }

    public static string GetCachePath(string bundleRoot)
    {
        return Path.Combine(GetReportsDir(bundleRoot), CacheFileName);
    }

    public static BuildManifest CreateManifest(
        BuildSetting setting,
        string buildId,
        BuildMode mode,
        BundleCatalogInfo[] bundleInfos,
        string bundlesRoot)
    {
        var entries = new List<BuildManifestBundleEntry>();
        if (bundleInfos != null)
        {
            foreach (BundleCatalogInfo info in bundleInfos)
            {
                if (info == null || string.IsNullOrEmpty(info.bundleName))
                    continue;

                string filePath = ResolveBundleFilePath(bundlesRoot, info.bundleName);
                entries.Add(new BuildManifestBundleEntry
                {
                    bundleName = info.bundleName,
                    sizeBytes = info.sizeBytes,
                    fileHash = info.fileHash,
                    crc32 = info.crc32,
                    resourcePriority = info.resourcePriority
                });
            }
        }

        return new BuildManifest
        {
            buildId = buildId,
            version = setting.version,
            buildNumber = setting.buildNumber,
            platform = setting.platform.ToString(),
            buildMode = mode.ToString(),
            compressionMode = setting.compressionMode.ToString(),
            buildTimeUtc = DateTime.UtcNow.ToString("o"),
            bundles = entries.ToArray()
        };
    }

    public static void WriteManifestAndDiff(string packageRoot, BuildManifest current)
    {
        string reportsDir = GetReportsDir(packageRoot);
        if (!Directory.Exists(reportsDir))
            Directory.CreateDirectory(reportsDir);

        string manifestPath = GetManifestPath(packageRoot);
        BuildManifest previous = TryLoadManifest(manifestPath);

        string json = JsonUtility.ToJson(current, true);
        File.WriteAllText(manifestPath, json);

        string versionDir = BundlePlatformPaths.ResolveVersionDir(packageRoot);
        if (!Directory.Exists(versionDir))
            Directory.CreateDirectory(versionDir);

        File.WriteAllText(Path.Combine(versionDir, BundlePlatformPaths.ManifestFileName), json);

        BuildManifestDiff diff = ComputeDiff(previous, current);
        File.WriteAllText(GetDiffPath(packageRoot), JsonUtility.ToJson(diff, true));
    }

    public static void WriteVersionPackageFiles(
        string packageRoot,
        string packageId,
        BuildSetting setting,
        AssetCatalog catalog,
        BuildManifest manifest)
    {
        if (string.IsNullOrEmpty(packageRoot))
            return;

        string versionDir = BundlePlatformPaths.ResolveVersionDir(packageRoot);
        if (!Directory.Exists(versionDir))
            Directory.CreateDirectory(versionDir);

        var versionInfo = new PackageVersionInfo
        {
            packageId = packageId,
            version = setting?.version ?? catalog?.version,
            buildNumber = setting?.buildNumber ?? catalog?.buildNumber ?? 0,
            platform = setting?.platform.ToString() ?? catalog?.platform,
            buildId = manifest?.buildId ?? catalog?.buildId,
            catalogueHash = catalog?.catalogueHash,
            buildMode = manifest?.buildMode ?? catalog?.buildMode
        };

        File.WriteAllText(
            Path.Combine(versionDir, BundlePlatformPaths.VersionFileName),
            JsonUtility.ToJson(versionInfo, true));
    }

    public static BuildManifestDiff ComputeDiff(BuildManifest previous, BuildManifest current)
    {
        var diff = new BuildManifestDiff
        {
            previousBuildId = previous?.buildId,
            currentBuildId = current?.buildId,
            added = Array.Empty<string>(),
            removed = Array.Empty<string>(),
            changed = Array.Empty<string>(),
            unchanged = Array.Empty<string>()
        };

        if (previous?.bundles == null || previous.bundles.Length == 0)
        {
            diff.added = current?.bundles?.Select(b => b.bundleName).ToArray() ?? Array.Empty<string>();
            return diff;
        }

        var prevMap = new Dictionary<string, BuildManifestBundleEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (BuildManifestBundleEntry e in previous.bundles)
        {
            if (e != null && !string.IsNullOrEmpty(e.bundleName))
                prevMap[e.bundleName] = e;
        }

        var curMap = new Dictionary<string, BuildManifestBundleEntry>(StringComparer.OrdinalIgnoreCase);
        if (current?.bundles != null)
        {
            foreach (BuildManifestBundleEntry e in current.bundles)
            {
                if (e != null && !string.IsNullOrEmpty(e.bundleName))
                    curMap[e.bundleName] = e;
            }
        }

        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<string>();
        var unchanged = new List<string>();

        foreach (string name in curMap.Keys)
        {
            if (!prevMap.ContainsKey(name))
                added.Add(name);
            else if (!string.Equals(prevMap[name].fileHash, curMap[name].fileHash, StringComparison.OrdinalIgnoreCase))
                changed.Add(name);
            else
                unchanged.Add(name);
        }

        foreach (string name in prevMap.Keys)
        {
            if (!curMap.ContainsKey(name))
                removed.Add(name);
        }

        diff.added = added.ToArray();
        diff.removed = removed.ToArray();
        diff.changed = changed.ToArray();
        diff.unchanged = unchanged.ToArray();
        return diff;
    }

    public static bool TryLoadManifest(string path, out BuildManifest manifest)
    {
        manifest = TryLoadManifest(path);
        return manifest != null;
    }

    static BuildManifest TryLoadManifest(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            return JsonUtility.FromJson<BuildManifest>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static bool HasSourceChanges(AssetBundleBuild[] builds, string bundleRoot, out bool cacheMissing)
    {
        cacheMissing = false;
        BuildCacheData cache = TryLoadCache(GetCachePath(bundleRoot));
        if (cache == null || cache.assets == null)
        {
            cacheMissing = true;
            return true;
        }

        var prev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (BuildCacheAssetEntry e in cache.assets)
        {
            if (e != null && !string.IsNullOrEmpty(e.guid))
                prev[e.guid] = e.contentHash ?? string.Empty;
        }

        if (builds == null)
            return false;

        foreach (AssetBundleBuild build in builds)
        {
            if (build.assetNames == null)
                continue;

            foreach (string assetPath in build.assetNames)
            {
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                    continue;

                string hash = AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
                if (!prev.TryGetValue(guid, out string oldHash) || oldHash != hash)
                    return true;
            }
        }

        return false;
    }

    public static void UpdateCache(string bundleRoot, string buildId, AssetBundleBuild[] builds, BundleCatalogInfo[] bundleInfos)
    {
        var assetEntries = new List<BuildCacheAssetEntry>();
        var bundleEntries = new List<BuildCacheBundleEntry>();

        if (builds != null)
        {
            foreach (AssetBundleBuild build in builds)
            {
                if (build.assetNames == null)
                    continue;

                foreach (string assetPath in build.assetNames)
                {
                    string guid = AssetDatabase.AssetPathToGUID(assetPath);
                    if (string.IsNullOrEmpty(guid))
                        continue;

                    assetEntries.Add(new BuildCacheAssetEntry
                    {
                        guid = guid,
                        contentHash = AssetDatabase.GetAssetDependencyHash(assetPath).ToString()
                    });
                }
            }
        }

        if (bundleInfos != null)
        {
            foreach (BundleCatalogInfo info in bundleInfos)
            {
                if (info == null || string.IsNullOrEmpty(info.bundleName))
                    continue;

                bundleEntries.Add(new BuildCacheBundleEntry
                {
                    bundleName = info.bundleName,
                    outputHash = info.fileHash
                });
            }
        }

        var data = new BuildCacheData
        {
            lastBuildId = buildId,
            assets = assetEntries.ToArray(),
            bundles = bundleEntries.ToArray()
        };

        string path = GetCachePath(bundleRoot);
        string dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonUtility.ToJson(data, true));
    }

    public static void DeleteCache(string bundleRoot)
    {
        string path = GetCachePath(bundleRoot);
        if (File.Exists(path))
            File.Delete(path);
    }

    static BuildCacheData TryLoadCache(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonUtility.FromJson<BuildCacheData>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    static string ResolveBundleFilePath(string bundlesRoot, string bundleName)
    {
        if (BundlePlatformPaths.TryResolveBundleFilePath(bundlesRoot, bundleName, out string path))
            return path;

        return Path.Combine(bundlesRoot, BundlePlatformPaths.NormalizeBundleName(bundleName));
    }

    public static void FillBundleIntegrity(string bundlesRoot, BundleCatalogInfo info)
    {
        if (info == null || string.IsNullOrEmpty(info.bundleName))
            return;

        string filePath = ResolveBundleFilePath(bundlesRoot, info.bundleName);
        if (!File.Exists(filePath))
            return;

        var fileInfo = new FileInfo(filePath);
        info.sizeBytes = fileInfo.Length;
        info.fileHash = BuildHashCalculator.ComputeFileSha256(filePath);
        info.crc32 = BuildHashCalculator.ComputeFileCrc32(filePath);
    }
}
