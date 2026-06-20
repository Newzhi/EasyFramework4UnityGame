# 设计目标与实现细节

---

## ⛔ 禁止修改区域 — 请勿编辑以下内容 ⛔

> **警告：从本行到「禁止修改区域结束」之间的内容为项目设计基线，禁止 AI 或人工擅自修改。**

### 设计目标

本加载系统是希望满足以下几点：

1. 我想发布一个不包含任何游戏资源的安装包，然后玩家边玩边下载。
2. 我想发布一个可以保证前期体验的安装包，然后玩家自己选择下载关卡内容。
3. 我想发布一个保证300MB以下内容的安装包，然后进入游戏之前把剩余内容下载完毕。
4. 我想发布一个偏单机的游戏安装包，在网络畅通的时候，支持正常更新。在没有网络的时候，支持游玩老版本。
5. 我想发布一个MOD游戏安装包，玩家可以把自己制作的MOD内容上传到服务器，其它玩家可以下载游玩。
6. 我们在制作一个超大体量的项目，有上百GB的资源内容，每次构建都花费大量时间，是否可以分工程构建？

我是一个初学者，所以希望按照AB包加载的原理开始接触学习，然后按照自己的理解完成上述系统。

我的构思是按照下列来实现的：

1. **打包规则制定器**：用户在unity编辑器界面呼出菜单，选择是什么样的打包模式（编辑器测试，真机，联网CDN等等热更），平台（windows,ios等等），打包规则（按照某一个根节点文件夹下的每个第一级子文件夹单独一个bundle或者其他常见业务的目录打包方式，或者用户自定义，自定义可以在面板上建立类似文件夹的树状嵌套列表结构，相当于控制对应路径下的文件夹作为一个包），（PS或者上述都不选直接默认），然后根据SO或者XML生成对应的规则

2. **打包器**：根据上一轮制定的规则开始执行打包，和规则制定器深度绑定，可能放在一个编辑器工具下，会自动扫描根据规则设置的文件以及对应文件夹，自动打上标签，处理信息，打包到对应的目录，这是最主要的功能。当然还需要清除对应打包后的文件，选择增量打包或者重新打包（全删了后再执行打包等等常见的功能），打包完成后会生成对应的资源清单目录，用于后续的加载和卸载。

   **打包器和规则制定器注意事项**：他们两个非常耦合，彼此依赖，同一个界面。但是打包器是真正的执行者，相当与 ViewModel 和 Controller 的区别。

   **中间桥梁**：生成一个资源清单作为打包器和资源加载器之间的桥梁，加载器根据清单梳理AB包的加载关系和针对性的加载决策。

3. **抽象资源**：这是为了实现引用计数和自动管理AB包的加载或者卸载，不让业务直接对资源进行一些操作而是通过中间的抽象资源。每次包内的资源被加载，会让引用计数增加一位，如果引用计数为0，则对应的包可以被安全的卸载。

4. **加载器**：让用户可以比较简单的加载某个资源，同时提供一些业务常见的API比如脚本 BundleResLoader.cs 现在封装的一些方法。后续会接入一个路由器。路由器会根据资源的位置自动加载对应的资源，是安全的高性能的。比如加载Resources或者AB包内的资源，最终让用户能够一个Api完成加载和卸载的写法：`var e = mgr.instance.Load<Gameobject>("e.Path")`

业务侧需要：
1.同步加载某个资源：简单的API调用
2.异步加载某个资源：最常用的API调用，默认调用
3.加载某个资源带完成后的回调：更方便加载完成后立即执行某个方法和判断是否完成
4.预加载某个资源包：用于解决一些大场景无缝加载等等问题的设想
5.卸载销毁某个资源：资源用完了正常析构
6.卸载销毁所有的已加载资源：一次性卸载完全部资源
7.支持CDN联网下载资源。需要一些网络请求下载资源的方式（目前留出扩展点，暂时不做，代母注释TODO，但是需要考虑）


测试要求：
1.能够正常调用API加载资源，正常解析依赖后按照顺序调用
2.能正常捕捉异常或者提前规避报错，用于给开发者的安全检查
3.能够保证竞态安全，假如两个脚本同时加载某一个资源，能够正常的处理竞态关系
4.引用计数正常

---

## ⛔ 禁止修改区域结束 ⛔

---

## 实现细节

> 本节随开发进度更新，可自由修改。上方 **禁止修改区域** 为原始构思；此处对照 **设计目标四条模块**，说明当前做到哪一步。  
> **复核日期：2026-06-13**（对照源码 + 集成测试 JSON；**禁止区正文未改**）。  
> **主路线（2026-06-08 定稿）**：不换同步地基，**阶段 B 异步 → 阶段 C CDN**；详见 **[MainRoadmap.md](./MainRoadmap.md)**。  
> **本阶段结论**：阶段 A **19/19** 已验收；阶段 **B-1 三端异步 19/19** 已验收；**B-2 真异步/inFlight** 已实现（异步门禁 **21/21**）；**B-Pool** `PrefabPool` 已实现；**阶段 C CDN 运行时** 已封板。

### 主路线符合度（摘要）

| 阶段 | 内容 | 状态 |
|------|------|------|
| **A** | 打包 + 清单 + 同步 Load + Ref | ✅ |
| **B-1** | 异步双 Runner 集成 JSON | ✅ 三端 19/0（`225805`/`230136`/`231720`） |
| **B-Pool** | `PrefabPool` + `PrefabPoolManager`；按 Active Scene 分池、`refCount` 共享 | ✅ |
| **B-RefTrace** | `AssetRefTraceLogger` + [LoaderOptimizationPlan.md](./LoaderOptimizationPlan.md) | 🟡 首版 |
| **B-2** | 真异步 / inFlight / ref==0 丢弃 | ✅ |
| **C** | CDN 下载 + 多 root + version 比对 | ✅ 阶段 C 封板 |

**不阻塞主路线**：DestroyInstance / AutoUnload — 见主路线 §4 延后项；Bundle LRU 延迟卸载已实现（`BundleManager` + `BundleLruUnloadPolicy`）。

