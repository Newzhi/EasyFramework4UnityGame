using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 静态工具类：按场景在 <see cref="RuntimeRootName"/> 下创建池父节点。
/// 同场景同 loadPath 共享节点；跨场景各一棵 PoolRuntime。借出/归还仅 SetActive。
/// </summary>
public static class PoolSceneRootsUtil
{
    #region 常量

    /// <summary>运行时根节点名；每个场景各一个，其下挂 <c>Pool_{loadPath}</c>。</summary>
    public const string RuntimeRootName = "PoolRuntime";

    #endregion

    #region 缓存字段

    static readonly Dictionary<int, Transform> runtimeRootByScene = new Dictionary<int, Transform>(4);
    static readonly Dictionary<int, Dictionary<string, Transform>> poolRootByScene =
        new Dictionary<int, Dictionary<string, Transform>>(4);

    #endregion

    #region 公开 API — 运行时根

    /// <summary>在指定场景内获取或创建 <see cref="RuntimeRootName"/>。</summary>
    public static Transform GetOrCreateRuntimeRoot(Scene scene)
    {
        int handle = scene.handle;
        if (runtimeRootByScene.TryGetValue(handle, out Transform cached) && cached != null)
            return cached;

        GameObject go = new GameObject(RuntimeRootName);
        SceneManager.MoveGameObjectToScene(go, scene);
        runtimeRootByScene[handle] = go.transform;
        return go.transform;
    }

    #endregion

    #region 公开 API — 池父节点

    /// <summary>指定场景内该 loadPath 的池父节点 <c>Pool_{loadPath}</c>。</summary>
    public static Transform GetOrCreatePoolRoot(string loadPath, Scene scene)
    {
        string childName = BuildPoolChildName(loadPath);
        int handle = scene.handle;

        if (!poolRootByScene.TryGetValue(handle, out Dictionary<string, Transform> map))
        {
            map = new Dictionary<string, Transform>(4);
            poolRootByScene[handle] = map;
        }

        if (map.TryGetValue(childName, out Transform cached) && cached != null)
            return cached;

        Transform runtimeRoot = GetOrCreateRuntimeRoot(scene);
        Transform child = runtimeRoot.Find(childName);
        if (child == null)
        {
            GameObject go = new GameObject(childName);
            go.transform.SetParent(runtimeRoot, false);
            child = go.transform;
        }

        map[childName] = child;
        return child;
    }

    #endregion

    #region 公开 API — 生命周期

    /// <summary>UnloadAll 时清空全部场景缓存。</summary>
    public static void ClearCache()
    {
        runtimeRootByScene.Clear();
        poolRootByScene.Clear();
    }

    /// <summary>场景卸载后移除该场景的 Transform 缓存（节点由 Unity 随场景销毁）。</summary>
    public static void ClearCacheForScene(Scene scene)
    {
        int handle = scene.handle;
        runtimeRootByScene.Remove(handle);
        poolRootByScene.Remove(handle);
    }

    /// <summary>仅查询已缓存的运行时根，不创建节点（供日志/诊断用）。</summary>
    public static bool TryGetRuntimeRoot(Scene scene, out Transform runtimeRoot)
    {
        int handle = scene.handle;
        if (runtimeRootByScene.TryGetValue(handle, out Transform cached) && cached != null)
        {
            runtimeRoot = cached;
            return true;
        }

        runtimeRoot = null;
        return false;
    }

    /// <summary>仅查询已缓存的池父节点，不创建（供日志/诊断用）。</summary>
    public static bool TryGetPoolRoot(string loadPath, Scene scene, out Transform poolRoot)
    {
        int handle = scene.handle;
        string childName = BuildPoolChildName(loadPath);
        if (poolRootByScene.TryGetValue(handle, out Dictionary<string, Transform> map)
            && map.TryGetValue(childName, out Transform cached)
            && cached != null)
        {
            poolRoot = cached;
            return true;
        }

        poolRoot = null;
        return false;
    }

    #endregion

    #region 内部实现 — 命名

    static string BuildPoolChildName(string loadPath) => "Pool_" + SanitizeLoadPath(loadPath);

    static string SanitizeLoadPath(string loadPath)
    {
        if (string.IsNullOrEmpty(loadPath))
            return "Unknown";

        return loadPath.Replace('/', '_').Replace('\\', '_');
    }

    #endregion
}
