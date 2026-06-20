# ABSystem_Beta 主路线

> **唯一排期与方向文档**（2026-06-08 定稿）  
> 文档目录：`Assets/vFramework/BaseFramework/BaseAssetSys/Docs/`（下文跨文档链接均相对 **仓库根**）。  
> 进度细节见 [DesignGoalsAndImplementation.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/DesignGoalsAndImplementation.md)；集成测试见 [集成测试归档.md](Assets/Test/AB_Test/集成测试归档.md)。

---

## 1. 原则（不换方向）

1. **已验收的主链路不动**：`打包 → 清单 → BundleResLoader 同步 Load + 双层 Ref` 是地基，不推倒重来。  
2. **在现有 API 上扩展**：业务入口仍是 `BundleResLoader.Instance` + `IAssetHandle`；异步走 `LoadUniTaskAsync`，CDN 走 `BundleManager` 扩展点，不另起一套 ResourceManager。  
3. **不做四不像**：不混用旧框架 API；Bundle 层 Ref=0 后走 **LRU 延迟卸载**（`UnloadAll` 仍立即全卸），业务 API 不变。  
4. **测试门禁**：任何加载侧改动不得破坏同步双 Runner **三端 19/19**（基准 JSON 见集成测试归档 §6）。

---

## 2. 主路线（四阶段）

```text
【阶段 A · 已完成】打包 + 清单 + 同步 Load + 三端双 Runner 19/19
        │
        ▼
【阶段 B-1 · 已完成】TestABScene 异步双 Runner 19/19（三端）
        │
        ▼
【阶段 B-2 · 已完成】真异步 / inFlight 合并
        │
        ▼
【阶段 B-Pool · 已完成】PrefabPoolManager + 按 Scene 分池
【阶段 P1.5 · 已完成】AssetReference / Ref Trace / LRU 卸包
【阶段 P1-B · 已完成】BundleBuildPipeline / 增量 / 公共包 / 依赖 Explorer
【阶段 C · 已完成】CDN 运行时 + 清单热更 + PreLoad
【P3-12 · 已完成】清单二进制 catalog.bytes（VCAT v1）+ Base/Version 布局
【P1 内容包 · 基础已完成】DLC_{id}/ 打包 + ContentPackageService；Steam Gate TODO
        │
        ▼
【远期】边玩边下策略细化 / MOD 上传 / 分工程 / 清单加密
```

| 阶段 | 目标 | 验收 |
|------|------|------|
| **A** | 规则制定器 + 打包器 + Catalogue + 同步 API | `ConcurrentLoad_*` passCount=19（Editor / Player / Android） |
| **B-1** | 在 **现有 `TestABScene`** 启用异步双 Runner，`LoadUniTaskAsync` 集成 | `UniConcurrentLoad_*` passCount=19（**Editor / Player / Android ✅**） |
| **B-2** | 真异步 I/O、同 path inFlight 合并、完成时 ref==0 丢弃 | 异步双 Runner **21/21** + **不回归**阶段 A 同步 19/19 |
| **B-Pool-1** | 对象池 **业务范式** + `PrefabPoolManager` | ✅ [业务API §5.4](./BusinessApiUsageGuide.md) |
| **B-Pool-2** | 对象池集成自动化 Case | ⏸ 暂不纳入 AB_Test 套系 |
| **B-Pool-3** | （可选）`IPooledObject` / 集中池服务 | ⏳ 见 [LoaderOptimizationPlan.md](./LoaderOptimizationPlan.md) §3.2 |
| **P1.5** | 加载侧优化：AssetReference、Ref Trace、LRU 卸包、池唯一入口 | ✅ 见 §4 P1.5 |
| **P1-B** | 打包侧：Pipeline、增量/Manifest、公共包、清单 hash/CRC | ✅ 见 §4 P1-B |
| **C-1** | `IBundlePathResolver` 多根目录（首包 / persistentDataPath） | ✅ 本地缓存命中 Load |
| **C-2** | `IRemoteBundleProvider` 清单对比 + HTTP 下载 | ✅ CdnHotUpdate 产物可拉取并 Load |
| **C-3** | 首包 / 远程分包策略上线 | ✅ ABCache → 首包 → CDN |