### 进度估算方法（2026-06-11 重估）

| 口径 | 含义 | 当前值 |
|------|------|--------|
| **模块 checklist** | 各模块下表 ✅ / ❌ 计数（🟡 在业务 API 表单独计 0.5） | 见「总体进度」列 |
| **综合实现进度** | 规则制定器 + 打包器 + 清单 + 抽象资源 + 加载器&路由器 **五行算术平均** | **约 70%** |
| **业务 API 七项（§36–43）** | 同步/卸载=1；异步/回调=0.5；PreLoad/CDN=0 | **约 57%** |
| **设计基线测试 §46–50** | 四项加权 | **约 81%** |
| **禁止区六条产品场景** | 边玩边下 / DLC / 预下载 / 离线热更 / MOD / 分工程 | **约 22～28%** |

### 文档索引

> 完整索引见 **[DocumentIndex.md](./DocumentIndex.md)**。各子文件夹均有 `README.md`（或 `LoaderDesignGuide.md`），Docs 放集中规划。

| 文档 | 内容 |
|------|------|
| [MainRoadmap.md](./MainRoadmap.md) | **方向 + 排期 + 测试门禁** |
| [DocumentIndex.md](./DocumentIndex.md) | 文档索引 + **新建文档门禁** |
| [DocumentIndex.md](./DocumentIndex.md) | 全模块文档导航 |
| [BusinessApiAndCdnPlanning.md](./BusinessApiAndCdnPlanning.md) | **业务侧 7 项需求** + CDN 扩展点（重点） |
| [CatalogueReference.md](./CatalogueReference.md) | 清单 `entries` / `bundles`、打包写端与加载读端 |
| [BuilderEditorBlueprint.html](./BuilderEditorBlueprint.html) | 打包窗口 UI 原型 |
| [../ResLoader/LoaderDesignGuide.md](../ResLoader/LoaderDesignGuide.md) | 双层加载、API 现状 |
| [../AbstractAssets/README.md](../AbstractAssets/README.md) | 抽象资源与 Ref |
| [../BundleRuleConfig/README.md](../BundleRuleConfig/README.md) | BuildSetting、AssetCatalog |
| [../Editor/README.md](../Editor/README.md) | 打包 Editor 模块 |
| [../../../../BaseLayer/ToDelete/ABSystemTester/ABSystem_BetaTestCases.md](../../../../BaseLayer/ToDelete/ABSystemTester/ABSystem_BetaTestCases.md) | 测试用例（历史表） |
| [BusinessApiUsageGuide.md](./BusinessApiUsageGuide.md) | **Load/Release 抄用范式**（与本节场景配套） |

---

<a id="business-scenarios"></a>

### 业务场景总结

> **读者**：Gameplay / UI 等业务程序。  
> **入口**：`BundleResLoader.Instance` + `IAssetHandle`；卸载见 [BusinessApiUsageGuide.md](./BusinessApiUsageGuide.md)。  
> **架构图**：[ResLoader/README.md](../ResLoader/README.md) § 加载侧架构图。

#### 1. 三条铁律（先记住再写业务）

| 铁律 | 说明 |
|------|------|
| **Load 与 Release 成对** | 每成功一次 `Load`（或命中缓存后的 Ref++）最终要有对应 `Release` / `Unload(handle, …)`；C# **无析构**，句柄出作用域不会自动卸 AB。 |
| **Instantiate / Destroy 与 Ref 无关** | `Instantiate` 100 次 **不会** Ref+100；`Destroy(go)` **不会** Ref-1。Ref 只统计 **Load 次数**，不统计实例个数。 |
| **业务只认简路径** | `Load<T>("Atlas/Role/Hog_Attack_000")`；bundle 名、依赖顺序、四源选路由由框架 + 清单完成，**不要**业务侧选 `AssetSource`。 |
| **谁创建谁销毁** | 谁 `Load` / `CreatPool` / `GetOrCreatPool`，谁对称 `Release` / `DestroyPoolByLoadPath`；借方 `TryGetPool` + `GetObj`/`ReleaseObj`；切场景 `UnloadAll` 或建池方先卸池。见 [业务API §5.5](./BusinessApiUsageGuide.md)。 |

#### 2. 常见场景速查

| 场景 | 推荐写法 | 卸载时机 | 集成测试对应 |
|------|----------|----------|--------------|
| **模块进出场**（一模块一批资源） | 模块内字段保存多个 `IAssetHandle`；进模块 `Load`，出模块逐个 `Release` | 模块 `OnDestroy` / 关闭界面 | Case Release |
| **Prefab + 换贴图/材质** | `Load<GameObject>` + `Load<Sprite>` / `Load<Material>`，`GetAsset<T>()` 赋给 Renderer | 先 Release 辅助资源，再 Release Prefab | Case 2～3 |
| **跨包 UI** | `Load<GameObject>("UI/UIRoot")`；依赖包由清单 `bundles[]` 自动 Acquire | 同模块卸载 | Case 5 Cross UI |
| **同 Prefab 多实例（刷怪/列表）** | **`Load` 一次**，循环 `handle.Instantiate()`；列表保存 `GameObject` | 先 `Destroy` 全部实例，再 **`Release` 一次** | ReLoad 测 Ref++，非实例数 |
| **对象池** | 各模块 `GetOrCreatPool`（同 Active Scene 同路径共享 `refCount`）；借方 `TryGetPool` | 各建池方 `OnDestroy` `DestroyPoolByLoadPath`；切场景 `UnloadAll`；`sceneUnloaded` 兜底 | comprehensiveTest `PlayerTest` / `enemyManager` / `enemyTest` |
| **Common / 常驻资源** | 启动时 `Load` 一次或 `PreLoadBundles` | 仅 `UnloadAll` 或关游戏 | 主路线 §5 |
| **切场景 / 关游戏** | `BundleResLoader.Instance.UnloadAll()`（进程级，慎用） | 独占 Runner 收尾 | Case 8 UnloadAll |
| **Resources 路径** | `Load<T>("Resources/子路径/名")`（无扩展名） | 同 AB，`Release` | Router 套系 Case 2 |
| **Editor 联调无 AB** | 打包 **EditorTest** + Editor Play → AssetDatabase | 同 Release | Router Case 5 |

