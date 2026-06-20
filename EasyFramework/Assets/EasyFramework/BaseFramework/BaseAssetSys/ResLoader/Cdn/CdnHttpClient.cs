using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// CDN HTTP 辅助（Editor/Player 真机 File API；Android StreamingAssets 走 StreamingAssetsIO）。
/// </summary>
public static class CdnHttpClient
{
    public static bool TryGetText(string url, out string text, int timeoutSeconds = 30)
    {
        text = null;
        if (string.IsNullOrEmpty(url))
            return false;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = timeoutSeconds;
            var operation = request.SendWebRequest();
            while (!operation.isDone) { }

#if UNITY_2020_1_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
            {
                Debug.LogWarning("[CdnHttpClient] GET failed: " + url + " | " + request.error);
                return false;
            }

            text = request.downloadHandler.text;
            return !string.IsNullOrEmpty(text);
        }
    }

    public static bool TryGetBytes(string url, out byte[] bytes, int timeoutSeconds = 60)
    {
        bytes = null;
        if (string.IsNullOrEmpty(url))
            return false;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = timeoutSeconds;
            var operation = request.SendWebRequest();
            while (!operation.isDone) { }

#if UNITY_2020_1_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
            {
                Debug.LogWarning("[CdnHttpClient] GET bytes failed: " + url + " | " + request.error);
                return false;
            }

            bytes = request.downloadHandler.data;
            return bytes != null && bytes.Length > 0;
        }
    }

    /// <summary>异步 GET 二进制；失败返回 null。</summary>
    public static async UniTask<byte[]> TryGetBytesAsync(
        string url,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = timeoutSeconds;

        try
        {
            await request.SendWebRequest().WithCancellation(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CdnHttpClient] GET bytes async failed: " + url + " | " + ex.Message);
            return null;
        }

#if UNITY_2020_1_OR_NEWER
        if (request.result != UnityWebRequest.Result.Success)
#else
        if (request.isNetworkError || request.isHttpError)
#endif
        {
            Debug.LogWarning("[CdnHttpClient] GET bytes async failed: " + url + " | " + request.error);
            return null;
        }

        byte[] data = request.downloadHandler.data;
        return data != null && data.Length > 0 ? data : null;
    }
}
