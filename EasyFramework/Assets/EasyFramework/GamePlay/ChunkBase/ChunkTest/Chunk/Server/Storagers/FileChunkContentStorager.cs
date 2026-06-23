using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 通用文件持久化策略：路径解析 + Envelope 文件头 + 可插拔 Serializer。
/// - 文件名：<c>chunk_{chunkId}{fileSuffix}.dat</c>
/// - Legacy：无 CHKT 头的整文件 JSON 仍可读
/// </summary>
[Serializable]
public sealed class FileChunkContentStorager<TPayload> : IChunkContentStorager<TPayload>
    where TPayload : class, new()
{
    private readonly IChunkContentSerializer<TPayload> _serializer;
    private readonly IChunkContentSerializer<TPayload> _jsonSerializer;
    private readonly byte _serializerKind;
    private readonly string _fileSuffix;
    private readonly string _fileExtension;

    private readonly ConcurrentDictionary<long, PendingSave> _pendingSaves = new ConcurrentDictionary<long, PendingSave>();
    private readonly AsyncAutoResetEventLite _saveSignal = new AsyncAutoResetEventLite(initialState: false);
    private int _workerStarted;

    public FileChunkContentStorager(
        IChunkContentSerializer<TPayload> serializer,
        string fileSuffix = "",
        string fileExtension = ".dat",
        byte serializerKind = ChunkStorageSerializerKind.Json)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _serializerKind = serializerKind;
        _fileSuffix = fileSuffix ?? string.Empty;
        _fileExtension = string.IsNullOrEmpty(fileExtension) ? ".dat" : fileExtension;
        _jsonSerializer = serializerKind == ChunkStorageSerializerKind.Json
            ? serializer
            : new JsonChunkContentSerializer<TPayload>(prettyPrint: false);
    }

    #region IChunkContentStorager

    public bool TryLoad(long chunkId, ChunkSettings settings, out TPayload payload)
    {
        using (ChunkProfilerMarkers.StoreFileTryLoad.Auto())
        {
            payload = null;

            if (!settings.EnableChunkObjectDiskCache)
            {
                return false;
            }

            try
            {
                string path = GetChunkFilePath(chunkId, settings);
                if (!File.Exists(path))
                {
                    ChunkLog.Verbose(settings, () =>
                        $"[FileChunkContentStorager<{typeof(TPayload).Name}>] cache miss (no file) chunkId={chunkId} path={path}");
                    return false;
                }

                byte[] fileBytes = File.ReadAllBytes(path);
                if (!ChunkStorageEnvelope.TryRead(fileBytes, out byte kind, out byte[] bodyBytes, out bool isLegacy))
                {
                    ChunkLog.Verbose(settings, () =>
                        $"[FileChunkContentStorager<{typeof(TPayload).Name}>] cache miss (envelope parse fail) chunkId={chunkId} path={path}");
                    return false;
                }

                if (!isLegacy && kind != _serializerKind && kind != ChunkStorageSerializerKind.Json)
                {
                    ChunkLog.Warn(
                        $"[FileChunkContentStorager<{typeof(TPayload).Name}>] serializer kind mismatch chunkId={chunkId} fileKind={kind} expected={_serializerKind}");
                    return false;
                }

                IChunkContentSerializer<TPayload> reader = isLegacy || kind == ChunkStorageSerializerKind.Json
                    ? _jsonSerializer
                    : _serializer;

                payload = reader.Deserialize(bodyBytes, 0, bodyBytes.Length);
                if (payload is null)
                {
                    ChunkLog.Verbose(settings, () =>
                        $"[FileChunkContentStorager<{typeof(TPayload).Name}>] cache miss (deserialize null) chunkId={chunkId} legacy={isLegacy} kind={kind}");
                    return false;
                }

                if (!ChunkPayloadValidator.TryValidate(payload, chunkId, settings, out string reason))
                {
                    ChunkLog.Warn(
                        $"[FileChunkContentStorager<{typeof(TPayload).Name}>] validate fail chunkId={chunkId} path={path} reason={reason}");
                    payload = null;
                    return false;
                }

                ChunkLog.Verbose(settings, () =>
                    $"[FileChunkContentStorager<{typeof(TPayload).Name}>] cache hit chunkId={chunkId} legacy={isLegacy} kind={kind} path={path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileChunkContentStorager<{typeof(TPayload).Name}>] 读取失败 chunkId={chunkId} err={ex}");
                payload = null;
                return false;
            }
        }
    }

    public bool Save(long chunkId, TPayload payload, ChunkSettings settings)
    {
        if (!settings.EnableChunkObjectDiskCache || payload is null)
        {
            return false;
        }

        try
        {
            string dir = ChunkStoragePathResolver.ResolveCacheDirectory(settings);
            Directory.CreateDirectory(dir);

            byte[] fileBytes = BuildFileBytes(payload);
            File.WriteAllBytes(GetChunkFilePath(chunkId, settings), fileBytes);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FileChunkContentStorager<{typeof(TPayload).Name}>] 保存失败 chunkId={chunkId} err={ex}");
            return false;
        }
    }

    public void SaveAsync(long chunkId, TPayload payload, ChunkSettings settings)
    {
        using (ChunkProfilerMarkers.StoreFileSaveQueue.Auto())
        {
            if (!settings.EnableChunkObjectDiskCache || payload is null)
            {
                return;
            }

            EnsureWorkerStarted();

            string dir = ChunkStoragePathResolver.ResolveCacheDirectory(settings);
            string path = GetChunkFilePath(chunkId, settings);
            byte[] fileBytes = BuildFileBytes(payload);

            _pendingSaves[chunkId] = new PendingSave(dir, path, fileBytes);
            _saveSignal.Set();
        }
    }

    #endregion

    private byte[] BuildFileBytes(TPayload payload)
    {
        using (ChunkProfilerMarkers.StoreFileSerialize.Auto())
        {
            byte[] bodyBytes = _serializer.Serialize(payload);
            return ChunkStorageEnvelope.Write(_serializerKind, bodyBytes);
        }
    }

    private string GetChunkFilePath(long chunkId, ChunkSettings settings)
    {
        return ChunkStoragePathResolver.ResolveChunkFilePath(settings, chunkId, _fileSuffix, _fileExtension);
    }

    #region 异步写入工作循环

    private void EnsureWorkerStarted()
    {
        if (Interlocked.CompareExchange(ref _workerStarted, 1, 0) != 0)
        {
            return;
        }

        SaveWorkerLoop().Forget();
    }

    private async UniTaskVoid SaveWorkerLoop()
    {
        while (true)
        {
            try
            {
                await _saveSignal.WaitAsync();

                await UniTask.RunOnThreadPool(() =>
                {
                    foreach (var kv in _pendingSaves)
                    {
                        long chunkId = kv.Key;
                        if (!_pendingSaves.TryRemove(chunkId, out PendingSave save))
                        {
                            continue;
                        }

                        Directory.CreateDirectory(save.Dir);
                        File.WriteAllBytes(save.Path, save.Bytes);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileChunkContentStorager<{typeof(TPayload).Name}>] 异步保存线程异常 err={ex}");
            }
        }
    }

    #endregion

    private readonly struct PendingSave
    {
        public readonly string Dir;
        public readonly string Path;
        public readonly byte[] Bytes;

        public PendingSave(string dir, string path, byte[] bytes)
        {
            Dir = dir;
            Path = path;
            Bytes = bytes;
        }
    }
}
