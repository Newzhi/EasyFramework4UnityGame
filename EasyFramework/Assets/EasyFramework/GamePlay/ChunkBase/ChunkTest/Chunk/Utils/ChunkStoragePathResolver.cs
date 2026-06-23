using System.IO;
using UnityEngine;

/// <summary>
/// 区块磁盘缓存路径解析：Editor / 打包平台统一入口。
/// </summary>
public static class ChunkStoragePathResolver
{
    public static string ResolveCacheRoot()
    {
#if UNITY_EDITOR
        return Application.dataPath;
#else
        return Application.persistentDataPath;
#endif
    }

    public static string ResolveCacheDirectory(in ChunkSettings settings)
    {
        string subfolder = string.IsNullOrWhiteSpace(settings.CacheSubfolder)
            ? ChunkSettingsDefaults.CacheSubfolder
            : settings.CacheSubfolder.Trim();
        return Path.Combine(
            ResolveCacheRoot(),
            subfolder.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string ResolveChunkFilePath(
        in ChunkSettings settings,
        long chunkId,
        string fileSuffix,
        string fileExtension)
    {
        string suffix = fileSuffix ?? string.Empty;
        string ext = string.IsNullOrEmpty(fileExtension) ? ".dat" : fileExtension;
        return Path.Combine(ResolveCacheDirectory(settings), $"chunk_{chunkId}{suffix}{ext}");
    }
}
