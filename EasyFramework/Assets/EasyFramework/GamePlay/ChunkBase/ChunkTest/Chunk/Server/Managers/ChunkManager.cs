using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 区块管理器：纯调度层。
/// - 调度：窗口刷新（委托 <see cref="IChunkLoadSource"/>）、Load/Unload、维护 ChunkData 缓存
/// - 内容：完全委托 <see cref="IChunkContentPipeline"/>（由 Generator + Storager 组合而成），本类不感知具体策略
///
/// 本类只剩三件事：
///   1) Composition Root：在 Awake 里把 Generator + Storager 组装成 Pipeline，组装 LoadSource
///   2) 区块字典 + 状态机：LoadChunk / UnloadChunk / TryGetChunk
///   3) RefreshChunksAroundPlayer：让 LoadSource 追加目标坐标 → 对比现有 → 调 Pipeline 加载/卸载
/// </summary>
public class ChunkManager : MonoBehaviour, IChunkManager, IChunkContentReader, IChunkContentStatsProvider
{
    #region 定义

    [Header("Player Center")]
    [SerializeField] private Transform player;

    private readonly List<IChunkContentPipeline> pipelines = new List<IChunkContentPipeline>(1);
    private readonly List<IChunkLoadSource> loadSources = new List<IChunkLoadSource>(1);

    private readonly Dictionary<long, ChunkData> chunks = new Dictionary<long, ChunkData>();
    private ChunkSettings settings;
    private ChunkCoord lastCenterCoord;
    private bool hasLastCenterCoord;

    private readonly HashSet<ChunkCoord> targetCoordsBuffer = new HashSet<ChunkCoord>();
    private readonly HashSet<long> targetChunkIds = new HashSet<long>();
    private readonly ChunkLoadQueue pendingLoadQueue = new ChunkLoadQueue();
    private readonly List<long> pendingUnloadIds = new List<long>();
    private readonly ChunkLoadThrottler throttler = new ChunkLoadThrottler();
    private readonly List<ChunkCoord> loadBatchBuffer = new List<ChunkCoord>(64);
    private readonly List<long> unloadBatchBuffer = new List<long>(64);
    private readonly HashSet<long> inFlightLoadIds = new HashSet<long>();
    private CancellationTokenSource destroyCts;

    public int PendingLoadCount => pendingLoadQueue.Count;
    public int PendingUnloadCount => pendingUnloadIds.Count;
    public int InFlightLoadCount => inFlightLoadIds.Count;

    public IReadOnlyDictionary<long, ChunkData> Chunks => chunks;

    /// <summary>payload 加载完成后触发。勿在回调内再触发 Load/Unload。</summary>
    public event Action<ChunkData> OnChunkLoaded;

    /// <summary>进入 Unload 流程、Pipeline.Unload 之前触发。</summary>
    public event Action<ChunkData> OnChunkUnloaded;

    /// <summary><see cref="ChunkData.CurrentLevel"/> 实际变化时触发。</summary>
    public event Action<ChunkData, ChunkActivationLevel, ChunkActivationLevel> OnChunkLevelChanged;

    /// <summary>payload 经由 IChunkContentEditor 编辑并标 dirty 后触发。</summary>
    public event Action<ChunkData, ChunkContentKey> OnChunkContentEdited;

    #endregion

    #region 生命周期

    private void Awake()
    {
        destroyCts = new CancellationTokenSource();
        settings = ChunkSettingsDefaults.Default;
        BuildPipelines();
        InitPlayerReference();
        BuildLoadSources();
    }

    private void OnDestroy()
    {
        destroyCts?.Cancel();
        destroyCts?.Dispose();
        destroyCts = null;
        OnChunkLoaded = null;
        OnChunkUnloaded = null;
        OnChunkLevelChanged = null;
        OnChunkContentEdited = null;
    }

    private void Start()
    {
        RefreshChunksAroundPlayer(force: true);
    }

    private void LateUpdate()
    {
        RefreshChunksAroundPlayer(force: false);
        TickPendingLoadUnload();
    }

    #endregion

    #region Composition Root

    private void BuildPipelines()
    {
        pipelines.Clear();
        pipelines.Add(new ChunkContentPipeline<HeightmapPayload>(
            new FbmHeightmapGenerator(),
            new FileChunkContentStorager<HeightmapPayload>(
                new JsonChunkContentSerializer<HeightmapPayload>(),
                fileSuffix: "_terrain"),
            "terrain.heightmap"));
    }

