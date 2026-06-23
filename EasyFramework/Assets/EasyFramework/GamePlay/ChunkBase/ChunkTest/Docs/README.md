# ChunkTest（区块框架基础层）

一套"像 MC 一样的区块加载/卸载/存档"框架基础层。

目标不是做出 MC，也不是绑定某个具体游戏规则，而是 **把"区块系统"拆成可以独立替换的几层契约**：换算法 / 换存储格式 / 换渲染方式 / 换加载策略时，**不重写主体**。具体地形、生物群系、动植物规则、建造规则应作为上层游戏模块接入。

---

# 第一部分 · 设计思想

## 0. 设计原则（置顶 · 改任何代码前先读）

这套架构落到代码里能稳定运行、能持续扩展，是因为下面这些原则被**严格执行**。后面所有章节、所有"扩展指南"，本质都是这些原则在具体场景下的展开。**与原则冲突的改动，宁可先停手讨论，也不要继续写下去。**

| # | 原则 | 一句话 | 本工程中的体现 |
|---|---|---|---|
| **P1** | **RAII**（Resource Acquisition Is Initialization） | 获取资源的同时绑定释放路径，**绝不留"忘记释放"的可能** | `ChunkData.AttachContent` ↔ `TryDetachContent`、`Presenter.Present` ↔ `Dismiss`、`Storager.SaveAsync` 入队即承诺最终落盘 |
| **P2** | **YAGNI**（You Aren't Gonna Need It） | **未被真实需求驱动**的抽象/字段/参数一律不加；要扩展时再加 | `IChunkContentPipeline` 没强制级别 API、`Generated` 级别留为扩展点、序列化器只有 JSON 一个实现、`pendingUpgradeQueue` 在真正用到双窗口时才会跑起来 |
| **P3** | **SRP**（单一职责） | 每个类/接口只回答**一个**问题 | Generator=怎么算 / Storager=怎么存 / Serializer=怎么编码 / Presenter=怎么出现在场景 / LoadSource=哪些 chunk 该活 / Pipeline=怎么串这些 / Manager=怎么调度 |
| **P4** | **DIP**（依赖倒置） | 高层只依赖**接口**，不依赖具体类 | Manager 只看见 `IChunkContentPipeline` / `IChunkLoadSource` / `IChunkContentBudget`；Pipeline 只看见 `IChunkContentGenerator<T>` 等 |
| **P5** | **OCP**（对扩展开放、对修改封闭） | 加新东西**只新增文件**，不改主体；改主体的 PR 应当极少 | 加新 LoadSource 0 行主体改动；加新 Pipeline 通过 descriptor 注册并声明依赖；§14 速查表里每一行都是"改 1–2 处" |
| **P6** | **ISP**（接口隔离） | 接口要**窄**，不让实现者被迫实现自己用不上的方法 | 接口全部保持 1–3 个方法；`IChunkContentSerializer<T>` 只有 `byte[]`↔`T`，不掺 IO；`IChunkLoadSource` 只有一个 `CollectTargetChunks` |
| **P7** | **Composition Root 唯一化** | 整个工程**只有一个地方**允许 `switch case` 具体类型 | `ChunkManager.BuildPipelines` 只负责注册 descriptor，执行顺序交给 `ChunkPipelineRegistry`；`BuildLoadSources` 只组装加载源 |
| **P8** | **数据与行为分离** | Payload 是纯数据（POCO），行为放在策略类 | `HeightColumnPayload` / `HeightmapPayload` 都是 `[Serializable] class` + 字段，**零方法**；所有逻辑都在 Generator/Presenter/Storager 里 |
| **P9** | **运行时引用 ≠ 序列化数据** | Unity 引用类型**绝不**进 Payload | `Transform / Mesh / GameObject / NativeArray` 一律走 Presenter 自己的 handle 挂在 `ChunkData` 内容槽；payload 只装 `int / float[] / string` 等可落盘字段 |
| **P10** | **失败显式化** | 业务层"取不到"是常态，不是异常 | `TryGetChunk` / `TryLoad` / `TryGetPayload<T>` / `TryDetachContent` 全部返回 `bool`，不抛异常；调用方按 `if (...) { ... }` 模式写 |
| **P11** | **Hot-path 零分配** | 每帧调用的代码不分配托管堆 | `ChunkSettings` 是 struct + 扁平字段直读；`targetCoordsBuffer / targetLevels / loadBatchBuffer` 全是复用容器；遍历用 `for + index` 而非 `foreach`（在 hot loop） |
| **P12** | **多源并联，HashSet 去重** | 横向扩展时不要引入"协调器"，让数据结构本身完成合并 | 多个 `LoadSource` 各自产坐标 → `HashSet` 自动去重 → 多源对同坐标取 `max(ActivationLevel)`；将来加 Pipeline、LoadSource 都遵循"互不知道彼此存在" |
| **P13** | **可观测优先** | 难调的系统必须有调试出口 | `IChunkContentStatsProvider`、`ChunkLog.Verbose`、队列计数、`OnDrawGizmos` 区块线框、`ShowChunkDebugger` HUD |
| **P14** | **同步先行，异步按需** | 默认走同步、单线程主线程；只在被 profile 证明必须异步时再上 | Generator/Presenter 当前全同步；只有 `Storager.SaveAsync` 是异步（落盘 IO 是已证明的 hot point）；`IChunkContentPipeline` 留了 `LoadAsync` 默认实现走同步路径 |
| **P15** | **改 ≥ 3 个策略层 = 接口设计漏了** | 见全文末尾的告诫 | 如果某次改动同时碰 Generator + Storager + Presenter + Manager，**回头审接口**，不要硬改 |

> **YAGNI 的实战边界**：本工程允许"已有明确需求的扩展点提前留位"（例如 `pendingUpgradeQueue` 为双窗口预留、`pipelines` 用 `List<>` 为对象层预留），但**不允许凭空猜测的抽象层**（例如"以后可能要换 ECS"就提前抽 World 接口）。判断标准很简单：**这个扩展点对应的真实需求，已经在 roadmap 上有名字了吗？**

