/// <summary>
/// 只读邻居 / 跨 chunk 查询：Generator / Presenter 在合法边界内读取其它 chunk 已生成的 payload。
/// 由 <see cref="ChunkManager"/> 实现；找不到时调用方必须有 fallback。
/// </summary>
public interface IChunkContentReader
{
    bool TryGetPayload<T>(ChunkCoord coord, out T payload) where T : class;
}
