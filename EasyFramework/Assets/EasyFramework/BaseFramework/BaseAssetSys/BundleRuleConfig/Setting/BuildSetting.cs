using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

#region 枚举 - 目标平台

public enum BuildPlatform
{
    Windows,
    iOS,
    Android,
    macOS,
    WebGL,
}

#endregion

#region 枚举 - 打包模式

// UI 文案：EditorTest=编辑器测试，DeviceDebug=首包（真机模式），CdnHotUpdate=CDN联网，DlcPackage=DLC分包
public enum BuildMode
{
    EditorTest,
    DeviceDebug,
    CdnHotUpdate,
    DlcPackage,
}

#endregion

#region 枚举 - 打包规则

public enum PackingRule
{
    Default,
    Detailed,
    Custom,
}

#endregion

#region 枚举 - 资源优先级（卸载 LRU / 下载排序）

/// <summary>
/// 资源（Bundle）优先级：数值越小越不易被 LRU 卸载、下载队列越靠前。
/// 写入清单 <see cref="BundleCatalogInfo.resourcePriority"/>，供运行时卸载与 CDN 调度。
/// </summary>
public enum ResourcePriority
{
    /// <summary>关键资源（常驻 UI、核心玩法），LRU 永不卸载。</summary>
    Critical = 0,
    /// <summary>高优先级热更/首包资源。</summary>
    High = 1,
    /// <summary>默认优先级。</summary>
    Normal = 2,
    /// <summary>低优先级，可延迟下载。</summary>
    Low = 3,
    /// <summary>可选内容（DLC、皮肤），最先被 LRU 淘汰。</summary>
    Optional = 4,
}

#endregion

#region 枚举 - 自定义配置项

/// <summary>自定义项为文件夹路径时的 AB 拆分粒度（与全局 Default/Detailed 规则语义一致）。</summary>
public enum BundleFolderRule
{
    /// <summary>整个目录打成一个包，使用配置项「包名」。</summary>
    EntireFolder,
    /// <summary>目录下每个第一级子文件夹各打一个包，包名取子文件夹名。</summary>
    FirstLevelSubfolders,
    /// <summary>目录下每一个子文件夹（含嵌套）各打一个包。</summary>
    AllSubfolders,
}

/// <summary>
/// AssetBundle 压缩方式，映射到 <c>BuildPipeline.BuildAssetBundles</c> 的 <c>BuildAssetBundleOptions</c>。
/// </summary>
public enum BundleCompressionMode
{
    /// <summary>Unity 默认 LZMA 压缩（未设置 Chunk / Uncompressed 标志）。</summary>
    LZMA,
    /// <summary>LZ4 分块压缩，加载时可按需解压（推荐移动端）。</summary>
    LZ4Chunk,
    /// <summary>不压缩，包体最大、加载最快。</summary>
    Uncompressed,
}

#endregion

#region 自定义打包配置项

[Serializable]
public class BundleConfigItem
{
    /// <summary>资源路径：文件夹或单个资产（Assets/ 下）。</summary>
    public string assetPath = "Assets/";

    /// <summary>包名；文件夹为 EntireFolder 或单文件时生效。</summary>
    public string bundleName = "bundle";

    /// <summary>该项产出归属的打包模式（首包 / CDN / EditorTest / DLC）。</summary>
    public BuildMode buildMode = BuildMode.EditorTest;

    /// <summary>该项资源优先级；聚合到所在 Bundle 的清单优先级（取最高）。</summary>
    [FormerlySerializedAs("downloadPriority")]
    public ResourcePriority resourcePriority = ResourcePriority.Normal;

    /// <summary>资源路径为文件夹时生效；单文件路径时忽略。</summary>
    public BundleFolderRule folderPackingRule = BundleFolderRule.EntireFolder;

    /// <summary>配置说明（仅 Editor 展示）。</summary>
    public string note;
}

[Serializable]
public class BundleCategoryMapping
{
    /// <summary>源资源一级文件夹名（如 UI、Model）。</summary>
    public string sourceFolderName;

