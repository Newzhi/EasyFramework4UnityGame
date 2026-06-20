using System;

/// <summary>
/// 单次打包产物的 Bundle 完整性条目（写入 Reports/BuildManifest.json）。
/// </summary>
[Serializable]
public class BuildManifestBundleEntry
{
    /// <summary>Bundle 文件名，如 model.bundle</summary>
    public string bundleName;

    /// <summary>文件大小（字节）</summary>
    public long sizeBytes;

    /// <summary>SHA256 十六进制</summary>
    public string fileHash;

    /// <summary>CRC32</summary>
    public uint crc32;

    /// <summary>资源优先级整型（ResourcePriority）</summary>
    public int resourcePriority;
}

/// <summary>
/// 单次构建的完整 Manifest（Reports/BuildManifest.json）。
/// </summary>
[Serializable]
public class BuildManifest
{
    /// <summary>与 AssetCatalog.buildId 一致</summary>
    public string buildId;

    /// <summary>应用版本号</summary>
    public string version;

    /// <summary>构建号</summary>
    public int buildNumber;

    /// <summary>平台</summary>
    public string platform;

    /// <summary>打包模式</summary>
    public string buildMode;

    /// <summary>压缩模式名</summary>
    public string compressionMode;

    /// <summary>构建时间 UTC ISO8601</summary>
    public string buildTimeUtc;

    /// <summary>各 Bundle 完整性</summary>
    public BuildManifestBundleEntry[] bundles;
}

/// <summary>
/// 相对上一份 BuildManifest 的差异（Reports/BuildManifest.diff.json）。
/// </summary>
[Serializable]
public class BuildManifestDiff
{
    /// <summary>上一份 buildId</summary>
    public string previousBuildId;

    /// <summary>当前 buildId</summary>
    public string currentBuildId;

    /// <summary>新增的 bundle 名</summary>
    public string[] added;

    /// <summary>删除的 bundle 名</summary>
    public string[] removed;

    /// <summary>hash 或 size 变化的 bundle 名</summary>
    public string[] changed;

    /// <summary>未变化的 bundle 名</summary>
    public string[] unchanged;
}

/// <summary>
/// Editor 增量缓存（Reports/BuildCache.json）：源资源与输出 bundle 的 hash 快照。
/// </summary>
[Serializable]
public class BuildCacheData
{
    /// <summary>上次成功构建的 buildId</summary>
    public string lastBuildId;

    /// <summary>源资产 GUID → 内容 hash</summary>
    public BuildCacheAssetEntry[] assets;

    /// <summary>bundle 名 → 输出文件 hash</summary>
    public BuildCacheBundleEntry[] bundles;
}

[Serializable]
public class BuildCacheAssetEntry
{
    /// <summary>Unity 资产 GUID</summary>
    public string guid;

    /// <summary>AssetDatabase 依赖 hash 或文件 hash 组合</summary>
    public string contentHash;
}

[Serializable]
public class BuildCacheBundleEntry
{
    /// <summary>bundle 文件名</summary>
    public string bundleName;

    /// <summary>输出 .bundle 的 SHA256</summary>
    public string outputHash;
}

/// <summary>内容包 Version/version.json 元数据。</summary>
[Serializable]
public class PackageVersionInfo
{
    public string packageId;
    public string version;
    public int buildNumber;
    public string platform;
    public string buildId;
    public string catalogueHash;
    public string buildMode;
}
