# Bundle 构建优化与拓扑排序 — 实现计划

> **状态**：✅ 已实现（2026-06-08）  
> **目标**：在不推翻「打包 → Catalogue → BundleResLoader」主链路的前提下，补齐 **Bundle 依赖拓扑排序** 与 **构建期 Bundle 优化分析**。  
> **关联**：[CatalogueReference.md](./CatalogueReference.md)、[MainRoadmap.md](./MainRoadmap.md)、`Editor/BundleBuilder/`、`ResLoader/Bundle/BundleManager.cs`

---

## 1. 背景与目标

### 1.1 现状

| 能力 | 现状 |
|------|------|
| 清单 `bundles[]` | ✅ 由 `AssetBundleManifest.GetAllDependencies` 写入 |
| 运行时预加载 | ✅ `AcquireBundleWithDependencies` 按 `dependencies[]` 顺序 `AcquireBundle` |
| **依赖顺序保证** | ✅ `BundleDependencyTopology` + `CatalogueWriter` 写拓扑序；`CatalogueReader` 读时幂等再排序 |
| **跨包冗余分析** | ✅ `BundleBuildAnalyzer` → `{bundleRoot}/Reports/BundleBuildReport.json` |
| **构建期校验** | ✅ loadPath 重复（Warning/Error 可配）、依赖环检测（阻断写清单） |

### 1.2 本期要交付的两项

| 项 | 交付物 | 不改变 |
|----|--------|--------|
| **A. 拓扑排序** | 清单中 `dependencies[]` 为 **拓扑序**；运行时按序 Acquire；构建期 **环检测** | 业务 `Load` API、双层 Ref |
| **B. Bundle 构建优化** | 打包后 **分析报告**（冗余资源、包体、loadPath 冲突）；可选 **规则建议** | 首版 **不自动改包**（避免破坏 19/19 基准） |

### 1.3 非目标（本期不做）

- 自动抽共享 Bundle 并二次 `BuildAssetBundles`（列为 Phase B-Opt-3 远期）
- asset 级运行时 DAG / 异步加载队列（见主路线 B-2）
- 清单 hash / 增量 diff（见主路线 C-2）

---

## 2. 项 A：Bundle 依赖拓扑排序

### 2.1 问题

```text
ui.bundle 依赖 atlas.bundle，atlas.bundle 依赖 common.bundle

若 dependencies[] 顺序为 [ atlas, common ] → Acquire atlas 时 common 可能尚未 LoadFromFile
→ 跨包引用异常或 Editor 下偶发「材质变粉」
```

当前 `CatalogueWriter` 直接使用 `GetAllDependencies` 的去重列表，**未**验证「被依赖包先于依赖包」。

### 2.2 设计原则

1. **排序在打包期完成**，运行时 **只读顺序**，不在 Player 热路径做图算法。  
2. **与现有结构兼容**：仍用 `BundleCatalogInfo.dependencies[]`，不强制改 JSON schema。  
3. **EditorTest** 无 Manifest 时 `bundles[]` 仍可为空；拓扑逻辑仅在 `manifest != null` 时执行。  
4. 排序后 **不改变依赖集合**，只改变 **Acquire 顺序**。

### 2.3 数据与图模型

**节点**：bundle 文件名（小写归一化，`BundlePlatformPaths.NormalizeBundleName`）。

**边**（推荐 Phase A-1 仍用全量依赖，Phase A-2 可改直接依赖）：

| 方案 | 边来源 | 清单体积 | 运行时 Acquire |
|------|--------|----------|----------------|
| **A-1（推荐先做）** | `GetAllDependencies` 得到集合，再用 `GetDirectDependencies` 建图做拓扑 | 与现网相同 | foreach 排序后的全量列表 |
| **A-2（可选）** | 清单只存 **直接依赖** | 更小 | 运行时递归 + 拓扑展开 |

