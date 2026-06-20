using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Android/iOS 等平台上 StreamingAssets 为 jar:/ 协议路径，不能用 System.IO.File 直接读。
/// </summary>
public static class StreamingAssetsIO
{
    public static bool IsNonFileProtocolPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        return path.IndexOf("://", StringComparison.Ordinal) >= 0;
    }

    public static string CombinePath(string root, params string[] parts)
    {
        if (string.IsNullOrEmpty(root))
            return parts.Length > 0 ? parts[0] : root;

        string path = root.Replace('\\', '/').TrimEnd('/');
        foreach (string part in parts)
        {
            if (string.IsNullOrEmpty(part))
                continue;

            path += "/" + part.Replace('\\', '/').TrimStart('/');
        }

        return path;
    }

    public static bool FileExists(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (!IsNonFileProtocolPath(path))
            return File.Exists(path);

        try
        {
            ReadAllText(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ReadAllText(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("path is empty", nameof(path));

        if (!IsNonFileProtocolPath(path))
            return File.ReadAllText(path);

        using (UnityWebRequest request = UnityWebRequest.Get(path))
        {
            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            while (!op.isDone)
            {
            }

#if UNITY_2020_1_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
                throw new IOException("StreamingAssets read failed: " + path + " | " + request.error);

            return request.downloadHandler.text;
        }
    }

    public static byte[] ReadAllBytes(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("path is empty", nameof(path));

        if (!IsNonFileProtocolPath(path))
            return File.ReadAllBytes(path);

        using (UnityWebRequest request = UnityWebRequest.Get(path))
        {
            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            while (!op.isDone)
            {
            }

#if UNITY_2020_1_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
                throw new IOException("StreamingAssets read failed: " + path + " | " + request.error);

            return request.downloadHandler.data ?? new byte[0];
        }
    }
}
