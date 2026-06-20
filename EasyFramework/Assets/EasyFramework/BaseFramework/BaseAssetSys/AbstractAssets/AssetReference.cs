using UnityEngine;

/// <summary>
/// 非池化实例自动管理 <see cref="IAssetHandle"/>：<see cref="OnDestroy"/> 时 <see cref="Release"/> 一次。
/// 池化实例勿挂本组件（由 <see cref="PrefabPool"/> 持句柄）。
/// </summary>
[DisallowMultipleComponent]
public sealed class AssetReference : MonoBehaviour
{
    IAssetHandle handle;
    string loadPath;
    bool released;

    /// <summary>是否仍持有未释放的句柄。</summary>
    public bool HasHandle => !released && handle != null;

    /// <summary>绑定 loadPath（仅追溯日志）。</summary>
    public string LoadPath => loadPath;

    /// <summary>为实例绑定句柄；Destroy 时自动 Release。重复绑定会打 Warning 并忽略。</summary>
    public static AssetReference Bind(GameObject instance, IAssetHandle assetHandle, string loadPathForTrace = null)
    {
        if (instance == null)
            return null;

        if (assetHandle == null)
        {
            Debug.LogError("AssetReference.Bind: assetHandle is null.");
            return null;
        }

        AssetReference reference = instance.GetComponent<AssetReference>();
        if (reference == null)
            reference = instance.AddComponent<AssetReference>();

        reference.Attach(assetHandle, loadPathForTrace);
        return reference;
    }

    /// <summary>主动释放句柄（可选）；之后 OnDestroy 不会再次 Release。</summary>
    public void ReleaseBinding()
    {
        ReleaseHandle("ReleaseBinding");
    }

    void OnDestroy()
    {
        ReleaseHandle("OnDestroy");
    }

    void Attach(IAssetHandle assetHandle, string loadPathForTrace)
    {
        if (released)
        {
            Debug.LogWarning("AssetReference: cannot attach after release, go=" + name);
            return;
        }

        if (handle != null)
        {
            Debug.LogWarning("AssetReference: already bound, go=" + name);
            return;
        }

        handle = assetHandle;
        loadPath = loadPathForTrace;
    }

    void ReleaseHandle(string reason)
    {
        if (released || handle == null)
            return;

        released = true;
        IAssetHandle releasing = handle;
        handle = null;

        releasing.Release();
    }
}
