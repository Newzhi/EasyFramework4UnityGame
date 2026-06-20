using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class AbBundleAssetProvider : IAssetProvider
{
    public AssetSource Source => AssetSource.ABUNDLE;

    public Object Load(ref AssetLoadContext ctx)
    {
        if (ctx.acquiredBundleNames == null)
            ctx.acquiredBundleNames = new List<string>();

        ctx.acquiredBundleNames.Clear();
        AssetBundle bundle = BundleManager.AcquireBundleWithDependencies(ctx.bundleName, ctx.acquiredBundleNames);
        if (bundle == null)
        {
            BundleAssetLoadHelper.ReleaseBundles(ctx.acquiredBundleNames);
            return null;
        }

        return BundleAssetLoadHelper.LoadFromBundle(bundle, ctx.assetName, ctx.assetType, ctx.assetPath);
    }

    public async UniTask<Object> LoadAsync(AssetLoadContext ctx)
    {
        if (ctx.acquiredBundleNames == null)
            ctx.acquiredBundleNames = new List<string>();

        ctx.acquiredBundleNames.Clear();
        AssetBundle bundle = await BundleManager.AcquireBundleWithDependenciesAsync(ctx.bundleName, ctx.acquiredBundleNames);
        if (bundle == null)
        {
            BundleAssetLoadHelper.ReleaseBundles(ctx.acquiredBundleNames);
            return null;
        }

        return await BundleAssetLoadHelper.LoadFromBundleAsync(bundle, ctx.assetName, ctx.assetType, ctx.assetPath);
    }

    public void Release(in AssetReleaseContext ctx)
    {
        BundleAssetLoadHelper.ReleaseBundles(ctx.acquiredBundleNames);
    }
}