    /// <summary>输出 Bundles 下分类目录（如 Core、Character）。</summary>
    public string categoryFolder;
}

#endregion

#region 打包规则 ScriptableObject

[CreateAssetMenu(fileName = "BuildSetting", menuName = "vFramework/Build Setting")]
public class BuildSetting : ScriptableObject
{
    /// <summary>构建目标平台。</summary>
    public BuildPlatform platform = BuildPlatform.Windows;

    /// <summary>应用版本号（x.y.z），写入 AssetCatalog.version。</summary>
    public string version = "1.0.0";

    /// <summary>递增构建号，写入 AssetCatalog.buildNumber。</summary>
    public int buildNumber = 1001;

    /// <summary>首包（DeviceDebug）AB 输出根路径。</summary>
    public string deviceOutputPath = "Assets/StreamingAssets";

    /// <summary>CDN 热更 AB 输出根路径。</summary>
    public string cdnOutputPath = "Bundles/CDN";

    /// <summary>为 true 时在输出路径下追加平台子目录（如 StandaloneWindows64）。</summary>
    public bool usePlatformSubfolders = true;

    /// <summary>非 Custom 规则下的默认打包模式。</summary>
    public BuildMode buildMode = BuildMode.DeviceDebug;

    /// <summary>分包规则：Default / Detailed / Custom。</summary>
    public PackingRule packingRule = PackingRule.Default;

    /// <summary>Default/Detailed 规则扫描的资源根目录。</summary>
    public string targetDirectory = "Assets/AssetBundle";

    /// <summary>Custom 规则下的逐项配置。</summary>
    public List<BundleConfigItem> customItems = new List<BundleConfigItem>();

    [Header("构建选项")]
    /// <summary>BuildPipeline 压缩选项（LZMA / LZ4 分块 / 不压缩）。</summary>
    public BundleCompressionMode compressionMode = BundleCompressionMode.LZ4Chunk;

    /// <summary>Custom 规则下 UI 可编辑；Default/Detailed 打包时使用 Normal，由 BundlePriorityResolver 写入清单。</summary>
    public ResourcePriority defaultBundlePriority = ResourcePriority.Normal;

    /// <summary>是否自动抽取跨包公共依赖为 shared_auto.bundle（全自动二次规划）。</summary>
    public bool enableAutoSharedBundle = true;

    /// <summary>资产被多少个 Bundle 引用时才进入公共包（默认 ≥2）。</summary>
    public int sharedBundleMinRefCount = 2;

    /// <summary>自动公共包文件名（含 .bundle 后缀）。</summary>
    public string sharedBundleName = "shared_auto.bundle";

    [Header("Catalogue / 构建分析")]
    /// <summary>写入 bundles[] 时对依赖做拓扑排序（推荐开启）。</summary>
    public bool useTopologicalSort = true;

    /// <summary>打包成功后生成 BundleBuildReport.json。</summary>
    public bool runBuildAnalyzer = true;

    /// <summary>loadPath 重复时阻断写清单；关闭则仅 Warning。</summary>
    public bool loadPathDuplicateAsError = false;

    /// <summary>清单 bundles[] 仅存直接依赖（体积更小；读端 BFS 展开）。</summary>
    public bool useDirectDependenciesOnly = false;

    [Header("CDN / 增量（运行时）")]
    /// <summary>CDN 清单与 AB 根 URL（末尾无斜杠）；CdnHotUpdate 运行时拉取对比用。</summary>
    public string cdnBaseUrl = "";

    [Header("内容包 / DLC")]
    /// <summary>DlcPackage 模式下的包 ID（如 Desert → DLC_Desert）。</summary>
    public string dlcPackageId = "";

    /// <summary>可选：*.bytes 配置表源目录，打包时拷贝到包内 Config/。</summary>
    public string configSourceDirectory = "";

    /// <summary>源一级文件夹 → Bundles 分类子目录；未配置则用小写文件夹名。</summary>
    public List<BundleCategoryMapping> bundleCategoryMappings = new List<BundleCategoryMapping>();
}

#endregion
