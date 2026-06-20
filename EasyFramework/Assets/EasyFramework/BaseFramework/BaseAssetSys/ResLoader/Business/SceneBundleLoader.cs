using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// AB 场景加载：Acquire bundle → LoadSceneAsync → 卸载时对称 Release。
/// 不走 AbstractResource 缓存；由 SceneLayer 编排生命周期。
/// </summary>
public static class SceneBundleLoader
{
    static readonly Dictionary<string, SceneBundleRecord> RecordsBySceneId =
        new Dictionary<string, SceneBundleRecord>(StringComparer.Ordinal);

    static readonly Dictionary<int, string> SceneIdByHandle = new Dictionary<int, string>();

    sealed class SceneBundleRecord
    {
        public string SceneId;
        public string SceneLoadPath;
        public List<string> AcquiredBundles = new List<string>();
    }

    /// <summary>从 catalogue loadPath 加载 AB 场景。</summary>
    public static async UniTask<bool> LoadSceneFromBundleAsync(
        string sceneId,
        string sceneLoadPath,
        LoadSceneMode mode,
        LocalPhysicsMode physics = LocalPhysicsMode.None,
        IProgress<float> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sceneLoadPath))
        {
            Debug.LogError("[SceneBundleLoader] sceneLoadPath is empty.");
            return false;
        }

        BundleResLoader loader = BundleResLoader.Instance;
        if (!loader.EnsureReady())
        {
            Debug.LogError("[SceneBundleLoader] BundleResLoader not ready.");
            return false;
        }

        CatalogueReader catalogue = loader.GetCatalogue();
        if (!catalogue.TryGetEntryByLoadPath(sceneLoadPath, out AssetCatalogEntry entry))
        {
            Debug.LogError("[SceneBundleLoader] loadPath not in catalogue: " + sceneLoadPath);
            return false;
        }

        ReleaseSceneBundles(sceneId);

        var acquired = new List<string>();
        AssetBundle bundle = BundleManager.AcquireBundleWithDependencies(entry.bundleName, acquired);
        if (bundle == null)
        {
            Debug.LogError("[SceneBundleLoader] AcquireBundle failed: " + entry.bundleName);
            return false;
        }

        loader.PreLoadBundles(acquired);

        string sceneName = entry.assetName;
        if (string.IsNullOrEmpty(sceneName))
            sceneName = System.IO.Path.GetFileNameWithoutExtension(entry.assetPath);

        AsyncOperation op = SceneManager.LoadSceneAsync(
            sceneName,
            new LoadSceneParameters(mode, physics));

        if (op == null)
        {
            Debug.LogError("[SceneBundleLoader] LoadSceneAsync returned null: " + sceneName);
            ReleaseAcquiredBundles(acquired);
            return false;
        }

        while (!op.isDone)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(op.progress);
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        if (!loadedScene.IsValid())
        {
            Debug.LogError("[SceneBundleLoader] Scene not valid after load: " + sceneName);
            ReleaseAcquiredBundles(acquired);
            return false;
        }

        var record = new SceneBundleRecord
        {
            SceneId = sceneId,
            SceneLoadPath = sceneLoadPath,
            AcquiredBundles = acquired
        };
        RecordsBySceneId[sceneId] = record;
        SceneIdByHandle[loadedScene.handle] = sceneId;
        progress?.Report(1f);
        return true;
    }

    /// <summary>卸载 AB 场景持有的 bundle 引用。</summary>
    public static void ReleaseSceneBundles(string sceneId)
    {
        if (string.IsNullOrEmpty(sceneId))
            return;

        if (!RecordsBySceneId.TryGetValue(sceneId, out SceneBundleRecord record))
            return;

        ReleaseAcquiredBundles(record.AcquiredBundles);
        RecordsBySceneId.Remove(sceneId);

        var handlesToRemove = new List<int>();
        foreach (KeyValuePair<int, string> pair in SceneIdByHandle)
        {
            if (pair.Value == sceneId)
                handlesToRemove.Add(pair.Key);
        }

        for (int i = 0; i < handlesToRemove.Count; i++)
            SceneIdByHandle.Remove(handlesToRemove[i]);
    }

    public static void ReleaseSceneBundlesByHandle(int sceneHandle)
    {
        if (SceneIdByHandle.TryGetValue(sceneHandle, out string sceneId))
            ReleaseSceneBundles(sceneId);
    }

    /// <summary>UnloadAll 后清空记录（不再 ReleaseBundle，避免双减引用）。</summary>
    public static void ClearRecords()
    {
        RecordsBySceneId.Clear();
        SceneIdByHandle.Clear();
    }

    /// <summary>释放全部 AB 场景持有的 bundle 引用并清空记录。</summary>
    public static void ReleaseAll()
    {
        foreach (KeyValuePair<string, SceneBundleRecord> pair in RecordsBySceneId)
            ReleaseAcquiredBundles(pair.Value.AcquiredBundles);

        ClearRecords();
    }

    public static bool IsAssetBundleScene(string sceneId) =>
        !string.IsNullOrEmpty(sceneId) && RecordsBySceneId.ContainsKey(sceneId);

    static void ReleaseAcquiredBundles(List<string> acquired)
    {
        if (acquired == null)
            return;

        for (int i = acquired.Count - 1; i >= 0; i--)
            BundleManager.ReleaseBundle(acquired[i]);
    }
}
