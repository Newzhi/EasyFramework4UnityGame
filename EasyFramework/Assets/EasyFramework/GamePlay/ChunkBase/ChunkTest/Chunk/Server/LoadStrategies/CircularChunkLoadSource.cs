using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 圆形加载窗口源：以指定中心 Transform 所在 chunk 为中心，dx²+dz² ≤ radius² 的圆形范围内全部加入活跃集合。
/// 比方形窗口更符合"渲染距离"直观语义，活跃区块数约 πR²。
/// </summary>
public sealed class CircularChunkLoadSource : IChunkLoadSource
{
    private readonly Transform centerProvider;

    /// <param name="centerProvider">作为窗口中心的 Transform（玩家/相机/载具…）。允许为 null，此时本源跳过。</param>
    public CircularChunkLoadSource(Transform centerProvider)
    {
        this.centerProvider = centerProvider;
    }

    public void CollectTargetChunks(ChunkSettings settings, ICollection<ChunkCoord> results)
    {
        if (results is null || centerProvider is null)
        {
            return;
        }

        ChunkCoord center = ChunkUtil.WorldToChunkCoord(centerProvider.position, settings.Size);
        int radius = settings.MaxRenderDistance;
        int radiusSqr = radius * radius;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (dx * dx + dz * dz > radiusSqr)
                {
                    continue;
                }
                results.Add(center.Offset(dx, dz));
            }
        }
    }
}
