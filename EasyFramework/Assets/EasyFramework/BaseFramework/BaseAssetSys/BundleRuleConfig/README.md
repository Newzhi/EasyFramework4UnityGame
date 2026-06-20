# BundleRuleConfig 模块说明

> 路径：`BaseAssetSys/BundleRuleConfig/`  
> 打包规则持久化 + 清单数据结构（**非** Editor 专用）。

---

## 子目录

| 目录 | 内容 |
|------|------|
| `Setting/` | `BuildSetting.cs`、`DefaultBuildSetting.asset` |
| `Catalogue/` | `AssetCatalog.cs`、`AssetCatalogBinaryCodec.cs`、`BundleDependencyTopology.cs` — 清单 schema、二进制编解码、拓扑工具 |
| 根目录 | `BundlePlatformPaths.cs` — 平台路径、包目录（`Base` / `DLC_*` / `Mods`）、bundle 名规范（Editor + Runtime 共用） |

---

## BuildSetting 要点

| 字段 | 默认 | 说明 |
|------|------|------|
| `deviceOutputPath` | `Assets/StreamingAssets` | 首包（真机模式）**根路径** |
| `cdnOutputPath` | `Bundles/CDN` | CDN 模式 **根路径** |
| `usePlatformSubfolders` | `true` | 在根路径下追加 `{平台}/`，如 `StandaloneWindows64`、`Android` |
| `buildMode` | `DeviceDebug` | 全局打包模式（Custom 时每项可覆盖） |
| `dlcPackageId` | — | DLC 模式包 ID（输出至 `DLC_{id}/`） |
| `version` / `buildNumber` | — | 写入清单，供热更 / CDN 版本对比（加载侧 TODO） |

**自定义配置项 `BundleConfigItem`：**

| 字段 | 说明 |
|------|------|
| `assetPath` | 文件夹或单文件路径 |
| `folderPackingRule` | 文件夹粒度：`EntireFolder` / `FirstLevelSubfolders` / `AllSubfolders` |
| `bundleName` | 「整个文件夹一个包」或单文件时使用 |
| `buildMode` | 该项 AB 输出到首包或 CDN 等 |
| `downloadPriority` | 热更优先级标记（运行时 TODO） |

---

## AssetCatalog 要点

- `entries[]`：资源路径 → bundle + assetName  
- `bundles[]`：bundle → 依赖 bundle 列表（打包时从 Manifest 写入）  
- 磁盘格式：**二进制** `catalog.bytes`（`AssetCatalogBinaryCodec`，魔数 `VCAT`）

| 副本 | 路径 |
|------|------|
| 工程内 | `BundleRuleConfig/Catalogue/AssetCatalog.bytes` |
| 运行时 Base | `{平台根}/Base/Version/catalog.bytes` |
| DLC 片段 | `{平台根}/DLC_{id}/Version/catalog.fragment.bytes` |

详见 [Docs/CatalogueReference.md](../Docs/CatalogueReference.md)。

---

## 实际输出示例（`usePlatformSubfolders = true`）

| 平台 | 首包（DeviceDebug） | CDN（CdnHotUpdate） |
|------|---------------------|---------------------|
| Windows | `Assets/StreamingAssets/StandaloneWindows64/Base/` | `Bundles/CDN/StandaloneWindows64/Base/` |
| Android | `Assets/StreamingAssets/Android/Base/` | `Bundles/CDN/Android/Base/` |

目录结构（Base 包）：

```text
{平台根}/Base/
├── Bundles/          ← *.bundle（可按分类子目录，如 core/ui.bundle）
├── Version/
│   ├── catalog.bytes
│   ├── manifest.json
│   └── version.json
└── Config/           ← 可选，来自 BuildSetting.configSourceDirectory
```

DLC：`{平台根}/DLC_{id}/` 结构同 Base；清单优先读 `catalog.fragment.bytes`。

Win / Android 可 **同时存在** 于同一工程中。`StreamingAssetsPlatformBuildFilter` 在 Build Player 前隔离非目标平台子目录。

运行时：`Init(null)` → `StreamingAssets/{当前平台}/`；`BundlePlatformPaths.TryResolveRuntimeCatalogPath` 定位 `Base/Version/catalog.bytes`。

- **CdnHotUpdate** 将 AB 打到 `cdnOutputPath`；运维上传至 CDN（含 `Catalogue/catalog.bytes`）。  
- 远程 hash、URL 等见 [Docs/BusinessApiAndCdnPlanning.md](../Docs/BusinessApiAndCdnPlanning.md)。

---

## 相关文档

- [Docs/MainRoadmap.md](../Docs/MainRoadmap.md)  
- [Editor/README.md](../Editor/README.md)  
- [ResLoader/README.md](../ResLoader/README.md)  
- [ResLoader/LoaderDesignGuide.md](../ResLoader/LoaderDesignGuide.md)