**拓扑序定义**：对 bundle `B` 的 `dependencies[]`，任意边 `D → B`（D 被 B 依赖），在数组中 **D 的下标 < B 相关 Acquire 顺序中 D 先于 B**。  
对 `B` 的依赖列表排序时，应对 **依赖子图** 做 Kahn 或 DFS 后序，输出 **从叶到根** 的顺序（先 Acquire 叶包）。

### 2.4 实现步骤（Phase A）

#### A-1 公共工具 `BundleDependencyTopology`（Editor + Runtime 同逻辑）

**建议路径**：`BundleRuleConfig/Catalogue/BundleDependencyTopology.cs`（纯 C#，无 UnityEditor 依赖）

```csharp
// 伪 API
struct BundleDependencyGraph
{
    IReadOnlyList<string> SortDependencies(string bundleName, IReadOnlyList<string> directOrAllDeps, IReadOnlyDictionary<string, string[]> allBundleDeps);
    bool TryTopologicalSort(IReadOnlyDictionary<string, string[]> graph, out string[] sorted, out string cyclePath);
}
```

**算法**：

1. 从 Manifest 为每个 `build.assetBundleName` 取 **直接依赖** `GetDirectDependencies` 建图 `graph[u] = direct deps`。  
2. 对目标包 `B`，收集 **所有传递依赖**（BFS/DFS 或继续用 `GetAllDependencies` 集合）。  
3. 在子图上 Kahn 拓扑排序；若 `sorted.Count != nodeCount` → **环**（Unity 极少见，但应 LogError 并中止写清单）。  
4. 输出排序后的 bundle 名列表写入 `BundleCatalogInfo.dependencies`。

#### A-2 接入 `CatalogueWriter.BuildBundleDependencies`

```text
manifest.GetAllDependencies(name)     → 集合（保留，用于校验不丢项）
manifest.GetDirectDependencies(name)  → 建图
BundleDependencyTopology.Sort(...)    → sorted[]
写入 BundleCatalogInfo.dependencies = sorted
```

#### A-3 运行时加固（可选双保险）

`CatalogueReader.GetBundleDependencies`：

- 若检测到逆序（调试断言），Editor/Dev 构建打 Warning；  
- 或读清单后在 `BuildLookupTables` 时对每个 `dependencies` 再 Sort 一次（与写端同工具类）。

`BundleManager.AcquireBundleWithDependencies`：**保持** foreach 顺序，注释标明 **依赖清单已拓扑序**。

#### A-4 测试与验收

| 验收项 | 方式 |
|--------|------|
| 单元 | 构造 `A→B→C` 链、`A→B`、`A→C` 菱形，断言 Sort 输出 `[C,B]` 或 `[B,C]` 等合法拓扑序 |
| 集成 | 主路线 **P1-3**：DeviceDebug 打包含真实 `bundles[]` 的 UI+Atlas 场景 Load 不粉 |
| 回归 | 同步双 Runner **19/19** 三端不变 |
| Editor | 故意 mock 环（单测），`CatalogueWriter` 应失败且不写坏 JSON |

### 2.5 工作量估算

| 步骤 | 预估 |
|------|------|
| A-1 工具类 + 单测 | 0.5～1 天 |
| A-2 接入 Writer | 0.5 天 |
| A-3 Reader 双保险 | 0.25 天 |
| A-4 集成 Case + 文档 | 0.5 天 |

---

## 3. 项 B：Bundle 构建优化

### 3.1 问题

当前 `RuleResolver` 按 **文件夹** 分包，不分析：

- 同一贴图/材质被多个 Bundle 重复打入（包体与内存冗余）  
- 某资源被 3 个包间接引用却未抽共享包  
- `loadPath`（业务简路径）冲突  

Unity 引擎会在 Manifest 层表达 **包间依赖**，但 **不会** 替项目做「零冗余分包策略」；需要在 **构建后分析** 或 **规则层** 补强。

### 3.2 分期策略

```text
B-Opt-1  构建后只读分析报告（不改包）     ← 本期
B-Opt-2  规则/清单校验 + Packer 窗口展示
B-Opt-3  自动建议/半自动抽 shared.bundle   ← 远期，需二次 Build
```