    private void BuildLoadSources()
    {
        loadSources.Clear();
        loadSources.Add(new CircularChunkLoadSource(player));
    }

    #endregion

    #region IChunkManager

    public ChunkData LoadChunk(ChunkCoord coord) => LoadChunk(coord, ChunkActivationLevel.Loaded);

    public ChunkData LoadChunk(ChunkCoord coord, ChunkActivationLevel level)
    {
        long id = coord.Id;
        if (chunks.TryGetValue(id, out ChunkData existing))
        {
            if (level > existing.RequestedLevel)
            {
                existing.SetRequestedLevel(level);
            }
            existing.SetState(ChunkState.Active);
            if (existing.CurrentLevel < ChunkActivationLevel.Loaded)
            {
                RunAllPipelinesLoad(existing);
            }
            return existing;
        }

        ChunkData chunk = new ChunkData(coord, settings, ChunkState.Loading);
        chunk.SetRequestedLevel(level);
        chunk.SetState(ChunkState.Active);
        chunks.Add(id, chunk);
        RunAllPipelinesLoad(chunk);
        return chunk;
    }

    public bool UnloadChunk(ChunkCoord coord)
    {
        long id = coord.Id;
        if (!chunks.TryGetValue(id, out ChunkData chunk))
        {
            return false;
        }

        ChunkActivationLevel prevLevel = chunk.CurrentLevel;
        OnChunkUnloaded?.Invoke(chunk);

        chunk.SetState(ChunkState.Unloaded);
        for (int i = pipelines.Count - 1; i >= 0; i--)
        {
            pipelines[i].Unload(chunk, settings);
        }

        if (chunk.CurrentLevel != prevLevel)
        {
            OnChunkLevelChanged?.Invoke(chunk, prevLevel, chunk.CurrentLevel);
        }

        return chunks.Remove(id);
    }

    public bool TryGetChunk(long chunkId, out ChunkData chunk) => chunks.TryGetValue(chunkId, out chunk);

    public bool TryGetNeighbor(ChunkCoord coord, int dx, int dz, ChunkActivationLevel minLevel, out ChunkData neighbor)
    {
        ChunkCoord nc = coord.Offset(dx, dz);
        if (TryGetChunk(nc.Id, out neighbor) && neighbor is not null && neighbor.CurrentLevel >= minLevel)
        {
            return true;
        }

        neighbor = null;
        return false;
    }

    bool IChunkContentReader.TryGetPayload<T>(ChunkCoord coord, out T payload) =>
        TryGetChunkPayload(coord, out payload);

    private void RunAllPipelinesLoad(ChunkData chunk)
    {
        ChunkActivationLevel prevLevel = chunk.CurrentLevel;
        for (int i = 0; i < pipelines.Count; i++)
        {
            pipelines[i].Load(chunk, settings);
        }

        ChunkActivationLevel currLevel = chunk.CurrentLevel;
        if (currLevel != prevLevel)
        {
            OnChunkLevelChanged?.Invoke(chunk, prevLevel, currLevel);
        }

        if (prevLevel < ChunkActivationLevel.Loaded && currLevel >= ChunkActivationLevel.Loaded)
        {
            OnChunkLoaded?.Invoke(chunk);
        }
    }

    public bool TryGetChunkPayload<T>(ChunkCoord coord, out T payload) where T : class
    {
        if (chunks.TryGetValue(coord.Id, out ChunkData chunk))
        {
            return chunk.TryGetPayload(out payload);
        }
        payload = null;
        return false;
    }

    public bool TryEditChunkContent<TPayload>(
        ChunkCoord coord,
        IChunkContentEditor<TPayload> editor,
        ChunkContentEditContext context,
        string layerKey = null)
        where TPayload : class
    {
        if (editor is null || !chunks.TryGetValue(coord.Id, out ChunkData chunk))
        {
            return false;
        }

        if (!chunk.TryGetPayload(layerKey, out TPayload payload))
        {
            return false;
        }

        if (!editor.TryEdit(chunk, payload, context))
        {
            return false;
        }

        chunk.MarkContentDirty<TPayload>(layerKey);
        OnChunkContentEdited?.Invoke(chunk, ChunkContentKey.Create<TPayload>(layerKey));
        return true;
    }

