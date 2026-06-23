using System;
using System.Text;
using UnityEngine;

/// <summary>
/// 默认序列化实现：UnityEngine.JsonUtility，落盘走 UTF-8 字节流。
/// 限制：TPayload 必须是 [Serializable] class 且字段为 JsonUtility 可序列化类型（list/array/基本类型/嵌套 [Serializable] 类）。
/// 落盘的 .dat 文件本质上仍是 UTF-8 编码的 JSON 文本，可以用记事本打开调试。
/// 未来要换 Binary / MessagePack / Protobuf 时，新增对应 IChunkContentSerializer&lt;TPayload&gt; 实现即可，
/// FileChunkContentStorager 与 Pipeline 完全不需要改。
/// </summary>
[Serializable]
public sealed class JsonChunkContentSerializer<TPayload> : IChunkContentSerializer<TPayload>
    where TPayload : class, new()
{
    private readonly bool _prettyPrint;

    public JsonChunkContentSerializer(bool prettyPrint = true)
    {
        _prettyPrint = prettyPrint;
    }

    public byte[] Serialize(TPayload payload)
    {
        if (payload is null)
        {
            return Array.Empty<byte>();
        }
        string json = JsonUtility.ToJson(payload, _prettyPrint);
        return Encoding.UTF8.GetBytes(json);
    }

    public TPayload Deserialize(byte[] data, int offset, int length)
    {
        if (data is null || length <= 0)
        {
            return null;
        }

        try
        {
            string json = Encoding.UTF8.GetString(data, offset, length);
            return JsonUtility.FromJson<TPayload>(json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[JsonChunkContentSerializer<{typeof(TPayload).Name}>] 反序列化失败 err={ex}");
            return null;
        }
    }
}