**本期只做 B-Opt-1 + 部分 B-Opt-2**，避免自动改包导致基准波动。

### 3.3 B-Opt-1：冗余与引用分析（`BundleBuildAnalyzer`）

**建议路径**：`Editor/BundleBuilder/BundleBuildAnalyzer.cs`（取代空壳 `BundleAnyazier` 的核心逻辑，Analyser 可保留为报告 View 入口）

**输入**：

- 本次 `AssetBundleBuild[] builds`  
- `AssetBundleManifest manifest`（可选）  
- `BuildSetting` / `resourceRoot`

**步骤**：

1. **asset → bundle 映射**  
   遍历每个 `build.assetNames`，记录 `assetPath → bundleName`。

2. **依赖收集**  
   对每个 assetPath 调用 `AssetDatabase.GetDependencies(assetPath, recursive: true)`，过滤脚本/内置资源。

3. **跨包引用统计**  
   对每个依赖 asset `D`：  
   - 若 `D` 归属 bundle `Bd`，被 bundle `B1,B2,...` 中资源依赖，且 `Bi != Bd` → 记 **跨包边** `Bi → Bd`。  
   - 若 `D` **未** 出现在任何 build.assetNames（仅作为依赖被引用），统计 **implicit dependency**。  
   - 若同一 **非 bundle 内资源** 被 **≥2 个 bundle** 的 primary asset 依赖 → 标记 **冗余候选**（类似 YooAsset 冗余检测思路，先报告不拆包）。

4. **输出 `BundleBuildReport`**（JSON + 可选 EditorWindow 表格）

```json
{
  "bundleCount": 12,
  "redundantAssets": [
    {
      "assetPath": "Assets/.../SharedIcon.png",
      "referencedByBundles": ["ui.bundle", "shop.bundle"],
      "suggestion": "Consider shared bundle or move asset to common folder rule"
    }
  ],
  "crossBundleEdges": [ ... ],
  "loadPathDuplicates": [ ... ],
  "bundleSizes": [ { "bundleName": "ui.bundle", "bytes": 1234567 } ]
}
```

5. **loadPath 冲突**（与 Catalogue 写端联动）  
   在 `CatalogueWriter.BuildCatalog` 内对 `ToLoadPath` 做 **Dictionary 检测**；重复则 **构建失败**（硬错误）。Analyzer 可复用同一函数做预检。

**接入点**：

```text
BundleBuilder.BuildByMode
  → BuildPipeline.BuildAssetBundles
  → CatalogueWriter.Write
  → BundleBuildAnalyzer.Analyze(builds, manifest, setting)   // 新增
  → 写 report 到 bundleRoot/Reports/BundleBuildReport.json
  → BundleRuleMaker 窗口增加「上次构建报告」入口
```

### 3.4 B-Opt-2：Packer UI 与清单校验

| 功能 | 说明 |
|------|------|
| 报告 Tab | 展示冗余 Top N、包体大小、`loadPath` 冲突 |
| 构建前预检 | `Validate` 扩展：Custom 项空路径、重复 bundleName |
| 清单写入前 | 与拓扑排序同一 pipeline：`ValidateCatalog → SortDeps → Write` |

### 3.5 B-Opt-3（远期）：半自动共享包

**思路（仅探讨，不纳入本期排期）**：

1. 从 `redundantAssets` 生成建议列表 `shared_xxx.bundle`。  
2. 用户确认后，**第二轮** `RuleResolver` 注入额外 `AssetBundleBuild`，再 `BuildAssetBundles`。  
3. 原引用包改为依赖 shared 包（依赖 Manifest 自动表达）。  
4. 风险：包名变化、清单 entries 变化 → 需全量回归 19/19 + 新 Case。

### 3.6 工作量估算

| 步骤 | 预估 |
|------|------|
| B-Opt-1 Analyzer 核心 + Report JSON | 1.5～2 天 |
| loadPath 硬校验接入 CatalogueWriter | 0.5 天 |
| B-Opt-2 Packer 报告 Tab | 1 天 |
| 文档 + 样例报告 | 0.5 天 |

