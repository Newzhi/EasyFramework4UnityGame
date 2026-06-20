# Catalogue 清单说明

> 打包器与加载器之间的桥梁。当前实现：**二进制 `catalog.bytes`（VCAT v1）+ `entries` + `bundles[]`**；运行时由 `CatalogueReader` 只读。  
> 文档索引：[Docs/DocumentIndex.md](./DocumentIndex.md)  
> **拓扑排序与构建优化计划**：[BundleBuildOptimizationAndTopologyPlan.md](./BundleBuildOptimizationAndTopologyPlan.md)

相关文件：

| 文件 | 作用 |
|------|------|
| `BundleRuleConfig/Catalogue/AssetCatalog.cs` | 清单数据结构（`AssetCatalogEntry` / `BundleCatalogInfo` / `AssetCatalog`） |
| `BundleRuleConfig/Catalogue/AssetCatalogBinaryCodec.cs` | 二进制编解码（魔数 `VCAT`，格式版本 1） |
| `Editor/BundleBuilder/Catalogue/CatalogueWriter.cs` | 打包后写清单（含 Manifest 依赖） |
| `ResLoader/Catalogue/CatalogueReader.cs` | 运行时读二进制清单 |
| `ResLoader/Bundle/BundleManager.cs` | 按 `bundles[]` 依赖预加载后 LoadFromFile |
| `ResLoader/ContentPackage/ContentPackageService.cs` | DLC/Mod 片段挂载与 Gate |

---

## 一、清单路径约定

| 用途 | 路径 |
|------|------|
| 工程内副本（Editor 回退） | `BundleRuleConfig/Catalogue/AssetCatalog.bytes` |
| 首包 / CDN 运行时（Base） | `{平台根}/Base/Version/catalog.bytes` |
| Base 包体目录 | `{平台根}/Base/Bundles/` |
| DLC 片段 | `{平台根}/DLC_{id}/Version/catalog.fragment.bytes` |
| DLC 挂载 | `ContentPackageService.TryMount` → `CatalogueReader.Merge`（`IContentPackageGate`） |
| CDN 热更缓存 | `persistentDataPath/ABCache/{平台}/Catalogue/catalog.bytes` |
| CDN 远程 URL | `{cdnBaseUrl}/Catalogue/catalog.bytes` |

`{平台根}` 示例：`Assets/StreamingAssets/StandaloneWindows64`、`Bundles/CDN/Android`。  
运行时由 `BundlePlatformPaths.TryResolveRuntimeCatalogPath` 解析 `Base/Version/catalog.bytes`。

---

## 二、二进制格式（VCAT v1）

由 `AssetCatalogBinaryCodec` 读写，**不**再使用 JSON。

| 段 | 内容 |
|----|------|
| 文件头 | 魔数 `VCAT`（4 字节 ASCII）+ `formatVersion`（`uint16`，当前为 `1`） |
| 根字段 | `version`、`buildNumber`、`platform`、`buildMode`、`packingRule`、`bundleRoot`、`resourceRoot`、`buildId`、`catalogueHash`、`compressionMode`、`cdnBaseUrl` |
| `entries[]` | 每条：`assetPath`、`bundleName`、`assetName`（UTF-8 字符串，长度前缀 `int32`，`-1` 表示 null） |
| `bundles[]` | 每条：`bundleName`、`resourcePriority`、`sizeBytes`、`fileHash`、`crc32`、`dependencies[]`、`dependenciesAll[]` |

**`catalogueHash`**：先将 `catalogueHash` 置空，对整份序列化字节做 SHA256（十六进制小写），再写入最终文件。运行时 CDN 比对用此字段。

逻辑字段语义与下述 JSON 示例一致（仅作阅读参考，磁盘上已是二进制）。

---

## 三、当前清单：`entries`

每条记录回答：**这个资源在哪个 bundle 里、叫什么名字**。

```json
{
  "assetPath": "Assets/AssetBundle/UI/UIRoot.prefab",
  "bundleName": "ui.bundle",
  "assetName": "UIRoot"
}
```

加载流程：

1. 业务调用同步 `Load("Atlas/Role/Hog_Attack_000")`（相对 `resourceRoot` 简路径）；
2. `CatalogueReader.TryGetEntryByLoadPath` 查表（由 `entries` + `resourceRoot` 构建）；
3. 得到 `bundleName`、`assetName` → `LoadByBundle` → 依赖预加载 → `LoadAsset`。

辅助：`LoadByAssetPath` 用 Unity 完整 `assetPath` 查 `entries`；`LoadByBundle` 直接按包名桥接。

---

## 四、为什么要单独的依赖表？

Unity 打 AB 时，包与包之间已有依赖。例如 `ui.bundle.manifest`：

```yaml
Dependencies:
- .../atlas.bundle
- .../background.bundle
- .../common.bundle
- .../icon.bundle
```

若只加载 `ui.bundle` 再 `LoadAsset`，跨包引用的材质/图集可能失败或显示异常，需要 **先加载依赖包**。

### 不应写在 `AssetCatalogEntry` 上

- 同一 bundle 内上百条 asset，**依赖完全相同**，逐条重复会撑大清单。
- 依赖是 **bundle 与 bundle** 的关系，不是 asset 级别。

