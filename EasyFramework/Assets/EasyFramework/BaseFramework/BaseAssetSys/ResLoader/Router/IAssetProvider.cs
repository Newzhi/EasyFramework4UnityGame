using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

public enum AssetSource
{
    ABUNDLE,
    RESOURCES,
    NETCDN,
    EDITORRESOURCES,
}

public struct AssetLoadContext
{
    public string loadPath;
    public string assetPath;
    public string bundleName;
    public string assetName;
    public Type assetType;
    public List<string> acquiredBundleNames;
}

public struct AssetReleaseContext
{
    public Object asset;
    public AssetSource source;
    public List<string> acquiredBundleNames;
}

public interface IAssetProvider
{
    AssetSource Source { get; }
    Object Load(ref AssetLoadContext ctx);
    UniTask<Object> LoadAsync(AssetLoadContext ctx);
    void Release(in AssetReleaseContext ctx);
}
