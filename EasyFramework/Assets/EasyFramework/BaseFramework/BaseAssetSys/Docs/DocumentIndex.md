# ABSystem_Beta 文档索引

> **Docs/**：集中说明、验收标准、跨模块规划。  
> **路径约定**：跨文档链接使用仓库根相对路径 `Assets/vFramework/BaseFramework/BaseAssetSys/Docs/...`（Cursor / VS Code 可 Ctrl+点击跳转）。
> **各子文件夹/**：对应模块的详细设计，与代码同目录维护。  
> **新建文档前**：必读本文 **§ 文档分类与合并规则** 与 [MainRoadmap.md §7](./MainRoadmap.md#7-文档地图)。

---

## 文档分类与合并规则（新建前必读）

| 类型 | 唯一/主文档 | 写什么 | 禁止 |
|------|-------------|--------|------|
| **排期 / TODO / 阶段状态** | [MainRoadmap.md](./MainRoadmap.md) §4、§8 | P0–P3、延后项、代码 TODO 登记 | 新建 `*Roadmap*`、`*TODO*`、独立排期表 |
| **设计基线 + 模块进度** | [DesignGoalsAndImplementation.md](./DesignGoalsAndImplementation.md) 实现区 | 对照禁止区的 checklist | 改禁止区；重复 MainRoadmap 排期 |
| **专题设计（无排期表）** | `*Plan.md`、`*Planning.md` | 方案、API 草案、风险 | §「实施排期」带 ✅/❌ 状态列（应链 MainRoadmap） |
| **业务 API / 范例** | [BusinessApiUsageGuide.md](./BusinessApiUsageGuide.md) | 调用范式、Copy 代码 | 总排期、长篇设计 |
| **引用计数附件** | [RefCountAppendix.md](./RefCountAppendix.md) | 逐步 Trace、三层计数 | 替代 MainRoadmap |
| **模块 README** | 各子目录 `README.md` | 职责、入口、链到 Docs | 总排期段落 |
| **测试归档** | `Assets/Test/**/集成测试归档.md` | JSON 基准、Case | 加载侧总排期 |
| **索引** | **本文** | 分类、阅读顺序 | — |

### 已有专题文档（勿重复新建）

| 文档 | 角色 | 排期在哪 |
|------|------|----------|
| [LoaderOptimizationPlan.md](./LoaderOptimizationPlan.md) | AssetReference、统一池、Ref Trace **设计** | MainRoadmap **P1.5** |
| [BusinessApiAndCdnPlanning.md](./BusinessApiAndCdnPlanning.md) | CDN / 异步 **设计** | MainRoadmap **B-2 ✅**；**阶段 C ✅** |
| [BundleBuildOptimizationAndTopologyPlan.md](./BundleBuildOptimizationAndTopologyPlan.md) | 拓扑 + 构建优化 **设计** | MainRoadmap **P1 #6/#7** |
| [CatalogueReference.md](./CatalogueReference.md) | 清单字段与二进制路径 | 已实现（VCAT v1）；加密见 **P3** |

### 新建文档检查清单

1. 在 `Docs/` 与 `DocumentIndex` 搜索关键词，是否已有同类文档可 **扩写**？  
2. 若含排期/TODO → **只写入 MainRoadmap.md**，专题文用「见 MainRoadmap §x」链接。  
3. 更新 **MainRoadmap §7 文档地图** 与 **本文必读表**（若新增常驻文档）。  
4. 代码内 `TODO` 注释 → 登记 **MainRoadmap §8**，勿新建 `TODO.md`。

Cursor 自动化：见项目 `.cursor/rules/documentation-governance.mdc` 与 `.cursor/skills/vframework-documentation/SKILL.md`。

---

## 必读（Docs）

| 文档 | 用途 |
|------|------|
| **[MainRoadmap.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/MainRoadmap.md)** | **方向 + 排期 + TODO + 测试门禁（唯一总纲）** |
| [DesignGoalsAndImplementation.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/DesignGoalsAndImplementation.md) | 设计基线（禁止区）+ 模块实现进度 |
| [BusinessApiAndCdnPlanning.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/BusinessApiAndCdnPlanning.md) | CDN / 异步扩展设计（阶段 C 运行时 ✅） |
| [StageCFreezeCR.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/StageCFreezeCR.md) | 阶段 C 封板 Code Review 交付物 |
| [BusinessApiUsageGuide.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/BusinessApiUsageGuide.md) | 业务侧加载/卸载范式（含 [§5.6 Bundle LRU](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/BusinessApiUsageGuide.md#bundle-lru-unload)） |
| [RefCountAppendix.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/RefCountAppendix.md) | **引用计数附件**：API 逐步追踪、三层计数、代码链路 |
| [LoaderOptimizationPlan.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/LoaderOptimizationPlan.md) | 加载侧优化 **设计**（排期 → MainRoadmap P1.5） |
| **本文 § 业务场景总结** | 模块/Prefab/Ref/依赖/路由 **场景速查** → [DesignGoals §业务场景总结](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/DesignGoalsAndImplementation.md#business-scenarios) |
| [CatalogueReference.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/CatalogueReference.md) | 清单 `entries` / `bundles`、[`resourcePriority`](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/CatalogueReference.md#resource-priority) |
| [BundleBuildOptimizationAndTopologyPlan.md](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/BundleBuildOptimizationAndTopologyPlan.md) | 依赖拓扑 + 构建优化 **设计** |
| [BuilderEditorBlueprint.html](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/BuilderEditorBlueprint.html) | 打包窗口 **Builder** 页签 UI 原型 |
| [ReportEditorBlueprint.html](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/ReportEditorBlueprint.html) | **Reporter** 页签 UI 设计原型（Editor 内无打开按钮，仅文档参考） |

---

## 按模块（子文件夹）

| 目录 | 文档 | 代码入口 |
|------|------|----------|
| `AbstractAssets/` | [README.md](../AbstractAssets/README.md) | `AbstractResource.cs` |
| `ResLoader/` | [README.md](../ResLoader/README.md)、[LoaderDesignGuide.md](../ResLoader/LoaderDesignGuide.md) | `Business/` `Bundle/` `Catalogue/` `ContentPackage/` `Router/` `Cdn/` |
| `BundleRuleConfig/` | [README.md](../BundleRuleConfig/README.md) | `BuildSetting`、`AssetCatalog`、`AssetCatalogBinaryCodec` |
| `Editor/` | [README.md](../Editor/README.md) | `BundlePacker`、`BundleBuilder`、`BundleReporter` |
| `AssetPool/` | `PrefabPool.cs`、`PrefabPoolManager.cs` | [LoaderOptimizationPlan](./LoaderOptimizationPlan.md)、[业务API §5.4](./BusinessApiUsageGuide.md) |
| `BaseLogSys/` | `AssetRefTraceLogger.cs` | Ref Trace；[LoaderOptimizationPlan §4](./LoaderOptimizationPlan.md) |
| `ABSystemTester/`（历史） | [ABSystem_BetaTestCases.md](../../../../BaseLayer/ToDelete/ABSystemTester/ABSystem_BetaTestCases.md) | [ABLoadSmokeTest.cs](../../../../BaseLayer/ToDelete/ABSystemTester/ABLoadSmokeTest.cs)（手测） |
| `Assets/Test/AB_Test/` | [集成测试归档.md](../../../../Test/AB_Test/集成测试归档.md) | 双 Runner 门禁 |
| `Assets/Test/comprehensiveTest/` | [综合测试归档.md](../../../../Test/comprehensiveTest/综合测试归档.md) | 池 / EventBus 手测 |

> **范围说明**：本索引仅覆盖 **BaseAssetSys（资源打包/加载）**。全局入口 **BaseGameRoot** 为独立子系统，文档见 [BaseGameRoot/README.md](../../BaseGameRoot/README.md) 与 [FrameworkDesign.md](../../../Docs/Overview/FrameworkDesign.md) §4.1，**不在此维护排期**。

---

## 外部参考（不改本项目方向）

| 文档 | 用途 |
|------|------|
| [ResourceSystemDesignGuide.md](../../../Resources/ResourceSystemDesignGuide.md) | Unity 资源系统通用对照 |
| [ApproachComparisonAndLearningGuide.md](../../../../BaseLayer/ToDelete/ApproachComparisonAndLearningGuide.md) | 学习教材（非 ABSystem 排期） |
| [ProjectGoals.md](../../../Docs/Overview/ProjectGoals.md) | 游戏产品目标（非 ABSystem 排期） |
| [StandaloneAndResourceHotfixGuide.md](../../../Docs/Guides/StandaloneAndResourceHotfixGuide.md) | 单机 / 只热更资源业务接入 |
| [Docs/README.md](../../../Docs/README.md) | vFramework 文档总索引 |

---

## 阅读顺序

1. **[MainRoadmap.md](./MainRoadmap.md)** → 做什么、排期、TODO  
2. **DesignGoals 实现细节** → 禁止区 + 进度  
3. **BusinessApiUsageGuide** + **RefCountAppendix** → 业务用法与计数  
4. **专题 Plan**（按需）→ Loader / CDN / Bundle 优化 **设计**  
5. **集成测试归档** → 验收 JSON  

---

## 文档维护约定

- 禁止修改 **DesignGoalsAndImplementation.md** 禁止修改区域。  
- **排期、阶段状态、代码 TODO** → 只维护 **MainRoadmap.md**。  
- 专题 Plan → 设计-only；状态改 MainRoadmap。  
- 子模块 README → 职责 + 链接，不写总排期。
