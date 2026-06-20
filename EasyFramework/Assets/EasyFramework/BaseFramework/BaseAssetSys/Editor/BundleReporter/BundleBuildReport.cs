using System;

[Serializable]
public class BundleBuildReport
{
    public int bundleCount;
    public string platform;
    public string buildMode;
    public double buildTimeSeconds;
    public BundleSizeEntry[] bundleSizes;
    public RedundantAssetEntry[] redundantAssets;
    public CrossBundleEdgeEntry[] crossBundleEdges;
    public LoadPathDuplicateEntry[] loadPathDuplicates;
}

[Serializable]
public class BundleSizeEntry
{
    public string bundleName;
    public long bytes;
}

[Serializable]
public class RedundantAssetEntry
{
    public string assetPath;
    public string[] referencedByBundles;
    public string suggestion;
}

[Serializable]
public class CrossBundleEdgeEntry
{
    public string consumerBundle;
    public string providerBundle;
    public string dependencyAssetPath;
}

[Serializable]
public class LoadPathDuplicateEntry
{
    public string loadPath;
    public string firstAssetPath;
    public string secondAssetPath;
}
