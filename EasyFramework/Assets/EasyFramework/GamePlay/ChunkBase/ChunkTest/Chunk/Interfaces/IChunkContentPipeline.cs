using System;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 区块内容管线（非泛型门面）：把 Generator + Storager 两件套包成一个内聚单元。
/// Manager 只依赖本接口，不知道具体 TPayload 与具体策略，从而和"内容形态"完全解耦。
///
/// 编排逻辑：
///   Load:   storager.TryLoad ?? generator.Generate → storager.SaveAsync → chunk.AttachContent
///   Unload: chunk.TryDetachContent → (脏则) storager.SaveAsync
///
/// 异步扩展：
/// - <see cref="LoadAsync"/> 把 Generate/IO 抬到 worker，回主线程 AttachContent
/// </summary>
public interface IChunkContentPipeline
{
    string Id { get; }
    Type PayloadType { get; }

    /// <summary>加载该区块的内容（同步）。</summary>
    void Load(ChunkData chunk, ChunkSettings settings);

    /// <summary>卸载该区块的内容，按需回写存档（同步）。</summary>
    void Unload(ChunkData chunk, ChunkSettings settings);

    /// <summary>异步加载。</summary>
    UniTask LoadAsync(ChunkData chunk, ChunkSettings settings, CancellationToken ct);

    /// <summary>异步卸载（默认实现：直接调同步版本）。</summary>
    UniTask UnloadAsync(ChunkData chunk, ChunkSettings settings, CancellationToken ct)
    {
        Unload(chunk, settings);
        return UniTask.CompletedTask;
    }
}
