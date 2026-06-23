/// <summary>
/// 区块内容编辑契约（只定义框架边界，不绑定任何具体 payload）。
/// 典型实现：地形挖/填、静态对象放置/删除、生态对象标 dirty。
/// </summary>
public interface IChunkContentEditor<TPayload>
    where TPayload : class
{
    /// <summary>
    /// 尝试编辑指定 chunk 的 payload。
    /// 返回 true 表示 payload 发生改变；调用方可据此标记 dirty 并安排存档/重建表现。
    /// </summary>
    bool TryEdit(ChunkData chunk, TPayload payload, in ChunkContentEditContext context);
}

/// <summary>一次内容编辑的上下文。只放通用字段，具体编辑参数由 Operation 与 UserData 解释。</summary>
public readonly struct ChunkContentEditContext
{
    public readonly string Operation;
    public readonly ChunkCoord Coord;
    public readonly int LocalX;
    public readonly int LocalY;
    public readonly int LocalZ;
    public readonly object UserData;

    public ChunkContentEditContext(
        string operation,
        ChunkCoord coord,
        int localX = 0,
        int localY = 0,
        int localZ = 0,
        object userData = null)
    {
        Operation = operation ?? string.Empty;
        Coord = coord;
        LocalX = localX;
        LocalY = localY;
        LocalZ = localZ;
        UserData = userData;
    }
}