---

## 4. 推荐实施顺序

```text
Week 1
  ├─ A-1 BundleDependencyTopology + 单测
  ├─ A-2 CatalogueWriter 接入排序 + 环检测
  └─ loadPath 重复 → 构建失败（CatalogueWriter）

Week 2
  ├─ A-4 P1-3 依赖集成 Case + 三端冒烟
  ├─ B-Opt-1 BundleBuildAnalyzer + Report JSON
  └─ B-Opt-2 报告 Tab（只读）

Gate
  └─ ConcurrentLoad_* 19/19 三端不退化
```

**与主路线关系**：

- 拓扑排序：**P1 增强**（与 P1-3 依赖 Case 合并验收）  
- Bundle 优化报告：**P1/P2 打包侧**，不阻塞 B-1 异步 JSON 归档  
- 自动抽共享包：**P3 或更远期**

---

## 5. 涉及文件（预估）

| 文件 | 变更 |
|------|------|
| `BundleRuleConfig/Catalogue/BundleDependencyTopology.cs` | **新增** |
| `Editor/BundleBuilder/CatalogueWriter.cs` | 拓扑排序、loadPath 校验 |
| `Editor/BundleBuilder/BundleBuildAnalyzer.cs` | **新增** |
| `Editor/BundleBuilder/BundleBuildReport.cs` | **新增** 报告 DTO |
| `Editor/BundleBuilder/BundleBuilder.cs` | Build 后调用 Analyzer |
| `Editor/BundleRuleMaker/` | 报告 Tab（B-Opt-2） |
| `ResLoader/Catalogue/CatalogueReader.cs` | 可选读时校验 |
| `ResLoader/Bundle/BundleManager.cs` | 注释 + 可选 Debug 断言 |
| `Assets/Test/AB_Test/` | 依赖拓扑 Case（P1-3） |
| `Editor/Tests/` 或 `Tests/Editor/` | Topology 单测（若项目已有 EditMode 测试目录则放入） |

---

## 6. 风险与回退

| 风险 | 缓解 |
|------|------|
| 排序后仍有个案 Load 失败 | P1-3 专用跨包 Prefab Case；保留 Editor 对比 Manifest YAML |
| Analyzer 慢（大项目 GetDependencies） | 仅 Build 后跑一次；进度条；可配置「仅分析 >1MB 包」 |
| loadPath 硬校验阻断旧工程 | 首次开启只 Warning，下一版改 Error；文档写迁移 |
| 与 Unity 自带增量 Build 冲突 | Analyzer 只读，不改 Build 选项 |

**回退**：`CatalogueWriter` 增加 `BuildSetting.useTopologicalSort`（默认 true）；Analyzer 由菜单「可选执行」开关。

---

## 7. 文档维护

| 文档 | 更新内容 |
|------|----------|
| [CatalogueReference.md](./CatalogueReference.md) | §三 补充拓扑序约定；链到本文 |
| [MainRoadmap.md](./MainRoadmap.md) | P1 #6/#7 排期（**唯一状态源**） |
| [DesignGoalsAndImplementation.md](./DesignGoalsAndImplementation.md) | 打包器 / 清单 checklist 增行 |
| [Editor/README.md](../Editor/README.md) | BundleBuildAnalyzer 职责 |
| [DocumentIndex.md](./DocumentIndex.md) | 文档分类；**勿在本文再维护排期** |

---

## 8. 变更记录

| 日期 | 说明 |
|------|------|
| 2026-06-08 | 初稿：拓扑排序 Phase A + Bundle 构建优化 B-Opt-1/2 计划 |
| 2026-06-08 | **已实现**：`BundleDependencyTopology`、`CatalogueValidator/Writer`、`BundleBuildAnalyzer`、`BundleRuleMaker` 报告 Tab、`MyDependencyTest`、EditMode 单测 |
