# ChunkTest 迭代记录

> 原 `TODO.md` 已完成第一波（P0）与部分第二波（P1）后归档删除。
> 本文档记录**每次迭代的改动、思路与验收**；未完成项在 [Backlog](#backlog) 维护。

---

## 迭代 1 — 框架地基（P0 + 部分 P1）

**日期**：2026-05-29  
**目标**：完成 TODO 第一波，解锁多 Pipeline / 双窗口 / 可观测 / 邻居读。

### 完成项

| 编号 | 内容 | 关键文件 |
|---|---|---|
| A1 ✅ | 打包缓存路径 + Envelope + Binary | （前序迭代已完成） |
| A2 ✅ | `ChunkData` 多 Payload 槽位 | `ChunkData.cs` |
| A3 ✅ | `ChunkSettings` 改 `init` 属性 + 对象初始化器 | `ChunkConfig.cs` |
| A4 ✅ | 生命周期事件 | `ChunkManager.cs` |
| A5 ✅ | `ChunkCoord.Offset` + `TryGetNeighbor` + 四/八邻常量 | `ChunkData.cs`, `ChunkUtil.cs`, `IChunkManager.cs` |
| B2 ✅ | `IChunkContentReader` + Mesh 接缝读邻居 | `IChunkContentReader.cs`, `MeshHeightmapPresenter.cs` |
| B4 ✅ | `ChunkProfilerMarkers` | `ChunkProfilerMarkers.cs`, Manager/Pipeline/Presenter/Generator |
| B5 ✅ | README §0.5 扩展确认表 | `README.md` |
| — | Pipeline 按 `ChunkActivationLevel` 分支（Generated 不 Present） | `ChunkContentPipeline.cs` |
| — | `ChunkSessionProfiler` 会话日志 | `Test/Debugger/` |
| — | TempData gitignore | 根 `.gitignore` |

### 思路摘要

1. **P0 先修契约，不加玩法**：Settings/init、事件、邻居 API 都是零破坏或低破坏扩展，为 C8/C9 铺路。
2. **Generated / Rendered 拆开**：双窗口 LoadSource 早已存在，瓶颈在 Pipeline 一步 Present；现在在 `ChunkContentPipeline` 按 `RequestedLevel` 分支，远圈只挂 payload。
3. **邻居读正式化**：`MeshHeightmapPresenter` 优先读邻居 `HeightmapPayload`，fallback 才 FBM 纯函数——为将来玩家改地形（C1）打基础。
4. **可观测**：ProfilerMarker + Session JSON，便于 AI/人工对比性能。

### 验收

- [ ] Editor Play `TestScene`：移动加载/卸载正常
- [ ] `UseDualWindow=true`：远圈 `chunksGenerated` > 0 且无对应 Mesh（`ShowChunkDebugger`）
- [ ] 订阅 `OnChunkLoaded` 能收到 Rendered 事件
- [ ] `TryGetNeighbor(coord, 1, 0, Rendered, out east)` 可用
- [ ] Unity Profiler 可见 `ChunkManager.Tick` / `ChunkPipeline.Load` 等

---

## 迭代 2 — 框架基础层抽离

**日期**：2026-05-29  
**目标**：把项目从玩法原型收束为 Chunk 框架基础层，后续生成算法、生态规则、特殊序列化作为上层游戏模块接入。

### 完成项

| 编号 | 内容 | 关键文件 |
|---|---|---|
| F1 ✅ | Pipeline 描述与注册表 | `IChunkPipelineDescriptor.cs`, `IChunkPipelineRegistry.cs`, `ChunkPipelineRegistry.cs` |
| F2 ✅ | Pipeline 依赖拓扑排序 | `ChunkPipelineRegistry.cs`, `ChunkManager.cs` |
| F3 ✅ | 预算抽象 | `IChunkContentBudget.cs`, `ChunkSettingsContentBudget.cs` |
| F4 ✅ | 统计出口 | `IChunkContentStatsProvider.cs`, `ChunkManager.CaptureStats()` |
| F5 ✅ | 内容编辑 dirty 闭环 | `IChunkContentEditor.cs`, `ChunkData.cs`, `ChunkContentPipeline.cs`, `ChunkManager.TryEditChunkContent` |
| F6 ✅ | Ticket 化加载 | `ChunkLoadTicket.cs`, `TicketChunkLoadSource.cs`, `ChunkManager.SubmitLoadTicket` |

### 思路摘要

1. **框架只做边界，不做玩法规则**：Editor、Budget、Stats、Pipeline Descriptor 都是基础层能力；具体挖矿、建造、生物群系仍在上层实现。
2. **Pipeline 顺序显式化**：生态层依赖地形层不再靠 `pipelines.Add` 的手写顺序，而由 descriptor 的 `DependsOn` 表达。
3. **预算来源可替换**：当前预算来自 `ChunkSettings`，未来可替换为根据 FPS/设备档位动态调节的实现。
4. **编辑闭环只到 dirty**：基础层负责 payload 查询、编辑调用、dirty 标记与 unload 回写；是否立即重建表现由上层策略决定。

### 验收

- [ ] Editor Play `TestScene`：地形、植物、动物 Pipeline 顺序仍正确
- [ ] `SubmitLoadTicket` / `RemoveLoadTicket` 能触发加载与卸载队列变化
- [ ] `CaptureStats()` 返回 loaded/generated/rendered/queue/ticket 统计
- [ ] `TryEditChunkContent` 对已有 payload 返回 true 并触发 `OnChunkContentEdited`
- [ ] Unity Console 无编译错误

---

## Backlog（未完成 · 按优先级）

| 编号 | 名称 | 优先级 | 说明 |
|---|---|---|---|
| B1 | `LoadAsync` 真异步 + in-flight 节流 | P1 | 配合 `MaxConcurrentMeshJobs`（C7） |
| B3 | MeshHeightmap 对象池 | P1 | R 16+ 必需 |
| C1 | 具体内容编辑器实现 | P2→P1 | `IChunkContentEditor` 抽象与 dirty 闭环已完成 |
| C9 | 玩家放置静态对象 | P2 | 依赖 C1 |
| C8 | `EntityChunkBinder` 生物归属 | P2 | 依赖 A4 ✅ |
| C6 | Pipeline 依赖拓扑排序 | Done | 已由 `ChunkPipelineRegistry` 完成 |
| C3 | 单元测试集 | P2 | 开源前 |
| C2 | Job/Burst | P2 | Profile 触发 |
| C4 | `IChunkSpatialIndex` | P2 | 按需 |
| C5 | 多 World / 存档槽 | P2 | 产品流程 |
| C7 | 动态预算器 | P2 | `IChunkContentBudget` 已有，缺动态实现 |

### 产品流程（未立项）

- 独立开始场景（Main Menu）
- 存档槽选择；每 slot 独立 `CacheSubfolder`（如 `Saves/{slotId}/`）

### 长期留路（不立项）

- 全体素世界、联机权威、DistanceManager 高级 Ticket 策略（见 `MINECRAFT_REFERENCES.md`）

---

## 迭代模板（复制用于下一轮）

```markdown
## 迭代 N — 标题

**日期**：  
**目标**：

### 完成项
| 项 | 说明 | 文件 |

### 思路摘要
1. …

### 验收
- [ ] …
```