## 0.5 未来扩展确认表

| 扩展方向 | 接口已支持 | 下一迭代 | 备注 |
|---|---|---|---|
| Chunk 分层（Generated / Rendered） | ✅ | — | Pipeline 已按级别分支 |
| 多 Pipeline 并行 | ✅ | — | A2 多槽位已完成 |
| Pipeline 描述 / 依赖排序 | ✅ | 业务注册扩展 | `IChunkPipelineDescriptor` + `IChunkPipelineRegistry` |
| 邻居 Payload 读取 | ✅ | — | `IChunkContentReader` |
| 生命周期事件 | ✅ | — | `OnChunkLoaded` / `OnChunkUnloaded` / `OnChunkLevelChanged` |
| 预算接口 | ✅ | 动态预算器 | `IChunkContentBudget`；默认从 `ChunkSettings` 快照读取 |
| 统计接口 | ✅ | Profiler 深接入 | `IChunkContentStatsProvider` / `ChunkContentStats` |
| 内部 Cell 化 | ✅ | — | Cell 是 Payload 内部细节 |
| 多单位寻路 | ✅ | B2 + 第二条 Pipeline | HPA*，单位作 LoadSource |
| 体素 / CubeMesh | ⚠️ 远期 | 不立项 | 接口留路；见 [`ITERATIONS.md`](ITERATIONS.md) |
| 植被 / 动物出生点 | ✅ | Biome 规则 | `VegetationPayload` / `AnimalSpawnPayload` 独立 Pipeline |
| Biome / 河流 / 湖泊 | ⚠️ | 下一轮设计 | 建议先做 `BiomeMapPayload`，再驱动地形与生态 |
| Job/Burst | ⚠️ | B1 + C2 | Profile 触发 |
| 挖矿 / 建造 | ⚠️ | C9 实现器 | 已有 `IChunkContentEditor<TPayload>` + dirty 标记闭环，尚未接具体编辑规则 |
| Ticket 化加载 | ✅ | 业务接入 | `ChunkLoadTicket` + `TicketChunkLoadSource` + `ChunkManager.SubmitLoadTicket` |
| 八叉树空间索引 | ❌ | C4 | 缺 `IChunkSpatialIndex` |
| 开始场景 + 存档槽 | ❌ | 产品流程 | 见 ITERATIONS  backlog |
| ECS 单位 | ✅ | — | 业务层自由用，与 chunk 调度正交 |

迭代过程与思路见 [`ITERATIONS.md`](ITERATIONS.md)（原 `TODO.md` 已归档删除）。

---

## 1. 一句话总览

> **数据按"形态"分层，业务按"职责"解耦，依赖倒置；管理器只负责统筹生成器与存储器；区块满足哈希性，读写友好。**

```
ChunkManager（纯调度，不知道内容）
   ├─ List<IChunkLoadSource>             ← 决定"哪些 chunk 此刻该活跃"
   │                                       多源并联，HashSet 去重，对同坐标取 max 级别
   │                                       TicketChunkLoadSource 处理临时/业务驱动加载
   ├─ IChunkContentBudget                ← 决定"这一帧/这一时刻允许做多少工作"
   ├─ IChunkPipelineRegistry             ← 决定"有哪些 Pipeline，以及依赖顺序"
   ├─ ChunkLoadQueue / ChunkLoadThrottler（Utils/Async）← 决定"今天每帧能 Load/Unload 几个" + "谁先谁后"
   └─ IChunkContentPipeline              ← 决定"区块里的内容怎么来 / 走到哪一步"
        ├─ IChunkContentGenerator<T>       ← 纯计算：生成 payload
        ├─ IChunkContentStorager<T>        ← 纯 IO：读盘 / 写盘
        │     └─ IChunkContentSerializer<T>  ← 字节流编解码（默认 JSON over UTF-8）
        └─ IChunkContentPresenter<T>       ← 纯场景副作用：实例化 / Mesh / Destroy
```

> Manager **不知道** Generator/Storager/Presenter 的存在；
> Pipeline **不知道**自己被组装在哪个 Manager 上；
> 三件套 + 序列化器 **互相不知道彼此**；
> 每个 LoadSource **也不知道**别的 LoadSource 存在。

## 2. 数据按"形态"分三层

| 数据层 | 代表类型 | 生命周期 | 职责 |
|---|---|---|---|
| **配置（不可变快照）** | `ChunkConfig` (MonoBehaviour) → `ChunkSettings` (struct) | 进入场景时一次性构建 | 给所有层提供"此世界长什么样"的只读字段，hot-path 零装箱直读 |
| **运行时数据（主对象）** | `ChunkData` + `ChunkCoord` + `ChunkBounds` + `ChunkState` | 区块从加载到卸载 | 区块的"身份与状态"。聚合坐标、边界、级别、payload 句柄；内容槽用 `ChunkContentKey(Type + LayerKey)` 支持同类型多层附加数据 |
| **存储序列化数据（Payload）** | `[Serializable] class HeightColumnPayload / HeightmapPayload` | 跨进程跨帧持久 | **可落盘的最小信息**：chunkId + 整数/数组。Unity 引用类型（Transform/Mesh/GameObject）一律不入 payload |

**关键约定**：运行时数据 ≠ 序列化数据。Unity 资源走 Presenter 自己的 handle 挂在 `ChunkData` 的内容槽上，永远不进 payload，序列化时不会牵连场景。

## 3. 业务按"职责"解耦

每一类只做**一件事**，互不知道彼此存在：

