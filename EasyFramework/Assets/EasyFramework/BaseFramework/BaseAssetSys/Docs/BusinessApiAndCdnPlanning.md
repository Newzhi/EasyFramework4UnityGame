# 业务 API 与 CDN 规划

> 对照 [MainRoadmap.md](./MainRoadmap.md) 阶段 B/C；实现细节见 [ResLoader/LoaderDesignGuide.md](../ResLoader/LoaderDesignGuide.md)。

---

## 一、业务侧需求对照表

| # | 需求（设计基线） | 目标 API（形态） | 现状 | 计划模块 |
|---|------------------|------------------|------|----------|
| 1 | 同步加载资源 | `Load<T>(loadPath)` 简路径；`LoadByBundle` / `LoadByAssetPath` 辅助 | ✅ 已实现 | `BundleResLoader` |
| 2 | 异步加载（设计基线 **默认 API**） | `LoadUniTaskAsync<T>(loadPath)` / `await` 形态 | ✅ 已接入（基础版） | `BundleResLoader` + UniTask |
| 3 | 加载 + 完成回调 | `LoadUniTaskWithCallback<T>(path, onComplete, onFailed, useUniTask)` | ✅ 已实现 | 基于 `Load/LoadUniTaskAsync` 封装 |
| 4 | 预加载资源包 | `PreLoadBundles(names)` / `PreLoad<T>(loadPath)` | ✅ 已实现 | `BundleResLoader` |
| 5 | 卸载单个资源 | `IAssetHandle.Release()` 或 `Unload(handle, instance, cb)` | ✅ Resource 立即卸；Bundle **LRU 延迟**（§5.6） | `BundleResLoader` + `BundleManager` |
| 6 | 卸载全部 | `BundleResLoader.UnloadAll()` | ✅ 立即全卸 Resource + Bundle | `BundleResLoader` + `BundleManager` |
| 7 | **CDN 联网下载** | 远程清单对比 → 下载 AB → 本地缓存 → 再 Load | ✅ 阶段 C | 见下文 §二 |

**测试要求**（设计基线）：依赖顺序 ✅；异常 Log 🟡；**竞态安全 ✅**（同步双 Runner 三端 19/19）；引用计数 ✅。

> 异步说明：`LoadUniTaskAsync` 经 `ResourceLoadCoordinator` 合并同 path inFlight；Bundle 层 `LoadFromFileAsync`；CDN 下载仍走 `BundleDownloadQueue`。

> UniTask 依赖：通过 `Packages/manifest.json` 的 OpenUPM 源接入 `com.cysharp.unitask: 2.5.10`。

---

## 二、CDN 联网加载（扩展点，首版已接）

### 2.1 与打包模式的关系

| 打包模式 | 编辑器产出 | 运行时角色 |
|----------|------------|------------|
| **DeviceDebug / 首包** | `deviceOutputPath`（默认 StreamingAssets） | 安装包内置，离线可用 |
| **CdnHotUpdate / CDN联网** | `cdnOutputPath`（默认 `Bundles/CDN`） | CI 上传到 **`CDN/{平台}/`**，如 `CDN/Android/`、`CDN/StandaloneWindows64/` |
| **DlcPackage** | `{deviceOutputPath}/{平台}/DLC_{id}/`（`Bundles/` + `Version/catalog.fragment.bytes`） | `ContentPackageService.TryMount` + `IContentPackageGate`（默认无 Gate 拒绝；Steam TODO） |

打包器只负责 **打出文件 + 写清单**；**下载与运行时选路** 属于加载侧扩展。

### 2.2 运行时资源查找优先级（目标态）

```text
persistentDataPath / ABCache /     ← CDN 已下载的新版 bundle + 清单
    ↓ 未命中
StreamingAssets / 首包 bundleRoot  ← 安装包内置
    ↓ 未命中
CDN / OSS                          ← HTTP(S) 下载，写入 persistentDataPath 后重试 Load
```

与清单字段：`catalogueHash` / `buildId` 用于对比 **是否需拉新清单**；`bundles[].fileHash` / `crc32` 用于 **下载校验**；`resourcePriority` 用于运行时 LRU 卸包与（规划中的）CDN 下载队列排序。

