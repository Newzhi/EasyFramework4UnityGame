/// <summary>
/// 读盘后的 payload 校验：失败时 TryLoad 返回 false，走 Regenerate。
/// </summary>
public static class ChunkPayloadValidator
{
    public static bool TryValidate(object payload, long chunkId, in ChunkSettings settings, out string reason)
    {
        if (payload is HeightmapPayload heightmap)
        {
            return TryValidateHeightmap(heightmap, chunkId, settings, out reason);
        }

        reason = null;
        return true;
    }

    public static bool TryValidateHeightmap(
        HeightmapPayload payload,
        long chunkId,
        in ChunkSettings settings,
        out string reason)
    {
        reason = null;
        if (payload is null)
        {
            reason = "payload is null";
            return false;
        }

        if (payload.chunkId != chunkId)
        {
            reason = $"chunkId mismatch: file={payload.chunkId} expected={chunkId}";
            return false;
        }

        int expectedCount = (settings.Size + 1) * (settings.Size + 1);
        int actualCount = payload.terrainHeights?.Count ?? 0;
        if (actualCount != expectedCount)
        {
            reason = $"terrainHeights count mismatch: file={actualCount} expected={expectedCount}";
            return false;
        }

        return true;
    }
}
