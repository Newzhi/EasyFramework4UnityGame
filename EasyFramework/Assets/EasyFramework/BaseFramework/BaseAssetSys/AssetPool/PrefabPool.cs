using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 单路径 Prefab 池（Queue + 句柄），风格对齐 <see cref="PoolTemp"/>。
/// 注册与 refCount 由 <see cref="PrefabPoolManager"/> 管理；本类只负责 GetObj / RecycleObj。
/// </summary>
public sealed class PrefabPool
{
    #region 变量定义

    IAssetHandle prefabHandle;
    readonly Transform poolRoot;
    readonly Queue<GameObject> inactivePool = new Queue<GameObject>(32);
    readonly HashSet<GameObject> activeInstances = new HashSet<GameObject>();
    readonly int baseInactiveCapacity;
    readonly string traceLoadPath;
    int maxInactiveCapacity;
    int refCount;
    bool isPoolCreated;

    /// <summary>池是否已初始化。</summary>
    public bool IsPoolCreated => isPoolCreated;

    /// <summary>无借出实例时可安全释放份额。</summary>
    public bool CanReleaseShare => isPoolCreated && activeInstances.Count == 0;

    /// <summary>当前借出（GetObj 后未 RecycleObj）数量。</summary>
    public int ActiveCount => activeInstances.Count;

    /// <summary>闲置队列深度。</summary>
    public int InactiveCount => inactivePool.Count;

    /// <summary>共享引用计数（GetOrCreatPool 次数）。</summary>
    public int RefCount => refCount;

    /// <summary>当前闲置上限；0 表示不限制。</summary>
    public int MaxInactiveCapacity => maxInactiveCapacity;

    #endregion

    internal PrefabPool(IAssetHandle prefabHandle, Transform poolRoot = null, int maxInactiveCapacity = 0, string loadPathForTrace = null)
    {
        this.prefabHandle = prefabHandle;
        this.poolRoot = poolRoot;
        this.baseInactiveCapacity = maxInactiveCapacity;
        this.maxInactiveCapacity = maxInactiveCapacity;
        this.traceLoadPath = loadPathForTrace;
    }

    #region 业务接口

    /// <summary>借出实例：队列有则 Dequeue 并激活；无则 Instantiate。</summary>
    public GameObject GetObj()
    {
        return GetObj(Vector3.zero, Quaternion.identity, null);
    }

    /// <summary>借出实例并设定位姿；parent 参数已忽略（单父节点方案）。</summary>
    public GameObject GetObj(Vector3 worldPosition, Quaternion worldRotation, Transform parent = null)
    {
        if (!isPoolCreated)
        {
            Debug.LogError("PrefabPool.GetObj: pool not initialized.");
            return null;
        }

        GameObject instance = null;
        while (inactivePool.Count > 0 && instance == null)
            instance = inactivePool.Dequeue();

        if (instance == null)
            instance = prefabHandle.InstantiateAt(worldPosition, worldRotation, poolRoot);
        else
        {
            Transform t = instance.transform;
            t.SetPositionAndRotation(worldPosition, worldRotation);
            if (!instance.activeSelf)
                instance.SetActive(true);
        }

        if (instance == null)
            return null;

        activeInstances.Add(instance);
        return instance;
    }

    /// <summary>回收实例到池：Deactivate 后 Enqueue；超上限则 Destroy。</summary>
    public void RecycleObj(GameObject instance)
    {
        if (instance == null)
            return;

        if (!isPoolCreated)
        {
            Object.Destroy(instance);
            return;
        }

        if (!activeInstances.Remove(instance))
            return;

        if (maxInactiveCapacity > 0 && inactivePool.Count >= maxInactiveCapacity)
        {
            Object.Destroy(instance);
            return;
        }

        DeactivateInstance(instance);
        inactivePool.Enqueue(instance);
    }

    #endregion

    #region 内部 — 生命周期（仅 PrefabPoolManager 调用）

    internal void Initialize()
    {
        if (prefabHandle == null || prefabHandle.GetAsset<GameObject>() == null)
        {
            Debug.LogError("PrefabPool.Initialize: invalid prefab handle.");
            return;
        }

        isPoolCreated = true;
        refCount = 1;
        ApplyCapacityForRefCount();
    }

    internal void RegisterShare()
    {
        if (!isPoolCreated)
        {
            Debug.LogError("PrefabPool.RegisterShare: pool not initialized.");
            return;
        }

        refCount++;
        ApplyCapacityForRefCount();
    }

    internal void ReleaseShare()
    {
        if (!isPoolCreated)
            return;

        refCount--;
        if (refCount > 0)
        {
            ApplyCapacityForRefCount();
            TrimInactiveExcess();
            return;
        }

        TearDown();
    }

    internal void ForceDelete()
    {
        if (!isPoolCreated)
            return;

        TearDown();
    }

    #endregion

    #region 辅助方法

    void ApplyCapacityForRefCount()
    {
        maxInactiveCapacity = baseInactiveCapacity > 0
            ? baseInactiveCapacity * refCount
            : 0;
    }

    void TrimInactiveExcess()
    {
        if (maxInactiveCapacity <= 0)
            return;

        while (inactivePool.Count > maxInactiveCapacity)
        {
            GameObject go = inactivePool.Dequeue();
            if (go != null)
                Object.Destroy(go);
        }
    }

    void DeactivateInstance(GameObject instance)
    {
        if (instance.activeSelf)
            instance.SetActive(false);
    }

    void TearDown()
    {
        isPoolCreated = false;
        refCount = 0;
        maxInactiveCapacity = baseInactiveCapacity;

        foreach (GameObject go in activeInstances)
            if (go != null)
                Object.Destroy(go);
        activeInstances.Clear();

        while (inactivePool.Count > 0)
        {
            GameObject go = inactivePool.Dequeue();
            if (go != null)
                Object.Destroy(go);
        }

        if (prefabHandle != null)
        {
            prefabHandle.Release();
            prefabHandle = null;
        }
        
    }

    int GetPrefabResourceRef()
    {
        if (prefabHandle is AbstractResource resource)
            return resource.GetRefForTrace();

        return -1;
    }

    #endregion
}
