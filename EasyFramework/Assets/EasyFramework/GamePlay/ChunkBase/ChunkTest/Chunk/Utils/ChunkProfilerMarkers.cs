using Unity.Profiling;

/// <summary>Chunk 框架 hot-path Profiler 标签（Release 下 no-op）。</summary>
public static class ChunkProfilerMarkers
{
    public static readonly ProfilerMarker ManagerTick = new("ChunkManager.Tick");
    public static readonly ProfilerMarker ManagerRefresh = new("ChunkManager.Refresh");
    public static readonly ProfilerMarker PipelineLoad = new("ChunkPipeline.Load");
    public static readonly ProfilerMarker PipelineLoadAsync = new("ChunkPipeline.LoadAsync");
    public static readonly ProfilerMarker PipelineAcquirePayload = new("ChunkPipeline.AcquirePayload");
    public static readonly ProfilerMarker PipelineUnload = new("ChunkPipeline.Unload");
    public static readonly ProfilerMarker GenFbmHeightmap = new("ChunkGen.FbmHeightmap");
    public static readonly ProfilerMarker StoreFile = new("ChunkStore.File");
    public static readonly ProfilerMarker StoreFileTryLoad = new("ChunkStore.File.TryLoad");
    public static readonly ProfilerMarker StoreFileSerialize = new("ChunkStore.File.Serialize");
    public static readonly ProfilerMarker StoreFileSaveQueue = new("ChunkStore.File.SaveQueue");
}
