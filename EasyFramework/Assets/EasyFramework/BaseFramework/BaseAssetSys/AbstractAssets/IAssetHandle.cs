using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 面向业务侧的资源句柄接口。
/// 业务只依赖该接口，不直接依赖 AbstractResource 内部实现。
/// </summary>
public interface IAssetHandle
{
    T GetAsset<T>() where T : Object;
    GameObject Instance { get; }
    GameObject Instantiate();
    GameObject InstantiateAt(Vector3 worldPosition, Quaternion worldRotation, Transform parent);
    void Release();
}