### 阶段 B：现有测试场景中的异步集成（不新建场景）

**场景**：`Assets/Test/AB_Test/TestABScene.unity`（与阶段 A 同步套系 **同一 Runner 节点** `ABundleTestRunner`）。

| 项 | 说明 |
|----|------|
| **已有脚本** | `MyLoadUniTest`（9 Case）+ `MyLoadUniTest2`（8 Case），Case 与同步套系对齐 |
| **API** | `BundleResLoader.LoadUniTaskAsync<T>`（当前 Yield 一帧 + 同步 Load） |
| **切换方式** | Inspector：**禁用** `Myloadtest` / `MyLoadTest2`，**启用** `MyLoadUniTest` / `MyLoadUniTest2` |
| **Collector** | `sessionIdPrefix=UniConcurrentLoad_`，`expectedConcurrentRunners=2`，`unloadAllRunnerSource=MyLoadUniTest` |
| **门禁** | `passCount=19`，`failCount=0`；**不得**与同步双 Runner 同时启用 |
| **三端顺序** | Editor Play → Windows Player → Android（与阶段 A 相同流程） |

详细步骤见 [集成测试归档.md §3](../../../../Test/AB_Test/集成测试归档.md)。

**阶段 B 不做**：新建测试场景、改同步 Case 语义、为异步单独改 Ref/Unload 规则。

### 阶段 B-Pool：对象池（介于 B 与 C 之间）

**原则**：对象池 **不新增** Load/Release API；在现有 `IAssetHandle` 上约定用法（见 [业务API调用指南 §6/§7.7](./BusinessApiUsageGuide.md)）。

| 子项 | 内容 | 状态 |
|------|------|------|
| **范式** | `PrefabPoolManager.GetOrCreatPool` + `GetObj`/`RecycleObj`；池持 Handle | ✅ |
| **场景隔离** | `PrefabPoolManager` + `PoolSceneRootsUtil`；同场景 `refCount` 共享 | ✅ §5.4 |
| **集成 Case** | AB_Test 自动化套系 | ⏸ 业务侧 `comprehensiveTest` 手测 |
| **框架封装** | `PrefabPool` + `PrefabPoolManager` | ✅ |
| **与 B/C 关系** | **依赖 B-1**：异步 Load 路径稳定后再跑池 Case；**先于 C**：CDN 下载与池化正交，避免并发改 Ref 语义 |

**延后（不纳入 B-Pool 首版）**：AutoUnload、延迟卸载队列、池内跨 bundle 热替换。

---

## 3. 当前状态（快照）

