using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class DefaultBundlePathResolver : IBundlePathResolver
{
    readonly string primaryRoot;
    readonly string cacheRoot;
    readonly List<string> packageBundlesRoots = new List<string>();

    public string CacheRoot => cacheRoot;

    public DefaultBundlePathResolver(string primaryRoot, string cacheRoot = null)
    {
        this.primaryRoot = primaryRoot;
        this.cacheRoot = cacheRoot;
    }

    public static DefaultBundlePathResolver Create(string primaryRoot)
    {
        return new DefaultBundlePathResolver(primaryRoot, CdnPaths.GetCacheRoot());
    }

    public void RegisterPackageBundlesRoot(string bundlesRoot)
    {
        if (string.IsNullOrEmpty(bundlesRoot))
            return;

        if (!packageBundlesRoots.Contains(bundlesRoot))
            packageBundlesRoots.Add(bundlesRoot);
    }

    public void UnregisterPackageBundlesRoot(string bundlesRoot)
    {
        if (string.IsNullOrEmpty(bundlesRoot))
            return;

        packageBundlesRoots.Remove(bundlesRoot);
    }

    public bool TryResolveLocalPath(string bundleName, out string localPath)
    {
        localPath = null;
        bundleName = BundlePlatformPaths.NormalizeBundleName(bundleName);

        if (!string.IsNullOrEmpty(cacheRoot))
        {
            if (TryResolveUnderRoot(cacheRoot, bundleName, out localPath))
                return true;
        }

        foreach (string packageRoot in packageBundlesRoots)
        {
            if (TryResolveUnderRoot(packageRoot, bundleName, out localPath))
                return true;
        }

        if (!string.IsNullOrEmpty(primaryRoot))
        {
            if (TryResolveUnderRoot(primaryRoot, bundleName, out localPath))
                return true;
        }

        return false;
    }

    public bool IsLocalBundleAvailable(string bundleName)
    {
        return TryResolveLocalPath(bundleName, out _);
    }

    static bool TryResolveUnderRoot(string root, string bundleName, out string localPath)
    {
        localPath = null;
        if (StreamingAssetsIO.IsNonFileProtocolPath(root))
        {
            localPath = StreamingAssetsIO.CombinePath(root, bundleName);
            return true;
        }

        if (BundlePlatformPaths.TryResolveBundleFilePath(root, bundleName, out string path))
        {
            localPath = path;
            return true;
        }

        return false;
    }
}
