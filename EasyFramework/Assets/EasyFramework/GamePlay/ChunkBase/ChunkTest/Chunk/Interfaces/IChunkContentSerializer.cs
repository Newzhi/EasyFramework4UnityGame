/// <summary>
/// 区块 payload 的序列化/反序列化策略：与 IO 介质正交。
///
/// 统一走 byte[] 是为了让"文本格式（如 JSON）"和"二进制格式"共用同一接口、
/// 同一 Storager（Storager 只关心字节流，不关心文本/二进制）。
///
/// JSON 实现内部 Encoding.UTF8.GetBytes(JsonUtility.ToJson(...))，落盘的 .dat 仍然是人眼可读的 JSON 文本。
/// 未来要换 Binary / MessagePack / Protobuf 时，新增一个实现即可，Storager 不用动。
/// </summary>
public interface IChunkContentSerializer<TPayload> where TPayload : class, new()
{
    /// <summary>把 payload 序列化为字节数组；payload 为 null 时返回长度为 0 的空数组。</summary>
    byte[] Serialize(TPayload payload);

    /// <summary>从字节流的 [offset, offset+length) 区间反序列化为 payload；失败返回 null。</summary>
    TPayload Deserialize(byte[] data, int offset, int length);
}
