# 加载器设计说明（双层架构）

> 目录：`BaseAssetSys/ResLoader/`（子目录见 [README.md](./README.md)）  
> 相关：`AbstractAssets/IAssetHandle.cs`、`AbstractAssets/AbstractResource.cs`、`BundleRuleConfig/Catalogue/`（清单）、[`CatalogueReference.md`](../Docs/CatalogueReference.md)

---

## 1. 总体思路

加载侧拆成 **两层**，职责分离：

| 层级 | 类 | 管什么 | 业务是否直接调用 |
|------|-----|--------|------------------|
| **Bundle 层** | `BundleManager` | `.bundle` 文件、包引用计数、**包依赖** | 一般不直接调用 |
| **Resource 层** | `BundleResLoader` | 包**内**具体资源、路径 API、缓存与释放 | **是**（业务入口） |

中间用 `IAssetHandle`（对外）+ `AbstractResource`（内部）做资源封装：Resource 层 Ref 归零时，再对 Bundle 层 `ReleaseBundle`。

架构总览见 **[README.md § 加载侧架构图](./README.md#加载侧架构图业务无感知)**（含 Mermaid 四源路由图）。简述：

```text
业务代码
    │  BundleResLoader.Instance.Load<T>("Atlas/Role/Hog_Attack_000")   ← 同步加载：相对 resourceRoot 的简路径
    ▼
BundleResLoader          ← 单例 + 首次 Load 懒 Init；查 Catalogue、缓存 AbstractResource（内部）
    ▼
AbstractResource         ← Resource 层 Ref；LoadAsset / Release
    ▼
AssetRouter              ← 按 loadPath / buildMode / 本地 bundle 可用性选源（业务无感）
    ├─ EDITORRESOURCES    ← Editor + Catalogue.buildMode==EditorTest → AssetDatabase
    ├─ RESOURCES          ← loadPath 以 Resources/ 开头 → Resources.Load
    ├─ NETCDN             ← 本地无 bundle → HttpRemoteBundleProvider + BundleDownloadQueue
    └─ ABUNDLE            ← BundleManager Acquire + LoadAsset
    ▼
BundleManager            ← AcquireBundleWithDependencies；依赖包顺序加载
    ▼
AssetBundle 文件         ← StreamingAssets / CDN 缓存目录等
```

---

## 2. Bundle 层：`BundleManager`

**职责**：只关心 **Bundle 容器**，不暴露给业务做 `LoadAsset`。

| 能力 | 说明 | 现状 |
|------|------|------|
| `Init(rootPath, reader)` | 设置 AB 根目录与 CatalogueReader | ✅ |
| `AcquireBundle(bundleName)` | 加载或命中缓存，Bundle Ref +1 | ✅ |
| `AcquireBundleWithDependencies(bundleName)` | 先 Acquire 清单依赖，再加载本体 | ✅ |
| `ReleaseBundle(bundleName)` | Ref -1；为 0 时进入 **LRU 空闲队列**（延迟 `Unload`） | ✅ |
| `TickLruUnload()` | 可选主动驱动淘汰（Acquire/Release 已会触发） | ✅ |
| `UnloadAll()` | 立即清空所有已加载包（含空闲队列） | ✅ |

**设计要点**

- 同一 `bundleName` 全局一份 `AssetBundle` 实例，多资源共用，靠 Ref 决定何时进入空闲队列。
- **LRU 延迟卸载**：Ref 归零后不立即 `Unload(true)`；按清单 `bundles[].resourcePriority`（越小保留越久）与 `BundleLruUnloadPolicy` 空闲上限淘汰。`UnloadAll` / `Init` 仍立即全卸。
- 依赖解析属于 Bundle 层：例如加载 `ui.bundle` 前，先 `AcquireBundle` atlas / common 等（数据来自打包清单，见 Catalogue 文档）。
- 业务 **不应** 直接 `LoadFromFile` 或手动 `Unload`，避免与 Resource 层 Ref 不一致。

---

## 3. 资源路由器：`AssetRouter`

**职责**：对业务隐藏加载源；`AbstractResource.LoadAsset` / `ReleaseLoadedAsset` 统一经路由器分发。

| 源 | 触发条件 | Provider |
|----|----------|----------|
| `RESOURCES` | `loadPath` 以 `Resources/` 开头 | `ResourcesAssetProvider` |
| `EDITORRESOURCES` | Editor Play 且 Catalogue `buildMode == EditorTest` | `EditorResourcesAssetProvider`（AssetDatabase） |
| `NETCDN` | 非 EditorTest 且 `IBundlePathResolver` 报告本地无 bundle | `CdnBundleAssetProvider` + `HttpRemoteBundleProvider` |
| `ABUNDLE` | 默认 | `AbBundleAssetProvider` → `BundleManager` |

**要点**

- `DeviceDebug` / 首包真机模式：仍走真 AB（`buildMode != EditorTest`）。
- `BundleResLoader.Init` 时注册 `DefaultBundlePathResolver`（cache → 首包）并 `AssetRouter.Init`。
- Editor 无 StreamingAssets 清单时，`CatalogueReader.LoadFromProjectCatalogue()` 回退读工程内 `AssetCatalog.bytes`。

---

## 4. Resource 层：`BundleResLoader`

**职责**：业务侧加载入口，管理 **包内资源** 与 **逻辑路径**。

| 能力 | 说明 | 现状 |
|------|------|------|
| `Instance` | 双检锁单例，进程内唯一 `BundleResLoader` | ✅ |
| `Init(bundleRootPath)` | 显式初始化（CDN/自定义 root）；默认由首次 `Load` 懒 Init | ✅ |
| `EnsureReady()` | 懒 Init 预热（加载 Catalogue），可在首次 `Load` 前调用 | ✅ |
| `Load<T>(loadPath)` | 同步加载简路径（相对 `resourceRoot`，无扩展名） | ✅ |
| `LoadByBundle<T>(bundle, asset)` | 按包名桥接 BundleManager | ✅ |
| `LoadByAssetPath<T>(assetPath)` | 按 Unity 完整路径 | ✅ |
| `LoadUniTaskAsync` / 回调 / 预加载包 | `LoadUniTaskAsync` + 回调已实现；`PreLoadBundles` / `PreLoad<T>` ✅ | ✅ |
| `UnloadAll()` | 释放全部 Resource + Bundle | ✅ |

**设计要点**

- **默认用法**：`BundleResLoader.Instance.Load("Atlas/Role/资源名")`，无需业务侧手动 `Init`；首次 `Load` 自动从 `StreamingAssets` + 当前平台子目录加载 Catalogue。
- **自定义 root**：游戏启动时一次 `BundleResLoader.Instance.Init(cacheRoot, usePlatformSubfolder)`（CDN 缓存、热更目录等）。
- `Instance` 与 `EnsureInitialized` 均用双检锁，避免多线程重复构造或重复 Init；`AssetBundle` 仍须在 Unity 主线程加载。
- `LoadByBundle` 按 `typeof(T)` 从包内 `LoadAsset`；短名失败时回退清单 `assetPath`；成功判定用 `GetAsset<T>()`。
- bundle 文件名统一小写（`BundlePlatformPaths.NormalizeBundleName`）；`BundleManager` 对磁盘路径做大小写不敏感解析（适配 Android/Linux）。
- 对外 **同步**：`Load("Atlas/Role/资源名")`，相对清单 `resourceRoot`（打包时的 `targetDirectory`）。
- 设计基线要求 **异步为默认 API**，`LoadUniTaskAsync` 已以 UniTask 形式提供；当前阶段内部仍复用同步 `Load`，后续再接 CDN 下载与并发合并。
- 内部通过 `CatalogueReader.TryGetEntryByLoadPath` → `LoadByBundle(bundleName, assetName)`。
- `resourceDic` 以 `bundleName/assetName`（或 `assetPath`）为 key 去重；同一资源多次 `Load` → Resource Ref 递增，不重复 IO。
- 使用完毕调用 `IAssetHandle.Release()`（或 `BundleResLoader.Unload(handle, instance, cb)`）；Ref 为 0 时触发 Bundle 层 Release。
- Prefab：`GetAsset` / `Instantiate()` 取原型；**实例 Destroy 与 Release 无关**（与 Unity 惯例一致）。

---

## 5. 两层引用计数如何联动

```text
Load 第 1 次 Panel.prefab
  → AbstractResource Ref = 1
  → BundleManager Acquire ui.bundle → Bundle Ref = 1（及依赖包 Ref）

Load 第 2 次同一 Panel（另一脚本）
  → AbstractResource Ref = 2
  → Bundle Ref 不再重复 LoadFromFile（已缓存）

Release × 2
  → AbstractResource Ref = 0 → UnLoad → ReleaseBundle(ui.bundle)
  → Bundle Ref = 0 → LruDefer（空闲队列；超时或超上限后 Unload(true)）
```

Resource Ref 与 Bundle Ref **不必相等**（一个包内多个 asset、或多个 asset 共享同一包时，Bundle Ref 可能更高）。

---

## 6. 与清单（Catalogue）的关系

| 清单字段 | 使用者 | 用途 |
|----------|--------|------|
| `entries[]` / `resourceRoot` | `CatalogueReader` | 工程路径 + 简路径 `loadPath` → `bundleName` + `assetName` |
| `bundles[]` | `BundleManager` | 加载某包前先 Acquire 依赖包 |

打包器写入清单；加载器 **只读** 运行时副本（`{平台根}/Base/Version/catalog.bytes`）。  
`bundleRoot` 默认含平台子目录与 `Base` 包，见 `BundlePlatformPaths` / `BuildSetting.usePlatformSubfolders`。

---

## 7. 文件与后续

| 文件 | 角色 |
|------|------|
| `BundleRuleConfig/BundlePlatformPaths.cs` | 平台子目录名、运行时 bundle 根路径解析 |
| `Catalogue/CatalogueReader.cs` | 运行时读清单、查 entries / bundles |
| `Bundle/BundleManager.cs` | Bundle 层 |
| `Business/BundleResLoader.cs` | Resource 层（业务 API） |
| `Router/AssetRouter.cs` + `Router/IAssetProvider.cs` + `Router/*Provider.cs` | 四源路由 |
| `Bundle/IBundlePathResolver.cs` | 本地 bundle 多根解析（`DefaultBundlePathResolver`） |
| `../AbstractAssets/AbstractResource.cs` | 单资源封装 + Resource Ref |
| `../../../../BaseLayer/ToDelete/ABSystemTester/ABLoadSmokeTest.cs` | 手动 Smoke 测试 |

**后续迭代**：阶段 **B-2** 已完成（见 [Docs/MainRoadmap.md](../Docs/MainRoadmap.md)）。

---

## 8. 业务侧需求对照（设计基线 §1–7）

完整路线图与 CDN 扩展见 **[Docs/BusinessApiAndCdnPlanning.md](../Docs/BusinessApiAndCdnPlanning.md)**。

| # | 需求 | 现状 |
|---|------|------|
| 1 同步加载 | ✅ `Load(loadPath)`；辅助 `LoadByBundle` / `LoadByAssetPath` |
| 2 异步加载（**设计基线默认 API**） | ✅ `LoadUniTaskAsync(loadPath)`（UniTask） |
| 3 加载+回调 | ✅ `LoadUniTaskWithCallback(loadPath, onComplete, onFailed, useUniTask)` |
| 4 预加载包 | ✅ `PreLoadBundles` / `PreLoad<T>(loadPath)` |
| 5 卸载单个 | ✅ `IAssetHandle.Release()` / `Unload(handle, instance, cb)` |
| 6 卸载全部 | ✅ `UnloadAll()` |
| 7 CDN 联网下载 | ✅ `HttpRemoteBundleProvider` + `CdnCatalogueSyncService` |

---

## 9. CDN 与多根目录（阶段 C ✅）

1. `DefaultBundlePathResolver`：**ABCache → StreamingAssets**  
2. `CdnCatalogueSyncService`：远程 `catalogueHash` → 写 `ABCache/Catalogue/catalog.bytes` → 重载 Reader  
3. `HttpRemoteBundleProvider` + `BundleDownloadQueue`：按 `resourcePriority` 排序；同 bundle in-flight 合并  
4. `PreLoadBundles`：Acquire 依赖链并保持 Bundle 引用至 `UnloadAll`  

打包侧 **CdnHotUpdate** 产出在 `cdnOutputPath`；清单 `cdnBaseUrl` 供运行时拉取。详见 **业务API与CDN规划** §2。
