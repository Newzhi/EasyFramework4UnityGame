using UnityEngine;

// 职责：区块系统的纯工具方法集合（坐标转换、坐标映射等）
public static class ChunkUtil
{
    // 世界坐标 -> 区块局部坐标（基于区块边界最小点偏移）。
    public static Vector3Int WorldToLocal(int worldX, int worldY, int worldZ, ChunkBounds bounds)
    {
        return new Vector3Int(
            worldX - bounds.MinX,
            worldY - bounds.MinY,
            worldZ - bounds.MinZ);
    }

    // 区块局部坐标 -> 世界坐标（基于区块边界最小点回推）。
    public static Vector3Int LocalToWorld(int localX, int localY, int localZ, ChunkBounds bounds)
    {
        return new Vector3Int(
            bounds.MinX + localX,
            bounds.MinY + localY,
            bounds.MinZ + localZ);
    }

    // 世界坐标 -> 区块网格坐标（XZ 平面）。
    public static ChunkCoord WorldToChunkCoord(Vector3 worldPosition, int chunkSize)
    {
        int chunkX = Mathf.FloorToInt(worldPosition.x / chunkSize);
        int chunkZ = Mathf.FloorToInt(worldPosition.z / chunkSize);
        return new ChunkCoord(chunkX, chunkZ);
    }

    /// <summary>四邻偏移 (dx, dz)：东、北、西、南。</summary>
    public static readonly (int dx, int dz)[] NeighborOffsets4 =
    {
        (1, 0), (0, 1), (-1, 0), (0, -1),
    };

    /// <summary>八邻偏移 (dx, dz)。</summary>
    public static readonly (int dx, int dz)[] NeighborOffsets8 =
    {
        (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1),
    };
}
