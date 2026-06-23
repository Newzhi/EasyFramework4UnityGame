using System.Collections.Concurrent;

/// <summary>
/// 内存 Storager：进程内缓存，不写磁盘。供 Debugger 对照或关闭 disk cache 时的快速迭代。
/// </summary>
public sealed class InMemoryChunkContentStorager<TPayload> : IChunkContentStorager<TPayload>
    where TPayload : class, new()
{
    private readonly ConcurrentDictionary<long, TPayload> _cache = new ConcurrentDictionary<long, TPayload>();

    public bool TryLoad(long chunkId, ChunkSettings settings, out TPayload payload)
    {
        return _cache.TryGetValue(chunkId, out payload);
    }

    public bool Save(long chunkId, TPayload payload, ChunkSettings settings)
    {
        if (payload is null)
        {
            return false;
        }

        _cache[chunkId] = payload;
        return true;
    }

    public void SaveAsync(long chunkId, TPayload payload, ChunkSettings settings)
    {
        Save(chunkId, payload, settings);
    }

    public int Count => _cache.Count;

    public void Clear() => _cache.Clear();
}
