using System.Collections.Generic;

/// <summary>
/// 区块加载源（Load Source）：本源决定"我希望哪些区块此刻处于活跃状态"。
///
/// 设计要点：
/// - 多个 LoadSource 由 <see cref="ChunkManager"/> 并联收集，活跃集合 = 各源产出的并集。
/// - 本接口与"中心来源"解耦：源自身负责决定参考点（玩家 Transform 等）。
/// - 本接口与 Pipeline 正交：本接口决定"加载哪些区块"，Pipeline 决定"区块内容怎么生/存"。
///
/// 典型实现：
/// - 形状窗口源：圆形 / 方形 / 球形 / 视锥裁剪…
/// </summary>
public interface IChunkLoadSource
{
    /// <summary>
    /// 本源期望的激活级别。MVP 默认 <see cref="ChunkActivationLevel.Loaded"/>。
    /// </summary>
    ChunkActivationLevel ActivationLevel => ChunkActivationLevel.Loaded;

    /// <summary>
    /// 把本源期望活跃的区块坐标"追加"写入 <paramref name="results"/>（不要 Clear，由调用方统一管理容器）。
    /// 调用方使用 <see cref="HashSet{ChunkCoord}"/> 自动去重，多源同坐标只算一份。
    /// 每帧调用，不允许在内部分配新集合。
    /// </summary>
    void CollectTargetChunks(ChunkSettings settings, ICollection<ChunkCoord> results);
}