#### 3. Load / Instantiate / Destroy / Release 关系

```text
Load 1 次          →  Resource Ref = 1，bundle 进内存，得到 Prefab 原型
Instantiate ×N     →  Ref 不变，场景里 N 个 GameObject
Destroy(实例) ×N   →  Ref 仍不变
Release 1 次       →  Ref 0 → 卸原型 + ReleaseBundle（及依赖 Ref）
                   →  Bundle Ref=0 → LRU 空闲队列（延迟 Unload，见 BundleLruUnloadPolicy）

错误：Load ×100 再 Instantiate ×100  →  Ref = 100，须 Release ×100
错误：只 Destroy 实例不 Release       →  AB 泄漏
```

**推荐多实例模板**：

```csharp
IAssetHandle _prefab;
readonly List<GameObject> _spawned = new List<GameObject>();

void SpawnMany(int count)
{
    if (_prefab == null)
        _prefab = BundleResLoader.Instance.Load<GameObject>("Model/Prefabs/tester");
    for (int i = 0; i < count; i++)
        _spawned.Add(_prefab.Instantiate());
}

void ClearModule()
{
    foreach (GameObject go in _spawned)
        if (go != null) Destroy(go);
    _spawned.Clear();
    _prefab?.Release();
    _prefab = null;
}
```

**`Load<T>` 里的 T**：告诉 Unity 从包/Resources 取出 **哪种对象**（`GameObject` / `Sprite` / `Material` …），用于 `GetAsset<T>()` 与 `LoadAsset`；**不是**模块 ID，也 **不是** 缓存分区键（缓存键是 `loadPath` 或 `bundle/asset`）。

#### 4. 依赖包（A 依赖 B）业务无感

```text
Load("UI/UIRoot")
  → 清单 entries 得到 ui.bundle
  → BundleManager.AcquireBundleWithDependencies
       先 LoadFromFile 依赖包（如 atlas.bundle）
       再 LoadFromFile ui.bundle
  → LoadAsset(UIRoot)
```

- 依赖关系 **打包时** 写入 `catalog.bytes` 的 `bundles[]`（来自 `AssetBundleManifest.GetAllDependencies`）。  
- **EditorTest** 无真 AB 时 `bundles[]` 常为空，**不会**预加载依赖。  
- Release 时按 `acquiredBundleNames` **对称** `ReleaseBundle`；业务仍只对 **句柄** `Release`，不必手动卸依赖包。

#### 5. 运行时路由（业务仍是一个 API）

| 条件 | 实际来源 |
|------|----------|
| `loadPath` 以 `Resources/` 开头 | `Resources.Load` |
| Editor Play + 清单 `buildMode=EditorTest` | `AssetDatabase` |
| 本地无 bundle（CDN 扩展） | NETCDN → `HttpRemoteBundleProvider` |
| 默认 DeviceDebug / 首包 | 真 AB + 上节依赖逻辑 |

详见 [ResLoader/README.md](../ResLoader/README.md)、[BusinessApiAndCdnPlanning.md](./BusinessApiAndCdnPlanning.md)。

#### 6. 打包模式与业务预期

| 打包模式 | 业务在 Editor / Player 的预期 |
|----------|-------------------------------|
| **EditorTest** | Editor Play 可无 StreamingAssets AB；清单 `buildMode=EditorTest` |
| **DeviceDebug（首包）** | AB 在 `StreamingAssets/{平台}/Base/Bundles/`；真机/Player 走 ABUNDLE |
| **CdnHotUpdate** | 产出在 `cdnOutputPath/{平台}/Base/`；运行时 **CDN 下载 + 清单热更** 已接（阶段 C） |
| **DlcPackage** | 产出在 `{deviceOutputPath}/{平台}/DLC_{id}/`；运行时 `ContentPackageService.TryMount`（需 Gate 放行） |

Player 构建时 **不会永久删除** StreamingAssets 里其它平台目录，仅 [StreamingAssetsPlatformBuildFilter](../Editor/StreamingAssetsPlatformBuildFilter.cs) **临时移走**非目标平台，构建后还原。

#### 7. 性能与并发（业务侧须知）

| 情况 | 行为 |
|------|------|
| 同路径第二次 `Load` | 缓存命中，**Ref++**，无重复 IO |
| 两脚本同时 `Load` 同路径 | 已验（双 Runner 19/19）；阶段 B-2 将补 **inFlight 合并** |
| 首次 Load 慢 | 主要在 `LoadFromFile` + 依赖包 + `LoadAsset`，非 Router 开销 |
| `LoadUniTaskAsync` | ✅ **真异步**（`ResourceLoadCoordinator` + `BundleManager.AcquireBundleAsync`） |

#### 8. 禁止区产品目标 vs 当前能力

| 产品目标（设计基线 §9–14） | 当前业务可怎么做 |
|----------------------------|----------------|
| 边玩边下 / 300MB 首包 | CDN + `catalogueHash` 热更已可用；首包 subset 策略与业务门控仍待产品定稿 |
| 自选关卡 / DLC | 打包至 `DLC_{id}/` + `ContentPackageService` 已接；商店 SDK Gate（Steam 等）TODO |
| 离线玩老版本 | 等 persistentDataPath 缓存 + 清单世代策略 |
| MOD / 分工程 | 远期；现按模块 **Load/Release** 组织即可 |

#### 9. 相关文档

| 文档 | 用途 |
|------|------|
| [BusinessApiUsageGuide.md](./BusinessApiUsageGuide.md) | 代码范式、Unload 组合 |
| [MainRoadmap.md](./MainRoadmap.md) | PreLoad / 真异步 / CDN 排期 |
| [集成测试归档.md](../../../../Test/AB_Test/集成测试归档.md) | Case 与 JSON 门禁 |
| [测试说明.md](../../../../Test/AB_Test/测试说明.md) | Router / Stress 套系 |

---

### 业务侧需求对照（设计基线 §36–43）

