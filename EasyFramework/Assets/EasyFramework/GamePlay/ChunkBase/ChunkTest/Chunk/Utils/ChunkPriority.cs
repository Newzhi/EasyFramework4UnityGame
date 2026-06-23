using UnityEngine;

/// <summary>
/// 区块优先级计算工具（纯函数，无状态）。
///
/// 用途：阶段 0 把"一帧加载所有目标 chunk"改成"按距离排序、每帧只处理 N 个"，
/// 排序键就是本工具计算的 sqr 距离（避免 sqrt 开销）。
///
/// 选 sqr 而非 abs：
/// - 距离比较保持等价（单调）
/// - 圆形 LoadSource 已经是欧氏距离驱动，sqr 与之对齐
/// - 后续如果要做"环形分层优先级"（先内圈再外圈），仍可基于 sqr 阈值切分
/// </summary>
public static class ChunkPriority
{
    /// <summary>
    /// 计算 chunk 中心到玩家世界坐标的水平 sqr 距离（XZ 平面，忽略 Y）。
    /// 数值意义：越小越优先。无玩家时返回 chunk 自身 sqr 距离原点（保证仍可排序）。
    /// </summary>
    public static float ComputeSqrDistanceToPlayer(ChunkCoord coord, Vector3 playerWorld, int chunkSize)
    {
        // chunk 中心世界坐标（XZ）：(coord * size) + size/2
        float halfSize = chunkSize * 0.5f;
        float centerX = coord.X * chunkSize + halfSize;
        float centerZ = coord.Z * chunkSize + halfSize;

        float dx = centerX - playerWorld.x;
        float dz = centerZ - playerWorld.z;
        return dx * dx + dz * dz;
    }

    /// <summary>
    /// 计算两个 chunk 网格坐标差的 sqr 距离（按 chunk 格数，不乘 size）。
    /// 用于 Unload 时按"离玩家中心多远"反向排序——远的先 Unload，给近距离 Load 让出帧预算。
    /// </summary>
    public static int ComputeSqrChunkDistance(ChunkCoord a, ChunkCoord b)
    {
        int dx = a.X - b.X;
        int dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }
}
