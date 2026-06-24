using System;
using System.Collections.Generic;
using UnityEngine;

#region 文件说明

// 设计目标：
// - 哈希性：用位运算得到稳定 ID，作为 Dictionary/HashMap 的 key（O(1) 查找）。
// - 随机性：放到"区块内容生成器"中，不耦合到区块管理数据。
// - 边界定义：由 chunk 坐标 + chunkSize 推导世界坐标边界，并提供常用边界/坐标转换工具。
//
// 结构上将 "坐标/ID（ChunkCoord）" 与 "边界（ChunkBounds）" 分离：
// - 坐标是区块的身份（identity），决定 ID/Hash；边界是派生数据（derived），由坐标+大小计算得出。
// - 这样不会出现"改了边界忘了改 ID"的一致性问题，也更利于后续扩展（不同维度、不同高度范围等）。
//
// 重构后：ChunkData 不再持有特定 Generator 的字段。
// 改为按 ChunkContentKey(Type + LayerKey) 分槽存储，由 Pipeline 管理。
// 默认 LayerKey 为空，兼容当前“一种 Payload 类型一层”的用法；未来可支持同类型多层附加数据。

#endregion

#region 核心类型（坐标 / 边界 / 状态）



/// <summary>
/// 职责：描述区块当前生命周期状态（是否已加载、是否正在使用）
/// TODO ：明确各个状态
/// </summary>
public enum ChunkState
{
    // 不在内存缓存中。
    Unloaded,
    // 正在进行加载流程（读档/初始化数据）。
    Loading,
    // 数据已可用，但当前不一定在玩家活跃范围内。
    Ready,
    // 活跃状态（例如在视距内、参与渲染/交互）。
    Active,
}


/// <summary>
/// 区块的"激活级别"（MVP 精简版）。
///
/// 偏序：None &lt; Loaded。
///
/// 语义：
/// - None：未加载（默认零值）
/// - Loaded：payload 已在内存中
///
/// 扩展说明：原 Generated / Rendered 曾为双窗口 + Presenter 预留，扩展场景表现层时可恢复。
/// </summary>
public enum ChunkActivationLevel : byte
{
    None = 0,
    Loaded = 1,
}


// 职责：定义区块在网格中的身份坐标，并提供可哈希的唯一 ID。
public readonly struct ChunkCoord : IEquatable<ChunkCoord>
{
    // 区块网格坐标（chunkX, chunkZ），不是世界坐标。
    public int X { get; }
    public int Z { get; }

    public ChunkCoord(int x, int z)
    {
        X = x;
        Z = z;
    }

    // 64-bit ID：高 32 位存 X，低 32 位存 Z
    // (chunkX << 32) | chunkZ
    // 这里对 Z 用 uint 视角，避免负数在 OR 时的符号扩展带来歧义。
    public long Id => ((long)X << 32) | (uint)Z;

    public ChunkCoord Offset(int dx, int dz) => new(X + dx, Z + dz);

    // 显式实现 IEquatable + 重写 Equals/GetHashCode：
    // 让 HashSet<ChunkCoord> / Dictionary<ChunkCoord,_> 走快速路径、零装箱。
    // 多个 IChunkLoadSource 并联收集目标坐标时依赖此实现去重。
    public bool Equals(ChunkCoord other) => X == other.X && Z == other.Z;
    public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);
    public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);
}

// 职责：根据区块坐标和配置推导边界，提供边界判定与坐标转换工具。
public sealed class ChunkBounds
{
    // 用来推导边界的身份信息：coord + settings
    public ChunkCoord Coord { get; }
    public ChunkSettings Settings { get; }
    public int Size => Settings.Size;
    public int MinY => Settings.MinY;
    public int MaxYInclusive => Settings.MaxYInclusive;

    // X/Z 采用半开区间：[Min, MaxExclusive)
    // - 能把 "是否在边界内" 的判断写成 < MaxExclusive，避免 +1/-1 的 off-by-one。
    // - 更符合数组/体素索引的常见习惯（0..Size-1）。
    public int MinX => Coord.X * Size;
    public int MinZ => Coord.Z * Size;
    public int MaxXExclusive => MinX + Size;
    public int MaxZExclusive => MinZ + Size;

