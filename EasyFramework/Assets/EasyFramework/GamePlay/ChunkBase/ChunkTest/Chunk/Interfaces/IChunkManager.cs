using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 区块缓存与 Load/Unload 的对外契约。
/// 内容读档/生成的全部细节由实现类内部通过组合 <see cref="IChunkContentPipeline"/> 完成，不暴露为接口成员；
/// 哪些区块该被加载由实现类内部通过组合 <see cref="IChunkLoadSource"/> 决定，同样不暴露。
/// </summary>
public interface IChunkManager
{
    IReadOnlyDictionary<long, ChunkData> Chunks { get; }

    ChunkData LoadChunk(ChunkCoord coord);

    ChunkData LoadChunk(ChunkCoord coord, ChunkActivationLevel level);

    bool UnloadChunk(ChunkCoord coord);

    bool TryGetChunk(long chunkId, out ChunkData chunk);

    /// <summary>
    /// 查询邻居 chunk；<paramref name="minLevel"/> 过滤 <see cref="ChunkData.CurrentLevel"/>。
    /// </summary>
    bool TryGetNeighbor(ChunkCoord coord, int dx, int dz, ChunkActivationLevel minLevel, out ChunkData neighbor);

    bool TryEditChunkContent<TPayload>(
        ChunkCoord coord,
        IChunkContentEditor<TPayload> editor,
        ChunkContentEditContext context,
        string layerKey = null)
        where TPayload : class;
}
