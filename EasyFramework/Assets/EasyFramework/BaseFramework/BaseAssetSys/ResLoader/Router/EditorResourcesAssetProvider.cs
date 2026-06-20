using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class EditorResourcesAssetProvider : IAssetProvider
{
    public AssetSource Source => AssetSource.EDITORRESOURCES;

    public Object Load(ref AssetLoadContext ctx)
    {
#if UNITY_EDITOR
        string path = ctx.assetPath;
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("EditorResources load failed, assetPath is empty for: " + ctx.loadPath);
            return null;
        }

        Type assetType = ctx.assetType ?? typeof(Object);
        Object loaded = UnityEditor.AssetDatabase.LoadAssetAtPath(path, assetType);
        if (loaded != null)
            return loaded;

        if (assetType == typeof(Sprite))
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);

        return null;
#else
        Debug.LogError("EditorResources is only available in Unity Editor.");
        return null;
#endif
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
}