| 职责 | 接口 | 输入 → 输出 | 特点 |
|---|---|---|---|
| **加载源** | `IChunkLoadSource` | (玩家/锚点/...) → `HashSet<ChunkCoord>` | 横向并联，多源取并集 |
| **加载级别源** | `IChunkLoadLevelSource` | `ChunkCoord` → `ChunkActivationLevel` | Ticket 等复合源可对不同坐标返回不同级别 |
| **Pipeline 描述** | `IChunkPipelineDescriptor` | id / payload type / dependsOn / factory | 描述内容层，不参与调度 |
| **Pipeline 注册表** | `IChunkPipelineRegistry` | descriptors → sorted pipelines | 统一处理依赖顺序 |
| **预算** | `IChunkContentBudget` | settings/device/runtime → limits | Manager 读取预算，不知道预算来源 |
| **统计** | `IChunkContentStatsProvider` | runtime state → `ChunkContentStats` | HUD / 日志 / 自动化测试统一出口 |
| **生成** | `IChunkContentGenerator<T>` | `ChunkCoord, Settings` → `TPayload` | 纯函数，无 IO |
| **存储** | `IChunkContentStorager<T>` | `chunkId` ↔ `TPayload` | 纯 IO，不感知场景 |
| **序列化** | `IChunkContentSerializer<T>` | `TPayload` ↔ `byte[]` | 与介质正交，可换 JSON/Binary |
| **场景化** | `IChunkContentPresenter<T>` | `TPayload` → handle (任意) | 仅 Unity 副作用 |
| **内容编辑** | `IChunkContentEditor<T>` | `ChunkData + TPayload + EditContext` → `bool dirty` | 只抽象编辑规则，Manager 负责标 dirty |
| **流水线** | `IChunkContentPipeline` | 把以上四件套串成一条 Load/Unload | Manager 看到的唯一抽象 |

依赖关系**单向向下**：Manager → Pipeline → (Generator/Storager/Presenter) → (Serializer)。**没有反向引用**。

## 4. 管理器：只统筹，不计算，不渲染

`ChunkManager` 只做框架调度，不做内容算法：

1. **Composition Root**：在 `Awake` 里注册 `IChunkPipelineDescriptor`，由 `ChunkPipelineRegistry` 按依赖排序创建 Pipeline；同时组装 LoadSources
2. **区块字典**：`Dictionary<long, ChunkData>` + `LoadChunk/UnloadChunk/TryGetChunk`
3. **窗口刷新**：让每个 `LoadSource` 各自产出目标坐标 → 自动去重 → 对比当前字典 → 入"加载/升级/卸载"三条队列
4. **每帧切片消费**：从队列出队最多 N 个 → 调 `Pipeline.Load/Unload`；预算来自 `IChunkContentBudget`
5. **通用编辑入口**：`TryEditChunkContent<T>` 调用上层 editor，若 payload 改变则标 dirty，Unload 时由 Pipeline 回写

**生命周期事件**（实例级，非 static）：`OnChunkLoaded`（Rendered 就绪）、`OnChunkUnloaded`（Unload 前）、`OnChunkLevelChanged`、`OnChunkContentEdited`。回调内**禁止**再触发 Load/Unload。邻居查询：`TryGetNeighbor(coord, dx, dz, minLevel, out neighbor)`。

## 5. 区块的"哈希性"与"读写友好"

```csharp
public readonly struct ChunkCoord {
    public int X { get; }
    public int Z { get; }
    public long Id => ((long)X << 32) | (uint)Z;   // 位运算唯一编码
}
```

- **哈希性**：`Id` 完全由坐标推导，无并发竞争、无版本号，可直接做 `Dictionary<long, ChunkData>` 的键
- **读写友好**：
  - **读**：`Manager.TryGetChunk` / `Storager.TryLoad` 都按 `Id` O(1) 查表
  - **写**：缓存命中先于生成（`storager.TryLoad` 命中即跳过 `generator.Generate`）；缓存写入异步落盘（`SaveAsync`），不阻塞主线程
  - **去重**：多 `LoadSource` 横向并联时直接靠 `HashSet<ChunkCoord>` 自然去重，不需要中央协调

---

# 第二部分 · 运行时流程（一帧到底发生了什么）

## 6. 启动时（Awake / Start）

```
Awake:
  RefreshSettings()                             // ChunkConfig → ChunkSettings 不可变快照
  BuildPipelines(pipelineType)                  // 唯一允许的"具体类型 switch"，可挂多条并行 Pipeline
  InitPlayerReference()
  BuildLoadSources()                            // 单窗口 / 双窗口 + 可选 PinnedAnchor

Start:
  RefreshChunksAroundPlayer(force: true)        // 冷启动一次，铺出第一批目标
```

`BuildPipelines` 做的就是依赖倒置的"接线"：先注册描述，再交给注册表排序创建。

```csharp
case ContentPipelineType.MeshHeightmap: {
    var heightGen = new FbmHeightmapGenerator();
    pipelineRegistry.Register(new ChunkPipelineDescriptor(
        "terrain.heightmap",
        typeof(HeightmapPayload),
        () => new ChunkContentPipeline<HeightmapPayload>(
            heightGen,
            new FileChunkContentStorager<HeightmapPayload>(...),
            new MeshHeightmapPresenter(heightGen, this),
            "terrain.heightmap")));
    RegisterPrefabEcologyPipelines("terrain.heightmap");
}
```

注意：`heightGen` 同时被 Generator 与 Presenter 共享，是为了让 Presenter 在 chunk 边界采样邻 chunk 的 FBM 噪声以保证法线接缝一致。植物/动物管线通过 `IChunkContentReader` 读取同 chunk 的 `HeightmapPayload`，依赖顺序由 descriptor 的 `DependsOn` 保证。

## 7. 每帧（LateUpdate）

```
LateUpdate:
  RefreshChunksAroundPlayer(force: false)       // 玩家走出当前 chunk 才重建队列
  TickPendingLoadUnload()                       // 按帧配额从队列出队，调 Pipeline
```

### 7.1 `RefreshChunksAroundPlayer`：算"当前应该有哪些 chunk 活着"

```
1. targetCoords = ⋃ src.CollectTargetChunks() for src in loadSources    // HashSet 自动去重
2. targetLevels[coord] = max(src.ActivationLevel) for each src that contributed coord
3. 重建三条队列（按距离玩家的 sqr 距离做优先级）：
     - 不在 chunks 字典 → pendingLoadQueue        （新加载，近的先生长）
     - 在字典但 requested > current → pendingUpgradeQueue   （升级路径，预留）
     - 在字典但已不在 targets → pendingUnloadIds   （卸载，远的先卸）
```