    public ChunkContentStats CaptureStats()
    {
        var stats = new ChunkContentStats
        {
            TotalChunks = chunks.Count,
            PendingLoad = pendingLoadQueue.Count,
            PendingUnload = pendingUnloadIds.Count,
            InFlightLoad = inFlightLoadIds.Count,
            PipelineCount = pipelines.Count,
        };

        foreach (var kv in chunks)
        {
            ChunkData chunk = kv.Value;
            if (chunk.CurrentLevel >= ChunkActivationLevel.Loaded)
            {
                stats.LoadedChunks++;
            }

            stats.ContentSlotCount += chunk.ContentSlotCount;
        }

        return stats;
    }

    #endregion

    #region 窗口刷新

    private void InitPlayerReference()
    {
        if (player is not null)
        {
            return;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj is not null)
        {
            player = playerObj.transform;
        }
    }

    private void RefreshChunksAroundPlayer(bool force)
    {
        using (ChunkProfilerMarkers.ManagerRefresh.Auto())
        {
            RefreshChunksAroundPlayerCore(force);
        }
    }

    private void RefreshChunksAroundPlayerCore(bool force)
    {
        if (pipelines.Count == 0 || loadSources.Count == 0)
        {
            return;
        }

        bool centerChanged;
        ChunkCoord center = default;
        if (player is not null)
        {
            center = ChunkUtil.WorldToChunkCoord(player.position, settings.Size);
            centerChanged = !hasLastCenterCoord || center.X != lastCenterCoord.X || center.Z != lastCenterCoord.Z;
        }
        else
        {
            centerChanged = false;
        }

        if (!force && !centerChanged)
        {
            return;
        }

        if (player is not null && settings.LogPlayerEnterChunk && centerChanged)
        {
            Debug.Log($"[ChunkManager] Player 进入区块 id={center.Id} coord=({center.X}, {center.Z})");
        }

        targetCoordsBuffer.Clear();
        for (int i = 0; i < loadSources.Count; i++)
        {
            loadSources[i].CollectTargetChunks(settings, targetCoordsBuffer);
        }

        targetChunkIds.Clear();
        foreach (ChunkCoord coord in targetCoordsBuffer)
        {
            targetChunkIds.Add(coord.Id);
        }

        Vector3 playerWorld = player is not null ? player.position : Vector3.zero;
        pendingLoadQueue.Clear();
        foreach (ChunkCoord coord in targetCoordsBuffer)
        {
            float priority = ChunkPriority.ComputeSqrDistanceToPlayer(coord, playerWorld, settings.Size);
            if (chunks.ContainsKey(coord.Id))
            {
                continue;
            }
            pendingLoadQueue.Enqueue(coord, priority);
        }

        pendingUnloadIds.Clear();
        foreach (long loadedId in chunks.Keys)
        {
            if (!targetChunkIds.Contains(loadedId))
            {
                pendingUnloadIds.Add(loadedId);
            }
        }
        if (pendingUnloadIds.Count > 1)
        {
            pendingUnloadIds.Sort((a, b) =>
            {
                if (!chunks.TryGetValue(a, out ChunkData ca) || !chunks.TryGetValue(b, out ChunkData cb))
                {
                    return 0;
                }
                float da = ChunkPriority.ComputeSqrDistanceToPlayer(ca.Coord, playerWorld, settings.Size);
                float db = ChunkPriority.ComputeSqrDistanceToPlayer(cb.Coord, playerWorld, settings.Size);
                return db.CompareTo(da);
            });
        }

        ChunkLog.Verbose(settings, () =>
            $"[ChunkManager] Refresh: targets={targetCoordsBuffer.Count} pendingLoad={pendingLoadQueue.Count} pendingUnload={pendingUnloadIds.Count}");

        if (player is not null)
        {
            lastCenterCoord = center;
            hasLastCenterCoord = true;
        }
    }

    private void TickPendingLoadUnload()
    {
        if (pipelines.Count == 0)
        {
            return;
        }

        using (ChunkProfilerMarkers.ManagerTick.Auto())
        {
            TickPendingLoadUnloadCore();
        }
    }

