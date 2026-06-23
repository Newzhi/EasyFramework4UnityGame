using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 区块内容管线的泛型实现：把"先查存档→否则生成→存盘"的核心流程在此处只写一份，
/// Generator 不再调 Storager，Storager 也不再知道场景。二者只通过 TPayload 在编译期关联。
///
/// MVP 两阶段 Load 流程：
///   None → Loaded：TryLoad / Generate → SaveAsync → AttachContent(payload)
///
/// Unload：TryDetachContent → 仅在 dirty 时 SaveAsync
/// </summary>
public sealed class ChunkContentPipeline<TPayload> : IChunkContentPipeline
    where TPayload : class, new()
{
    private readonly IChunkContentGenerator<TPayload> _generator;
    private readonly IChunkContentStorager<TPayload> _storager;

    public string Id { get; }
    public Type PayloadType => typeof(TPayload);

    public ChunkContentPipeline(
        IChunkContentGenerator<TPayload> generator,
        IChunkContentStorager<TPayload> storager,
        string id = null)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _storager = storager ?? throw new ArgumentNullException(nameof(storager));
        Id = string.IsNullOrEmpty(id) ? typeof(TPayload).Name : id;
    }

    public void Load(ChunkData chunk, ChunkSettings settings)
    {
        if (chunk is null)
        {
            return;
        }

        using (ChunkProfilerMarkers.PipelineLoad.Auto())
        {
            if (chunk.TryGetPayload<TPayload>(out _))
            {
                chunk.SetCurrentLevel(ChunkActivationLevel.Loaded);
                return;
            }

            TPayload payload = AcquirePayload(chunk, settings);
            if (payload is null)
            {
                return;
            }

            chunk.AttachContent(payload);
            chunk.SetCurrentLevel(ChunkActivationLevel.Loaded);
        }
    }

    public async UniTask LoadAsync(ChunkData chunk, ChunkSettings settings, CancellationToken ct)
    {
        if (chunk is null)
        {
            return;
        }

        if (chunk.TryGetPayload<TPayload>(out _))
        {
            chunk.SetCurrentLevel(ChunkActivationLevel.Loaded);
            return;
        }

        TPayload payload;
        await UniTask.SwitchToThreadPool();
        ct.ThrowIfCancellationRequested();
        payload = AcquirePayload(chunk, settings);
        await UniTask.SwitchToMainThread();
        ct.ThrowIfCancellationRequested();

        if (payload is null)
        {
            return;
        }

        using (ChunkProfilerMarkers.PipelineLoadAsync.Auto())
        {
            chunk.AttachContent(payload);
            chunk.SetCurrentLevel(ChunkActivationLevel.Loaded);
        }
    }

    public void Unload(ChunkData chunk, ChunkSettings settings)
    {
        if (chunk is null)
        {
            return;
        }

        using (ChunkProfilerMarkers.PipelineUnload.Auto())
        {
            if (!chunk.TryDetachContent(out TPayload payload, out bool dirty))
            {
                chunk.SetCurrentLevel(ChunkActivationLevel.None);
                return;
            }

            if (dirty && payload is not null)
            {
                _storager.SaveAsync(chunk.Id, payload, settings);
            }

            chunk.SetCurrentLevel(ChunkActivationLevel.None);
        }
    }

    private TPayload AcquirePayload(ChunkData chunk, ChunkSettings settings)
    {
        using (ChunkProfilerMarkers.PipelineAcquirePayload.Auto())
        {
            if (_storager.TryLoad(chunk.Id, settings, out TPayload payload))
            {
                return payload;
            }

            payload = _generator.Generate(chunk.Coord, settings);
            if (payload is not null)
            {
                _storager.SaveAsync(chunk.Id, payload, settings);
            }

            return payload;
        }
    }
}
