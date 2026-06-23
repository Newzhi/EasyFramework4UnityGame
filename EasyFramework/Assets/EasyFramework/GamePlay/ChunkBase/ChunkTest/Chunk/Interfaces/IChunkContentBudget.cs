/// <summary>
/// Chunk 框架预算接口。Manager 只读取预算，不关心预算来自 Inspector、设备档位还是动态调节器。
/// </summary>
public interface IChunkContentBudget
{
    int MaxLoadPerFrame { get; }
    int MaxUnloadPerFrame { get; }
    int MaxConcurrentLoads { get; }
}
