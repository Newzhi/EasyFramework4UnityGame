using System;
using UnityEditor;
using UnityEngine;

/// <summary>AssetBundle Packer — Builder 页签：规则、清单开关、Build/Clean。</summary>
static class BundleBuilderTabView
{
    static readonly string[] BuildModeLabels =
    {
        "编辑器测试", "首包（真机模式）", "CDN联网", "DLC分包"
    };

    static readonly string[] FolderRuleLabels =
    {
        "整个文件夹一个包",
        "第一级子文件夹各一个包",
        "每一个子文件夹（含嵌套）各一个包",
    };

    static readonly BundleFolderRule[] FolderRules =
    {
        BundleFolderRule.EntireFolder,
        BundleFolderRule.FirstLevelSubfolders,
        BundleFolderRule.AllSubfolders,
    };

    static readonly BuildMode[] BuildModes =
    {
        BuildMode.EditorTest,
        BuildMode.DeviceDebug,
        BuildMode.CdnHotUpdate,
        BuildMode.DlcPackage,
    };

    static readonly string[] RuleLabels =
    {
        "默认打包 - 按第一级子文件夹打包",
        "细化打包 - 按所有子文件夹打包",
        "自定义打包 - 手动配置每个资源",
    };

    public static void Draw(BuildSetting setting)
    {
        DrawBasicSettings(setting);
        EditorGUILayout.Space(8);
        DrawRuleConfig(setting);

        if (setting.packingRule == PackingRule.Custom)
        {
            EditorGUILayout.Space(8);
            DrawCustomConfigList(setting);
        }

        EditorGUILayout.Space(8);
        DrawCatalogueSettings(setting);
        EditorGUILayout.Space(8);
        DrawActions(setting);
    }

