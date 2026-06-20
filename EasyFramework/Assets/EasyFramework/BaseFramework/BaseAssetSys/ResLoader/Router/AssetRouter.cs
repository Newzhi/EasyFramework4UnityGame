using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class AssetRouter
{
    static volatile AssetRouter instance;
    static readonly object instanceLock = new object();

    readonly Dictionary<AssetSource, IAssetProvider> providers = new Dictionary<AssetSource, IAssetProvider>();
    CatalogueReader catalogue;
    IBundlePathResolver pathResolver;
    bool editorSimulateMode;

    public static AssetRouter Instance
    {
        get
        {
            if (instance == null)
            {
                lock (instanceLock)
                {
                    if (instance == null)
                        instance = new AssetRouter();
                }
            }
            return instance;
        }
    }

    public void Init(
        CatalogueReader reader,
        IBundlePathResolver resolver = null,
        IRemoteBundleProvider remoteProvider = null)
    {
        catalogue = reader;
        pathResolver = resolver;
        editorSimulateMode = IsEditorTestCatalogue(reader);

        providers.Clear();
        providers[AssetSource.ABUNDLE] = new AbBundleAssetProvider();
        providers[AssetSource.RESOURCES] = new ResourcesAssetProvider();
        providers[AssetSource.EDITORRESOURCES] = new EditorResourcesAssetProvider();
        providers[AssetSource.NETCDN] = new CdnBundleAssetProvider(
            resolver ?? pathResolver,
            remoteProvider ?? new StubRemoteBundleProvider());

        if (resolver != null)
            BundleManager.SetPathResolver(resolver);
    }

    public AssetSource RouteAssetSource(in AssetLoadContext ctx)
    {
        if (ResourcesAssetProvider.IsResourcesLoadPath(ctx.loadPath))
            return AssetSource.RESOURCES;

#if UNITY_EDITOR
        if (editorSimulateMode && !string.IsNullOrEmpty(ctx.assetPath))
            return AssetSource.EDITORRESOURCES;
#endif

        if (ShouldUseNetCdn(ctx.bundleName))
            return AssetSource.NETCDN;

        return AssetSource.ABUNDLE;
    }

    public Object Load(ref AssetLoadContext ctx, out AssetSource source)
    {
        source = RouteAssetSource(in ctx);
        if (!providers.TryGetValue(source, out IAssetProvider provider))
        {
            Debug.LogError("AssetRouter has no provider for source: " + source);
            return null;
        }

        return provider.Load(ref ctx);
    }

    public async UniTask<Object> LoadAsync(AssetLoadContext ctx)
    {
        AssetSource source = RouteAssetSource(in ctx);
        if (!providers.TryGetValue(source, out IAssetProvider provider))
        {
            Debug.LogError("AssetRouter has no provider for source: " + source);
            return null;
        }

        return await provider.LoadAsync(ctx);
    }

    public void Release(in AssetReleaseContext ctx)
    {
        if (ctx.asset == null && (ctx.acquiredBundleNames == null || ctx.acquiredBundleNames.Count == 0))
            return;

        if (!providers.TryGetValue(ctx.source, out IAssetProvider provider))
        {
            Debug.LogError("AssetRouter has no provider for release source: " + ctx.source);
            return;
        }

        provider.Release(in ctx);
    }

    bool ShouldUseNetCdn(string bundleName)
    {
        if (string.IsNullOrEmpty(bundleName))
            return false;

#if UNITY_EDITOR
        if (editorSimulateMode)
            return false;
#endif

        if (pathResolver == null)
            return false;

        return !pathResolver.IsLocalBundleAvailable(bundleName);
    }

    static bool IsEditorTestCatalogue(CatalogueReader reader)
    {
        if (reader == null || !reader.IsLoaded || reader.Catalog == null)
            return false;

        return string.Equals(reader.Catalog.buildMode, "EditorTest", StringComparison.OrdinalIgnoreCase);
    }
}
