using UnityEngine;

/// <summary>
/// C-3 运行时 Bootstrap：Init 后清单热更 + 远程 Provider 创建；解析顺序 ABCache → 首包 → CDN。
/// </summary>
public static class CdnRuntimeBootstrap
{
    /// <summary>Init 后调用：可选同步远程清单；失败回退首包/本地缓存清单。</summary>
    public static void SyncCatalogueIfNeeded(CatalogueReader catalogue, string cacheRoot)
    {
        if (catalogue == null || !catalogue.IsLoaded)
            return;

        string cdnBaseUrl = catalogue.Catalog?.cdnBaseUrl;
        if (string.IsNullOrEmpty(cdnBaseUrl))
            return;

        CdnCatalogueSyncService.TrySyncCatalogue(catalogue, cdnBaseUrl, cacheRoot, out _);
    }

    /// <summary>根据清单 cdnBaseUrl 创建远程 Provider；无配置时返回 Stub。</summary>
    public static IRemoteBundleProvider CreateRemoteProvider(CatalogueReader catalogue, string cacheRoot)
    {
        string cdnBaseUrl = catalogue?.Catalog?.cdnBaseUrl;
        if (string.IsNullOrEmpty(cdnBaseUrl))
            return new StubRemoteBundleProvider();

        return new HttpRemoteBundleProvider(cdnBaseUrl.TrimEnd('/'), cacheRoot, catalogue);
    }
}
