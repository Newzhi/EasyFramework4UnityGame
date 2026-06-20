using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 运行时清单热更：远程 catalogueHash 变化时下载 catalog.bytes 至 ABCache 并重载 CatalogueReader。
/// </summary>
public static class CdnCatalogueSyncService
{
    /// <summary>
    /// 尝试同步远程清单；失败时保留本地首包清单（单机可玩老版本）。
    /// </summary>
    /// <returns>是否成功完成同步流程（含「无需更新」）。</returns>
    public static bool TrySyncCatalogue(CatalogueReader reader, string cdnBaseUrl, string cacheRoot, out bool catalogueUpdated)
    {
        catalogueUpdated = false;

        if (reader == null || !reader.IsLoaded)
            return false;

        if (string.IsNullOrEmpty(cdnBaseUrl))
            return true;

        cdnBaseUrl = cdnBaseUrl.TrimEnd('/');
        string remoteUrl = cdnBaseUrl + "/Catalogue/" + CatalogueReader.RuntimeCatalogueFileName;

        if (!CdnHttpClient.TryGetBytes(remoteUrl, out byte[] remoteBytes))
        {
            Debug.LogWarning("[CdnCatalogueSyncService] 远程清单拉取失败，继续使用本地清单: " + remoteUrl);
            return false;
        }

        if (!AssetCatalogBinaryCodec.TryDeserialize(remoteBytes, out AssetCatalog remote))
        {
            Debug.LogWarning("[CdnCatalogueSyncService] 远程清单解析失败");
            return false;
        }

        string localHash = reader.Catalog?.catalogueHash;
        if (!string.IsNullOrEmpty(localHash)
            && string.Equals(localHash, remote.catalogueHash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string cacheCataloguePath = CdnPaths.GetCacheCataloguePath(cacheRoot);
        string cacheCatalogueDir = Path.GetDirectoryName(cacheCataloguePath);
        CdnPaths.EnsureDirectory(cacheCatalogueDir);

        try
        {
            File.WriteAllBytes(cacheCataloguePath, remoteBytes);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CdnCatalogueSyncService] 写入缓存清单失败: " + ex.Message);
            return false;
        }

        if (!reader.LoadFromFile(cacheCataloguePath))
        {
            Debug.LogWarning("[CdnCatalogueSyncService] 缓存清单重载失败，保留原清单");
            return false;
        }

        catalogueUpdated = true;
        Debug.Log("[CdnCatalogueSyncService] 清单已热更 buildId=" + remote.buildId
            + " hash=" + remote.catalogueHash);
        return true;
    }
}
