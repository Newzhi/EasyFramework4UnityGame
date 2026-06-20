using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// StreamingAssets 下多平台 AB 子目录的临时隔离：Player 构建前移出非目标平台，构建后还原。
/// 工程内可 Win/Android 并存联调；打进 Player/APK 时仅保留当前平台。
/// </summary>
public static class StreamingAssetsPlatformIsolation
{
    const string BackupFolderName = "StreamingAssetsPlatformBackup";
    const string StateFileName = "moved-folders.json";

    public static readonly string[] KnownPlatformFolders =
    {
        BundlePlatformPaths.WindowsFolder,
        BundlePlatformPaths.AndroidFolder,
        BundlePlatformPaths.IOSFolder,
        BundlePlatformPaths.MacFolder,
        BundlePlatformPaths.WebGLFolder,
    };

    static readonly List<string> MovedFolderNames = new List<string>();

    static string BackupRoot =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", BackupFolderName));

    static string StateFilePath => Path.Combine(BackupRoot, StateFileName);

    static string StreamingAssetsRoot =>
        Path.Combine(Application.dataPath, "StreamingAssets");

    [InitializeOnLoadMethod]
    static void RecoverOrphanedBackupOnLoad()
    {
        if (!File.Exists(StateFilePath))
            return;

        if (!TryLoadState(out List<string> folders) || folders == null || folders.Count == 0)
            return;

        bool needsRestore = false;
        foreach (string folder in folders)
        {
            string backupPath = Path.Combine(BackupRoot, folder);
            string destPath = Path.Combine(StreamingAssetsRoot, folder);
            if (Directory.Exists(backupPath) && !Directory.Exists(destPath))
            {
                needsRestore = true;
                break;
            }
        }

        if (needsRestore)
        {
            Debug.LogWarning(
                "[StreamingAssetsPlatformIsolation] 检测到未还原的平台目录备份，正在自动恢复…");
            RestoreFromBackup(folders);
        }
    }

    public static string GetFolderForBuildTarget(BuildTarget target)
    {
        switch (target)
        {
            case BuildTarget.Android:
                return BundlePlatformPaths.AndroidFolder;
            case BuildTarget.iOS:
                return BundlePlatformPaths.IOSFolder;
            case BuildTarget.StandaloneOSX:
                return BundlePlatformPaths.MacFolder;
            case BuildTarget.WebGL:
                return BundlePlatformPaths.WebGLFolder;
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return BundlePlatformPaths.WindowsFolder;
            default:
                return null;
        }
    }

    public static string GetFolderForBuildPlatform(BuildPlatform platform)
    {
        return BundlePlatformPaths.GetFolderName(platform);
    }

    public static bool IsStreamingAssetsAssetPath(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;

        string normalized = assetPath.Replace("\\", "/");
        return normalized == "Assets/StreamingAssets"
            || normalized.StartsWith("Assets/StreamingAssets/", StringComparison.Ordinal);
    }

    public static bool IsUnderStreamingAssetsAbsolute(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return false;

        string streaming = Path.GetFullPath(StreamingAssetsRoot);
        string full = Path.GetFullPath(absolutePath);
        return full.StartsWith(streaming, StringComparison.OrdinalIgnoreCase);
    }

    public static List<string> ListPresentPlatformFolders()
    {
        var present = new List<string>();
        if (!Directory.Exists(StreamingAssetsRoot))
            return present;

        foreach (string folder in KnownPlatformFolders)
        {
            if (Directory.Exists(Path.Combine(StreamingAssetsRoot, folder)))
                present.Add(folder);
        }

        return present;
    }

    public static void WarnIfMultiplePlatformFoldersPresent()
    {
        List<string> present = ListPresentPlatformFolders();
        if (present.Count <= 1)
            return;

        Debug.LogWarning(
            "[StreamingAssetsPlatformIsolation] StreamingAssets 内存在 " + present.Count +
            " 个平台 AB 子目录（" + string.Join(", ", present) +
            "）。Build Player 时会自动仅保留目标平台；若手动复制工程 StreamingAssets，请只拷贝对应平台目录。");
    }