只有玩家**进入新的 chunk** 或调用 `ForceRefresh()` 时才会重建队列；玩家在原 chunk 内移动时这一帧直接跳过。

### 7.2 `TickPendingLoadUnload`：按配额消费队列

每帧最多处理 `MaxLoadPerFrame` / `MaxUnloadPerFrame` 个，顺序为：

```
1. Unload  ← 优先腾出资源（GameObject / Mesh）
2. Upgrade ← 升级已存在的 chunk（占用 Load 配额，优先于新加载）
3. Load    ← 新加载远方目标
```

这套节流是 R 较大时不卡死主线程的关键。

### 7.3 Ticket 化加载

Ticket 是业务层提交给 `ChunkManager` 的临时加载请求，用于任务点、AI 单位、远程编辑、传送预热等不适合写进 Inspector 的运行时需求。

```csharp
int ticketId = chunkManager.SubmitLoadTicket(worldPosition, radius: 2, ChunkActivationLevel.Generated);
// 业务结束后撤销；如果没有其它 LoadSource 覆盖，这些 chunk 会进入卸载队列。
chunkManager.RemoveLoadTicket(ticketId);
```

内部流程是：`SubmitLoadTicket` 写入常驻的 `TicketChunkLoadSource` → 强制刷新目标集合 → `IChunkLoadLevelSource.GetLevelForCoord` 为每个坐标返回请求级别 → 与玩家窗口、PinnedAnchor、双窗口源取最高级别。Ticket 只决定"保持哪些 chunk 到什么级别"，不参与内容生成、不保存业务对象，也不绕过现有队列和限流。

### 7.4 预算与统计

`IChunkContentBudget` 是加载预算来源的抽象。默认实现 `ChunkSettingsContentBudget` 从 Inspector 配置生成快照；以后要做"低配/高配档位"或"帧率过低自动降载"，只需要替换预算实现，不需要改 Manager 的队列逻辑。

`IChunkContentStatsProvider` 是框架统计出口，当前由 `ChunkManager` 实现。HUD、会话日志、自动化测试可以读取 `CaptureStats()`，避免散落访问 `PendingLoadCount`、`Chunks.Count` 等内部细节。

## 8. Pipeline 的 Load / Unload 编排

`ChunkContentPipeline<T>` 是把"先查存档→否则生成→存盘→按级别场景化"的核心流程**集中写一份**，三个具体策略只通过 `TPayload` 在编译期关联。多 Pipeline 时，每条 Pipeline 按自己的 `TPayload` 判断是否已 Generated / Rendered，避免地形完成后把植物/动物误判为完成。

Pipeline 现在先注册为 `IChunkPipelineDescriptor`，再由 `ChunkPipelineRegistry` 根据 `DependsOn` 拓扑排序。比如生态层依赖地形层：

```csharp
new ChunkPipelineDescriptor(
    "ecology.vegetation",
    typeof(VegetationPayload),
    () => new ChunkContentPipeline<VegetationPayload>(...),
    dependsOn: new[] { "terrain.heightmap" });
```

这让"BiomeMap → Terrain → Vegetation → AnimalSpawn"这类顺序成为框架元信息，而不是靠手写 `pipelines.Add(...)` 的位置记忆。

### 8.1 Load 流程

```
Pipeline.Load(chunk, settings):
  current = chunk.HasPayload<T>() ? (HasPresenterHandle<T> ? Rendered : Generated) : None
  if current >= chunk.RequestedLevel: return

  payload = storager.TryLoad(chunk.Id)                  // ① 优先走存档（O(1) 命中）
  if payload is null:
      payload = generator.Generate(chunk.Coord, settings)  // ② 缓存未命中：纯计算
      storager.SaveAsync(chunk.Id, payload)             // ③ 异步落盘，不阻塞主线程

  if chunk.RequestedLevel == Generated:
      chunk.AttachContent(payload, null)                // ④ 远圈只挂 payload
      return

  handle = presenter.Present(chunk, payload, settings)  // ④ 场景化（GameObject/Mesh）
  chunk.AttachContent(payload, handle)                  // ⑤ 缓存 payload + handle 到 ChunkData
  chunk.SetCurrentLevel(Rendered)
```

**这正是你架构思想的精髓**：管理器根据加载策略动态加载/卸载；**已加载过的区块直接从存储器拿数据**，不重新计算。

### 8.2 Unload 流程

```
Pipeline.Unload(chunk, settings):
  payload, handle, dirty = chunk.TryDetachContent() // 取出 payload / handle / 编辑脏标
  dirty |= presenter.Dismiss(chunk, handle, ref payload, settings) // 销毁场景对象
  if dirty:
      storager.SaveAsync(chunk.Id, payload)     // 玩家改过地形才回写
  chunk.SetCurrentLevel(None)
```

地形是确定性 FBM 噪声 + 玩家不能改 → 当前 Presenter 永远返回 `dirty=false`，跳过回写。

### 8.3 编辑闭环

`IChunkContentEditor<TPayload>` 只表达"如何修改某类 payload"。`ChunkManager.TryEditChunkContent` 负责找到 chunk/payload、调用 editor、标记 dirty、触发 `OnChunkContentEdited`。真正的挖矿/建造规则仍由上层游戏实现。

```csharp
bool changed = chunkManager.TryEditChunkContent(
    coord,
    terrainEditor,
    new ChunkContentEditContext("mine", coord, localX, localY, localZ));
```

被标 dirty 的 payload 会在 Pipeline Unload 时回写；如果上层需要立即重建表现，可以监听 `OnChunkContentEdited` 后安排对应 chunk 的刷新策略。

### 8.4 Storager 内部的存档约定

```
File 路径：{CacheRoot}/{CacheSubfolder}/chunk_{chunkId}{suffix}.dat
内容    ：Envelope（CHKT 头）+ payload；Legacy 无头 JSON 仍可读
读      ：File.ReadAllBytes → Serializer.Deserialize(bytes, 0, len) → TPayload
写      ：Serializer.Serialize(payload) → byte[]
异步写  ：ConcurrentDictionary 入队 + AsyncAutoResetEventLite 唤醒 + UniTask.RunOnThreadPool 写盘
```

