using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>AssetBundle Packer — Reporter 页签：读取 BundleBuildReport.json 并展示。</summary>
static class BundleReporterTabView
{
    const int ReportTopN = 10;

    public static void Draw(BuildSetting setting, ref BuildMode reportBrowseMode)
    {
        EditorGUILayout.LabelField("构建报告", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "读取指定打包模式对应 bundleRoot 下的 BundleBuildReport.json。"
            + "需先在 Builder 页签执行打包，并开启「打包后生成报告」。",
            MessageType.Info);

        EditorGUILayout.Space(4);
        DrawReportSource(setting, ref reportBrowseMode);

        string bundleRoot = BundleBuilder.ResolveBundleRoot(reportBrowseMode, setting);
        if (!BundleBuildAnalyzer.TryLoadReport(bundleRoot, out BundleBuildReport report))
        {
            EditorGUILayout.HelpBox("未找到报告文件。", MessageType.Warning);
            DrawOpenReportActions(setting, reportBrowseMode);
            return;
        }

        DrawSummary(report);
        EditorGUILayout.Space(8);
        DrawReportList("冗余候选 Top " + ReportTopN, report.redundantAssets, ReportTopN, item =>
            item.assetPath + " ← [" + string.Join(", ", item.referencedByBundles ?? Array.Empty<string>()) + "]");
        EditorGUILayout.Space(4);
        DrawReportList("包体 Top " + ReportTopN, report.bundleSizes, ReportTopN, item =>
            item.bundleName + "  " + BundlePackerUiShared.FormatBytes(item.bytes));
        EditorGUILayout.Space(4);
        DrawReportList("跨包引用边 Top " + ReportTopN, report.crossBundleEdges, ReportTopN, item =>
            item.consumerBundle + " → " + item.providerBundle + "  (" + item.dependencyAssetPath + ")");
        EditorGUILayout.Space(4);
        DrawReportList("loadPath 冲突", report.loadPathDuplicates, ReportTopN, item =>
            item.loadPath + " : " + item.firstAssetPath + " / " + item.secondAssetPath);

        EditorGUILayout.Space(8);
        DrawOpenReportActions(setting, reportBrowseMode);

        EditorGUILayout.Space(12);
        BundleDependencyExplorer.Draw(setting, reportBrowseMode);
    }

    static void DrawReportSource(BuildSetting setting, ref BuildMode reportBrowseMode)
    {
        string bundleRoot = BundleBuilder.ResolveBundleRoot(reportBrowseMode, setting);
        string reportPath = BundleBuildAnalyzer.GetReportPath(bundleRoot);

        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField("bundleRoot", bundleRoot, EditorStyles.miniLabel);
        EditorGUILayout.LabelField("报告路径", reportPath, EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("浏览模式", GUILayout.Width(100));
        reportBrowseMode = (BuildMode)EditorGUILayout.EnumPopup(reportBrowseMode);
        EditorGUILayout.EndHorizontal();
        EditorGUI.indentLevel--;
    }

    static void DrawSummary(BundleBuildReport report)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("摘要", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "bundleCount=" + report.bundleCount
            + "  platform=" + report.platform
            + "  buildMode=" + report.buildMode
            + "  analyzeTime=" + report.buildTimeSeconds.ToString("F2") + "s",
            EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    static void DrawOpenReportActions(BuildSetting setting, BuildMode reportBrowseMode)
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button(BundlePackerUiShared.Tip("刷新报告", "重新从磁盘读取 JSON。")))
            GUI.FocusControl(null);

        if (GUILayout.Button(BundlePackerUiShared.Tip("在资源管理器中打开", "打开 Reports 目录或 JSON 文件。")))
        {
            string reportPath = BundleBuildAnalyzer.GetReportPath(
                BundleBuilder.ResolveBundleRoot(reportBrowseMode, setting));
            string openPath = File.Exists(reportPath) ? reportPath : Path.GetDirectoryName(reportPath);

            if (!string.IsNullOrEmpty(openPath) && (File.Exists(openPath) || Directory.Exists(openPath)))
                EditorUtility.RevealInFinder(openPath);
            else
                Debug.LogWarning("报告目录不存在: " + reportPath);
        }

        EditorGUILayout.EndHorizontal();
    }

    static void DrawReportList<T>(string title, T[] items, int topN, Func<T, string> formatter)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        if (items == null || items.Length == 0)
        {
            EditorGUILayout.LabelField("(无)", EditorStyles.miniLabel);
            return;
        }

        int count = Math.Min(topN, items.Length);
        for (int i = 0; i < count; i++)
            EditorGUILayout.LabelField("• " + formatter(items[i]), EditorStyles.miniLabel);

        if (items.Length > count)
            EditorGUILayout.LabelField("… 另有 " + (items.Length - count) + " 条", EditorStyles.miniLabel);
    }
}