    /// <summary>Player 构建前：仅保留目标平台子目录。</summary>
    public static bool IsolateForPlayerBuild(BuildTarget target, bool failBuildOnError)
    {
        string keepFolder = GetFolderForBuildTarget(target);
        if (string.IsNullOrEmpty(keepFolder))
        {
            Debug.LogWarning(
                "[StreamingAssetsPlatformIsolation] 未映射 BuildTarget " +
                target + "，跳过平台过滤。");
            return true;
        }

        if (!Directory.Exists(StreamingAssetsRoot))
            return true;

        RestoreFromBackupIfPending();
        ClearBackupRoot();
        MovedFolderNames.Clear();

        foreach (string folder in KnownPlatformFolders)
        {
            if (folder == keepFolder)
                continue;

            if (TryMoveOut(folder))
                MovedFolderNames.Add(folder);
        }

        if (MovedFolderNames.Count > 0)
        {
            SaveState(MovedFolderNames, keepFolder);
            AssetDatabase.Refresh();
            Debug.Log(
                "[StreamingAssetsPlatformIsolation] Player 构建仅保留 StreamingAssets/" +
                keepFolder + "/，已临时移出 " + MovedFolderNames.Count + " 个其它平台子目录。");
        }

        List<string> stillPresent = ListOtherPlatformFolders(keepFolder);
        if (stillPresent.Count > 0)
        {
            string msg =
                "[StreamingAssetsPlatformIsolation] 隔离后仍存在非目标平台目录: " +
                string.Join(", ", stillPresent);
            if (failBuildOnError)
            {
                Debug.LogError(msg);
                return false;
            }

            Debug.LogWarning(msg);
        }

        return true;
    }

    public static void RestoreAfterPlayerBuild()
    {
        List<string> folders = MovedFolderNames.Count > 0
            ? new List<string>(MovedFolderNames)
            : LoadStateFolders();

        if (folders != null && folders.Count > 0)
            RestoreFromBackup(folders);
    }

    static List<string> ListOtherPlatformFolders(string keepFolder)
    {
        var others = new List<string>();
        foreach (string folder in KnownPlatformFolders)
        {
            if (folder == keepFolder)
                continue;

            if (Directory.Exists(Path.Combine(StreamingAssetsRoot, folder)))
                others.Add(folder);
        }

        return others;
    }

    static bool TryMoveOut(string folderName)
    {
        string src = Path.Combine(StreamingAssetsRoot, folderName);
        if (!Directory.Exists(src))
            return false;

        string assetPath = ToStreamingAssetsAssetPath(folderName);
        Directory.CreateDirectory(BackupRoot);
        string dest = Path.Combine(BackupRoot, folderName);

        if (Directory.Exists(dest) && !DeleteDirectoryRecursive(dest))
        {
            Debug.LogError("[StreamingAssetsPlatformIsolation] 无法清理备份目录: " + dest);
            return false;
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            if (!CopyDirectoryRecursive(src, dest, includeMeta: true))
            {
                Debug.LogError(
                    "[StreamingAssetsPlatformIsolation] 复制到备份失败: " + src + " → " + dest);
                return false;
            }

            if (!DeleteStreamingAssetsPlatformFolder(assetPath, src))
            {
                Debug.LogError(
                    "[StreamingAssetsPlatformIsolation] 移出 StreamingAssets 失败: " +
                    assetPath + "。请关闭占用该目录的进程后重试。");
                DeleteDirectoryRecursive(dest);
                return false;
            }

            return true;
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }
    }

    static void RestoreFromBackupIfPending()
    {
        List<string> pending = LoadStateFolders();
        if (pending != null && pending.Count > 0)
            RestoreFromBackup(pending);
    }

    static void RestoreFromBackup(List<string> folderNames)
    {
        if (folderNames == null || folderNames.Count == 0)
            return;

        Directory.CreateDirectory(StreamingAssetsRoot);

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string folderName in folderNames)
            {
                string src = Path.Combine(BackupRoot, folderName);
                if (!Directory.Exists(src))
                    continue;

                string dest = Path.Combine(StreamingAssetsRoot, folderName);
                if (Directory.Exists(dest) && !DeleteDirectoryRecursive(dest))
                {
                    Debug.LogWarning(
                        "[StreamingAssetsPlatformIsolation] 还原时目标已存在且无法覆盖: " + dest);
                    continue;
                }

                if (!CopyDirectoryRecursive(src, dest, includeMeta: true))
                {
                    Debug.LogError(
                        "[StreamingAssetsPlatformIsolation] 还原复制失败: " + src + " → " + dest);
                    continue;
                }

                DeleteDirectoryRecursive(src);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        MovedFolderNames.Clear();
        ClearBackupRoot();
        AssetDatabase.Refresh();
        Debug.Log("[StreamingAssetsPlatformIsolation] 已还原 StreamingAssets 多平台子目录。");
    }

