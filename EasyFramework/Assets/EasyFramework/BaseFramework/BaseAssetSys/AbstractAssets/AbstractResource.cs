using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

//抽象资源：Resource层引用计数，Ref为0时释放Bundle引用
internal class AbstractResource : IAssetHandle
{
    #region 变量定义

    private string assetKey;
    private string bundleName;
    private string assetName;
    private string catalogueAssetPath;
    private string loadPath;
    private Object asset;
    private int Ref;
    private bool isLoading;
    private AssetSource loadedSource;
    private readonly List<string> acquiredBundleNames = new List<string>();
    internal Action onUnLoad;

    #endregion

    #region 构造

    internal AbstractResource(string assetKey, string bundleName, string assetName, string catalogueAssetPath = null, string loadPath = null)
    {
        this.assetKey = assetKey;
        this.bundleName = bundleName;
        this.assetName = assetName;
        this.catalogueAssetPath = catalogueAssetPath;
        this.loadPath = loadPath;
    }

    #endregion

    #region 引用计数

    internal void AddReference()
    {
        Ref++;
    }

    internal void ReduceReference()
    {
        Ref--;
        if (Ref < 0)
        {
            Debug.LogError("AbstractResource ReduceReference less than 0, key:" + assetKey);
            Ref = 0;
        }
    }

    internal int CurrentRef => Ref;

    internal bool IsLoading => isLoading;

    #endregion

    #region 加载/卸载

    internal void LoadAsset(Type assetType, string fallbackAssetPath = null, string explicitLoadPath = null)
    {
        if (assetType == null)
            assetType = typeof(Object);

        ReleaseLoadedAsset();

        string resolvedLoadPath = explicitLoadPath ?? loadPath ?? assetKey;
        string resolvedAssetPath = !string.IsNullOrEmpty(fallbackAssetPath) ? fallbackAssetPath : catalogueAssetPath;

        var ctx = new AssetLoadContext
        {
            loadPath = resolvedLoadPath,
            assetPath = resolvedAssetPath,
            bundleName = bundleName,
            assetName = assetName,
            assetType = assetType,
            acquiredBundleNames = acquiredBundleNames
        };

        asset = AssetRouter.Instance.Load(ref ctx, out loadedSource);

        if (asset == null)
        {
            ReleaseLoadedAsset();
            Debug.LogError("Asset load failed: " + assetName + " in " + bundleName + ", loadPath=" + resolvedLoadPath);
            return;
        }
    }

    internal async UniTask<bool> LoadAssetAsync(Type assetType, string fallbackAssetPath = null, string explicitLoadPath = null)
    {
        if (assetType == null)
            assetType = typeof(Object);

        ReleaseLoadedAsset();

        string resolvedLoadPath = explicitLoadPath ?? loadPath ?? assetKey;
        string resolvedAssetPath = !string.IsNullOrEmpty(fallbackAssetPath) ? fallbackAssetPath : catalogueAssetPath;

        isLoading = true;
        try
        {
            var ctx = new AssetLoadContext
            {
                loadPath = resolvedLoadPath,
                assetPath = resolvedAssetPath,
                bundleName = bundleName,
                assetName = assetName,
                assetType = assetType,
                acquiredBundleNames = acquiredBundleNames
            };

            asset = await AssetRouter.Instance.LoadAsync(ctx);
            loadedSource = AssetRouter.Instance.RouteAssetSource(in ctx);

            if (asset == null)
            {
                ReleaseLoadedAsset();
                return false;
            }

            if (Ref <= 0)
            {
                ReleaseLoadedAsset();
                return false;
            }
            return true;
        }
        finally
        {
            isLoading = false;
        }
    }

    internal void UnLoad()
    {
        ReleaseLoadedAsset();
        onUnLoad?.Invoke();
        onUnLoad = null;
    }

    public void Release()
    {
        if (Ref <= 0)
            return;

        ReduceReference();
        if (Ref == 0 && !isLoading)
            UnLoad();
    }

    internal void ForceUnloadFromCoordinator()
    {
        if (Ref > 0)
            Ref = 0;

        UnLoad();
    }

    #endregion

    #region 辅助函数

    public T GetAsset<T>() where T : Object
    {
        return asset as T;
    }

    public GameObject Instance => Instantiate();

    public GameObject Instantiate()
    {
        return InstantiateAt(Vector3.zero, Quaternion.identity, null);
    }

    public GameObject InstantiateAt(Vector3 worldPosition, Quaternion worldRotation, Transform parent)
    {
        if (!(asset is GameObject prefab))
        {
            Debug.LogError("Asset is not GameObject, key:" + assetKey);
            return null;
        }

        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.transform.SetPositionAndRotation(worldPosition, worldRotation);
        if (parent != null)
            instance.transform.SetParent(parent, true);

        return instance;
    }

    void ReleaseLoadedAsset()
    {
        if (asset == null && acquiredBundleNames.Count == 0)
            return;

        var ctx = new AssetReleaseContext
        {
            asset = asset,
            source = loadedSource,
            acquiredBundleNames = acquiredBundleNames
        };

        AssetRouter.Instance.Release(in ctx);
        asset = null;
        acquiredBundleNames.Clear();
        loadedSource = AssetSource.ABUNDLE;
    }

    string GetTraceKey()
    {
        return !string.IsNullOrEmpty(loadPath) ? loadPath : assetKey;
    }

    internal int GetRefForTrace()
    {
        return Ref;
    }

    #endregion
}
