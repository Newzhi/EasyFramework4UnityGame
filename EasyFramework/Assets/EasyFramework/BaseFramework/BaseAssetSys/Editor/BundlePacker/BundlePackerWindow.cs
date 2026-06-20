using UnityEditor;
using UnityEngine;

/// <summary>
/// AssetBundle Packer 统一窗口：Builder（打包）与 Reporter（构建报告）双页签。
/// 菜单：vFramework → AssetBundle Packer
/// </summary>
public class BundlePackerWindow : EditorWindow
{
    enum PackerTab
    {
        Builder = 0,
        Reporter = 1,
    }

    BuildSetting setting;
    Vector2 scrollPos;
    PackerTab activeTab = PackerTab.Builder;
    BuildMode reportBrowseMode = BuildMode.DeviceDebug;

    static readonly string[] TabLabels = { "打包 Builder", "报告 Reporter" };

    [MenuItem("vFramework/AssetBundle Packer")]
    static void OpenWindow()
    {
        BundlePackerWindow window = GetWindow<BundlePackerWindow>();
        window.titleContent = new GUIContent(BundlePackerUiShared.WindowTitle);
        window.minSize = new Vector2(720, 640);
        window.Show();
    }

    void OnEnable()
    {
        setting = BundlePackerUiShared.LoadOrCreateSetting();
        reportBrowseMode = setting.buildMode;
    }

    void OnGUI()
    {
        if (setting == null)
            setting = BundlePackerUiShared.LoadOrCreateSetting();

        activeTab = (PackerTab)GUILayout.Toolbar((int)activeTab, TabLabels);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        switch (activeTab)
        {
            case PackerTab.Builder:
                BundleBuilderTabView.Draw(setting);
                break;
            case PackerTab.Reporter:
                BundleReporterTabView.Draw(setting, ref reportBrowseMode);
                break;
        }
        EditorGUILayout.EndScrollView();

        if (GUI.changed)
            EditorUtility.SetDirty(setting);
    }
}