    private void TickPendingLoadUnloadCore()
    {
        int loadBudget = settings.MaxLoadPerFrame > 0 ? settings.MaxLoadPerFrame : int.MaxValue;
        int unloadBudget = settings.MaxUnloadPerFrame > 0 ? settings.MaxUnloadPerFrame : int.MaxValue;
        int asyncSlots = GetAvailableAsyncLoadSlots();
        if (asyncSlots <= 0)
        {
            loadBudget = 0;
        }
        else if (loadBudget > asyncSlots)
        {
            loadBudget = asyncSlots;
        }

        if (pendingUnloadIds.Count > 0)
        {
            unloadBatchBuffer.Clear();
            int unloadTaken = throttler.TakeUnloadBatch(pendingUnloadIds, unloadBudget, unloadBatchBuffer);
            for (int i = 0; i < unloadTaken; i++)
            {
                long unloadId = unloadBatchBuffer[i];
                if (chunks.TryGetValue(unloadId, out ChunkData chunk))
                {
                    UnloadChunk(chunk.Coord);
                }
            }
        }

        if (pendingLoadQueue.Count > 0 && loadBudget > 0)
        {
            loadBatchBuffer.Clear();
            int loadTaken = throttler.DequeueLoadBatch(pendingLoadQueue, loadBudget, loadBatchBuffer);
            for (int i = 0; i < loadTaken; i++)
            {
                ChunkCoord coord = loadBatchBuffer[i];
                if (!chunks.ContainsKey(coord.Id) && !inFlightLoadIds.Contains(coord.Id))
                {
                    ScheduleLoadChunkAsync(coord, ChunkActivationLevel.Loaded);
                }
            }
        }
    }

    private int GetAvailableAsyncLoadSlots()
    {
        int max = settings.MaxConcurrentLoads > 0
            ? settings.MaxConcurrentLoads
            : Mathf.Max(1, Environment.ProcessorCount - 1);
        return Mathf.Max(0, max - inFlightLoadIds.Count);
    }

    private void ScheduleLoadChunkAsync(ChunkCoord coord, ChunkActivationLevel level)
    {
        long id = coord.Id;
        if (inFlightLoadIds.Contains(id))
        {
            return;
        }

        ChunkData chunk;
        if (chunks.TryGetValue(id, out ChunkData existing))
        {
            chunk = existing;
            if (level > chunk.RequestedLevel)
            {
                chunk.SetRequestedLevel(level);
            }
            chunk.SetState(ChunkState.Active);
        }
        else
        {
            chunk = new ChunkData(coord, settings, ChunkState.Loading);
            chunk.SetRequestedLevel(level);
            chunk.SetState(ChunkState.Active);
            chunks.Add(id, chunk);
        }

        ScheduleRunAllPipelinesLoadAsync(chunk);
    }

    private void ScheduleRunAllPipelinesLoadAsync(ChunkData chunk)
    {
        if (chunk is null || !inFlightLoadIds.Add(chunk.Id))
        {
            return;
        }

        RunAllPipelinesLoadAsync(chunk, destroyCts?.Token ?? CancellationToken.None).Forget();
    }

    private async UniTaskVoid RunAllPipelinesLoadAsync(ChunkData chunk, CancellationToken ct)
    {
        try
        {
            ChunkActivationLevel prevLevel = chunk.CurrentLevel;
            for (int i = 0; i < pipelines.Count; i++)
            {
                await pipelines[i].LoadAsync(chunk, settings, ct);
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            ChunkActivationLevel currLevel = chunk.CurrentLevel;
            if (currLevel != prevLevel)
            {
                OnChunkLevelChanged?.Invoke(chunk, prevLevel, currLevel);
            }

            if (prevLevel < ChunkActivationLevel.Loaded && currLevel >= ChunkActivationLevel.Loaded)
            {
                OnChunkLoaded?.Invoke(chunk);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ChunkManager] 异步加载失败 chunkId={chunk?.Id} err={ex}");
        }
        finally
        {
            if (chunk is not null)
            {
                inFlightLoadIds.Remove(chunk.Id);
            }
        }
    }

    public void ForceRefresh() => RefreshChunksAroundPlayer(force: true);

    #endregion

    #region 调试方法

    private void OnDrawGizmos()
    {
        if (!settings.DrawActiveChunkWireframe || chunks.Count == 0)
        {
            return;
        }

        Gizmos.color = settings.ActiveChunkWireColor;
        foreach (ChunkData chunk in chunks.Values)
        {
            if (chunk is not { State: ChunkState.Active })
            {
                continue;
            }

            ChunkBounds bounds = chunk.Bounds;
            float sizeY = bounds.MaxYInclusive - bounds.MinY + 1;
            Vector3 center = new Vector3(
                bounds.MinX + bounds.Size * 0.5f,
                bounds.MinY + sizeY * 0.5f,
                bounds.MinZ + bounds.Size * 0.5f);
            Vector3 size = new Vector3(bounds.Size, sizeY, bounds.Size);

            Gizmos.DrawWireCube(center, size);
        }
    }

    #endregion
}
