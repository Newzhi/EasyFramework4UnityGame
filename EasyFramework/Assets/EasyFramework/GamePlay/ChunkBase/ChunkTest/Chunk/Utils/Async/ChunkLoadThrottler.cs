using System.Collections.Generic;

/// <summary>
/// 每帧加载/卸载配额限流器（无状态结构 + 主线程使用，不做线程安全）。
///
/// 与 <see cref="ChunkLoadQueue"/> 的关系：
/// - Throttler 不持有队列，只在每帧由 Manager 注入"待入队的目标集合"与"待 Unload 的 chunkId 列表"
/// - 它根据 ChunkSettings.MaxLoadPerFrame / MaxUnloadPerFrame 上限切片输出
///
/// 阶段 0 的最小化策略：
/// - LoadQueue 出队上限：MaxLoadPerFrame
/// - Unload 上限：MaxUnloadPerFrame（按"距玩家最远优先"排序，让出帧预算给 Load）
/// </summary>
public sealed class ChunkLoadThrottler
{
    /// <summary>从队列出队最多 maxCount 个 coord，写入 outBuffer（调用方提前 Clear）。返回实际出队数量。</summary>
    public int DequeueLoadBatch(ChunkLoadQueue queue, int maxCount, List<ChunkCoord> outBuffer)
    {
        if (queue is null || outBuffer is null || maxCount <= 0)
        {
            return 0;
        }

        int taken = 0;
        while (taken < maxCount && queue.TryDequeue(out ChunkCoord coord))
        {
            outBuffer.Add(coord);
            taken++;
        }
        return taken;
    }

    /// <summary>从 unloadCandidates 取前 maxCount 个写入 outBuffer。调用方负责事先按"远→近"排好序。</summary>
    public int TakeUnloadBatch(List<long> unloadCandidates, int maxCount, List<long> outBuffer)
    {
        if (unloadCandidates is null || outBuffer is null || maxCount <= 0)
        {
            return 0;
        }

        int n = unloadCandidates.Count;
        int take = n < maxCount ? n : maxCount;
        for (int i = 0; i < take; i++)
        {
            outBuffer.Add(unloadCandidates[i]);
        }
        // 已消费的从 candidates 头部移除，剩余的留待下一帧继续消费
        if (take > 0)
        {
            unloadCandidates.RemoveRange(0, take);
        }
        return take;
    }
}
