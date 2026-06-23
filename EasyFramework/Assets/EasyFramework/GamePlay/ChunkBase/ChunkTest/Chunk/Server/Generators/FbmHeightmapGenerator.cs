using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Heightmap 策略的纯计算生成器：在 (chunkSize+1)² 顶点网格上对世界坐标采样 FBM(Perlin) 噪声，
/// 产出相对 chunk 原点的高度偏移列表。仅返回 <see cref="HeightmapPayload"/>，
/// 不构建 Mesh / 不接触 Storager。
///
/// 主线程同步实现，无 Burst / Job 依赖；单 chunk 1089 顶点 ~1ms 量级，Demo 用够了。
/// </summary>
public sealed class FbmHeightmapGenerator : IChunkContentGenerator<HeightmapPayload>
{
    public HeightmapPayload Generate(ChunkCoord coord, ChunkSettings settings)
    {
        using (ChunkProfilerMarkers.GenFbmHeightmap.Auto())
        {
            return GenerateCore(coord, settings);
        }
    }

    private static HeightmapPayload GenerateCore(ChunkCoord coord, ChunkSettings settings)
    {
        ChunkBounds bounds = new ChunkBounds(coord, settings);
        int size = Mathf.Max(1, bounds.Size);
        int vertPerAxis = size + 1;
        int heightCount = vertPerAxis * vertPerAxis;

        List<float> heights = new List<float>(heightCount);
        int seed = settings.WorldSeed * 739;

        for (int z = 0; z < vertPerAxis; z++)
        {
            for (int x = 0; x < vertPerAxis; x++)
            {
                int wx = bounds.MinX + x;
                int wz = bounds.MinZ + z;
                float n = SampleFbm(wx, wz, settings.NoiseSmoothness, seed);
                // worldY = baseY + n * heightAmplitude；存的是相对 baseY 的偏移。
                heights.Add(n * ChunkSettingsDefaults.HeightAmplitude);
            }
        }

        return new HeightmapPayload
        {
            chunkId = coord.Id,
            terrainHeights = heights
        };
    }

    /// <summary>给定世界坐标采样 FBM 噪声 ∈ [0,1]。</summary>
    private static float SampleFbm(int worldX, int worldZ, float smoothness, int seed)
    {
        float sum = 0f;
        float amp = 1f;
        float norm = 0f;
        float frequency = 1f / Mathf.Max(0.0001f, smoothness);
        float octaveFrequency = frequency;

        for (int o = 0; o < ChunkSettingsDefaults.FbmOctaves; o++)
        {
            float nx = (worldX + seed) * octaveFrequency;
            float nz = (worldZ + seed * 3) * octaveFrequency;
            sum += Mathf.PerlinNoise(nx, nz) * amp;
            norm += amp;
            amp *= ChunkSettingsDefaults.FbmPersistence;
            octaveFrequency *= ChunkSettingsDefaults.FbmLacunarity;
        }

        return norm > 0f ? Mathf.Clamp01(sum / norm) : 0f;
    }
}