### 2.3 建议模块划分（代码扩展点）

```text
BundleResLoader          业务 API 不变
    ↓
AbstractResource         LoadAsset / Release
    ↓
AssetRouter              RouteAssetSource → 四 Provider
    ↓
BundleManager            AcquireBundle 前解析物理路径
    ↓
IBundlePathResolver      本地多根目录优先级（首包 / 缓存）     ← ✅ DefaultBundlePathResolver
    ↓
IRemoteBundleProvider    清单 hash 比对、HTTP 下载、CRC 校验       ← ✅ HttpRemoteBundleProvider + CdnCatalogueSyncService
```

**当前代码**：`AbstractResource` 经 `AssetRouter` 加载；`BundleManager.AcquireBundle` 优先 `IBundlePathResolver`（**ABCache → 首包**）；本地无包时路由 `NETCDN` → `HttpRemoteBundleProvider`（`BundleDownloadQueue` 合并 in-flight + 按 `resourcePriority` 排序）。  
`BundleResLoader.Init` 在 `cdnBaseUrl` 非空时自动注入 RemoteProvider 并执行清单热更；业务 **不应** 直接写 UnityWebRequest。

### 2.4 CDN 接入步骤（实施 checklist）

1. **配置**：`BuildSetting.cdnBaseUrl` 写入清单根字段 `cdnBaseUrl` + **`{Platform}/`** 子路径（与 `usePlatformSubfolders` 产出一致）。  
2. **启动**：`CdnCatalogueSyncService` 拉远程 `catalog.bytes`，`catalogueHash` 变化时写入 `ABCache/Catalogue/` 并重载 Reader。  
3. **下载**：`HttpRemoteBundleProvider` + `BundleDownloadQueue`；`fileHash` / `crc32` 校验后写入 `persistentDataPath/ABCache/{平台}/`。  
4. **Init**：`BundleResLoader.Init` 自动 `DefaultBundlePathResolver` + RemoteProvider（无需业务手动 Init cacheRoot）。  
5. **Load**：同步 `Load(loadPath)` 与 `PreLoadBundles` 不变；异步入口 **LoadUniTaskAsync**（B-2 真异步 + Resource 级 inFlight 合并 + ref==0 丢弃）。

### 2.5 本阶段明确不做

- 断点续传、后台 Worker 线程 I/O  
- 多 CDN 容灾、加密 bundle  
- B-2 全量 inFlight（`LoadUniTaskAsync` 真异步）— **已实现**

**已实现（2026-06-13 阶段 C 封板）**：`CdnCatalogueSyncService` 清单热更；`BundleDownloadQueue`；`PreLoadBundles`；Reporter `BundleDependencyExplorer` + `DependencyGraph.json`。  
**已实现（2026-06-08）**：`AssetRouter` 四源统一入口；EditorTest 走 AssetDatabase；`Resources/` 前缀走 Resources。  
**已实现（2026-06-13）**：Bundle **LRU 延迟卸包**（见 [BusinessApiUsageGuide.md §5.6](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/BusinessApiUsageGuide.md#bundle-lru-unload)）。

---

## 三、与禁止区设计目标的对应

| 设计目标场景 | 依赖能力 |
|--------------|----------|
| 边玩边下、300MB 以下首包 | CDN 下载 + 清单版本 + 按需 Load |
| 玩家自选关卡/DLC | `DLC_{id}/` 打包 + `ContentPackageService` + Gate（Steam TODO） |
| 单机可玩老版本 | 本地缓存清单世代 ≤ 远程失败时回退首包 |
| MOD 上传下载 | 独立 Package / CDN 路径（远期） |

---

## 四、相关文档

- [MainRoadmap.md](./MainRoadmap.md) — 阶段 B/C 排期  
- [DesignGoalsAndImplementation.md](./DesignGoalsAndImplementation.md) — 首包 / CDN / persistentDataPath 目录约定  
- [CatalogueReference.md](./CatalogueReference.md) — 清单字段与版本号  
- [ResLoader/ContentPackage/ContentPackageService.cs](../ResLoader/ContentPackage/ContentPackageService.cs) — DLC/Mod 挂载  