    public ChunkBounds(ChunkCoord coord, ChunkSettings settings)
    {
        Coord = coord;
        Settings = settings;
    }

    public bool ContainsWorld(int worldX, int worldY, int worldZ)
    {
        return worldX >= MinX && worldX < MaxXExclusive
            && worldZ >= MinZ && worldZ < MaxZExclusive
            && worldY >= MinY && worldY <= MaxYInclusive;
    }

    public Vector3Int WorldToLocal(int worldX, int worldY, int worldZ)
    {
        // 将世界坐标转换为区块内局部坐标（通常用于访问体素数组）。
        return ChunkUtil.WorldToLocal(worldX, worldY, worldZ, this);
    }

    public Vector3Int LocalToWorld(int localX, int localY, int localZ)
    {
        // 将区块内局部坐标转换回世界坐标。
        return ChunkUtil.LocalToWorld(localX, localY, localZ, this);
    }
}

#endregion

#region 区块运行时数据（ChunkData）

/// <summary>
/// ChunkData 中一份内容的唯一键。
/// Type 负责区分 Heightmap / Vegetation / Animal 等内容形态；
/// LayerKey 负责给未来同类型多层数据留口子，例如 "terrain:surface" / "terrain:cave" / "objects:static"。
/// </summary>
public readonly struct ChunkContentKey : IEquatable<ChunkContentKey>
{
    public static readonly string DefaultLayerKey = string.Empty;

    public Type PayloadType { get; }
    public string LayerKey { get; }

    public ChunkContentKey(Type payloadType, string layerKey = null)
    {
        PayloadType = payloadType ?? throw new ArgumentNullException(nameof(payloadType));
        LayerKey = string.IsNullOrEmpty(layerKey) ? DefaultLayerKey : layerKey;
    }

    public static ChunkContentKey Create<T>(string layerKey = null) where T : class =>
        new(typeof(T), layerKey);

    public bool Equals(ChunkContentKey other) =>
        PayloadType == other.PayloadType && string.Equals(LayerKey, other.LayerKey, StringComparison.Ordinal);

    public override bool Equals(object obj) => obj is ChunkContentKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((PayloadType != null ? PayloadType.GetHashCode() : 0) * 397)
                ^ StringComparer.Ordinal.GetHashCode(LayerKey ?? DefaultLayerKey);
        }
    }

    public override string ToString() =>
        string.IsNullOrEmpty(LayerKey) ? PayloadType.Name : $"{PayloadType.Name}@{LayerKey}";
}

// 职责：作为区块元数据聚合体，组合 Coord/Settings/Bounds/State，供管理器缓存与调度。
// 通过 Dictionary<ChunkContentKey, ContentSlot> 与具体内容形态解耦，支持多条 Pipeline 并行挂载不同 payload。
public sealed class ChunkData
{
    private sealed class ContentSlot
    {
        public object Payload;
        public bool IsDirty;
    }

    #region 定义 — 身份与边界

    // 基本的区块信息定义
    public long Id => Coord.Id;    // (1) 哈希性：ID 完全由坐标位运算得到；用于缓存/加载表的 key。
    public ChunkCoord Coord { get; }
    public ChunkSettings Settings { get; }
    public ChunkBounds Bounds { get; }
    public ChunkState State { get; private set; }

    // RequestedLevel：本帧由 LoadSource 提出的目标级别
    // CurrentLevel  ：Pipeline 实际推进到的级别
    public ChunkActivationLevel RequestedLevel { get; private set; } = ChunkActivationLevel.Loaded;
    public ChunkActivationLevel CurrentLevel { get; private set; } = ChunkActivationLevel.None;
    public int ContentSlotCount => _contents.Count;

    public void SetRequestedLevel(ChunkActivationLevel level) => RequestedLevel = level;
    public void SetCurrentLevel(ChunkActivationLevel level) => CurrentLevel = level;

    #endregion

    #region 定义 — 通用内容存储（解耦具体策略）

    // 由 Pipeline 在 Load/Unload 时通过 AttachContent/TryDetachContent 管理。
    // 键 = PayloadType + LayerKey；默认 LayerKey 为空，保持现有按类型隔离的行为。
    private readonly Dictionary<ChunkContentKey, ContentSlot> _contents = new Dictionary<ChunkContentKey, ContentSlot>(4);