    static bool DeleteStreamingAssetsPlatformFolder(string assetPath, string absolutePath)
    {
        ClearReadOnlyRecursive(absolutePath);

        if (AssetDatabase.IsValidFolder(assetPath))
        {
            if (AssetDatabase.DeleteAsset(assetPath))
                return !Directory.Exists(absolutePath);

            AssetDatabase.Refresh();
        }

        if (!Directory.Exists(absolutePath))
            return true;

        try
        {
            FileUtil.DeleteFileOrDirectory(absolutePath);
            string metaPath = absolutePath + ".meta";
            if (File.Exists(metaPath))
                FileUtil.DeleteFileOrDirectory(metaPath);
            AssetDatabase.Refresh();
            return !Directory.Exists(absolutePath);
        }
        catch (Exception ex)
        {
            Debug.LogError(
                "[StreamingAssetsPlatformIsolation] Delete failed: " + absolutePath + " | " + ex.Message);
            return false;
        }
    }

    static bool CopyDirectoryRecursive(string sourceDir, string destDir, bool includeMeta)
    {
        if (!Directory.Exists(sourceDir))
            return false;

        Directory.CreateDirectory(destDir);
        ClearReadOnlyRecursive(sourceDir);

        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            if (!includeMeta && file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            string relative = file.Substring(sourceDir.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string targetFile = Path.Combine(destDir, relative);
            string targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            try
            {
                File.Copy(file, targetFile, true);
            }
            catch (Exception ex)
            {
                Debug.LogError("[StreamingAssetsPlatformIsolation] Copy failed: " + file + " | " + ex.Message);
                return false;
            }
        }

        return true;
    }

    static bool DeleteDirectoryRecursive(string path)
    {
        if (!Directory.Exists(path))
            return true;

        ClearReadOnlyRecursive(path);
        try
        {
            Directory.Delete(path, true);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[StreamingAssetsPlatformIsolation] DeleteDirectory: " + path + " | " + ex.Message);
            try
            {
                FileUtil.DeleteFileOrDirectory(path);
                return !Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }
    }

    static void ClearReadOnlyRecursive(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                TryClearReadOnly(file);
            return;
        }

        if (File.Exists(path))
            TryClearReadOnly(path);
    }

    static void TryClearReadOnly(string filePath)
    {
        try
        {
            FileAttributes attrs = File.GetAttributes(filePath);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
        }
        catch
        {
            // ignored
        }
    }

    static string ToStreamingAssetsAssetPath(string folderName)
    {
        return "Assets/StreamingAssets/" + folderName;
    }

    static void SaveState(List<string> movedFolders, string keepFolder)
    {
        var state = new BackupState
        {
            keepFolder = keepFolder,
            movedFolders = movedFolders.ToArray()
        };

        Directory.CreateDirectory(BackupRoot);
        File.WriteAllText(StateFilePath, JsonUtility.ToJson(state, true));
    }

    static List<string> LoadStateFolders()
    {
        if (!TryLoadState(out List<string> folders))
            return null;

        return folders;
    }

    static bool TryLoadState(out List<string> folders)
    {
        folders = null;
        if (!File.Exists(StateFilePath))
            return false;

        try
        {
            BackupState state = JsonUtility.FromJson<BackupState>(File.ReadAllText(StateFilePath));
            if (state?.movedFolders == null || state.movedFolders.Length == 0)
                return false;

            folders = new List<string>(state.movedFolders);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void ClearBackupRoot()
    {
        if (!Directory.Exists(BackupRoot))
            return;

        try
        {
            Directory.Delete(BackupRoot, true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[StreamingAssetsPlatformIsolation] ClearBackupRoot: " + ex.Message);
        }
    }

    [Serializable]
    class BackupState
    {
        public string keepFolder;
        public string[] movedFolders;
    }
}
