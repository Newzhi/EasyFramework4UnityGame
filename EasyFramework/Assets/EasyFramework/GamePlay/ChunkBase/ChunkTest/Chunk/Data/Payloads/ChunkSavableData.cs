using System;
using System.Collections.Generic;

#region 可序列化存档 Payload（按内容形态拆分，避免胖 DTO）

/// <summary>
/// Heightmap 策略的强类型 payload：行主序 (chunkSize+1)² 高度数组，值为相对 chunk 原点 Y 的偏移。
/// 由 FbmHeightmapGenerator 生产、Storager 通过 IChunkContentSerializer 落盘。
/// </summary>
[Serializable]
public sealed class HeightmapPayload
{
    public long chunkId;
    public List<float> terrainHeights;
}

#endregion
