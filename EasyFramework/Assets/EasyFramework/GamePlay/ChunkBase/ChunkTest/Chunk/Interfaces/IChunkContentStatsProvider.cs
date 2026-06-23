/// <summary>
/// 区块内容统计快照。
/// </summary>
public struct ChunkContentStats
{
    public int TotalChunks;
    public int LoadedChunks;
    public int PendingLoad;
    public int PendingUnload;
    public int InFlightLoad;
    public int PipelineCount;
    public int ContentSlotCount;
}

/// <summary>
/// 提供 Chunk 框架运行期统计信息。
/// </summary>
public interface IChunkContentStatsProvider
{
    ChunkContentStats CaptureStats();
}