序列化器目前唯一实现是 `JsonChunkContentSerializer<T>`（基于 `JsonUtility`，落盘文件本质上仍是 UTF-8 编码的 JSON，可记事本打开调试）。要换格式只需新建一个 `IChunkContentSerializer<T>` 实现，**Storager 与 Pipeline 一行不动**。

---

# 第三部分 · 工程结构与具体实现

## 9. 目录结构

```
ChunkTest/
├── Chunk/
│   ├── Config/
│   │   └── ChunkConfig.cs                    # MonoBehaviour + 子配置 + ChunkSettings 快照
│   ├── Data/
│   │   ├── Core/
│   │   │   ├── ChunkActivationLevel.cs
│   │   │   ├── ChunkLoadTicket.cs            # Ticket 化加载请求
│   │   │   └── ChunkData.cs                  # ChunkCoord / Bounds / State / ChunkContentKey / 内容槽
│   │   └── Payloads/
│   │       └── ChunkSavableData.cs           # Heightmap / Vegetation / Animal 等可落盘 Payload
│   ├── Utils/                                # 纯工具，无业务策略
│   │   ├── Async/
│   │   │   ├── ChunkLoadQueue.cs             # 最小堆 + HashSet 去重（Manager 调度用）
│   │   │   └── ChunkLoadThrottler.cs         # 每帧 Load/Unload 配额切片
│   │   ├── Storage/
│   │   │   └── ChunkStorageEnvelope.cs       # 存档 CHKT 文件头 + Legacy 兼容
│   │   ├── ChunkLog.cs
│   │   ├── ChunkPriority.cs
│   │   ├── ChunkUtil.cs
│   │   ├── ChunkSettingsContentBudget.cs
│   │   ├── ChunkStoragePathResolver.cs
│   │   ├── ChunkPayloadValidator.cs
│   │   ├── ChunkProfilerMarkers.cs
│   │   └── SharedHeightmapMeshBuilder.cs
│   ├── Interfaces/
│   │   ├── IChunkManager.cs
│   │   ├── IChunkContentEditor.cs
│   │   ├── IChunkContentBudget.cs
│   │   ├── IChunkContentStatsProvider.cs
│   │   ├── IChunkPipelineDescriptor.cs
│   │   ├── IChunkPipelineRegistry.cs
│   │   ├── IChunkContentReader.cs
│   │   ├── IChunkLoadSource.cs
│   │   ├── IChunkContentPipeline.cs
│   │   ├── IChunkContentGenerator.cs
│   │   ├── IChunkContentStorager.cs
│   │   ├── IChunkContentSerializer.cs
│   │   └── IChunkContentPresenter.cs
│   └── Server/                               # 具体策略实现
│       ├── Managers/
│       │   └── ChunkManager.cs               # Composition Root + 调度
│       ├── LoadStrategies/                   # IChunkLoadSource（加载窗口，与内容管线正交）
│       │   ├── SquareChunkLoadSource.cs
│       │   ├── CircularChunkLoadSource.cs
│       │   ├── PinnedAnchorLoadSource.cs
│       │   ├── SimRadiusLoadSource.cs
│       │   ├── RenderRadiusLoadSource.cs
│       │   └── TicketChunkLoadSource.cs
│       ├── Pipelines/
│       │   ├── ChunkContentPipeline.cs
│       │   ├── ChunkPipelineDescriptor.cs
│       │   └── ChunkPipelineRegistry.cs
│       ├── Generators/
│       │   ├── HeightColumnGenerator.cs
│       │   └── FbmHeightmapGenerator.cs
│       │   ├── VegetationGenerator.cs
│       │   └── AnimalSpawnGenerator.cs
│       ├── Storagers/
│       │   ├── FileChunkContentStorager.cs
│       │   └── InMemoryChunkContentStorager.cs
│       ├── Serializers/
│       │   ├── JsonChunkContentSerializer.cs
│       │   └── HeightmapBinaryChunkContentSerializer.cs
│       └── Presenters/
│           ├── PrefabHeightColumnPresenter.cs
│           └── MeshHeightmapPresenter.cs
│           ├── VegetationPresenter.cs
│           └── AnimalSpawnPresenter.cs
└── Test/
    ├── Debugger/
    │   ├── ShowChunkDebugger.cs
    │   ├── ChunkStorageDebugger.cs
    │   ├── ChunkSessionProfiler.cs
    │   └── Log/
    ├── PlayerAndCarmera/
    └── ArtRes/
Docs/
├── README.md
├── ITERATIONS.md
└── MINECRAFT_REFERENCES.md
```

## 10. 配置（`ChunkConfig` + `ChunkSettings`）

`ChunkConfig`（MonoBehaviour）按"被谁消费"分成多个 `[Serializable]` 子配置，Inspector 自动呈现为可折叠分组：

| 子配置 | 关键字段 | 谁会读 |
|---|---|---|
| `ChunkBasicConfig` | `Size / MinY / MaxYInclusive` | **所有** Pipeline |
| `LoadWindowConfig` | `MaxRenderDistance` | 窗口形状 LoadSource |
| `TerrainNoiseConfig` | `WorldSeed / NoiseSmoothness` | 噪声驱动的 Generator/Presenter |
| `PrefabContentConfig` | `SpawnPrefabs / ChunkObjectParent` | Prefab 形态 Pipeline；ChunkObjectParent 被所有 Presenter 共享 |
| `VegetationSpawnConfig` | `EnableVegetation / Density / MaxInstancesPerChunk / Prefabs[]` | `VegetationGenerator` / `VegetationPresenter` |
| `AnimalSpawnConfig` | `EnableAnimals / Density / MaxAnimalsPerChunk / Prefabs[]` | `AnimalSpawnGenerator` / `AnimalSpawnPresenter` |
| `FileStorageConfig` | `EnableChunkObjectDiskCache / CacheRoot / CacheSubfolder` | File Storager |
| `PerformanceConfig` | `MaxLoadPerFrame / MaxUnloadPerFrame / UseDualWindow / SimRadius` | Manager |
| `DebugVisualizationConfig` | `DrawActiveChunkWireframe / LogPlayerEnterChunk / LogVerbose` | Manager / ChunkLog |