| 项 | 状态 |
|----|------|
| 同步 `Load<T>` + Release / UnloadAll | ✅ 三端双 Runner 19/19 |
| 打包三种模式 + Player 平台过滤 | ✅ |
| `LoadUniTaskAsync` / 回调 API | ✅ **Editor B-2 21/21**（`215550`）；B-1 三端 19/19（`225805`/`230136`/`231720`）；Player/Android B-2 待复测 |
| `BundleResLoader` 对象池 | ✅ 已迁至 `PrefabPoolManager` |
| `AssetRefTraceLogger`（Resource/Bundle/Pool Trace） | 🟡 首版 + 关键路径接入 |
| `AssetReference` 自动 Release（非池） | ✅ `AssetReference` + `LoadGameObject` |
| CDN 打包产出 | ✅ `cdnOutputPath` |
| CDN 运行时下载 | ✅ `HttpRemoteBundleProvider` + `BundleDownloadQueue` + `CdnCatalogueSyncService` |
| `AssetRouter` 四源路由 | ✅ ABUNDLE / RESOURCES / EDITORRESOURCES / NETCDN |
| `PreLoad` / `PreLoadBundles` | ✅ 包级预热（Acquire 依赖链） |
| 清单 `catalogueHash` / `buildId` 运行时比对 | ✅ Init 时 `CdnCatalogueSyncService` 热更 ABCache 清单 |
| 清单 `version` / `buildNumber` 运行时比对 | 🟡 同上；业务仍不直接读 |
| Bundle 依赖 **拓扑排序** | ✅ `BundleDependencyTopology` + `CatalogueWriter` + `CatalogueReader` |
| Bundle **LRU 延迟卸载** | ✅ Ref=0 入空闲队列；按 `resourcePriority` + 时长淘汰；`UnloadAll` 立即全卸 |
| Bundle **构建优化分析**（冗余报告） | ✅ `BundleBuildAnalyzer` + Packer「上次构建报告」 |
| **P1-B 打包侧优化**（Pipeline / 增量 / hash / 公共包） | ✅ B0–B4 + 阶段 C 封板 |
| **清单二进制** `catalog.bytes`（VCAT v1） | ✅ `AssetCatalogBinaryCodec` |
| **DLC 目录布局** `{平台}/DLC_{id}/` + `ContentPackageService` | ✅ 挂载/卸载；`IContentPackageGate` 默认拒绝，Steam TODO |

---

## 4. 排期（按顺序执行）

### P0 — 阶段 B-1：TestABScene 异步双 Runner

| # | 项 | 完成标准 |
|---|-----|----------|
| 1 | Editor：`TestABScene` 切换异步套系 | ✅ `225805`，passCount=19 |
| 2 | Player 同场景同套系 | ✅ `230136`，passCount=19 |
| 3 | Android 异步双 Runner | ✅ `231720`，passCount=19 |
| 4 | 回归同步门禁 | 切回同步套系，`ConcurrentLoad_*` 仍 19/19 |

**操作**（均在 **现有** `TestABScene`，不新建场景）：

1. 禁用 `Myloadtest`、`MyLoadTest2`；启用 `MyLoadUniTest`、`MyLoadUniTest2`  
2. Collector：`sessionIdPrefix=UniConcurrentLoad_`，`unloadAllRunnerSource=MyLoadUniTest`  
3. Play → 归档 JSON → 切回同步套系验证未退化  

详见 [集成测试归档.md §3](../../../../Test/AB_Test/集成测试归档.md)。

### P0.5 — 阶段 B-Pool（B-1 通过后、阶段 C 之前）

| # | 项 | 完成标准 |
|---|-----|----------|
| 1 | 对象池范式 + **谁创建谁销毁** + **按 Active Scene 分池** | ✅ [业务API §5.4–5.5 / §7.7](./BusinessApiUsageGuide.md)；`PoolSceneRootsUtil`；手测 `comprehensiveTest` |
| 2 | （可选）业务模块手测 GetObj/DestroyPool | 不纳入 AB_Test JSON 门禁 |
| 3 | （可选）`PrefabPool` 扩展 | Clear 时单次 Release |

### P1.5 — 加载侧优化（与 B-2 / C 并行）

| # | 项 | 说明 | 状态 |
|---|-----|------|------|
| 1 | [LoaderOptimizationPlan.md](./LoaderOptimizationPlan.md) | AssetReference/TEngine、统一池持 Handle、Trace 规范 | ✅ 文档 |
| 2 | `BaseLogSys/AssetRefTraceLogger` | Resource/Bundle/Pool 关键路径 Trace；CDN 下载 + UnloadAll 残余 Ref | ✅ |
| 3 | `AssetReference` + `LoadGameObject` 门面 | 非池自动 Release | ✅ |
| 4 | 池路径禁止业务直接 `Load`（文档 + Lint） | `DEVELOPMENT_BUILD` / `VF_POOL_LOAD_LINT` Warning | ✅ |
| 5 | Bundle **LRU 延迟卸载** | `BundleManager` + `BundleLruUnloadPolicy`；清单 `resourcePriority` | ✅ |

