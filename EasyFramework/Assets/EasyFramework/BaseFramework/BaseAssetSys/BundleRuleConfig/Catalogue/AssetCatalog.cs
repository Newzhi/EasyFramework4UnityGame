using System;

#region 资源清单 - 单条资源

/// <summary>
/// 单条资源在清单中的定位：逻辑路径 → 所在 bundle + 包内 asset 名。
/// </summary>
/// <remarks>
/// 包间依赖不在此记录，见 BundleCatalogInfo / AssetCatalog.bundles。
/// </remarks>
[Serializable]
public class AssetCatalogEntry
{
    /// <summary>Unity 工程内完整路径，如 Assets/AssetBundle/Model/Prefabs/Bullet.prefab</summary>
    public string assetPath;

    /// <summary>所在 AssetBundle 文件名，如 model.bundle</summary>
    public string bundleName;

    /// <summary>包内加载名（通常为文件名无扩展名）</summary>
    public string assetName;
}

#endregion

#region 资源清单 - Bundle 依赖

/// <summary>
/// 按 AssetBundle 粒度记录依赖与其它 bundle 的完整性元数据。
/// 与 AssetCatalogEntry（资源→包）互补，供加载器 AcquireBundle 与 CDN/卸载策略使用。
/// </summary>
[Serializable]
public class BundleCatalogInfo
{
    /// <summary>本包名，如 ui.bundle</summary>
    public string bundleName;

    /// <summary>
    /// 依赖的包名列表（拓扑序：叶→根）。全量或仅直接依赖由打包设置 useDirectDependenciesOnly 决定。
    /// </summary>
    public string[] dependencies;

    /// <summary>
    /// 全量传递依赖（可选，兼容旧读端）；仅当 useDirectDependenciesOnly 时写入。
    /// </summary>
    public string[] dependenciesAll;

    /// <summary>资源优先级整型，对应 <see cref="ResourcePriority"/>；越小越不易 LRU 卸载。</summary>
    public int resourcePriority;

    /// <summary>构建后 .bundle 文件大小（字节）。</summary>
    public long sizeBytes;

    /// <summary>构建后 .bundle 文件 SHA256（十六进制小写）。</summary>
    public string fileHash;

    /// <summary>构建后 .bundle 文件 CRC32（与下载校验一致）。</summary>
    public uint crc32;
}

#endregion

#region 资源清单 - 根结构

/// <summary>
/// 资源清单根结构（二进制 catalog.bytes）。
/// </summary>
[Serializable]
public class AssetCatalog
{
    /// <summary>应用版本号 x.y.z</summary>
    public string version;

    /// <summary>递增构建号</summary>
    public int buildNumber;

    /// <summary>构建目标平台名</summary>
    public string platform;

    /// <summary>打包模式（DeviceDebug / CdnHotUpdate 等）</summary>
    public string buildMode;

    /// <summary>分包规则（Default / Detailed / Custom）</summary>
    public string packingRule;

    /// <summary>本次 AB 输出根目录（建议相对 StreamingAssets 或项目根）</summary>
    public string bundleRoot;

    /// <summary>打包资源根目录，用于解析业务 Load 简路径</summary>
    public string resourceRoot;

    /// <summary>本次构建唯一 ID（GUID），用于增量 diff 关联</summary>
    public string buildId;

    /// <summary>整份清单内容哈希（SHA256），运行时比对是否需要拉新清单</summary>
    public string catalogueHash;

    /// <summary>本次 BuildPipeline 使用的压缩模式名（LZMA / LZ4Chunk / Uncompressed）</summary>
    public string compressionMode;

    /// <summary>CDN 根 URL（末尾无斜杠）；CdnHotUpdate 运行时拉取清单与 AB，打包时从 BuildSetting 写入。</summary>
    public string cdnBaseUrl;

    /// <summary>资源 → 包 映射表</summary>
    public AssetCatalogEntry[] entries;

    /// <summary>bundle → 依赖与完整性 映射表</summary>
    public BundleCatalogInfo[] bundles;
}

#endregion