`ChunkConfig.ToSettings()` 把所有子配置合并为一个**扁平的只读** `ChunkSettings` struct，让 hot-path 直接 `settings.X` 读字段，零装箱。

## 11. 当前内置 Pipeline

| `ContentPipelineType` | Payload | Generator | Storager | Presenter | 适用规模 |
|---|---|---|---|---|---|
| `HeightColumnPrefab` | `HeightColumnPayload` | `HeightColumnGenerator`（Perlin 高度柱） | `FileChunkContentStorager` + `JsonChunkContentSerializer` | `PrefabHeightColumnPresenter`（每方块 1 GO） | 闭环演示，**R≤5** |
| `MeshHeightmap` | `HeightmapPayload` | `FbmHeightmapGenerator` | `FileChunkContentStorager` + `HeightmapBinaryChunkContentSerializer` | `MeshHeightmapPresenter`（Mesh + 共享材质 + 可选 MeshCollider） | 当前主线 |
| `MeshHeightmap` 附属 | `VegetationPayload` | `VegetationGenerator`（读 Heightmap） | `FileChunkContentStorager` + JSON，后缀 `_vegetation` | `VegetationPresenter`（静态 prefab） | 可选，受 `EnableVegetation` 控制 |
| `MeshHeightmap` 附属 | `AnimalSpawnPayload` | `AnimalSpawnGenerator`（读 Heightmap） | `FileChunkContentStorager` + JSON，后缀 `_animals` | `AnimalSpawnPresenter`（出生 prefab） | 可选，受 `EnableAnimals` 控制 |

### 11.1 计划中的 Pipeline（未实现）

| `ContentPipelineType`（预留） | Payload | Generator | Presenter | 说明 |
|---|---|---|---|---|
| `VoxelGreedyMesh`（待定名） | `VoxelPayload`（blockId 网格） | **`VoxelGenerator`** | `GreedyMeshPresenter` | 远期不立项；见 [`ITERATIONS.md`](ITERATIONS.md) |

体素生成器尚未落地；接入方式与现有 Pipeline 相同（Payload + Generator + Storager + Serializer + Presenter + `BuildPipelines` 加 case）。

## 12. 当前内置的 LoadSource

| 形状/语义 | 类 | 默认 ActivationLevel | 半径来源 |
|---|---|---|---|
| 方形窗口 | `SquareChunkLoadSource` | Rendered | `MaxRenderDistance` |
| 圆形窗口 | `CircularChunkLoadSource` | Rendered | `MaxRenderDistance` |
| 大圈仿真 | `SimRadiusLoadSource` | Generated | `SimRadius` |
| 小圈渲染 | `RenderRadiusLoadSource` | Rendered | `MaxRenderDistance` |
| 强制驻留 | `PinnedAnchorLoadSource` | Rendered | 锚点列表 + `pinnedAnchorPadding` |

`ChunkManager.BuildLoadSources` 的策略：

- `UseDualWindow = true` → `Sim + Render` 组合
- `UseDualWindow = false` → 按 `LoadWindowShape` 选 `Square` / `Circular`
- `pinnedAnchors` 非空 → 额外挂一个 `PinnedAnchorLoadSource`

`ChunkActivationLevel.Generated` 已在 LoadSource、Manager、ChunkData 与通用 Pipeline 内生效：远圈只生成/读取 payload，不实例化 GameObject；近圈升级到 Rendered 时才调用 Presenter。

## 13. 数据约定

### 13.1 区块身份与坐标

- `ChunkCoord(X, Z)`：语义身份
- `ChunkCoord.Id`：`(long)X << 32 | (uint)Z`，作 `Dictionary` 主键与存档 slot 索引
- `ChunkBounds`：`Coord + Settings` 派生，**不是身份**
- chunk 内部用 chunk-local（`localX/Z ∈ [0, Size)`，`localY = worldY - MinY`），转换走 `ChunkUtil`，禁止业务代码手算偏移

### 13.2 Payload 与运行时数据隔离

- Payload 是**可落盘的**最小信息（chunkId + 整数/数组）
- 运行时引用（Transform / Mesh / GameObject / NativeArray）**不进 Payload**，放在 Presenter 自己的 handle 里挂到 `ChunkData` 的内容槽
- 内容槽键为 `ChunkContentKey(PayloadType, LayerKey)`：
  - 默认 `LayerKey=""`，兼容当前 `HeightmapPayload` / `VegetationPayload` / `AnimalSpawnPayload`
  - 后续可用 `LayerKey` 支持同类型多层数据，例如 `HeightmapPayload@surface`、`HeightmapPayload@cave`、`ObjectPayload@static`
  - 当前常用查询仍是 `TryGetPayload<T>`，复杂度保持 O(1)

### 13.3 存档文件位置

| Pipeline | 存档形态 | 路径（Editor 默认） |
|---|---|---|
| `HeightColumnPrefab` | Envelope + JSON body | `Assets/ChunkBaseGame/ChunkTest/TempData/chunk_{id}.dat` |
| `MeshHeightmap` | Envelope + Binary body | `Assets/ChunkBaseGame/ChunkTest/TempData/chunk_{id}_terrain.dat` |
| `VegetationPayload` | Envelope + JSON body | `Assets/ChunkBaseGame/ChunkTest/TempData/chunk_{id}_vegetation.dat` |
| `AnimalSpawnPayload` | Envelope + JSON body | `Assets/ChunkBaseGame/ChunkTest/TempData/chunk_{id}_animals.dat` |

打包后 `CacheRoot=Auto` 时使用 `Application.persistentDataPath` 下同名子目录。