### P1 — 加载补全 + 清单/打包增强（与 B/C 并行）

| # | 项 | 说明 |
|---|-----|------|
| 3 | Catalogue 非空 dependencies Case | ✅ `MyDependencyTest`（默认禁用；需 DeviceDebug 打包） |
| 4 | 无效路径 Case | 异常路径 |
| 5 | Custom 规则集成 JSON | 可选 |
| **6** | **Bundle 依赖拓扑排序** | ✅ `CatalogueWriter` 写序 + 环检测；详见 [BundleBuildOptimizationAndTopologyPlan.md](./BundleBuildOptimizationAndTopologyPlan.md) §2 |
| **7** | **Bundle 构建优化报告** | ✅ 冗余/包体/loadPath 分析 + Packer 报告 Tab；同上 §3 |

#### P1-B — 打包侧优化（Editor Pipeline）

| # | 项 | 状态 |
|---|-----|------|
| B0 | `BundleBuildPipeline` + `BundleBuilder` 薄门面 | ✅ |
| B1 | `ResourcePriority` + 清单 `buildId` / `catalogueHash` / `fileHash` / `crc32` | ✅ |
| B2 | `BuildManifest` / diff / `BuildCache` + 增量/覆盖按钮 | ✅ |
| B2-UI | 压缩格式 + 增量/覆盖按钮 + `BuilderEditorBlueprint.html`；**资源优先级仅 Custom 规则 UI 暴露** | ✅ |
| B3 | `SharedBundlePlanner` 全自动 `shared_auto.bundle` | ✅ |
| B4 | `BundleDependencyExplorer` + `DependencyGraph.json` | ✅ |
| C-2 | `HttpRemoteBundleProvider` + 清单热更 + 下载队列 | ✅ |

详见 [BundleBuildOptimizationAndTopologyPlan.md](./BundleBuildOptimizationAndTopologyPlan.md)。

### P2 — 异步内核（阶段 B-2）

| # | 项 | 模块 |
|---|-----|------|
| 6 | inFlight 合并 + ref==0 完成丢弃 | ✅ `ResourceLoadCoordinator` + `BundleResLoader` |
| 7 | 真异步 Load / 下载队列 | ✅ `BundleManager.AcquireBundleAsync` + Provider `LoadAsync` |

### P2 — CDN（阶段 C）

| # | 项 | 模块 |
|---|-----|------|
| 8 | `IBundlePathResolver` | ✅ `DefaultBundlePathResolver`（ABCache → 首包）；`BundleManager.SetPathResolver` |
| 9 | `IRemoteBundleProvider` + 清单热更 | ✅ `HttpRemoteBundleProvider` + `CdnCatalogueSyncService` + `BundleDownloadQueue` |
| 10 | `PreLoad` 包级预热 | ✅ `PreLoadBundles` / `PreLoad<T>(loadPath)` |

### P3 — 工程化

| # | 项 |
|---|-----|
| 11 | CI 解析 JSON，failCount==0 |
| 12 | 清单加密（可选）；**二进制 `catalog.bytes`（VCAT v1）已实现** |

### 延后（不阻塞主路线）

| 项 | 说明 |
|----|------|
| DestroyInstance / AutoUnload | 释放体验优化；业务当前用「保存句柄 + Release」即可 |
| `Unload(false)` 两阶段 | 有内存 profiling 再评估 |
| 断点续传 / 多 CDN 容灾 | 阶段 C 封板后 |
| B-2 全量 inFlight | ✅ `LoadUniTaskAsync` 真异步 + `ResourceLoadCoordinator` |

---

## 5. 业务 API 约定（稳定面）

**加载（推荐）**

```csharp
IAssetHandle h = BundleResLoader.Instance.Load<GameObject>("UI/UIRoot");
GameObject go = h?.Instance;
// 异步：await LoadUniTaskAsync<T>(path)  // B-2：真异步 I/O + 同 path inFlight 合并
```