禁止区内新增 **§7 CDN 联网下载**；实现进度如下（细节见 [BusinessApiAndCdnPlanning.md](./BusinessApiAndCdnPlanning.md)）：

| # | 需求 | 状态 | 说明 |
|---|------|------|------|
| 1 | 同步加载 | ✅ | `Load<T>(loadPath)`；辅助 `LoadByBundle` / `LoadByAssetPath`；**三端双 Runner 已验** |
| 2 | 异步加载（设计基线 **默认 API**） | 🟡 | `LoadUniTaskAsync` **三端双 Runner 19/19**；内核仍为 `Yield` + 同步 `Load`；无后台 I/O |
| 3 | 加载 + 回调 | 🟡 | `LoadUniTaskWithCallback` 等已实现，内核同上 |
| 4 | 预加载资源包 | ✅ | `PreLoadBundles` / `PreLoad<T>(loadPath)` |
| 5 | 卸载单个资源 | ✅ | `IAssetHandle.Release()` / `Unload(handle, instance, cb)` |
| 6 | 卸载全部 | ✅ | `UnloadAll()`；双 Runner 中仅 `Myloadtest` 独占 |
| 7 | **CDN 联网下载** | ✅ | `HttpRemoteBundleProvider` + `CdnCatalogueSyncService` + `BundleDownloadQueue` |

**设计基线测试要求（§46–50）对照**（实现细节区，非禁止区）：

| # | 测试要求 | 状态 | 说明 |
|---|----------|------|------|
| 1 | API 加载 + 依赖顺序 | ✅ | `AcquireBundleWithDependencies` + 跨包 Case；清单 `bundles[]` 已写入（依赖项多为空数组，**依赖预加载 Case 未单独验**） |
| 2 | 异常捕捉 / 规避 | 🟡 | 失败路径 `LogError`；无统一错误码 / 断言框架 / 非法路径 Case |
| 3 | 竞态安全（多脚本同资源） | ✅ | **同步**双 Runner：**Editor / Windows Player / Android** 均 `passCount=19`（`004641` / `004530` / `004612`） |
| 4 | 引用计数 | ✅ | 同步/异步双 Runner；**Resource Ref=0 → UnLoad**；B-2「在途 ref==0 丢弃」✅ |

**阶段判断（加载）**：阶段 A 已达标；主路线见 **[MainRoadmap.md](./MainRoadmap.md)**（阶段 B 异步 → 阶段 C CDN）。

### 总体进度（概念设计 → 实现）

> **估算口径**：各模块 checklist 行计数（✅=1，❌=0，🟡=0.5）；**不等于**禁止区六条产品场景整体完成度。

| 设计模块 | 概念是否清晰 | 实现进度（2026-06-11 重估） | checklist | 说明 |
|----------|--------------|------------------------------|-----------|------|
| **1. 打包规则制定器** | ✅ | **约 76%** | 9/12 | 同窗口 + SO；Custom + `BundleFolderRule` 三档；缺树状 UI、XML、自动 AB 标签 |
| **2. 打包器** | ✅ | **约 73%** | 10/14 | Build/Clean/Validate、三规则、Player 平台过滤；缺增量 UI、Bundle 分析、DLC 路径、分模式清单策略 |
| **中间桥梁 · 资源清单** | ✅ | **约 85%** | 8/10 | 二进制双份 + `entries` + `bundles[]`；`catalogueHash` CDN 比对已用；运行时 **version/buildNumber 比对** 未做 |
| **3. 抽象资源** | ✅ | **约 68%** | — | 双层 Ref + 依赖 Acquire；三端并发 PASS；无 MOD/远程资源抽象 |
| **4. 加载器 + 路由器** | ✅ | **约 85%** | — | CDN 运行时 + PreLoad 已接；B-2 真异步/inFlight 仍拉低加权 |

**综合实现进度（五行平均）**：**约 70%**（较上一版「感估 63～82% 分散值」改为按 checklist 重算并拉齐口径）。

**阶段判断**：阶段 A **「打包 → 清单 → 同步 Load + 三端双 Runner」** 已完成；后续按 **[MainRoadmap.md](./MainRoadmap.md)** 推进异步与 CDN。

---

### 1. 打包规则制定器（设计目标 §1）

**设计要点**：Editor 菜单呼出；选打包模式、平台、打包规则（默认 / 按目录 / 自定义）；规则持久化为 SO 或 XML；与打包器同界面、强耦合（View + 规则编辑）。

**当前实现**

| 能力 | 状态 |
|------|------|
| 菜单 `vFramework → AssetBundle Packer` | ✅ |
| 基本设置：平台、版本号、构建号、双输出路径 | ✅ |
| 打包模式：编辑器测试 / **首包（真机模式）** / CDN联网 | ✅（Default/Detailed 默认首包（真机模式）） |
| 打包规则：Default / Detailed / Custom | ✅ |
| Custom：配置列表（路径、**文件夹粒度**、包名、每项独立打包模式等） | ✅（**平面列表** + `BundleFolderRule` 三档；非树状嵌套） |
| 规则持久化 | ✅ **ScriptableObject**（`DefaultBuildSetting.asset`），非 XML |
| 字段说明 | ✅ 悬停 tooltip |
| 蓝图 HTML 原型 | ✅ 与窗口大致对齐 |
| 自动给资源打 AssetBundle 标签 | ❌ 未做（当前由 `RuleResolver` 扫描目录，不依赖 Inspector 标签） |
| 树状嵌套 Custom 配置 | ❌ 未做 |
| XML 规则导出 | ❌ 未做（仅 SO） |

**主要文件**：`BuildSetting.cs`、`BundlePackerWindow.cs`、`DefaultBuildSetting.asset`

---

### 2. 打包器（设计目标 §2）

**设计要点**：按规则扫描、打包到对应目录；与制定器同工具；支持清理、增量/全量；打完生成清单。

**当前实现**

