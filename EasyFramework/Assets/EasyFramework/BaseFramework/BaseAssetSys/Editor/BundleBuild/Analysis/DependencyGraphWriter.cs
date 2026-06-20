using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 构建后写入 Reports/DependencyGraph.json（节点=bundle，边=直接/反向/传递闭包）。
/// </summary>
public static class DependencyGraphWriter
{
    public const string GraphFileName = "DependencyGraph.json";

    [Serializable]
    public class DependencyGraphDocument
    {
        public string buildId;
        public string platform;
        public string buildMode;
        public DependencyGraphNode[] nodes;
    }

    [Serializable]
    public class DependencyGraphNode
    {
        public string bundleName;
        public string[] directDependencies;
        public string[] reverseDependencies;
        public string[] transitiveClosure;
    }

    public static string GetGraphPath(string bundleRoot)
    {
        return Path.Combine(bundleRoot, BundleBuildAnalyzer.ReportsFolderName, GraphFileName);
    }

    public static bool Write(string bundleRoot, AssetCatalog catalog)
    {
        if (string.IsNullOrEmpty(bundleRoot) || catalog?.bundles == null || catalog.bundles.Length == 0)
            return false;

        var reverseMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var directMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (BundleCatalogInfo info in catalog.bundles)
        {
            if (info == null || string.IsNullOrEmpty(info.bundleName))
                continue;

            string name = BundlePlatformPaths.NormalizeBundleName(info.bundleName);
            string[] direct = info.dependenciesAll != null && info.dependenciesAll.Length > 0
                ? info.dependenciesAll
                : info.dependencies ?? Array.Empty<string>();
            directMap[name] = direct;

            foreach (string dep in direct)
            {
                if (string.IsNullOrEmpty(dep))
                    continue;

                string normalizedDep = BundlePlatformPaths.NormalizeBundleName(dep);
                if (!reverseMap.TryGetValue(normalizedDep, out List<string> consumers))
                {
                    consumers = new List<string>();
                    reverseMap[normalizedDep] = consumers;
                }

                if (!consumers.Contains(name))
                    consumers.Add(name);
            }
        }

        var nodes = new List<DependencyGraphNode>();
        foreach (BundleCatalogInfo info in catalog.bundles)
        {
            if (info == null || string.IsNullOrEmpty(info.bundleName))
                continue;

            string name = BundlePlatformPaths.NormalizeBundleName(info.bundleName);
            directMap.TryGetValue(name, out string[] direct);
            reverseMap.TryGetValue(name, out List<string> reverseList);

            nodes.Add(new DependencyGraphNode
            {
                bundleName = name,
                directDependencies = direct ?? Array.Empty<string>(),
                reverseDependencies = reverseList != null ? reverseList.ToArray() : Array.Empty<string>(),
                transitiveClosure = ComputeTransitiveClosure(name, directMap)
            });
        }

        var document = new DependencyGraphDocument
        {
            buildId = catalog.buildId,
            platform = catalog.platform,
            buildMode = catalog.buildMode,
            nodes = nodes.ToArray()
        };

        string path = GetGraphPath(bundleRoot);
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonUtility.ToJson(document, true));
        return true;
    }

    public static bool TryLoad(string bundleRoot, out DependencyGraphDocument document)
    {
        document = null;
        string path = GetGraphPath(bundleRoot);
        if (!File.Exists(path))
            return false;

        try
        {
            document = JsonUtility.FromJson<DependencyGraphDocument>(File.ReadAllText(path));
            return document != null && document.nodes != null;
        }
        catch
        {
            return false;
        }
    }

    static string[] ComputeTransitiveClosure(string root, Dictionary<string, string[]> directMap)
    {
        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (!directMap.TryGetValue(current, out string[] deps) || deps == null)
                continue;

            foreach (string dep in deps)
            {
                string normalized = BundlePlatformPaths.NormalizeBundleName(dep);
                if (string.IsNullOrEmpty(normalized) || !closure.Add(normalized))
                    continue;

                stack.Push(normalized);
            }
        }

        var list = new List<string>(closure);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
    }
}
