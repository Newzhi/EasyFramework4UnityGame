using System.Diagnostics;
using UnityEngine;

/// <summary>
/// Chunk 系统统一日志包装：
/// - Verbose / Info：受 <c>ChunkSettings.LogVerbose</c> 控制（运行时开关）
/// - Warn / Error：始终直出（生产环境也要看到）
/// - 用 [Conditional("UNITY_EDITOR")] 把 Verbose 路径在发布构建里整段编译掉，避免字符串拼接成本
///
/// 用法：
///   ChunkLog.Verbose(settings, () => $"...");   // lambda 仅在 verbose 开启时求值，零分配热路径
///   ChunkLog.Warn("[ChunkManager] foo");
/// </summary>
public static class ChunkLog
{
    [Conditional("UNITY_EDITOR"), Conditional("CHUNK_LOG_VERBOSE")]
    public static void Verbose(in ChunkSettings settings, string message)
    {
        if (settings.LogVerbose)
        {
            UnityEngine.Debug.Log(message);
        }
    }

    [Conditional("UNITY_EDITOR"), Conditional("CHUNK_LOG_VERBOSE")]
    public static void Verbose(in ChunkSettings settings, System.Func<string> messageFactory)
    {
        if (settings.LogVerbose && messageFactory != null)
        {
            UnityEngine.Debug.Log(messageFactory());
        }
    }

    public static void Info(string message) => UnityEngine.Debug.Log(message);

    public static void Warn(string message) => UnityEngine.Debug.LogWarning(message);

    public static void Error(string message) => UnityEngine.Debug.LogError(message);
}