| 能力 | 状态 |
|------|------|
| 与制定器同一窗口触发 Build / Clean / Save | ✅ |
| `RuleResolver` 解析 Default / Detailed / Custom | ✅ |
| `BuildPipeline.BuildAssetBundles` | ✅（**编辑器测试**模式跳过，仅写清单） |
| 输出：`deviceOutputPath` / `cdnOutputPath` | ✅ |
| 按平台分子目录（`usePlatformSubfolders`） | ✅ |
| Custom 按每项 `buildMode` 分组输出 | ✅ |
| 打包后 `CatalogueWriter` 写清单 | ✅ |
| `Clean`：两输出路径 + 工程 Catalogue，删 meta | ✅ |
| `Validate` | ✅ |
| Player 构建平台子目录过滤（`StreamingAssetsPlatformBuildFilter`） | ✅ |
| 增量打包（显式 UI/策略） | ❌（依赖 Unity `BuildAssetBundles` 自带增量） |
| 打包后分析（`BundleBuildAnalyzer`） | ✅ `{bundleRoot}/Reports/BundleBuildReport.json` + Packer 报告 Tab |
| DLC 专用输出路径 | ✅ `{平台}/DLC_{id}/`（`dlcPackageId`）；Custom 独立 `dlcOutputPath` 字段仍 TODO |
| 按打包模式差异化清单策略 | ❌ `BuildByMode` 内 TODO |

**职责划分（与设计一致）**

- **BundlePackerWindow**：编辑 `BuildSetting`、Builder / Reporter 双页签 UI（≈ View + 规则编辑）
- **BundleBuilder**：执行 Build / Clean / Validate（≈ Controller）
- **RuleResolver / CatalogueWriter**：规则与清单（≈ 执行层子模块）

**主要文件**：`BundleBuilder.cs`、`RuleResolver.cs`、`CatalogueWriter.cs`

---

### 3. 中间桥梁：资源清单（设计「中间桥梁」）

**设计要点**：清单连接打包器与加载器；加载器据清单决定加载关系与策略。

**当前实现（写端 + 读端）**

| 能力 | 状态 |
|------|------|
| 数据结构 `AssetCatalog`（`AssetCatalog.cs`，#region 分区） | ✅ |
| `entries[]`：`assetPath` → `bundleName` + `assetName` | ✅ 已写入二进制清单 |
| 工程内 `AssetCatalog.bytes` + `{平台}/Base/Version/catalog.bytes` 双份 | ✅ |
| 版本 / 平台 / 打包模式等元数据 | ✅ |
| `bundles[]` 包依赖表 | ✅ 从 `AssetBundleManifest` 写入（三端 Load 已验） |
| `bundles[]` **拓扑排序** + 环检测 | ✅ `BundleDependencyTopology` + `CatalogueWriter` |
| 构建后 **Bundle 冗余/包体分析** | ✅ `BundleBuildAnalyzer` → `Reports/BundleBuildReport.json` |
| `loadPath` 重复构建期校验 | ✅ `CatalogueValidator`（Warning/Error 可配） |
| 运行时读取 `version` / `buildNumber` 做热更决策 | ❌ 仅打包写入 |
| 清单二进制 `catalog.bytes`（VCAT v1） | ✅ `AssetCatalogBinaryCodec` |
| `CatalogueReader`、运行时读清单 | ✅ |

详见 [CatalogueReference.md](./CatalogueReference.md)。

---

### 4. 抽象资源 & 加载器（设计目标 §3、§4）

| 模块 | 现状 |
|------|------|
| `AbstractResource` / `IAssetHandle` | Resource 层 Ref；`LoadAsset` / Release 经 `AssetRouter` |
| `BundleManager` | `LoadFromFile` + `IBundlePathResolver`；`AcquireBundleWithDependencies` 读清单 `bundles[]` |
| `CatalogueReader` + `StreamingAssetsIO` | 读 `catalog.bytes`；Editor 可 `LoadFromProjectCatalogue`；Android `jar:` 已验 |
| `BundleResLoader` | 同步 `Load` ✅；`LoadUniTaskAsync` + 回调 ✅；`PreLoadBundles` ✅ |
| `AssetPool` | ✅ `PrefabPool` + `PrefabPoolManager` + `PoolSceneRootsUtil` |
| `BaseLogSys` | 🟡 `AssetRefTraceLogger`（Ref Trace）；`DebugLogger` 占位 |
| `AssetRouter` | ✅ 四源：`ABUNDLE` / `RESOURCES` / `EDITORRESOURCES` / `NETCDN`（`HttpRemoteBundleProvider`） |
| **集成测试** | 同步/异步双 Runner **三端 19/19** |

设计基线 §1 同步 `Load(简路径)` 与 §3 回调入口已实现；§2 **默认异步** 仅 UniTask 形态，**非**设计终态的真异步 I/O。§4 预加载、§7 CDN、路由器仍见 [BusinessApiAndCdnPlanning.md](./BusinessApiAndCdnPlanning.md)。

---

### 目录结构

```text
Assets/vFramework/BaseFramework/BaseAssetSys/
├── AbstractAssets/          # AbstractResource + README.md
├── AssetPool/               # PrefabPool、PrefabPoolManager、PoolSceneRootsUtil
├── BaseLogSys/              # AssetRefTraceLogger（见 LoaderOptimizationPlan §4）
├── ResLoader/               # Business / Bundle / Catalogue / Router + README.md
├── BundleRuleConfig/        # BuildSetting、AssetCatalog、BundleDependencyTopology + README.md
├── Editor/                  # 打包工具 + README.md
│   ├── BundlePacker/        # 统一窗口 UI（Builder + Reporter 页签）
│   ├── BundleBuilder/       # Build 编排 + Catalogue/ + Tests/
│   ├── BundleReporter/      # BundleBuildAnalyzer + 报告 DTO
│   └── StreamingAssetsPlatformBuildFilter.cs
└── Docs/                    # 文档索引、蓝图（Builder / Reporter）、设计目标
```

---

### 打包侧配置速查

**打开方式**：Unity → **vFramework → AssetBundle Packer**

**打包规则**

