using UnityEngine;

// 职责：区块配置硬编码常量（MVP 原型）。
//
// 设计要点
// --------
// 1) ChunkSettings 是一个"扁平的运行时只读快照"，给 Generator/Storager/LoadSource 直接读字段用。
//    扁平是为了 hot-path 无装箱、无字典查找；它本身不区分"哪些字段属于哪个策略"。
// 2) ChunkSettingsDefaults 提供 const 常量 + Default 快照，替代原 ChunkConfig MonoBehaviour。
// 3) 新增内容形态时：在 ChunkSettingsDefaults 里加 const，在 Default 里赋值；老 Pipeline 完全不受影响。

/// <summary>
/// 区块运行时配置快照。
/// 字段按"被谁消费"分块注释，但内存布局保持扁平（zero-cost），所有 Pipeline 都通过 settings.X 直接取。
/// 注意：本结构不区分"哪个字段属于哪个策略"——是否生效取决于当前组装的 Pipeline 是否读它。
/// </summary>
public struct ChunkSettings
{
    // ── Basic（所有 Pipeline 通用） ─────────────────────────────────────────
    public int Size { get; set; }
    public int MinY { get; set; }
    public int MaxYInclusive { get; set; }

    // ── Load Window（窗口形状 LoadSource 通用） ──
    public int MaxRenderDistance { get; set; }

    // ── Terrain Noise（仅噪声驱动的 Generator 使用） ──────────────
    public int WorldSeed { get; set; }
    public float NoiseSmoothness { get; set; }

    // ── Debug Visualization（被 Manager 消费） ──────────────────────────────
    public bool DrawActiveChunkWireframe { get; set; }
    public Color ActiveChunkWireColor { get; set; }
    public bool LogPlayerEnterChunk { get; set; }
    public bool LogVerbose { get; set; }

    // ── Performance（Manager 限流 / 异步加载并发） ────────
    public int MaxLoadPerFrame { get; set; }
    public int MaxUnloadPerFrame { get; set; }
    public int MaxConcurrentLoads { get; set; }

    // ── File Storage（仅 FileChunkContentStorager 使用） ────────────────────
    public bool EnableChunkObjectDiskCache { get; set; }
    public string CacheSubfolder { get; set; }
}

/// <summary>
/// MVP 硬编码配置常量。修改参数请直接改本类中的 const 值。
/// </summary>
public static class ChunkSettingsDefaults
{
    // ── Basic ─────────────────────────────────────────
    public const int Size = 16;
    public const int MinY = 0;
    public const int MaxYInclusive = 255;

    // ── Load Window ───────────────────────────────────
    public const int MaxRenderDistance = 3;

    // ── Terrain Noise ─────────────────────────────────
    public const int WorldSeed = 0;
    public const float NoiseSmoothness = 32f;
    public const float HeightAmplitude = 18f;
    public const int FbmOctaves = 4;
    public const float FbmLacunarity = 2f;
    public const float FbmPersistence = 0.5f;

    // ── Debug ─────────────────────────────────────────
    public const bool DrawActiveChunkWireframe = true;
    public static readonly Color ActiveChunkWireColor = Color.green;
    public const bool LogPlayerEnterChunk = true;
    public const bool LogVerbose = false;

    // ── Performance ───────────────────────────────────
    public const int MaxLoadPerFrame = 16;
    public const int MaxUnloadPerFrame = 32;
    public const int MaxConcurrentLoads = 8;

    // ── File Storage ──────────────────────────────────
    public const bool EnableChunkObjectDiskCache = true;
    public const string CacheSubfolder = "EasyFramework/GamePlay/ChunkBaseGame/ChunkTest/TempData";

    /// <summary>运行时只读快照（从 const 赋值）。</summary>
    public static readonly ChunkSettings Default = new()
    {
        Size = Size,
        MinY = MinY,
        MaxYInclusive = MaxYInclusive,
        MaxRenderDistance = MaxRenderDistance,
        WorldSeed = WorldSeed,
        NoiseSmoothness = NoiseSmoothness,
        DrawActiveChunkWireframe = DrawActiveChunkWireframe,
        ActiveChunkWireColor = ActiveChunkWireColor,
        LogPlayerEnterChunk = LogPlayerEnterChunk,
        LogVerbose = LogVerbose,
        MaxLoadPerFrame = MaxLoadPerFrame,
        MaxUnloadPerFrame = MaxUnloadPerFrame,
        MaxConcurrentLoads = MaxConcurrentLoads,
        EnableChunkObjectDiskCache = EnableChunkObjectDiskCache,
        CacheSubfolder = CacheSubfolder,
    };
}
