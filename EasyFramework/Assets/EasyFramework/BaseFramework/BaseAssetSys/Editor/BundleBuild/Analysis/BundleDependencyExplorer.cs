using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reporter 页签：Bundle 依赖图浏览（读取 Reports/DependencyGraph.json）。
/// </summary>
static class BundleDependencyExplorer
{
    static string searchText = string.Empty;
    static int selectedIndex = -1;
    static Vector2 leftScroll;
    static Vector2 rightScroll;
    static DependencyGraphWriter.DependencyGraphDocument cachedDocument;
    static string cachedBundleRoot;

    public static void Draw(BuildSetting setting, BuildMode reportBrowseMode)
    {
        EditorGUILayout.LabelField("依赖 Explorer（B4）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "读取 Reports/DependencyGraph.json。打包成功后自动生成；左列选 bundle，右列查看直接/反向/传递依赖。",
            MessageType.Info);

        string bundleRoot = BundleBuilder.ResolveBundleRoot(reportBrowseMode, setting);
        if (!string.Equals(cachedBundleRoot, bundleRoot, StringComparison.OrdinalIgnoreCase)
            || cachedDocument == null)
        {
            cachedBundleRoot = bundleRoot;
            DependencyGraphWriter.TryLoad(bundleRoot, out cachedDocument);
        }

        if (cachedDocument == null || cachedDocument.nodes == null || cachedDocument.nodes.Length == 0)
        {
            EditorGUILayout.HelpBox("未找到 DependencyGraph.json，请先执行 DeviceDebug 打包。", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField(
            "buildId=" + cachedDocument.buildId
            + "  nodes=" + cachedDocument.nodes.Length,
            EditorStyles.miniLabel);

        searchText = EditorGUILayout.TextField("搜索 bundle", searchText);

        EditorGUILayout.BeginHorizontal();
        DrawLeftList();
        DrawRightDetail();
        EditorGUILayout.EndHorizontal();
    }

    static void DrawLeftList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(260));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll, GUILayout.Height(280));

        var filtered = cachedDocument.nodes
            .Where(n => n != null && (string.IsNullOrEmpty(searchText)
                || n.bundleName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToArray();

        for (int i = 0; i < filtered.Length; i++)
        {
            bool selected = selectedIndex == i;
            if (GUILayout.Toggle(selected, filtered[i].bundleName, EditorStyles.toolbarButton))
                selectedIndex = i;
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    static void DrawRightDetail()
    {
        EditorGUILayout.BeginVertical("box");
        rightScroll = EditorGUILayout.BeginScrollView(rightScroll, GUILayout.Height(280));

        if (selectedIndex < 0)
        {
            EditorGUILayout.LabelField("请选择左侧 bundle。");
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        var filtered = cachedDocument.nodes
            .Where(n => n != null && (string.IsNullOrEmpty(searchText)
                || n.bundleName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToArray();

        if (selectedIndex >= filtered.Length)
        {
            EditorGUILayout.LabelField("索引无效。");
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        DependencyGraphWriter.DependencyGraphNode node = filtered[selectedIndex];
        EditorGUILayout.LabelField(node.bundleName, EditorStyles.boldLabel);
        DrawList("直接依赖", node.directDependencies);
        DrawList("反向依赖", node.reverseDependencies);
        DrawList("传递闭包", node.transitiveClosure);

        EditorGUILayout.Space(8);
        if (GUILayout.Button("在 Analyzer 冗余表中搜索该 bundle"))
        {
            EditorGUIUtility.systemCopyBuffer = node.bundleName;
            Debug.Log("[BundleDependencyExplorer] 已复制 bundle 名到剪贴板: " + node.bundleName);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    static void DrawList(string title, string[] items)
    {
        EditorGUILayout.LabelField(title + " (" + (items?.Length ?? 0) + ")", EditorStyles.miniBoldLabel);
        if (items == null || items.Length == 0)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("(无)", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
            return;
        }

        EditorGUI.indentLevel++;
        foreach (string item in items)
            EditorGUILayout.LabelField(item, EditorStyles.miniLabel);
        EditorGUI.indentLevel--;
    }
}
