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
