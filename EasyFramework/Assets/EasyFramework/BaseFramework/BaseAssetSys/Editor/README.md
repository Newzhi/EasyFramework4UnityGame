# Editor 打包模块说明

> 路径：`BaseAssetSys/Editor/`  
> 菜单：**vFramework → AssetBundle Packer**（单窗口双页签）

---

## 分层架构

```text
BundlePacker/          UI 层：统一窗口（Builder 页签 + Reporter 页签）
        │
        ├── BundleBuild/Pipeline/   编排：BundleBuildPipeline、BuildAssetBundleOptionsFactory
        ├── BundleBuild/Shared/     SharedBundlePlanner（全自动公共包）
        ├── BundleBuild/Integrity/  BuildManifest、BuildCache、Hash
        ├── BundleBuilder/          薄门面：Build / Clean / Validate / RuleResolver
        │   └── Catalogue/          清单写入与 loadPath 校验
        ├── BundleReporter/         构建后只读分析（Analyzer → JSON 报告）
        │
        ▼
BundleRuleConfig/      配置层：BuildSetting、AssetCatalog schema、拓扑工具、BundleIntegrityUtil
        │
        ▼
产出                   {deviceOutputPath|cdnOutputPath}/{平台}/Base/
                       ├── Bundles/*.bundle
                       └── Version/catalog.bytes (+ manifest.json / version.json)
                       + BundleRuleConfig/Catalogue/AssetCatalog.bytes（工程内副本）
                       + Reports/*.json（构建报告，非资源清单）
```

| 模块 | 目录 | 职责 |
|------|------|------|
| **Packer UI** | `BundlePacker/` | `BundlePackerWindow` 双页签；`BundleBuilderTabView` / `BundleReporterTabView` |
| **Builder** | `BundleBuilder/` | `BundleBuilder` 编排 BuildPipeline |
| **Builder** | `BundleBuilder/RuleResolver.cs` | Default / Detailed / Custom → `AssetBundleBuild[]` |
| **Builder** | `BundleBuilder/Catalogue/` | `CatalogueWriter`（拓扑序 + 环检测）、`CatalogueValidator` |
| **Builder** | `BundleBuilder/Tests/` | EditMode 单测（`BundleDependencyTopologyTests`） |
| **Reporter** | `BundleReporter/` | `BundleBuildAnalyzer`、`BundleBuildReport` DTO |
| **Analysis** | `BundleBuild/Analysis/` | `DependencyGraphWriter`、`BundleDependencyExplorer`（Reporter 页签） |
| **配置** | `BundleRuleConfig/` | `BuildSetting`、`AssetCatalog`、`BundleDependencyTopology` |
| **Player** | 根目录 | `StreamingAssetsPlatformBuildFilter`、`StreamingAssetsPlatformIsolation` |

---

## 打包流水线（Builder）

```text
BundlePackerWindow [Builder 页签] → Save BuildSetting
  → BundleBuilder.Build(setting, Incremental | FullOverwrite)
    → BundleBuildPipeline.Execute
      → RuleResolver.Resolve
      → SharedBundlePlanner（可选）
      → BuildPipeline.BuildAssetBundles（增量可跳过 / 覆盖 ForceRebuild）
      → CatalogueWriter（priority + hash/crc + buildId）
      → BuildManifestService（Manifest / diff / Cache）
      → BundleBuildAnalyzer（可选）
```

**操作区**：`增量打包` | `覆盖打包` | `清理打包` | `保存规则`（见 BuilderEditorBlueprint.html）。

**Reporter 页签**只读上述 JSON，不触发 Build。

---

## UI 蓝图

| 页签 | HTML 原型 | Editor 内 |
|------|-----------|-----------|
| Builder | [BuilderEditorBlueprint.html](../Docs/BuilderEditorBlueprint.html) | 仅设计参考 |
| Reporter | [ReportEditorBlueprint.html](../Docs/ReportEditorBlueprint.html) | 仅设计参考（**无**「打开 HTML」按钮；Reporter 页签直接读 JSON 报告） |

---

## 打包模式与输出

| 模式 | AB | 清单 | 报告 |
|------|-----|------|------|
| EditorTest | 不打 AB | 写 `catalog.bytes`（`bundles` 空） | loadPath 等（无包体） |
| DeviceDebug | `deviceOutputPath` | 双份 `.bytes`（工程 + `Base/Version/`） | 完整 |
| CdnHotUpdate | `cdnOutputPath` | 双份 `.bytes` | 完整 |
| DlcPackage | `DLC_{id}/` 布局 | `catalog.fragment.bytes` + 双份 `.bytes` | 完整 |

---

## 相关文档

- [Docs/MainRoadmap.md](../Docs/MainRoadmap.md)  
- [BundleRuleConfig/README.md](../BundleRuleConfig/README.md)  
- [Docs/CatalogueReference.md](../Docs/CatalogueReference.md)  
- [Docs/BundleBuildOptimizationAndTopologyPlan.md](../Docs/BundleBuildOptimizationAndTopologyPlan.md)