| 规则 | 行为 |
|------|------|
| Default | 目标目录每个一级子文件夹 → `{文件夹名}.bundle` |
| Detailed | 所有嵌套子文件夹 → `{相对路径_下划线}.bundle` |
| Custom | 配置列表；文件夹路径可选 **整包 / 第一级子文件夹 / 全嵌套子文件夹**（`BundleFolderRule`）；单文件打单资源 |

**输出路径**

| 字段 | 默认 | 用途 |
|------|------|------|
| `deviceOutputPath` | `Assets/StreamingAssets` | 真机首包根路径 |
| `cdnOutputPath` | `Bundles/CDN` | CDN 根路径 |
| `usePlatformSubfolders` | `true` | 实际输出 `{根}/{StandaloneWindows64|Android|…}/`，多端并存 |

**打包模式与产物**（`usePlatformSubfolders = true` 时）

| 模式 | 示例输出 |
|------|----------|
| DeviceDebug / Windows | `Assets/StreamingAssets/StandaloneWindows64/*.bundle` |
| CdnHotUpdate / Android | `Bundles/CDN/Android/*.bundle` |

| 模式 | UI | AB | 清单 |
|------|-----|-----|------|
| EditorTest | 编辑器测试 | 不调用 BuildPipeline | 仍写 `catalog.bytes` |
| DeviceDebug | 首包（真机模式） | `{deviceOutputPath}/{平台}/Base/` | 仍写 `catalog.bytes` |
| CdnHotUpdate | CDN联网 | `{cdnOutputPath}/{平台}/Base/` | 仍写 `catalog.bytes` |

Default / Detailed 用规则区全局 `buildMode`（**默认首包（真机模式）**）；Custom 每项可指定不同模式。

---

### 打包侧未实现（摘要）

- 显式增量打包策略 UI（`BundleBuildAnalyzer` 报告已接入 Reporter 页签）
- 按模式差异化清单策略、EditorTest 纯模拟目录  
- 运行时 **version/buildNumber 比对**  
- Custom 树状 UI、XML 规则、打包前自动 AB 标签  
- **Custom 规则 / 异步双 Runner** 的集成测试 JSON（`UniConcurrentLoad_*` 待归档）  
- Custom 项独立 `dlcOutputPath` 配置字段（当前 Custom DLC 仍复用 `cdnOutputPath` 占位校验）

细节与接入步骤见 [CatalogueReference.md](./CatalogueReference.md) 及代码内 `TODO`。

---

### 打包联调步骤（当前可执行）

1. 在目标目录（默认 `Assets/AssetBundle`）下建子文件夹并放入资源  
2. 打开 **vFramework → AssetBundle Packer**，选规则与打包模式，**开始打包**  
3. 确认 AB 输出路径与 `BundleRuleConfig/Catalogue/AssetCatalog.bytes`（编辑器测试无新 AB，仅有清单）  
4. **Play 加载**：`Init` → 同步 `Load("Atlas/Role/xxx")`；或跑 `TestABScene` 双 Runner（目标 `ConcurrentLoad_*` **19/0**）  
5. **清理打包** 验证两输出路径与 Catalogue 已清理、无 orphan `.meta`  
6. **Build Player** 时确认 `StreamingAssets` 仅含目标平台子目录（`StreamingAssetsPlatformBuildFilter`）

---

## 下一步测试依据：打包与清单是否正常

> 本节说明「编辑器模拟」与「真机构建」的差异，作为验收 **AB 包** 与 **`catalog.bytes`** 是否生成正确的对照标准。  
> 当前 **ABSystem_Beta** 的 **编辑器测试 / 首包（真机模式） / CDN联网** 三种模式，长期应对齐下表路径约定；现阶段可按模式分别验证输出路径。

> **⚠️ 核心注意点**  
> **首包在 `StreamingAssets`，其他包通常不在安装包里**——它们在 **CDN / 资源服务器**，下载后落在设备的 **可写目录**（最常见是 `persistentDataPath`）。  
> 当前 ABSystem_Beta 开发联调时可能暂时把所有 AB 都打进 `StreamingAssets`，这是简化做法；**提测 / 上线前必须按策略拆分首包与远程包**。

### 核心概念：两种「环境」不是同一种「打包」

| 模式 | 本质 | 是否产出真实 AB 文件 |
|------|------|----------------------|
| **编辑器模拟（EditorSimulateMode）** | 用模拟清单直接映射工程内资源 | 一般 **不** 产出完整 AB 二进制 |
| **真机构建（Offline / Host 等）** | 真正打 AssetBundle | **是**，产出完整包体文件 |

### ABSystem_Beta 打包模式与上述概念的对应（目标态）

| ABSystem_Beta 模式 | 应对齐的概念 | 当前实现（测试时预期） |
|--------------|-------------|------------------------|
| 编辑器测试 | 编辑器模拟 + 本地快速验证 | **不** 调用 `BuildPipeline`，无新 `.bundle`；仍写 `catalog.bytes` |
| 首包（真机模式） | 真机构建 | AB 输出到 `deviceOutputPath`（默认 StreamingAssets） |
| CDN联网 | Host 热更 | AB 输出到 `cdnOutputPath`（默认 `Bundles/CDN`） |

---

### 注意点：首包、热更包与本地缓存（一张图看懂）

```text
安装包（APK/IPA）
└── StreamingAssets/
    └── [PackageName]/
        ├── 清单 manifest
        ├── 版本 hash
        └── 少量首包 AB          ← 首包 / 内置包

CDN / 资源服务器
└── [Platform]/[PackageName]/
    ├── 新版本 manifest
    └── 全部或增量 AB           ← 热更包 / 远程包

玩家设备（运行时）
└── persistentDataPath/
    └── [PackageName]/
        └── 已下载的 AB + 清单   ← 本地缓存（覆盖/补充首包）
```

#### 「其他包」通常指什么

**1. 热更资源（最常见）**

| 资源 | 位置 |
|------|------|
| 首包（启动必需） | `StreamingAssets` |
| 热更包（新版本资源） | **CDN** → 下载到 **`persistentDataPath`** |

运行时常见优先级：

