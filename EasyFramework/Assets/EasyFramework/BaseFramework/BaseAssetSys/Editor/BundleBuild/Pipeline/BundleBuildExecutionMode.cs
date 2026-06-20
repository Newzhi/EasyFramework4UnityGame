/// <summary>
/// 打包面板触发的构建策略：增量（复用缓存）或覆盖（全量重建）。
/// </summary>
public enum BundleBuildExecutionMode
{
    /// <summary>对比 BuildCache，仅在有变更时调用 BuildPipeline；无变更可跳过 AB 构建。</summary>
    Incremental,

    /// <summary>忽略缓存并 ForceRebuild，刷新 hash 与 BuildManifest。</summary>
    FullOverwrite,
}
