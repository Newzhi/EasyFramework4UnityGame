using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>打包窗口 Builder / Reporter 页签共用 UI 辅助。</summary>
static class BundlePackerUiShared
{
    public const string WindowTitle = "AssetBundle Packer";
    public const string DefaultSettingPath = BundleBuilder.DefaultSettingPath;

    public static GUIContent Tip(string label, string tooltip) => new GUIContent(label, tooltip);

    public static BuildSetting LoadOrCreateSetting()
    {
        BuildSetting setting = AssetDatabase.LoadAssetAtPath<BuildSetting>(DefaultSettingPath);
        if (setting == null)
        {
            EnsureAssetFolder(Path.GetDirectoryName(DefaultSettingPath).Replace("\\", "/"));
            setting = ScriptableObject.CreateInstance<BuildSetting>();
            AssetDatabase.CreateAsset(setting, DefaultSettingPath);
            AssetDatabase.SaveAssets();
        }

        EnsureDefaultPaths(setting);
        return setting;
    }

    public static void EnsureDefaultPaths(BuildSetting setting)
    {
        if (string.IsNullOrEmpty(setting.deviceOutputPath))
            setting.deviceOutputPath = "Assets/StreamingAssets";
        if (string.IsNullOrEmpty(setting.cdnOutputPath))
            setting.cdnOutputPath = "Bundles/CDN";
        if (string.IsNullOrEmpty(setting.targetDirectory))
            setting.targetDirectory = "Assets/AssetBundle";
    }

    public static void SaveSetting(BuildSetting setting)
    {
        if (setting == null)
            return;

        EditorUtility.SetDirty(setting);
        AssetDatabase.SaveAssets();
        Debug.Log("打包规则已保存: " + DefaultSettingPath);
    }

    public static void DrawOutputPathField(string label, string tooltip, ref string pathField)
    {
        EditorGUILayout.BeginHorizontal();
        pathField = EditorGUILayout.TextField(Tip(label, tooltip), pathField);
        if (GUILayout.Button(Tip("浏览", "打开文件夹选择对话框，选择输出目录。"), GUILayout.Width(60)))
        {
            string abs = EditorUtility.OpenFolderPanel(
                "选择输出目录",
                BundleBuilder.ToAbsoluteAssetsPath(pathField),
                "");
            if (!string.IsNullOrEmpty(abs))
            {
                string relative = BundleBuilder.ToAssetsRelativePath(abs);
                pathField = !string.IsNullOrEmpty(relative) ? relative : abs;
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";

        double kb = bytes / 1024d;
        if (kb < 1024)
            return kb.ToString("F1") + " KB";

        return (kb / 1024d).ToString("F2") + " MB";
    }

    static void EnsureAssetFolder(string assetsFolder)
    {
        if (AssetDatabase.IsValidFolder(assetsFolder))
            return;

        string parent = Path.GetDirectoryName(assetsFolder).Replace("\\", "/");
        string folderName = Path.GetFileName(assetsFolder);
        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}