    #endregion

    #region 构造

    public ChunkData(ChunkCoord coord, ChunkSettings settings, ChunkState state)
    {
        Coord = coord;
        Settings = settings;
        Bounds = new ChunkBounds(coord, settings);
        State = state;
    }

    public void SetState(ChunkState state) => State = state;

    #endregion

    #region 通用内容附加 / 分离

    /// <summary>由 Pipeline 在 Load 完成后调用：把当前 chunk 的 payload 缓存到本对象上。</summary>
    public void AttachContent<T>(T payload) where T : class =>
        AttachContent(payload, ChunkContentKey.DefaultLayerKey);

    /// <summary>附加某个逻辑层的 payload。用于未来同类型多层数据，例如多套高度图或对象子层。</summary>
    public void AttachContent<T>(T payload, string layerKey) where T : class
    {
        ChunkContentKey key = ChunkContentKey.Create<T>(layerKey);
        bool wasDirty = _contents.TryGetValue(key, out ContentSlot existing) && existing.IsDirty;
        _contents[key] = new ContentSlot
        {
            Payload = payload,
            IsDirty = wasDirty,
        };
    }

    /// <summary>
    /// 由 Pipeline 在 Unload 阶段调用：取出之前 AttachContent 缓存的 payload，
    /// 并把内部引用置空。类型不匹配返回 false（防御性，正常流程不会触发）。
    /// </summary>
    public bool TryDetachContent<T>(out T payload) where T : class =>
        TryDetachContent(ChunkContentKey.DefaultLayerKey, out payload, out _);

    public bool TryDetachContent<T>(out T payload, out bool dirty) where T : class =>
        TryDetachContent(ChunkContentKey.DefaultLayerKey, out payload, out dirty);

    /// <summary>分离某个逻辑层的 payload。</summary>
    public bool TryDetachContent<T>(string layerKey, out T payload, out bool dirty) where T : class
    {
        if (_contents.TryGetValue(ChunkContentKey.Create<T>(layerKey), out ContentSlot slot) && slot.Payload is T p)
        {
            payload = p;
            dirty = slot.IsDirty;
            _contents.Remove(ChunkContentKey.Create<T>(layerKey));
            return true;
        }

        payload = null;
        dirty = false;
        return false;
    }

    /// <summary>
    /// 让外部业务（UI / AI / 交互层）以"只读"方式查询当前 chunk 的 payload。
    /// 返回 false 表示该 chunk 暂未持有这种类型的 payload（未加载完成 / 是别条流水线产物 / 类型不匹配）。
    /// 注意：
    /// - 返回的引用是**活引用**——只读访问安全；如需写入运行时数据，必须经由 IChunkContentEditor
    ///   按"标 dirty + Unload 时回写"的协议进行，否则改动可能不会落盘。
    /// - 不会清空内部引用，与 <see cref="TryDetachContent{T}(out T)"/> 的语义不同：此方法纯查询，不影响生命周期。
    /// </summary>
    public bool TryGetPayload<T>(out T payload) where T : class =>
        TryGetPayload(ChunkContentKey.DefaultLayerKey, out payload);

    /// <summary>按逻辑层查询 payload。</summary>
    public bool TryGetPayload<T>(string layerKey, out T payload) where T : class
    {
        if (_contents.TryGetValue(ChunkContentKey.Create<T>(layerKey), out ContentSlot slot) && slot.Payload is T p)
        {
            payload = p;
            return true;
        }

        payload = null;
        return false;
    }

    public bool MarkContentDirty<T>(string layerKey = null) where T : class
    {
        if (_contents.TryGetValue(ChunkContentKey.Create<T>(layerKey), out ContentSlot slot))
        {
            slot.IsDirty = true;
            return true;
        }

        return false;
    }

    /// <summary>调试/验证：收集当前 chunk 已挂载的 payload 类型名。</summary>
    public void CollectAttachedPayloadTypeNames(ICollection<string> results)
    {
        if (results is null)
        {
            return;
        }

        foreach (var kv in _contents)
        {
            results.Add(kv.Key.ToString());
        }
    }

    #endregion
}

#endregion
