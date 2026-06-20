using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Bundle 依赖拓扑排序（打包期写清单、运行时读清单双保险）。
/// 边语义：若 B 直接依赖 D，则 Acquire 顺序中 D 须先于 B（叶→根）。
/// </summary>
public static class BundleDependencyTopology
{
    public static Dictionary<string, List<string>> CreateDirectDependencyGraph(
        IEnumerable<(string bundleName, string[] directDependencies)> bundleDirectDeps)
    {
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (bundleDirectDeps == null)
            return graph;

        foreach ((string bundleName, string[] directDependencies) in bundleDirectDeps)
        {
            string normalizedBundle = BundlePlatformPaths.NormalizeBundleName(bundleName);
            if (string.IsNullOrEmpty(normalizedBundle))
                continue;

            if (!graph.ContainsKey(normalizedBundle))
                graph[normalizedBundle] = new List<string>();

            if (directDependencies == null)
                continue;

            foreach (string dep in directDependencies)
            {
                string normalizedDep = NormalizeDependencyName(dep, bundleName);
                if (string.IsNullOrEmpty(normalizedDep))
                    continue;

                if (!graph[normalizedBundle].Contains(normalizedDep))
                    graph[normalizedBundle].Add(normalizedDep);

                if (!graph.ContainsKey(normalizedDep))
                    graph[normalizedDep] = new List<string>();
            }
        }

        return graph;
    }

    /// <summary>
    /// 对 closureNodes（通常为 GetAllDependencies 集合）在 directDepGraph 上拓扑排序。
    /// </summary>
    public static string[] SortDependencyClosure(
        IReadOnlyDictionary<string, List<string>> directDepGraph,
        IReadOnlyCollection<string> closureNodes)
    {
        if (closureNodes == null || closureNodes.Count == 0)
            return Array.Empty<string>();

        var nodeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string node in closureNodes)
        {
            string normalized = BundlePlatformPaths.NormalizeBundleName(node);
            if (!string.IsNullOrEmpty(normalized))
                nodeSet.Add(normalized);
        }

        if (!TryTopologicalSort(nodeSet, directDepGraph, out string[] sorted, out _))
            return nodeSet.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

        return sorted;
    }

    /// <summary>
    /// 基于 Catalogue 中各 bundle 的全量 dependencies 列表，为 targetBundle 的依赖列表再排序（运行时无 Manifest）。
    /// </summary>
    public static string[] SortUsingCatalogAllDeps(
        IReadOnlyList<BundleCatalogInfo> allBundles,
        string targetBundle,
        string[] targetAllDependencies)
    {
        if (targetAllDependencies == null || targetAllDependencies.Length == 0)
            return Array.Empty<string>();

        var bundleToDeps = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (allBundles != null)
        {
            foreach (BundleCatalogInfo info in allBundles)
            {
                if (info == null || string.IsNullOrEmpty(info.bundleName))
                    continue;

                string key = BundlePlatformPaths.NormalizeBundleName(info.bundleName);
                bundleToDeps[key] = new List<string>();
                if (info.dependencies == null)
                    continue;

                foreach (string dep in info.dependencies)
                {
                    string normalizedDep = BundlePlatformPaths.NormalizeBundleName(dep);
                    if (!string.IsNullOrEmpty(normalizedDep) && !bundleToDeps[key].Contains(normalizedDep))
                        bundleToDeps[key].Add(normalizedDep);
                }
            }
        }

        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dep in targetAllDependencies)
        {
            string normalized = BundlePlatformPaths.NormalizeBundleName(dep);
            if (!string.IsNullOrEmpty(normalized))
                closure.Add(normalized);
        }

        var inferredGraph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, List<string>> pair in bundleToDeps)
        {
            string bundle = pair.Key;
            if (!closure.Contains(bundle) && !string.Equals(bundle, NormalizeDependencyName(targetBundle, null), StringComparison.OrdinalIgnoreCase))
                continue;

            if (!inferredGraph.ContainsKey(bundle))
                inferredGraph[bundle] = new List<string>();

            foreach (string dep in pair.Value)
            {
                if (!closure.Contains(dep))
                    continue;

                if (!inferredGraph[bundle].Contains(dep))
                    inferredGraph[bundle].Add(dep);

                if (!inferredGraph.ContainsKey(dep))
                    inferredGraph[dep] = new List<string>();
            }
        }

        foreach (string node in closure)
        {
            if (!inferredGraph.ContainsKey(node))
                inferredGraph[node] = new List<string>();
        }

        return SortDependencyClosure(inferredGraph, closure);
    }

    public static bool TryTopologicalSort(
        IReadOnlyCollection<string> nodes,
        IReadOnlyDictionary<string, List<string>> directDepGraph,
        out string[] sorted,
        out string cycleHint)
    {
        sorted = Array.Empty<string>();
        cycleHint = null;

        if (nodes == null || nodes.Count == 0)
            return true;

        var nodeSet = new HashSet<string>(nodes, StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string node in nodeSet)
        {
            inDegree[node] = 0;
            adjacency[node] = new List<string>();
        }

        foreach (string node in nodeSet)
        {
            if (directDepGraph == null || !directDepGraph.TryGetValue(node, out List<string> directDeps) || directDeps == null)
                continue;

            foreach (string dep in directDeps)
            {
                string normalizedDep = BundlePlatformPaths.NormalizeBundleName(dep);
                if (string.IsNullOrEmpty(normalizedDep) || !nodeSet.Contains(normalizedDep))
                    continue;

                adjacency[normalizedDep].Add(node);
                inDegree[node]++;
            }
        }

        var queue = new Queue<string>();
        foreach (KeyValuePair<string, int> pair in inDegree)
        {
            if (pair.Value == 0)
                queue.Enqueue(pair.Key);
        }

        var result = new List<string>(nodeSet.Count);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            result.Add(current);

            foreach (string next in adjacency[current])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        if (result.Count != nodeSet.Count)
        {
            foreach (KeyValuePair<string, int> pair in inDegree)
            {
                if (pair.Value <= 0)
                    continue;

                cycleHint = pair.Key;
                break;
            }

            return false;
        }

        sorted = result.ToArray();
        return true;
    }

    public static bool SetsEqual(IReadOnlyCollection<string> sorted, IReadOnlyCollection<string> expected)
    {
        if (sorted == null && expected == null)
            return true;

        if (sorted == null || expected == null)
            return false;

        var a = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string item in sorted)
        {
            string normalized = BundlePlatformPaths.NormalizeBundleName(item);
            if (!string.IsNullOrEmpty(normalized))
                a.Add(normalized);
        }

        var b = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string item in expected)
        {
            string normalized = BundlePlatformPaths.NormalizeBundleName(item);
            if (!string.IsNullOrEmpty(normalized))
                b.Add(normalized);
        }

        return a.SetEquals(b);
    }

    static string NormalizeDependencyName(string dependency, string ownerBundleName)
    {
        if (string.IsNullOrEmpty(dependency))
            return null;

        string normalized = BundlePlatformPaths.NormalizeBundleName(Path.GetFileName(dependency.Replace("\\", "/")));
        if (string.IsNullOrEmpty(normalized))
            return null;

        if (!string.IsNullOrEmpty(ownerBundleName)
            && string.Equals(normalized, BundlePlatformPaths.NormalizeBundleName(ownerBundleName), StringComparison.OrdinalIgnoreCase))
            return null;

        return normalized;
    }
}