```text
persistentDataPath（本地已下载的新版）
    ↓ 没有
StreamingAssets（首包内置旧版）
    ↓ 没有
CDN 下载 → 写入 persistentDataPath
```

首包可能是 `v1.0`，热更后是 `v1.5`；**新版本不在 StreamingAssets 里**，在 CDN，下完进本地缓存。

**阶段 C 运行时验收（✅ 2026-06-13 封板）**

- [x] `DefaultBundlePathResolver`：ABCache → StreamingAssets → NETCDN 下载  
- [x] `CdnCatalogueSyncService`：`catalogueHash` 变化写 `ABCache/Catalogue/` 并重载  
- [x] CDN 不可达：Warning + 继续使用首包/本地 cache 清单  
- [x] `PreLoadBundles` / `HttpRemoteBundleProvider` + `BundleDownloadQueue`  

**2. 多个资源包（Package）**

YooAsset 等支持多 Package，例如：

- `DefaultPackage`：主资源
- `RawPackage`：原生文件
- `DLCPackage`：DLC

| Package | 首包内置 | 其他 |
|---------|----------|------|
| 主包核心资源 | 可放 `StreamingAssets` | 热更走 CDN |
| DLC / 可选内容 | 通常 **不放** 首包 | 按需从商店 / CDN 下载到 `persistentDataPath` |
| 纯远程内容 | 不放 | 全在 CDN |

逻辑仍是：**要进安装包的 → StreamingAssets；不进安装包的 → 远程 + 本地缓存**。

**3. 编辑器 / 开发环境**

| 环境 | 「其他包」在哪 |
|------|----------------|
| 编辑器模拟 | 不真打 AB，直接映射工程资源 |
| 编辑器真机构建预览 | 多在项目 `Bundles/[Platform]/`，未必进 StreamingAssets |

#### 各目录职责（简表）

| 目录 | 放什么 | 谁写入 |
|------|--------|--------|
| `Bundles/[Platform]/`（项目外 / 构建输出） | 构建完成的全部 AB | 打包工具 |
| `StreamingAssets` | 打进 APK 的首包资源 | 构建脚本拷贝 |
| CDN / OSS | 热更、DLC、完整远程包 | 运维 / CI 上传 |
| `persistentDataPath` | 已下载的热更 / DLC | 游戏运行时 |

#### 常见分包策略

**首包极小（联机热更）**

```text
StreamingAssets：启动代码依赖的 manifest + 极少 AB
CDN：绝大部分游戏资源
persistentDataPath：玩家已下载内容
```

**离线单机**

```text
StreamingAssets：几乎全部 AB
CDN：无
persistentDataPath：一般不用（或只做存档）
```

**DLC / 分包下载**

```text
StreamingAssets：本体必需资源
CDN：每个 DLC 独立目录
persistentDataPath：玩家买了 / 下了哪个 DLC，就缓存哪个
```

**与 ABSystem_Beta 的关系**

- **编辑器测试**：不生成 `.bundle`，仅更新 `catalog.bytes`。  
- **首包（真机模式）**：AB 输出到 `{deviceOutputPath}/{平台}/Base/Bundles/`。  
- **CDN联网**：AB 输出到 `{cdnOutputPath}/{平台}/Base/Bundles/`。  
- **Catalogue**：打包侧双份 `.bytes`；加载侧 `CdnCatalogueSyncService` 热更已接。

**一句话**

- **首包** = 装进 `StreamingAssets` 的「快递盒里自带的那部分」
- **其他包** = 在 **CDN** 上，玩家需要时下载到 **`persistentDataPath`**
- **构建时**所有包往往先打到 **`Bundles/[Platform]/`**，再按策略：**一部分拷进 StreamingAssets，其余上传 CDN**

---

### 一、编辑器模拟环境：打到哪里？

**目的**：开发期快速迭代，跳过完整 AB 构建。

典型行为：

1. 调用类似 `SimulateBuild()` 的接口
2. 生成 **模拟清单（Manifest）**，记录资源路径、依赖、版本等
3. 运行时 **不读真实 `.bundle`**，而是按清单 **直接读工程内资源**（或走虚拟文件系统）

**输出位置（常见约定）**：

```text
项目根目录/Bundles/[PackageName]/Simulate/     ← 模拟清单
或
Library/YooAsset/...                           ← 部分版本放 Library（可清理）
或
YooAsset/EditorSimulate/...
```

特点：

- 路径多在 **项目目录内**，不进 `StreamingAssets`
- 产物以 **清单 / 元数据** 为主，不是给真机用的 AB
- 改资源后通常 **重新 Simulate 即可**，很快

**ABSystem_Beta 现阶段说明**：**编辑器测试** 已跳过 `BuildPipeline`，仅更新 `catalog.bytes`；**首包（真机模式）** / **CDN联网** 打出真实 `.bundle` 并写清单。

---

### 二、真机环境：打到哪里？

**目的**：产出可部署、可热更的真实资源包。

#### 1. 构建输出（Build 阶段）

打包工具先写到 **构建输出目录**，常见结构：

```text
项目根目录/Bundles/
├── DefaultPackage/
│   ├── Android/
│   │   ├── *.bundle
│   │   ├── *.bytes          # 清单
│   │   └── *.hash           # 版本哈希
│   ├── iOS/
│   ├── StandaloneWindows64/
│   └── ...
```

具体根路径可在 **Collector / Builder 配置** 里改，但逻辑都是：**按平台分目录存放构建产物**。

**ABSystem_Beta 现阶段输出**（验收清单 — 开发联调简化版，**不等同于上线分包策略**）：

| 产物 | 路径 |
|------|------|
| AB 包（首包（真机模式）） | `{deviceOutputPath}/{平台}/Base/Bundles/`，如 `Assets/StreamingAssets/StandaloneWindows64/Base/Bundles/` |
| AB 包（CDN联网） | `{cdnOutputPath}/{平台}/Base/Bundles/`，如 `Bundles/CDN/Android/Base/Bundles/` |
| AB 包（编辑器测试） | **不生成** `.bundle`（仅 `catalog.bytes`） |
| 平台 manifest | `Base/Bundles/` 下 Unity 平台 manifest 及 `*.manifest` |
| 资源清单（工程内） | `BundleRuleConfig/Catalogue/AssetCatalog.bytes` |
| 资源清单（运行时） | `{平台根}/Base/Version/catalog.bytes` |

