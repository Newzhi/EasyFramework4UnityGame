using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 基于 CDN 清单与 HTTP 下载的远程 Bundle 提供者（C-2）；下载经 <see cref="BundleDownloadQueue"/> 合并与排序。
/// </summary>
public sealed class HttpRemoteBundleProvider : IRemoteBundleProvider
{
    readonly string cdnBaseUrl;
    readonly string localBundleRoot;
    readonly CatalogueReader catalogueReader;
    readonly BundleDownloadQueue downloadQueue;

    public HttpRemoteBundleProvider(string cdnBaseUrl, string localBundleRoot, CatalogueReader catalogueReader)
    {
        this.cdnBaseUrl = (cdnBaseUrl ?? string.Empty).TrimEnd('/');
        this.localBundleRoot = localBundleRoot;
        this.catalogueReader = catalogueReader;
        downloadQueue = new BundleDownloadQueue(
            DownloadBundleAsync,
            name => catalogueReader != null ? catalogueReader.GetBundleResourcePriority(name) : (int)ResourcePriority.Normal);
    }

    public bool EnsureBundle(string bundleName)
    {
        return downloadQueue.Enqueue(BundlePlatformPaths.NormalizeBundleName(bundleName));
    }

    public UniTask<bool> EnsureBundleAsync(string bundleName)
    {
        return downloadQueue.EnqueueAsync(BundlePlatformPaths.NormalizeBundleName(bundleName));
    }

    async UniTask<bool> DownloadBundleAsync(string bundleName)
    {
        bundleName = BundlePlatformPaths.NormalizeBundleName(bundleName);
        if (string.IsNullOrEmpty(bundleName))
            return false;

        if (string.IsNullOrEmpty(cdnBaseUrl))
        {
            Debug.LogError("[HttpRemoteBundleProvider] cdnBaseUrl 为空");
            return false;
        }

        string localPath = CdnPaths.GetCacheBundlePath(bundleName, localBundleRoot);
        if (IsLocalBundleValid(bundleName, localPath))
            return true;

        string url = cdnBaseUrl + "/" + bundleName;
        byte[] data;
        try
        {
            data = await CdnHttpClient.TryGetBytesAsync(url, cancellationToken: CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        if (data == null || data.Length == 0)
            return false;

        string dir = Path.GetDirectoryName(localPath);
        CdnPaths.EnsureDirectory(dir);

        try
        {
            await UniTask.RunOnThreadPool(
                () => File.WriteAllBytes(localPath, data),
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.LogError("[HttpRemoteBundleProvider] 写入缓存失败: " + bundleName + " | " + ex.Message);
            return false;
        }

        bool hashOk = await UniTask.RunOnThreadPool(
            () => IsLocalBundleValid(bundleName, localPath),
            cancellationToken: CancellationToken.None);
        if (!hashOk)
        {
            Debug.LogError("[HttpRemoteBundleProvider] 下载后校验失败: " + bundleName);
            if (File.Exists(localPath))
                File.Delete(localPath);
            return false;
        }

        Debug.Log("[HttpRemoteBundleProvider] 已下载: " + bundleName);
        return true;
    }

    bool IsLocalBundleValid(string bundleName, string localPath)
    {
        if (!File.Exists(localPath))
            return false;

        AssetCatalog catalog = catalogueReader?.Catalog;
        if (catalog?.bundles == null)
            return true;

        foreach (BundleCatalogInfo info in catalog.bundles)
        {
            if (info == null || !string.Equals(info.bundleName, bundleName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (info.sizeBytes > 0 && new FileInfo(localPath).Length != info.sizeBytes)
                return false;

            if (!string.IsNullOrEmpty(info.fileHash))
            {
                string hash = BundleIntegrityUtil.ComputeFileSha256(localPath);
                if (!string.Equals(hash, info.fileHash, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (info.crc32 != 0)
            {
                uint crc = BundleIntegrityUtil.ComputeFileCrc32(localPath);
                if (crc != info.crc32)
                    return false;
            }

            return true;
        }

        return true;
    }
}
