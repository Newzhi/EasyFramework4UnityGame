/// <summary>
/// 区块内容持久化策略（IO 编排）：与 <typeparamref name="TPayload"/> 类型绑定，但与"如何生成"无关。
/// 职责边界：只关心"按 chunkId 读/写 payload"；具体存储介质（文件/内存/远程）与序列化格式
/// （JSON/Binary/MessagePack…）通过组合 <see cref="IChunkContentSerializer{TPayload}"/> 解耦。
/// </summary>
public interface IChunkContentStorager<TPayload> where TPayload : class, new()
{
    /// <summary>尝试读取已存在的区块 payload；未命中或被禁用时返回 false。</summary>
    bool TryLoad(long chunkId, ChunkSettings settings, out TPayload payload);

    /// <summary>同步写入（阻塞当前线程）。</summary>
    bool Save(long chunkId, TPayload payload, ChunkSettings settings);

    /// <summary>异步/队列写入；实现可合并同一 chunk 的多次保存。</summary>
    void SaveAsync(long chunkId, TPayload payload, ChunkSettings settings);
}