### 推荐结构：`bundles[]`（✅ 已启用）

```json
{
  "version": "1.0.0",
  "entries": [ "..." ],
  "bundles": [
    {
      "bundleName": "ui.bundle",
      "dependencies": [
        "atlas.bundle",
        "background.bundle",
        "common.bundle",
        "icon.bundle"
      ]
    },
    {
      "bundleName": "atlas.bundle",
      "dependencies": []
    }
  ]
}
```

约定：

- 只存 **bundle 文件名**（如 `atlas.bundle`），不存绝对路径；
- 当前写入 **全量依赖**（`GetAllDependencies`），并经 **`BundleDependencyTopology` 拓扑排序**（叶→根）；
- 无依赖的包：`dependencies: []`。

对应 C# 类型：`BundleCatalogInfo`（`bundleName` + `dependencies[]`）。

### P1-B 扩展字段（✅ 已实现）

<a id="resource-priority"></a>

| 字段 | 位置 | 说明 |
|------|------|------|
| `buildId` | `AssetCatalog` 根 | 本次构建 GUID，关联 `Reports/BuildManifest.json` |
| `catalogueHash` | 根 | 整份清单 SHA256（hash 字段为空时的序列化字节） |
| `cdnBaseUrl` | 根 | CDN 根 URL；Init 注入 RemoteProvider |
| `compressionMode` | 根 | `LZMA` / `LZ4Chunk` / `Uncompressed` |
| `resourcePriority` | `bundles[]` | 对应 `ResourcePriority` 整型 |
| `sizeBytes` / `fileHash` / `crc32` | `bundles[]` | 构建后 .bundle 完整性 |
| `dependenciesAll` | `bundles[]` | `useDirectDependenciesOnly=true` 时存全量传递依赖 |

Editor 增量产物（`{packageRoot}/Version/` 或 `Reports/`）仍为 **JSON**（构建元数据，非资源清单）：

- `manifest.json` — 各包 hash/crc/优先级快照  
- `version.json` — 包版本信息  
- `Reports/BuildManifest.diff.json`、`BuildCache.json`、`DependencyGraph.json` — Reporter / 增量构建

### 运行时路径（阶段 C ✅）

| 层级 | 路径 | 说明 |
|------|------|------|
| 热更缓存 | `persistentDataPath/ABCache/{平台}/` | CDN 下载的 bundle + `Catalogue/catalog.bytes` |
| 首包 | `StreamingAssets/{平台}/Base/` | 安装包内置 subset |
| 远程 | `cdnBaseUrl` + `/Catalogue/catalog.bytes`、bundle 按名 HTTP | `HttpRemoteBundleProvider` |

解析顺序：**ABCache → StreamingAssets → CDN**（`DefaultBundlePathResolver` + `AssetRouter` NETCDN）。

### 拓扑序约定（✅ 已实现）

- **边语义**：若 `ui.bundle` 依赖 `atlas.bundle`，则 `dependencies[]` 中 **`atlas.bundle` 排在 `ui.bundle` 之前**（叶→根）。
- **写端**：`CatalogueWriter.TryBuildBundleDependencies` 用 Manifest 建图 + Kahn 排序；环或排序改变集合 → `Write` 返回 `false`。
- **读端**：`CatalogueReader.BuildLookupTables` 对 `dependencies` 幂等再排序。

### `Write` 失败条件

| 条件 | 行为 |
|------|------|
| 依赖环 | `LogError`，不写清单 |
| 拓扑排序改变依赖集合 | `LogError`，不写清单 |
| loadPath 重复 + `loadPathDuplicateAsError=true` | `LogError`，不写清单 |
| loadPath 重复 + 默认 `false` | `LogWarning`，仍写清单 |

**权威来源**：Unity 本次 `BuildPipeline.BuildAssetBundles` 生成的 **AssetBundleManifest**。

---

## 五、接入步骤 checklist

1. **数据结构** — ✅ `AssetCatalog` + `AssetCatalogBinaryCodec`  
2. **CatalogueWriter** — ✅ 二进制双份输出（工程内 + `{package}/Version/`）  
3. **BundleBuilder** — ✅ 捕获 `BuildAssetBundles` 返回值  
4. **CatalogueReader** — ✅ 仅读 `.bytes`  
5. **BundleManager** — ✅ `AcquireBundleWithDependencies`  
6. **验收** — DeviceDebug 打包 + `ABLoadSmokeTest`（L-024 / L-033 / P-055）

---

## 六、与 Unity `.manifest` 的关系

| 来源 | 用途 |
|------|------|
| `{name}.bundle.manifest` | 单包 YAML，调试、对比 |
| `AssetBundleManifest` API | **打包时写入 Catalogue 的首选** |
| `AssetCatalog.bundles` | **运行时加载器单入口** |

不必在运行时解析 YAML；Catalogue 是项目自己的「加载用 manifest」。

---

## 七、其它规划

- 清单 **加密**（可选）：见 [MainRoadmap.md](./MainRoadmap.md) P3。  
- 运行时 **version/buildNumber 比对**：见 [BusinessApiAndCdnPlanning.md](./BusinessApiAndCdnPlanning.md) §2。
