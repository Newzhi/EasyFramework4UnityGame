/// <summary>
/// 区块内容生成策略：纯计算，无 IO、无场景副作用。
/// 职责边界：给定 chunk 坐标 + 配置 → 返回强类型 payload；不接触 Storager / Unity 场景。
/// 与 <see cref="IChunkContentStorager{TPayload}"/> 通过 TPayload 关联，
/// 二者由 <see cref="IChunkContentPipeline"/> 编排，互不感知。
/// </summary>
public interface IChunkContentGenerator<TPayload> where TPayload : class, new()
{
    /// <summary>给定区块坐标与配置，生成该区块的强类型 payload。</summary>
    TPayload Generate(ChunkCoord coord, ChunkSettings settings);
}
