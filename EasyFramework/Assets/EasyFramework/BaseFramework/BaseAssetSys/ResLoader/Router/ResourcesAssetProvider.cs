using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class ResourcesAssetProvider : IAssetProvider
{
    public const string PathPrefix = "Resources/";

    public AssetSource Source => AssetSource.RESOURCES;

    public Object Load(ref AssetLoadContext ctx)
    {
        string relativePath = ToResourcesRelativePath(ctx.loadPath);
        if (string.IsNullOrEmpty(relativePath))
        {
            Debug.LogError("Resources load failed, invalid path: " + ctx.loadPath);
            return null;
        }

        Type assetType = ctx.assetType ?? typeof(Object);
        return Resources.Load(relativePath, assetType);
    }

    public async UniTask<Object> LoadAsync(AssetLoadContext ctx)
    {
        await UniTask.Yield(PlayerLoopTiming.Update);
        AssetLoadContext mutable = ctx;
        return Load(ref mutable);
    }

    public void Release(in AssetReleaseContext ctx)
    {
        if (ctx.asset == null)
            return;

        if (ctx.asset is GameObject)
            return;

        Resources.UnloadAsset(ctx.asset);
    }

    public static bool IsResourcesLoadPath(string loadPath)
    {
        if (string.IsNullOrEmpty(loadPath))
            return false;

        return loadPath.StartsWith(PathPrefix, System.StringComparison.OrdinalIgnoreCase);
    }

    public static string ToResourcesRelativePath(string loadPath)
    {
        if (string.IsNullOrEmpty(loadPath))
            return null;

        string normalized = loadPath.Replace("\\", "/");
        if (normalized.StartsWith(PathPrefix, System.StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(PathPrefix.Length);

        return CatalogueReader.NormalizeLoadPath(normalized);
    }
}