> **远期（未实现）**：独立开始场景 + 存档槽选择；每个 slot 使用独立 `CacheSubfolder`（如 `Saves/{slotId}/`），详见 [`ITERATIONS.md`](ITERATIONS.md) backlog。

---

# 第四部分 · 扩展指南（要做某件事改哪里）

## 14. 速查表

| 我想… | 修改 |
|---|---|
| 改一个数值（半径、噪声平滑度、限流配额…） | 只改 `ChunkConfig` Inspector 字段 |
| 换加载窗口形状 | 新建 `IChunkLoadSource` + 在 `LoadWindowShape` enum 加项 + Manager `BuildPlayerWindowSource` 加 case |
| 让某些 GameObject 所在 chunk 强制驻留 | Inspector 拖进 `Pinned Anchors`；运行时也可 `AddLoadSource(new PinnedAnchorLoadSource(...))` |
| 临时保持某个世界位置附近加载 | 调 `SubmitLoadTicket(worldPosition, radius, level)`，结束后 `RemoveLoadTicket(ticketId)` |
| 加入新类别加载源（任务点/队友/视锥/服务器下发） | 新建 `IChunkLoadSource` 实现 + 业务层 `chunkManager.AddLoadSource(...)`；不用动 enum |
| 启用双窗口（远圈仅 payload，近圈渲染） | Inspector 把 `UseDualWindow=true`；Pipeline 已按 payload 类型支持 Generated / Rendered |
| 换内容生成方式（含渲染） | 新建 Payload + Generator + Presenter + 注册 `IChunkPipelineDescriptor` |
| 配置植物/动物 prefab | `ChunkConfig` 的 `Vegetation` / `Animals` 分组填 prefab 表、权重、密度；Payload 只保存 prefabIndex 和位置 |
| 加一个生态附属层 | 新建 Payload + Generator + Presenter，注册 descriptor 并声明 `dependsOn` |
| 添加动态性能档位 | 实现 `IChunkContentBudget` 并调用 `SetContentBudget(...)` |
| 记录框架运行状态 | 读取 `IChunkContentStatsProvider.CaptureStats()` |
| 实现挖矿/建造规则 | 实现 `IChunkContentEditor<TPayload>`，通过 `TryEditChunkContent` 修改 payload |
| 换序列化格式（JSON → MessagePack / Protobuf / 自定义二进制） | 新建 `IChunkContentSerializer<TPayload>` + 组装行换一行 |
| 换存储后端（远程/SQLite/内存） | 新建 `IChunkContentStorager<TPayload>` 实现 + 组装行换一行 |
| 给现有 Pipeline 加新配置 | `ChunkConfig` 加 `[Serializable]` 子配置 + `ChunkSettings` 加字段 + `ToSettings()` 塞进去 |
| 改区块身份 / 坐标维度 | 改 `ChunkCoord` + `ChunkUtil.EncodeChunkId` + `ChunkBounds`，策略层不动 |

## 15. 加一个新 LoadSource

### 15.1 窗口形状源（替换/新增玩家窗口形状）

```csharp
// 1) Server/LoadStrategies/SphericalChunkLoadSource.cs
public sealed class SphericalChunkLoadSource : IChunkLoadSource
{
    private readonly Transform centerProvider;
    public SphericalChunkLoadSource(Transform t) { this.centerProvider = t; }
    public ChunkActivationLevel ActivationLevel => ChunkActivationLevel.Rendered;
    public void CollectTargetChunks(ChunkSettings settings, ICollection<ChunkCoord> results)
    {
        if (centerProvider is null) return;
        var center = ChunkUtil.WorldToChunkCoord(centerProvider.position, settings.Size);
        int r = settings.MaxRenderDistance;
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
            if (dx*dx + dz*dz <= r*r)
                results.Add(new ChunkCoord(center.X + dx, center.Z + dz));
    }
}

// 2) ChunkManager: enum LoadWindowShape 加一项
public enum LoadWindowShape { Square, Circular, Spherical }

// 3) ChunkManager.BuildPlayerWindowSource 加一个 case
LoadWindowShape.Spherical => new SphericalChunkLoadSource(centerProvider),
```

**就这三处**。

### 15.2 非窗口源（强制驻留 / 任务点 / 服务器下发列表）

```csharp
// 不用碰任何枚举
public sealed class QuestMarkerLoadSource : IChunkLoadSource
{
    private readonly IReadOnlyList<QuestMarker> markers;
    public QuestMarkerLoadSource(IReadOnlyList<QuestMarker> markers) { this.markers = markers; }
    public ChunkActivationLevel ActivationLevel => ChunkActivationLevel.Rendered;
    public void CollectTargetChunks(ChunkSettings settings, ICollection<ChunkCoord> results)
    {
        for (int i = 0; i < markers.Count; i++)
            if (markers[i].IsActive)
                results.Add(ChunkUtil.WorldToChunkCoord(markers[i].Position, settings.Size));
    }
}

// 业务层
chunkManager.AddLoadSource(new QuestMarkerLoadSource(questSystem.ActiveMarkers));
chunkManager.ForceRefresh();   // 立即生效
```

## 16. 加一个新 Pipeline（以"体素 + Greedy Mesh"为例）

体素是 **Generator 层的下一目标形态**：在 `Server/Generators/` 新增 `VoxelGenerator`（实现 `IChunkContentGenerator<VoxelPayload>`），按 chunk 坐标填充离散 blockId 网格；Present 侧用 Greedy Mesh 或逐方块 Mesh 出图。

| 问题 | 实现 |
|---|---|
| 区块数据本体？ | 定义 `VoxelPayload`（`[Serializable] class`，字段如 `chunkId` + `byte[] blocks`） |
| 怎么算？ | **`VoxelGenerator`**：`IChunkContentGenerator<VoxelPayload>`（纯计算，无 IO） |
| 怎么存/取？ | `FileChunkContentStorager<VoxelPayload>` |
| 怎么序列化？ | `JsonChunkContentSerializer<VoxelPayload>` 或自定义 Binary（体素体积大时优先 Binary） |
| 怎么"看见"？ | `GreedyMeshPresenter`：`IChunkContentPresenter<VoxelPayload>` |

