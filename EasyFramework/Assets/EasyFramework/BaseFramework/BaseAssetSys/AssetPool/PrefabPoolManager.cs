using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Prefab 对象池管理器（单例）。结构对齐 <see cref="PoolTemp"/>：按路径队列复用实例；
/// 加载走 <see cref="BundleResLoader"/>，按 Active Scene + loadPath 分池并支持 refCount 共享。
/// </summary>
public sealed class PrefabPoolManager
{
    #region 单例

    static volatile PrefabPoolManager instance;
    static readonly object instanceLock = new object();

    public static PrefabPoolManager Instance
    {
        get
        {
            if (instance == null)
            {
                lock (instanceLock)
                {
                    if (instance == null)
                        instance = new PrefabPoolManager();
                }
            }
            return instance;
        }
    }

    #endregion

    #region 字段

    readonly Dictionary<int, Dictionary<string, PrefabPool>> poolsBySceneAndPath =
        new Dictionary<int, Dictionary<string, PrefabPool>>();

    #endregion

    static PrefabPoolManager()
    {
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    static void OnSceneUnloaded(Scene scene)
    {
        Instance.DeletePoolsForScene(scene);
    }

    #region 建池 / 查询

    static Scene ResolvePoolScene() => SceneManager.GetActiveScene();

    Dictionary<string, PrefabPool> GetOrCreatePoolMapForScene(Scene scene)
    {
        int handle = scene.handle;
        if (!poolsBySceneAndPath.TryGetValue(handle, out Dictionary<string, PrefabPool> map))
        {
            map = new Dictionary<string, PrefabPool>(4);
            poolsBySceneAndPath[handle] = map;
        }

        return map;
    }

    /// <summary>
    /// 当前 Active Scene 内同路径共享池：已存在则 refCount++，否则 Load 并注册。
    /// </summary>
    public PrefabPool GetOrCreatPool(string loadPath, Transform poolRoot = null, int maxInactiveCapacity = 0)
    {
        if (string.IsNullOrEmpty(loadPath))
        {
            Debug.LogError("PrefabPoolManager.GetOrCreatPool: loadPath is null or empty.");
            return null;
        }

        Scene scene = ResolvePoolScene();
        Dictionary<string, PrefabPool> map = GetOrCreatePoolMapForScene(scene);

        if (map.TryGetValue(loadPath, out PrefabPool existing) && existing != null && existing.IsPoolCreated)
        {
            existing.RegisterShare();
            return existing;
        }

        Transform root = poolRoot ?? PoolSceneRootsUtil.GetOrCreatePoolRoot(loadPath, scene);
        PrefabPool pool = CreatePool(loadPath, root, maxInactiveCapacity);
        if (pool != null && pool.IsPoolCreated)
            map[loadPath] = pool;

        return pool;
    }

    /// <summary>查询当前 Active Scene 已注册池，不增加 refCount。</summary>
    public bool TryGetPool(string loadPath, out PrefabPool pool)
    {
        return TryGetPool(loadPath, ResolvePoolScene(), out pool);
    }

    /// <summary>查询指定场景已注册池，不增加 refCount。</summary>
    public bool TryGetPool(string loadPath, Scene scene, out PrefabPool pool)
    {
        if (string.IsNullOrEmpty(loadPath))
        {
            pool = null;
            return false;
        }

        if (!poolsBySceneAndPath.TryGetValue(scene.handle, out Dictionary<string, PrefabPool> map))
        {
            pool = null;
            return false;
        }

        return map.TryGetValue(loadPath, out pool) && pool != null && pool.IsPoolCreated;
    }

    PrefabPool CreatePool(string loadPath, Transform poolRoot, int maxInactiveCapacity)
    {
        IAssetHandle handle = BundleResLoader.Instance.Load<GameObject>(loadPath);
        if (handle == null)
        {
            Debug.LogError("PrefabPoolManager.CreatePool: Load failed, path=" + loadPath);
            return null;
        }

        PrefabPool pool = new PrefabPool(handle, poolRoot, maxInactiveCapacity, loadPath);
        pool.Initialize();
        if (!pool.IsPoolCreated)
        {
            Debug.LogError("PrefabPoolManager.CreatePool: Initialize failed, path=" + loadPath);
            handle.Release();
            return null;
        }

        return pool;
    }

    #endregion

    #region GetObj / RecycleObj（PoolTemp 风格入口）

    /// <summary>从当前 Active Scene 池借出实例；池未注册时返回 null。</summary>
    public GameObject GetObj(string loadPath)
    {
        return GetObj(loadPath, Vector3.zero, Quaternion.identity, null);
    }

    /// <summary>从当前 Active Scene 池借出实例并设定位姿。</summary>
    public GameObject GetObj(string loadPath, Vector3 worldPosition, Quaternion worldRotation, Transform parent = null)
    {
        if (!TryGetPool(loadPath, out PrefabPool pool))
        {
            Debug.LogError("PrefabPoolManager.GetObj: pool not found, path=" + loadPath);
            return null;
        }

        return pool.GetObj(worldPosition, worldRotation, parent);
    }

    /// <summary>将实例回收到当前 Active Scene 对应路径池。</summary>
    public void RecycleObj(GameObject instance, string loadPath)
    {
        if (instance == null)
            return;

        if (TryGetPool(loadPath, out PrefabPool pool))
            pool.RecycleObj(instance);
        else
            Object.Destroy(instance);
    }

    #endregion

    #region 卸池 / 删池

    /// <summary>释放当前 Active Scene 下一次池份额（refCount--）；归零时销毁池。</summary>
    public bool ReleasePoolShare(string loadPath)
    {
        if (string.IsNullOrEmpty(loadPath))
            return false;

        Scene scene = ResolvePoolScene();
        if (!poolsBySceneAndPath.TryGetValue(scene.handle, out Dictionary<string, PrefabPool> map))
            return false;

        if (!map.TryGetValue(loadPath, out PrefabPool pool) || pool == null)
            return false;

        pool.ReleaseShare();
        if (!pool.IsPoolCreated)
            map.Remove(loadPath);

        return true;
    }

    /// <summary>强制删除当前 Active Scene 下该路径池（无视 refCount 与借出状态）。</summary>
    public bool DeletePool(string loadPath)
    {
        if (string.IsNullOrEmpty(loadPath))
            return false;

        Scene scene = ResolvePoolScene();
        if (!poolsBySceneAndPath.TryGetValue(scene.handle, out Dictionary<string, PrefabPool> map))
            return false;

        if (!map.TryGetValue(loadPath, out PrefabPool pool) || pool == null)
            return false;

        pool.ForceDelete();
        map.Remove(loadPath);
        return true;
    }

    /// <summary>强制删除指定场景下该路径池。</summary>
    public bool DeletePool(string loadPath, Scene scene)
    {
        if (string.IsNullOrEmpty(loadPath))
            return false;

        if (!poolsBySceneAndPath.TryGetValue(scene.handle, out Dictionary<string, PrefabPool> map))
            return false;

        if (!map.TryGetValue(loadPath, out PrefabPool pool) || pool == null)
            return false;

        pool.ForceDelete();
        map.Remove(loadPath);
        return true;
    }

    /// <summary>强制销毁全部场景下全部池。</summary>
    public void DeleteAllPools()
    {
        foreach (Dictionary<string, PrefabPool> map in poolsBySceneAndPath.Values)
        {
            foreach (PrefabPool pool in map.Values)
            {
                if (pool != null)
                    pool.ForceDelete();
            }
        }

        poolsBySceneAndPath.Clear();
        PoolSceneRootsUtil.ClearCache();
    }

    void DeletePoolsForScene(Scene scene)
    {
        int handle = scene.handle;
        if (!poolsBySceneAndPath.TryGetValue(handle, out Dictionary<string, PrefabPool> map))
        {
            PoolSceneRootsUtil.ClearCacheForScene(scene);
            return;
        }

        foreach (PrefabPool pool in map.Values)
        {
            if (pool != null)
                pool.ForceDelete();
        }

        poolsBySceneAndPath.Remove(handle);
        PoolSceneRootsUtil.ClearCacheForScene(scene);
    }

    #endregion
}
