# 阶段 C 封板 — Code Review 交付物

## 变更摘要（M1–M3）

### M1 — C-2 运行时接通
- `AssetCatalog.cdnBaseUrl` + `CatalogueWriter` 打包写入
- `BundleResLoader.Init` 注入 `HttpRemoteBundleProvider`（`CdnRuntimeBootstrap`）
- `CdnCatalogueSyncService` 清单 hash 热更 → ABCache → `CatalogueReader` 重载
- `HttpRemoteBundleProvider` + `BundleDownloadQueue`（priority + in-flight 合并）
- `CdnHttpClient` / `CdnPaths` 路径约定

### M2 — C-3 + PreLoad
- `CdnRuntimeBootstrap.SyncCatalogueIfNeeded`（Init 后；失败回退首包清单）
- 解析顺序：**ABCache → StreamingAssets → CDN**（`DefaultBundlePathResolver`）
- `PreLoadBundles` / `PreLoad<T>(loadPath)` 包级预热

### M3 — P1.5 + P1-B B4
- 池路径 `Load` Warning（`DEVELOPMENT_BUILD` / `VF_POOL_LOAD_LINT`）
- `AssetRefTraceLogger`：CDN 下载 Trace、`UnloadAll` 非零 Ref 摘要
- `DependencyGraphWriter` + Reporter `BundleDependencyExplorer`

## 已知限制（封板后/远期）

| 项 | 说明 |
|----|------|
| B-2 全量 inFlight | ✅ `LoadUniTaskAsync` 真异步 + `ResourceLoadCoordinator` |
| 断点续传 / 多 CDN | 未实现 |
| 清单二进制 | ✅ `catalog.bytes`（VCAT v1）；加密仍 P3 |
| DLC 目录 `DLC_{id}/` | ✅ P1 基础布局；Steam Gate TODO |
| `Unload(false)` 两阶段 | 未做 |

## 回归门禁

| 套系 | 期望 | 平台 |
|------|------|------|
| `ConcurrentLoad_*` | 19/19 | Editor / Player / Android |
| `UniConcurrentLoad_*` | 19/19 | 抽测至少 1 端 |
| `MyDependencyTest` | 通过 | Editor（DeviceDebug 后） |
| `MyCdnHotUpdateTest` | 通过（默认禁用） | Editor / Player |

> 请在 Unity 中执行上述套系并记录 passCount 后合入。

## diff 范围

- `BaseAssetSys/ResLoader/`（含 `Cdn/`）
- `BaseAssetSys/Editor/BundleBuild/`
- `BaseLogSys/AssetRefTraceLogger.cs`
- `Assets/Test/AB_Test/MyCdnHotUpdateTest.cs`
- `BaseAssetSys/Docs/`（封板文档同步）