最后在 `ChunkManager.BuildPipelines` 加一个 case 即可（或在已有 case 中追加 `pipelines.Add` 启用多形态）。**Manager 主体、其它 Pipeline、其它 LoadSource 全部不动。** 详见 §11.1、§14 速查表。

## 17. Biome / 水系 / 生态生成方法（讨论稿）

后续若要按噪声划分生物群系、湖泊、河流，并据此生成地形与动植物，建议新增一个**世界语义层**，不要把所有判断散落到地形、植物、动物各自的 Generator 里。

### 17.1 推荐数据顺序

```
BiomeMapPipeline
  -> BiomeMapPayload（温度/湿度/大陆性/侵蚀/水系/biomeId）

TerrainPipeline
  -> HeightmapPayload（读取 BiomeMap，决定山地/平原/湖底/河床高度）

VegetationPipeline
  -> VegetationPayload（读取 BiomeMap + Heightmap，决定树/草/石头）

AnimalPipeline
  -> AnimalSpawnPayload（读取 BiomeMap + Heightmap，决定动物出生点）
```

第一版可以先不单独落盘 `BiomeMapPayload`，让 `TerrainGenerator`、`VegetationGenerator`、`AnimalSpawnGenerator` 共享一个 `BiomeSampler` 纯函数；当规则复杂或需要调试可视化时，再把 BiomeMap 提升为独立 Pipeline。

### 17.2 BiomeSampler 应该输出什么

`BiomeSampler` 不负责实例化，也不负责存档，只负责把世界坐标变成"环境语义"：

| 字段 | 来源 | 用途 |
|---|---|---|
| `temperature` | 低频噪声 + 纬度/高度修正 | 决定雪地、森林、草原、沙漠 |
| `humidity` | 低频噪声 | 决定森林/草地/荒漠 |
| `continentalness` | 超低频噪声 | 决定海洋/大陆/湖泊概率 |
| `erosion` | 中频噪声 | 决定山脊、河谷、丘陵 |
| `riverMask` | 河流噪声或距离场 | 压低地形，生成河床 |
| `lakeMask` | 洼地/水位规则 | 决定湖泊 |
| `biomeId` | 上述字段分类结果 | 给地形/生态查表 |

### 17.3 地形如何读取

地形 Generator 不再只用一条 FBM 曲线，而是：

1. 对每个世界点采样 `BiomeSample`。
2. 根据 `biomeId` 选地形参数：基础高度、振幅、粗糙度、山脉权重。
3. 如果 `riverMask` 命中，沿河道压低高度并平滑两岸。
4. 如果低于水位或 `lakeMask` 命中，生成湖底/水面标记。
5. 输出 `HeightmapPayload`；未来可扩展为 `TerrainPayload`，同时包含 `heights + waterMask + biomeIds`。

### 17.4 植物和动物如何读取

植物/动物 Generator 不直接判断"这里是不是森林"，而是查配置表：

```
BiomeDefinition
  biomeId = Forest
  vegetationTable = Pine / Bush / Grass
  animalTable = Deer / Rabbit / Wolf
  densityMultiplier = ...
```

生成流程：

1. 读取同坐标的 `HeightmapPayload`。
2. 采样 `BiomeSampler` 或读取 `BiomeMapPayload`。
3. 用 `biomeId` 找 `BiomeDefinition`。
4. 检查高度、坡度、水面、密度。
5. 按权重选择 prefabIndex，写入 `VegetationPayload` / `AnimalSpawnPayload`。

### 17.5 配置演进建议

当前 `ChunkConfig.Vegetation` / `ChunkConfig.Animals` 是全局 prefab 表，适合第一版验证多管线。下一步应演进为：

- `BiomeDefinition[] Biomes`：每个群系独立配置地形参数、植物表、动物表。
- `WaterConfig`：水位、河宽、河床深度、湖泊概率。
- `BiomeSamplerConfig`：温度/湿度/大陆性/侵蚀噪声的 scale、seedOffset、权重。

不要把 prefab 引用写进 payload；payload 继续只存 `prefabIndex + position + rotation + scale`。如果改为按 biome 表索引，payload 可以存 `biomeId + prefabIndex`，Presenter 再从配置查 prefab。

## 18. 注意事项 & 已知约束

### 18.1 Prefab 形态的性能上限

`HeightColumnPrefab` 仅用于验证管线闭环，**1089 chunk 时不可用**：每方块一个 GameObject 必然 Transform/Renderer 爆炸 + Instantiate/Destroy 卡顿 + JSON 体积膨胀。

### 18.2 大半径必开限流

`PerformanceConfig.MaxLoadPerFrame` 推荐：

- R≤5：64
- R=10：32
- R=20：8–16

不开限流（设为 0/负数）会卡死主线程数秒。

### 18.3 prefabIndex 稳定性

`HeightColumnPayload.spawns[i].prefabIndex` 指向 `ChunkSettings.SpawnPrefabs` 数组下标；`VegetationPayload` / `AnimalSpawnPayload` 的 `prefabIndex` 分别指向 `Vegetation.Prefabs` / `Animals.Prefabs`。**重排 prefab 数组会让旧存档错位**——原型阶段可接受，正式版应改为稳定 `prefabId` / `speciesId`。

### 18.4 2D ↔ 3D 区块的扩展点

当前 `ChunkCoord` 是 2D `(X, Z)`。扩展到真 3D 体素世界时**唯一需要动的底层**是：

- `ChunkCoord` 增加 `Y` 维度
- `ChunkUtil.EncodeChunkId` 改用 3D 编码（如三段 21 bit 拼一个 long）
- `ChunkBounds` 计算用上 `Y`

Generator / Storager / Serializer / Presenter / Pipeline / LoadSource 这六个策略接口对 2D→3D 是**零侵入**的。

---

> 如果你发现做某件事必须同时改 ≥ 3 个策略层文件，那大概率是出现了泄露的耦合，
> **先停手回头审接口边界**，而不是继续改下去。