**卸载**

```csharp
h?.Release();  // Resource 立即卸原型；Bundle Ref=0 进入 LRU 空闲队列（非立即卸 AB）
BundleResLoader.Instance.UnloadAll();  // 仅切场景/关游戏；立即全卸 Resource + Bundle
```

Bundle 层 LRU 策略见 [BusinessApiUsageGuide.md §5.6](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/BusinessApiUsageGuide.md#bundle-lru-unload)；打包期 `resourcePriority` 见 [CatalogueReference.md §P1-B](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/CatalogueReference.md#resource-priority)。

完整业务 API 见 [BusinessApiUsageGuide.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/BusinessApiUsageGuide.md)。

**Common 等资源**：无单独常驻策略；需长期占用则 **`PreLoadBundles`** 或 **Load 一次且不 Release**。

---

## 6. 集成测试门禁

| 套系 | 场景 | JSON 前缀 | passCount | 阶段 |
|------|------|-----------|-----------|------|
| 同步单 Runner | `TestABScene` | `Myloadtest_*` | 9 | A |
| 同步双 Runner | `TestABScene` | `ConcurrentLoad_*` | **19**（回归基准） | A |
| 异步双 Runner | `TestABScene` | `UniConcurrentLoad_*` | **19** | **B-1**（三端 ✅） |
| CDN 热更（可选） | `TestABScene` | `MyCdnHotUpdateTest_*` | **5** | **C**（默认禁用；需 `cdnBaseUrl` 方可跑下载 Case） |

同步基准：`004612`（Android）、`004530`（Player）、`004641`（Editor）。

---

## 7. 文档地图

| 文档 | 角色 |
|------|------|
| **本文（MainRoadmap.md）** | 方向 + 排期 + 门禁 |
| [DesignGoalsAndImplementation.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/DesignGoalsAndImplementation.md) | 禁止区基线 + 模块进度 checklist |
| [BusinessApiAndCdnPlanning.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/BusinessApiAndCdnPlanning.md) | CDN / 异步扩展设计细节 |
| [BusinessApiUsageGuide.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/BusinessApiUsageGuide.md) | 业务抄用范式 |
| [CatalogueReference.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/CatalogueReference.md) | 清单字段、二进制路径（VCAT v1） |
| [StageCFreezeCR.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/StageCFreezeCR.md) | 阶段 C 封板交付物 |
| [BundleBuildOptimizationAndTopologyPlan.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/BundleBuildOptimizationAndTopologyPlan.md) | 拓扑 + 构建优化 **设计**（排期见 §4 P1） |
| [LoaderOptimizationPlan.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/LoaderOptimizationPlan.md) | 加载侧优化 **设计**（排期见 §4 P1.5） |
| [RefCountAppendix.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/RefCountAppendix.md) | 引用计数附件 / Trace 对照 |
| [DocumentIndex.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/DocumentIndex.md) | 文档索引 + **新建文档门禁** |
| [ResLoader/README.md](Assets/vFramework/BaseFramework/BaseAssetSys/ResLoader/README.md) | 加载侧架构图 + 子目录索引 |
| [LoaderDesignGuide.md](Assets/vFramework/BaseFramework/BaseAssetSys/ResLoader/LoaderDesignGuide.md) | 双层架构 + Router 细节 |
| [集成测试归档.md](Assets/Test/AB_Test/集成测试归档.md) | Case + JSON 基准 |
| [ResourceSystemDesignGuide.md](Assets/vFramework/Resources/ResourceSystemDesignGuide.md) | 外部通用参考（**不驱动本项目改方向**） |

**维护约定**：**排期、阶段状态、代码 TODO 登记** 只改 **MainRoadmap.md**（§4、§8）与 **DesignGoals 实现细节区**；专题 Plan 只写设计不写状态表；子模块 `README.md` 只链主路线。

---

## 8. 代码内 TODO 登记（合并自源码注释）

> **不在此新建独立 `TODO.md`**。新增代码 `TODO` 应在本表补一行并链到 **§4 排期项**。

| 位置 | 摘要 | 主路线 |
|------|------|--------|
| `BundleResLoader.PreLoad` / `PreLoadBundles` | 包级预加载 | ✅ P2-10 |
| `BundleResLoader.LoadWithAutoUnLoad` | 实例绑定自动卸 | ✅ → `LoadGameObject` |
| `BundleResLoader.LoadUniTaskAsynWithAutoUnLoad` | 异步版 | ✅ |
| `BundleBuilder` / `BundleBuilderTabView` | Custom 项独立 `dlcOutputPath` 字段；Steam/TapTap Gate 配置 | 远期 |
| `ContentPackageService` | `IContentPackageGate` 第三方 SDK 门控 | Steam / TapTap TODO |
| `CatalogueWriter` / `AssetCatalogBinaryCodec` | 清单二进制 | ✅ P3-12 |
| `BundleManager` / `CdnBundleAssetProvider` | CDN 下载队列、本地 Catalogue 热更 | ✅ 阶段 C |
| `BundleLruUnloadPolicy` | grace / MaxIdleBundles 可配置化 | 可选 |
| `LoaderOptimizationPlan` §2.2 | `AssetReference`、`LoadGameObject` | P1.5-3 |
| `LoaderOptimizationPlan` §3.2 | 池路径禁止业务直接 `Load` | P1.5-4 |

---

## 9. 变更记录

| 日期 | 说明 |
|------|------|
| 2026-06-08 | 定稿主路线：不换同步地基，阶段 B 异步 → 阶段 C CDN；合并原「下一步计划」「通用资源设计对照」 |
| 2026-06-08 | 落地 `AssetRouter` 四源（EditorTest→AssetDatabase、Resources、AB、CDN Stub）；业务 API 不变 |
| 2026-06-08 | 阶段 B 明确在 **TestABScene** 切换异步双 Runner；新增 **阶段 B-Pool**（对象池，介于 B 与 C）；P2 拆为 B-2 与 C |
| 2026-06-13 | 集成归档：Android 同步 19/19 复测；异步 Editor/Player **19/19**（`225805`/`230136`）；场景 Collector 默认异步套系 |
| 2026-06-13 | **B-Pool**：`PrefabPool` + 按 Active Scene 分池（`PoolSceneRootsUtil`、`poolsBySceneAndPath`） |
| 2026-06-13 | 异步 **Android 19/19**（`231720`）；**阶段 B-1 三端完成** |
| 2026-06-13 | **阶段 B-2 实现**：`ResourceLoadCoordinator` + 真异步 `LoadFromFileAsync`；+InflightParallel / InflightAbandon Case |
| 2026-06-17 | **阶段 B-2 Editor 门禁 21/21**（`215550`）；TCS inFlight 修复 `212707`/`214738` |
| 2026-06-13 | **P1.5**：`PrefabPoolManager` 迁出 Loader、`AssetRefTraceLogger`、文档合并门禁 |
| 2026-06-13 | **P1-B**：`BundleBuildPipeline`、增量/Manifest、`SharedBundlePlanner`、`ResourcePriority` 写清单 |
| 2026-06-13 | **P1.5-5**：`BundleManager` LRU 延迟卸包 + `BundleLruUnloadPolicy` |
| 2026-06-13 | **阶段 C 封板**：`CdnCatalogueSyncService`、`BundleDownloadQueue`、`PreLoadBundles`、B4 `DependencyGraph` + Explorer、`MyCdnHotUpdateTest` |
| 2026-06-13 | **P3-12**：资源清单改二进制 `catalog.bytes`（`AssetCatalogBinaryCodec` VCAT v1）；`Base/Version/` 布局 |
| 2026-06-13 | **文档链接**：跨文档链接改为仓库根路径 `Assets/...`；§5 LRU / `resourcePriority` 显式锚点 |