> 正式上线时：全部 AB 不应都进 `StreamingAssets`，仅首包 subset 进入；其余见上文「注意点：首包、热更包与本地缓存」。
#### 2. 部署到设备（Deploy 阶段）

构建完成后，按运行模式再分发到不同 **运行时读取位置**：

| 运行模式 | 首包内置资源 | 热更 / 远程资源 |
|----------|--------------|-----------------|
| **OfflinePlayMode（单机）** | 复制到 `StreamingAssets/yoo/...` | 一般无远程 |
| **HostPlayMode（联机热更）** | 首包版本 → `StreamingAssets` | 完整包上传 **CDN/OSS**，设备缓存到 `persistentDataPath` |
| **WebPlayMode** | 放 Web 服务器目录 | 浏览器缓存 |

**真机上的典型路径**：

```text
# 首包内置（只读）
Application.streamingAssetsPath/yoo/[PackageName]/

# 下载缓存（可读写）
Application.persistentDataPath/yoo/[PackageName]/

# 远程
https://cdn.example.com/bundles/Android/[PackageName]/
```

---

### 三、流程对比（简化）

```text
┌─────────────────────────────────────────────────────────────┐
│  编辑器模拟模式                                                │
│  SimulateBuild() → 项目内 Simulate 目录（清单）               │
│  运行时 → 直接读 Assets 下原始资源                             │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  真机构建                                                      │
│  Build() → Bundles/[Package]/[Platform]/（真实 .bundle）      │
│       ↓                                                       │
│  首包 → StreamingAssets                                       │
│  热更 → CDN → persistentDataPath                              │
└─────────────────────────────────────────────────────────────┘
```

---

### 四、和其他框架的对应关系

| 框架 | 编辑器模拟 | 真机构建输出 | 运行时首包 | 热更缓存 |
|------|-----------|-------------|-----------|---------|
| **YooAsset** | Simulate 目录 / 虚拟 FS | `Bundles/平台/` | StreamingAssets | persistentDataPath + CDN |
| **Addressables** | Play Mode = Fast / Virtual | `ServerData` 或本地 Build 目录 | StreamingAssets | `persistentDataPath` + Remote |
| **xAsset** | Simulation 模式 | `Bundles` 输出目录 | StreamingAssets | 下载目录 + 远程 |
| **ABSystem_Beta（本项目）** | 编辑器测试跳过 BuildPipeline | `deviceOutputPath` / `cdnOutputPath` + 平台子目录 | 当前首包进 `StreamingAssets` | **阶段 C 运行时 CDN ✅**；打包 CDN 模式 ✅；**三端双 Runner 集成 ✅** |

思路一致：**开发用模拟、发布用真包；首包进 StreamingAssets，补丁走 CDN + 本地缓存**。

---

### 五、选型上的常见原则

1. **日常开发**：编辑器模拟，避免每次改资源都全量打 AB。
2. **联调 / 提测前**：用目标平台真机构建，验证压缩、加载、依赖。
3. **首包**：只放启动必需资源（配置、核心 UI、首场景等）。
4. **热更包**：打完整平台包 → 上传 CDN → 版本号 / Hash 驱动增量更新。
5. **路径配置**：构建输出目录（CI 产物）和运行时读取目录（StreamingAssets / 缓存）要分开理解，不要混成一个路径。

---

### 六、打包与清单验收检查表（ABSystem_Beta 当前可执行）

打包完成后，按下列项逐项确认即视为 **打包 + 清单生成正常**：

- [ ] **编辑器测试**：无新增 `.bundle`（或 StreamingAssets 无新增），但 `AssetCatalog.bytes` 已更新
- [ ] **首包（真机模式）**：`{deviceOutputPath}/{平台}/Base/Bundles/` 下存在预期数量的 `*.bundle`
- [ ] **CDN联网**：`{cdnOutputPath}/{平台}/Base/Bundles/` 下存在预期数量的 `*.bundle`
- [ ] 每个 bundle 有对应 `.manifest`（Unity 自动生成，编辑器测试模式除外）
- [ ] `BundleRuleConfig/Catalogue/AssetCatalog.bytes` 存在且可被 `AssetCatalogBinaryCodec` 解析
- [ ] `{平台根}/Base/Version/catalog.bytes` 与工程内副本 `catalogueHash` 一致
- [ ] 清单中每条 `entries` 含 `assetPath`、`bundleName`、`assetName`，且与 Default/Detailed/Custom 规则一致
- [ ] 清单 `version` / `buildNumber` / `platform` / `packingRule` 与 BuildSetting 窗口一致
- [ ] 清理打包后，两输出路径 + Catalogue 被清理，**无 orphan `.meta` 警告**
- [ ] **Build Player**：`StreamingAssets` 内仅有当前平台 AB 子目录（`StreamingAssetsPlatformBuildFilter`）

（加载侧：**同步**双 Runner — `Myloadtest` + `MyLoadTest2`，目标 `ConcurrentLoad_*` **passCount=19**；基准 `004641` / `004530` / `004612`。单 Runner 9 Case：`004007` / `004128`。异步套系 `MyLoadUniTest` 待 `UniConcurrentLoad_*`。）

---

### 七、一句话总结

- **编辑器模拟**：清单多在 **项目内 Simulate / Editor 目录**，运行时 **映射回工程资源**，不依赖真实 AB。
- **首包（真机模式）**：先打到 **`Bundles/[包名]/[平台]/`**，再按策略：**首包 → `StreamingAssets`**，**其他包 → CDN → `persistentDataPath`**。
- **ABSystem_Beta 当前阶段**：**打包 + 清单 + 同步 Load + CDN 运行时 + 三端双 Runner 集成** 已验收；开发期可暂全进 `StreamingAssets`；热更细节见 [BusinessApiAndCdnPlanning.md](./BusinessApiAndCdnPlanning.md)。