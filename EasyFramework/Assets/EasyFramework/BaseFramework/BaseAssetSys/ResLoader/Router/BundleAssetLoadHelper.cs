using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class BundleAssetLoadHelper
{
    internal static Object LoadFromBundle(
        AssetBundle bundle,
        string assetName,
        Type assetType,
        string fallbackAssetPath)
    {
        if (bundle == null)
            return null;

        if (!string.IsNullOrEmpty(assetName))
        {
            Object loaded = bundle.LoadAsset(assetName, assetType);
            if (loaded != null)
                return loaded;
        }

        if (string.IsNullOrEmpty(fallbackAssetPath))
            return null;

        Object byPath = bundle.LoadAsset(fallbackAssetPath, assetType);
        if (byPath != null)
            return byPath;

        string fileName = Path.GetFileName(fallbackAssetPath);
        if (!string.IsNullOrEmpty(fileName) && fileName != assetName && fileName != fallbackAssetPath)
        {
            byPath = bundle.LoadAsset(fileName, assetType);
            if (byPath != null)
                return byPath;
        }

        string nameNoExt = Path.GetFileNameWithoutExtension(fallbackAssetPath);
        if (!string.IsNullOrEmpty(nameNoExt) && nameNoExt != assetName)
        {
            byPath = bundle.LoadAsset(nameNoExt, assetType);
            if (byPath != null)
                return byPath;
        }

        return bundle.LoadAsset(fallbackAssetPath);
    }

    internal static async UniTask<Object> LoadFromBundleAsync(
        AssetBundle bundle,
        string assetName,
        Type assetType,
        string fallbackAssetPath)
    {
        if (bundle == null)
            return null;

        if (assetType == null)
            assetType = typeof(Object);

        if (!string.IsNullOrEmpty(assetName))
        {
            Object loaded = await LoadAssetAsync(bundle, assetName, assetType);
            if (loaded != null)
                return loaded;
        }

        if (string.IsNullOrEmpty(fallbackAssetPath))
            return null;

        Object byPath = await LoadAssetAsync(bundle, fallbackAssetPath, assetType);
        if (byPath != null)
            return byPath;

        string fileName = Path.GetFileName(fallbackAssetPath);
        if (!string.IsNullOrEmpty(fileName) && fileName != assetName && fileName != fallbackAssetPath)
        {
            byPath = await LoadAssetAsync(bundle, fileName, assetType);
            if (byPath != null)
                return byPath;
        }

        string nameNoExt = Path.GetFileNameWithoutExtension(fallbackAssetPath);
        if (!string.IsNullOrEmpty(nameNoExt) && nameNoExt != assetName)
        {
            byPath = await LoadAssetAsync(bundle, nameNoExt, assetType);
            if (byPath != null)
                return byPath;
        }

        return await LoadAssetAsync(bundle, fallbackAssetPath, assetType);
    }

    static async UniTask<Object> LoadAssetAsync(AssetBundle bundle, string name, Type assetType)
    {
        AssetBundleRequest request = bundle.LoadAssetAsync(name, assetType);
        if (request == null)
            return null;

        await request;
        return request.asset;
    }

    internal static void ReleaseBundles(List<string> acquiredBundleNames)
    {
        if (acquiredBundleNames == null)
            return;

        for (int i = acquiredBundleNames.Count - 1; i >= 0; i--)
            BundleManager.ReleaseBundle(acquiredBundleNames[i]);

        acquiredBundleNames.Clear();
    }
}