    static void DrawBasicSettings(BuildSetting setting)
    {
        EditorGUILayout.LabelField("基本设置", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        setting.platform = (BuildPlatform)EditorGUILayout.EnumPopup(
            BundlePackerUiShared.Tip("目标平台", "构建目标平台，决定 BuildPipeline 使用的 BuildTarget。"),
            setting.platform);
        setting.usePlatformSubfolders = EditorGUILayout.Toggle(
            BundlePackerUiShared.Tip("按平台分子目录", "开启后输出到 {首包或CDN路径}/{平台名}/。"),
            setting.usePlatformSubfolders);
        if (setting.usePlatformSubfolders)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(
                BundlePackerUiShared.Tip("当前平台输出预览", "真机首包与 CDN 在开启平台子目录时的实际路径。"),
                new GUIContent(GetPlatformOutputPreview(setting)),
                EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }

        setting.version = EditorGUILayout.TextField(
            BundlePackerUiShared.Tip("版本号", "应用版本号（x.y.z），写入资源清单。"),
            setting.version);
        setting.buildNumber = EditorGUILayout.IntField(
            BundlePackerUiShared.Tip("构建号", "递增构建编号，写入资源清单。"),
            setting.buildNumber);

        BundlePackerUiShared.DrawOutputPathField(
            "首包输出路径",
            "首包（真机模式）下 AB 包的输出目录，默认 Assets/StreamingAssets。",
            ref setting.deviceOutputPath);
        BundlePackerUiShared.DrawOutputPathField(
            "联网 CDN 输出路径",
            "CDN联网模式下 AB 包的输出目录，默认 Bundles/CDN。",
            ref setting.cdnOutputPath);

        setting.compressionMode = (BundleCompressionMode)EditorGUILayout.EnumPopup(
            BundlePackerUiShared.Tip("压缩格式", "映射 BuildAssetBundleOptions：LZMA / LZ4 分块 / 不压缩。"),
            setting.compressionMode);

        if (GUILayout.Button(BundlePackerUiShared.Tip("更新版本号（patch+1 / build+1）", "版本号 patch 位 +1，同时 buildNumber +1。")))
            BumpVersion(setting);

        EditorGUI.indentLevel--;
    }

    static void DrawRuleConfig(BuildSetting setting)
    {
        EditorGUILayout.LabelField("打包规则配置", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        int ruleIndex = (int)setting.packingRule;
        int newRuleIndex = EditorGUILayout.Popup(
            BundlePackerUiShared.Tip("打包规则", GetRuleDescription((PackingRule)ruleIndex)),
            ruleIndex,
            RuleLabels);
        PackingRule newRule = (PackingRule)newRuleIndex;
        if (newRuleIndex != ruleIndex && newRule != PackingRule.Custom)
            setting.buildMode = BuildMode.DeviceDebug;
        setting.packingRule = newRule;

        if (setting.packingRule != PackingRule.Custom)
        {
            int modeIndex = Array.IndexOf(BuildModes, setting.buildMode);
            if (modeIndex < 0)
                modeIndex = 0;
            int newModeIndex = EditorGUILayout.Popup(
                BundlePackerUiShared.Tip("打包模式", GetBuildModeFieldTooltip(setting.buildMode)),
                modeIndex,
                BuildModeLabels);
            setting.buildMode = BuildModes[newModeIndex];
        }

        EditorGUILayout.BeginHorizontal();
        setting.targetDirectory = EditorGUILayout.TextField(
            BundlePackerUiShared.Tip("目标资源目录", "Default / Detailed 规则下扫描并打包资源的根目录。"),
            setting.targetDirectory);
        if (GUILayout.Button(BundlePackerUiShared.Tip("浏览", "选择资源根目录。"), GUILayout.Width(60)))
        {
            string abs = EditorUtility.OpenFolderPanel(
                "选择资源根目录",
                BundleBuilder.ToAbsoluteAssetsPath(setting.targetDirectory),
                "");
            string relative = BundleBuilder.ToAssetsRelativePath(abs);
            if (!string.IsNullOrEmpty(relative))
                setting.targetDirectory = relative;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
    }

    static void DrawCustomConfigList(BuildSetting setting)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            BundlePackerUiShared.Tip("资源打包配置", "自定义打包：每项可指定路径、粒度、打包模式与资源优先级。"),
            EditorStyles.boldLabel);
        if (GUILayout.Button(BundlePackerUiShared.Tip("+ 添加配置", "新增一条自定义打包配置项。"), GUILayout.Width(100)))
        {
            setting.customItems.Add(new BundleConfigItem
            {
                assetPath = setting.targetDirectory,
                bundleName = "bundle_" + (setting.customItems.Count + 1),
                buildMode = setting.buildMode,
                folderPackingRule = BundleFolderRule.EntireFolder,
                resourcePriority = ResourcePriority.Normal,
            });
        }
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < setting.customItems.Count; i++)
        {
            BundleConfigItem item = setting.customItems[i];
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("配置项 #" + (i + 1), EditorStyles.boldLabel);
            if (GUILayout.Button(BundlePackerUiShared.Tip("删除", "移除此配置项。"), GUILayout.Width(60)))
            {
                setting.customItems.RemoveAt(i);
                EditorGUILayout.EndVertical();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            item.assetPath = EditorGUILayout.TextField(
                BundlePackerUiShared.Tip("资源路径", "文件夹或单文件路径。"),
                item.assetPath);
            if (GUILayout.Button(BundlePackerUiShared.Tip("浏览", "选择资源路径。"), GUILayout.Width(60)))
            {
                string abs = EditorUtility.OpenFolderPanel(
                    "选择资源路径",
                    BundleBuilder.ToAbsoluteAssetsPath(item.assetPath),
                    "");
                string relative = BundleBuilder.ToAssetsRelativePath(abs);
                if (!string.IsNullOrEmpty(relative))
                    item.assetPath = relative;
            }
            EditorGUILayout.EndHorizontal();

            bool isFolderPath = AssetDatabase.IsValidFolder(item.assetPath);
            int folderRuleIndex = Array.IndexOf(FolderRules, item.folderPackingRule);
            if (folderRuleIndex < 0)
                folderRuleIndex = 0;
            EditorGUI.BeginDisabledGroup(!isFolderPath);
            int newFolderRuleIndex = EditorGUILayout.Popup(
                BundlePackerUiShared.Tip("文件夹打包粒度", "资源路径为文件夹时生效。"),
                folderRuleIndex,
                FolderRuleLabels);
            EditorGUI.EndDisabledGroup();
            if (isFolderPath)
                item.folderPackingRule = FolderRules[newFolderRuleIndex];

            bool needsBundleName = !isFolderPath || item.folderPackingRule == BundleFolderRule.EntireFolder;
            EditorGUI.BeginDisabledGroup(!needsBundleName);
            item.bundleName = EditorGUILayout.TextField(
                BundlePackerUiShared.Tip("包名 (Bundle Name)", "整包或单文件时使用的 AssetBundle 名称。"),
                item.bundleName);
            EditorGUI.EndDisabledGroup();

            int itemModeIndex = Array.IndexOf(BuildModes, item.buildMode);
            if (itemModeIndex < 0)
                itemModeIndex = 0;
            int newItemModeIndex = EditorGUILayout.Popup(
                BundlePackerUiShared.Tip("打包模式", GetBuildModeDescription(item.buildMode)),
                itemModeIndex,
                BuildModeLabels);
            item.buildMode = BuildModes[newItemModeIndex];

            item.resourcePriority = (ResourcePriority)EditorGUILayout.EnumPopup(
                BundlePackerUiShared.Tip("资源优先级", "该项资源优先级；聚合到 Bundle 的 resourcePriority（取最高）。"),
                item.resourcePriority);
            item.note = EditorGUILayout.TextField(
                BundlePackerUiShared.Tip("备注说明", "可选的配置说明。"),
                item.note);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }
    }

    static void DrawCatalogueSettings(BuildSetting setting)
    {
        EditorGUILayout.LabelField("清单 / 报告开关", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        setting.useTopologicalSort = EditorGUILayout.Toggle(
            BundlePackerUiShared.Tip("依赖拓扑排序", "写入 bundles[] 时对依赖做拓扑排序（推荐开启）。"),
            setting.useTopologicalSort);
        setting.runBuildAnalyzer = EditorGUILayout.Toggle(
            BundlePackerUiShared.Tip("打包后生成报告", "Build 成功后写入 Reports/BundleBuildReport.json，在 Reporter 页签查看。"),
            setting.runBuildAnalyzer);
        setting.loadPathDuplicateAsError = EditorGUILayout.Toggle(
            BundlePackerUiShared.Tip("loadPath 重复阻断", "关闭时仅 Warning；开启时重复 loadPath 会阻断写清单。"),
            setting.loadPathDuplicateAsError);
        setting.useDirectDependenciesOnly = EditorGUILayout.Toggle(
            BundlePackerUiShared.Tip("仅存直接依赖", "bundles[] 仅存直接依赖，并写入 dependenciesAll 兼容字段。"),
            setting.useDirectDependenciesOnly);
        setting.enableAutoSharedBundle = EditorGUILayout.Toggle(
            BundlePackerUiShared.Tip("自动公共包", "跨包引用 ≥ 阈值的资产自动抽到 shared_auto.bundle。"),
            setting.enableAutoSharedBundle);
        setting.cdnBaseUrl = EditorGUILayout.TextField(
            BundlePackerUiShared.Tip("CDN 根 URL", "运行时拉取清单与 AB 的根 URL（末尾无斜杠）。"),
            setting.cdnBaseUrl);

        EditorGUI.indentLevel--;
    }

    static void DrawActions(BuildSetting setting)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        GUI.backgroundColor = new Color(0.2f, 0.55f, 0.35f);
        if (GUILayout.Button(BundlePackerUiShared.Tip("增量打包", "对比 BuildCache，无源变更时跳过 Unity 构建；仍更新清单/Manifest。"), GUILayout.Width(100), GUILayout.Height(30)))
        {
            BundlePackerUiShared.SaveSetting(setting);
            BundleBuilder.Build(setting, BundleBuildExecutionMode.Incremental);
        }

        GUI.backgroundColor = new Color(0.2f, 0.5f, 0.9f);
        if (GUILayout.Button(BundlePackerUiShared.Tip("覆盖打包", "ForceRebuild 全量重建并刷新 BuildCache。"), GUILayout.Width(100), GUILayout.Height(30)))
        {
            BundlePackerUiShared.SaveSetting(setting);
            BundleBuilder.Build(setting, BundleBuildExecutionMode.FullOverwrite);
        }

        GUI.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
        if (GUILayout.Button(BundlePackerUiShared.Tip("清理打包", "清理首包/CDN 输出、清单与 BuildCache。"), GUILayout.Width(100), GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("清理打包", "确定清理输出目录中的 bundle 与清单？", "确定", "取消"))
                BundleBuilder.Clean(setting);
        }

        GUI.backgroundColor = Color.white;
        if (GUILayout.Button(BundlePackerUiShared.Tip("保存规则", "将当前配置写入 DefaultBuildSetting.asset。"), GUILayout.Width(100), GUILayout.Height(30)))
            BundlePackerUiShared.SaveSetting(setting);

        EditorGUILayout.EndHorizontal();
    }

    static string GetPlatformOutputPreview(BuildSetting setting)
    {
        string folder = BundlePlatformPaths.GetFolderName(setting.platform);
        string device = (setting.deviceOutputPath ?? "").Replace("\\", "/").TrimEnd('/');
        string cdn = (setting.cdnOutputPath ?? "").Replace("\\", "/").TrimEnd('/');
        return "首包: " + device + "/" + folder + "/" + BundlePlatformPaths.BasePackageId
            + "  |  CDN: " + cdn + "/" + folder + "/" + BundlePlatformPaths.BasePackageId;
    }

    static void BumpVersion(BuildSetting setting)
    {
        string[] parts = setting.version.Split('.');
        if (parts.Length < 3)
        {
            Debug.LogWarning("版本号格式应为 x.y.z");
            return;
        }

        int patch = int.TryParse(parts[2], out int p) ? p : 0;
        parts[2] = (patch + 1).ToString();
        setting.version = string.Join(".", parts);
        setting.buildNumber++;
    }

    static string GetRuleDescription(PackingRule rule)
    {
        switch (rule)
        {
            case PackingRule.Detailed:
                return "细化打包：指定目录下每一个子文件夹（含嵌套）各打一个 AB 包。";
            case PackingRule.Custom:
                return "自定义打包：每项可指定路径、文件夹粒度与打包模式。";
            default:
                return "默认打包：指定目录下每个第一级子文件夹各打一个 AB 包。";
        }
    }

    static string GetBuildModeFieldTooltip(BuildMode mode)
    {
        return "默认/细化打包时默认为「首包（真机模式）」。\n" + GetBuildModeDescription(mode);
    }

    static string GetBuildModeDescription(BuildMode mode)
    {
        switch (mode)
        {
            case BuildMode.DeviceDebug:
                return "首包（真机模式）：AB 输出到首包输出路径。";
            case BuildMode.CdnHotUpdate:
                return "CDN联网：AB 输出到 CDN 输出路径。";
            case BuildMode.DlcPackage:
                return "DLC分包：输出到 {平台}/DLC_{dlcPackageId}/（Version + Bundles + Config）。";
            default:
                return "编辑器测试：不生成 .bundle，仅更新清单。";
        }
    }
}
