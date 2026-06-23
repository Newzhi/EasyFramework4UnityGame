# Minecraft 区块系统参考资料与可迁移设计思想

> 本文档为 `ChunkTest` 区块系统的扩展（修饰器 / 多管线 / 生物群系 / 结构生成 / 实体管理）提供参考资料索引，并对照当前实现给出可迁移的设计思想与差异对比。
>
> 与 [`README.md`](./README.md)、[`ITERATIONS.md`](./ITERATIONS.md) 同级，遇到具体落地问题先回到这两份文档。

---

## 一、参考资料索引

### 1. 官方 / 半官方 Wiki

| 资源 | 链接 | 关注点 |
| --- | --- | --- |
| Minecraft Wiki - Chunk | https://minecraft.wiki/w/Chunk | 区块尺寸、坐标、Section、Heightmap 概念 |
| Minecraft Wiki - Chunk format (Anvil) | https://minecraft.wiki/w/Chunk_format | NBT 字段、Status 枚举、Structures/Heightmaps 存储 |
| Minecraft Wiki - Region file format | https://minecraft.wiki/w/Region_file_format | `.mca` 文件结构、扇区分配、本地化压缩 |
| Minecraft Wiki - Custom world generation | https://minecraft.wiki/w/Custom_world_generation | Density Function、SurfaceRules、Noise Settings |
| Fabric Wiki - World Generation | https://fabricmc.net/wiki/tutorial:worldgen_index | ChunkGenerator / Feature / Structure 编写指引 |

### 2. 开源服务端 / 客户端实现

| 项目 | 链接 | 价值 |
| --- | --- | --- |
| **PaperMC (Paper)** | https://github.com/PaperMC/Paper | 业界优化最深的服务端，区块加载/Ticket/异步 IO 改进可直接对照 |
| PaperMC DeepWiki | https://deepwiki.com/PaperMC/Paper | 自动生成的源码导览，找类找流程很方便 |
| **Minestom** | https://github.com/Minestom/Minestom | 从零写的服务端，去掉历史包袱，`Instance/Chunk/Generator` API 简洁，最值得照抄结构 |
| Minestom Docs | https://minestom.net/docs/world/instances | Instance / Chunk / Generator / Batch 概念 |
| **Cuberite** (C++) | https://github.com/cuberite/cuberite | 轻量服务端，区块状态机和保存/加载流程清晰 |
| **Pumpkin** (Rust) | https://github.com/Pumpkin-MC/Pumpkin | 现代 Rust 重写，并发模型可参考 |
| **Hydrogen / Hydrazine / Folia** | https://github.com/PaperMC/Folia | Region 化线程模型，超大世界并行 Tick 思路 |
| Yarn Mappings | https://github.com/FabricMC/yarn | 把混淆类名翻译成可读名，配合 Fabric 源码阅读 |
| Project Lodestone | https://wiki.bedrock.dev/world-generation/anvil-format | Anvil 文件格式整理稿（可访问性较好） |

### 3. 高质量博客 / 文章

| 标题 | 链接 | 关键概念 |
| --- | --- | --- |
| izzel.io – Chunk Loading and the Ticket System | https://izzel.io/2021/02/01/chunk-loading-paper/ | **Ticket** 机制：谁请求加载、加载到什么级别、何时释放 |
| Henrik Kniberg – Minecraft world generation talk (GDC) | https://www.youtube.com/watch?v=CSa5O6knuwI | 噪声 + 生物群系 + 结构 三层管线高层视图 |
| Henrik Kniberg – Caves & Cliffs world generation | https://www.youtube.com/watch?v=YyVAaJqYAfE | 1.18 后 Density Function 重构思路 |
| Alan Zucconi – Procedural Worlds | https://www.alanzucconi.com/?s=procedural | 噪声/Domain Warp/Biome 混合的可视化讲解 |
| Red Blob Games – Map Generation | https://www.redblobgames.com/maps/terrain-from-noise/ | 高度图与生物群系映射的最佳入门 |

### 4. 协议 / 数据格式

- **NBT 规范**：https://minecraft.wiki/w/NBT_format
- **wiki.vg 协议（区块包）**：https://wiki.vg/Protocol#Chunk_Data_and_Update_Light
- **Anvil `.mca` 二进制布局**：https://minecraft.wiki/w/Region_file_format

---

## 二、Minecraft 区块管线核心概念速查

### 2.1 ChunkStatus 状态机（最值得迁移的设计）

MC 把"一个区块"从无到完整渲染拆成一条**有序状态链**，每一级都是独立的小步骤，前一级是后一级的输入：

```
EMPTY
  → STRUCTURE_STARTS      // 决定哪些结构会出现在这个区块（仅放种子）
  → STRUCTURE_REFERENCES  // 收集邻居区块投影到本区块的结构引用
  → BIOMES                // 生物群系采样
  → NOISE                 // 主噪声地形（石头/水/空气）
  → SURFACE               // 表面材质（草/沙/雪）
  → CARVERS               // 雕刻洞穴/峡谷
  → FEATURES              // 装饰：树、矿、花、湖、村庄方块……
  → INITIALIZE_LIGHT      // 光照初始化
  → LIGHT                 // 光照计算
  → SPAWN                 // 实体生成
  → FULL                  // 可被玩家看见、可 Tick
```

每一级被记录为 `ChunkStatus`，落盘时写入 NBT `Status` 字段。重启后能从中断的那一级继续推进。

### 2.2 ProtoChunk vs LevelChunk

- **ProtoChunk**：尚未到 `FULL` 的"半成品"，允许跨区块写入（例如树超出本区块边界时把方块写进邻居的 ProtoChunk）。
- **LevelChunk**：到达 `FULL` 后转正，挂上实体、TileEntity，可被玩家访问。

### 2.3 Ticket 系统

谁让区块"活着"？不是玩家直接持有，而是发一张 **Ticket(类型, 等级, TTL)**：

- 玩家：`PLAYER` Ticket，等级最高（`FULL`/`TICKING`）。
- 传送门、强制加载方块、命令 `/forceload`、Mod：各自的 Ticket 类型。
- **DistanceManager** 把所有 Ticket 在区块图上做"传播"，越靠近源等级越高，越远越低（仅 BORDER → 仅 GENERATED → 仅可见 → 完整 Tick）。
- TTL 到期自动释放，无人持有时区块降级 → 卸载 → 落盘。

### 2.4 Region 文件（Anvil `.mca`）

- 每个 `.mca` 存 32×32 = 1024 个区块。
- 文件头 8KiB：4KiB 扇区表 + 4KiB 时间戳。
- 每区块按 4KiB 扇区对齐，单块内部用 zlib/gzip 压缩 NBT。
- 优势：少文件、顺序写、可定位读；劣势：碎片化需要偶尔重整。

### 2.5 Feature / Structure 二分

- **Feature**：小型本地装饰（树、矿脉、花丛、瀑布），在 `FEATURES` 阶段按 BiomeSource 配置批量放置，**只读取本区块及 8 邻居**。
- **Structure**：大型多区块建筑（村庄、要塞、神殿），分两步：
  1. `STRUCTURE_STARTS` 先在"骨架区块"决定中心和形状。
  2. 各被覆盖区块在 `STRUCTURE_REFERENCES` 阶段拿到引用，到 `FEATURES` 时把自己范围内那一片放下去。

---

## 三、对照当前 ChunkTest 系统的差异

| 维度 | Minecraft | 当前 ChunkTest | 差距 / 启示 |
| --- | --- | --- | --- |
| 加载触发 | Ticket + DistanceManager 传播 | `IChunkLoadSource` 计算坐标集 → `ChunkManager` 比 diff | 当前是"集合差"模型，只能整体 Generated/Rendered，缺少"按源分级、按 TTL 释放" |
| 激活级别 | 多档（INACCESSIBLE/BORDER/TICKING/ENTITY_TICKING/FULL） | `ChunkActivationLevel.None/Generated/Rendered` 已有但 Pipeline 不识别 | 概念已铺好，差一步把 Pipeline 拆成"按级别"执行 |
| 内容管线 | ChunkStatus 状态机，强顺序、可中断、可恢复 | `ChunkContentPipeline.Load = TryLoad → Generate → Save → Present` 整体一步完成 | **最该迁移**：把单一 Load 拆成多阶段，每段产物落 ChunkData 不同槽位 |
| ChunkData | ProtoChunk 持有多 section、heightmap、structure refs… 多字段 | `_payload + _presenterHandle` 单槽位 | **第一性问题**：单槽位让多 Pipeline 互相覆盖，必须先改成多槽位（按 PipelineId 索引） |
| 跨区块写入 | ProtoChunk 允许写邻居，结构与大特征自然支持 | 各 Pipeline 只看本区块 | 引入"延迟写入队列 / 邻居 ProtoPayload"再做大型 Feature |
| 存盘 | Anvil `.mca` 1024 合 1，二进制 NBT | `chunk_{id}{suffix}.dat` 一区块一文件，JsonUtility 文本 | 当前可读性高、调试方便；要规模化时考虑分段聚合（不必上 Anvil，按 8×8 region 即可） |
| 异步策略 | IO/光照/Worldgen 各自线程池 + 严格的同步点 | `ChunkLoadQueue` 最小堆 + `ChunkLoadThrottler` 节流 | 已有节流骨架，可以再分 IO/CPU 两类配额 |
| 热重载 / 中断恢复 | NBT `Status` 字段记录到哪一步 | 全部 Generated 完一次性写入 | 多阶段后建议在 ChunkData 上记录 `CompletedStages` 位标志 |

---

## 四、可迁移的设计思想（按落地优先级）

### P0 · 多槽位 ChunkData + IChunkContentReader

> 对应 MC：ProtoChunk 持有多个并行字段（biomes、heightmap、blocks、structureStarts…）

最小变更：

```csharp
// ChunkData 内部
private readonly Dictionary<string, object> _payloads = new();        // pipelineId -> payload
private readonly Dictionary<string, object> _presenters = new();
public bool TryGetPayload<T>(string pipelineId, out T value);
public void SetPayload(string pipelineId, object payload);
```

对外暴露只读视图 `IChunkContentReader`，让后续 Generator 能读前序产物（地形高度图 → 装饰器读它）。**这是所有后续扩展的地基**，没有它任何"修饰器/多管线"都会互相覆盖。

### P1 · 状态化 Pipeline（Generated 与 Rendered 拆开）

> 对应 MC：ChunkStatus 把"逻辑生成"和"可见呈现"分两级

- `IChunkContentPipeline.RequiredLevel` ∈ { Generated, Rendered }。
- ChunkManager 按当前激活级别调度，进入卸载时反向退级。
- 好处：Sim 半径只跑 Generated 管线（地形 + 实体逻辑），Render 半径才跑 Presenter，省 GPU/Mesh。

### P2 · Modifier Chain（修饰器）

> 对应 MC：Feature/Carver 在固定阶段批量执行

```csharp
public interface IChunkPayloadModifier<T>
{
    int Order { get; }                  // 决定执行顺序
    void Modify(ChunkCoord coord, T payload, IChunkContentReader reader);
}
```

把 `HeightColumnGenerator` 改成 `DecoratedGenerator { Base, Modifiers[] }`，地形装饰（草/花/小树）优先用这条路，**改动最小、收益最快**。

### P3 · 结构生成（StructurePlan + 跨区块投影）

> 对应 MC：STRUCTURE_STARTS / STRUCTURE_REFERENCES / Features 三级

- 新增 `IStructurePlanner`：在"骨架区块"产 `StructurePlan`（中心 + AABB + 模板）。
- 邻居区块在 Generated 阶段查询"我的范围内有哪些 plan"，把覆盖到自己的部分作为 `StructureSlice` 写入自己的 payload 槽位。
- 对应 Presenter 渲染（独立 prefab/mesh）。

### P4 · Ticket 化加载源

> 对应 MC：Ticket + DistanceManager

把现在的 `CircularChunkLoadSource / PinnedAnchorLoadSource` 统一抽象为 Ticket：

```csharp
public readonly struct ChunkTicket {
    public string SourceId;             // player_1 / portal_xx / forceload_3
    public ChunkCoord Center;
    public int Radius;
    public ChunkActivationLevel Level;  // 该 Source 想要的最高级别
    public float TtlSeconds;            // 0 表示常驻
}
```

ChunkManager 维护 Ticket 集合 → 对每个区块取 max(Level)，自然支持"传送门拉一格 GENERATED 区块给玩家瞬移"等高级用例。

### P5 · Region 化存储（按需）

> 对应 MC：Anvil `.mca`

只在区块数量过多导致 IO/磁盘碎片明显时再上：把 16×16 或 32×32 个区块合并为一个 `.region` 文件，文件头存扇区表，区块按 4KB 对齐。**优先级最低**，文本 JSON 在调试期更友好。

---

## 五、设计取舍对比表

| 设计选择 | 简单方案 | MC 风格方案 | 推荐场景 |
| --- | --- | --- | --- |
| 数据组织 | 单 payload 槽位 | ProtoChunk 多字段 | 一旦超过 1 条 Pipeline，必上多字段 |
| 阶段管理 | 一次性 Load | ChunkStatus 状态机 | 想要中断恢复 / 分级激活时必上状态机 |
| 装饰逻辑 | 写在 Generator 里 | Feature/Modifier 列表 | 装饰种类 ≥ 3 种或要数据驱动配置时上 Modifier |
| 大型建筑 | 直接在 Generator 里造 | StructurePlan + 跨区块投影 | 建筑跨区块 / 形状不可预测时必上 Plan |
| 加载触发 | 圆形/方形半径 | Ticket + Distance 传播 | 出现"非玩家"加载源（传送门/命令/AI）时必上 Ticket |
| 落盘格式 | 单区块单文件 JSON | Anvil region 二进制 | 区块数过万 / 跨平台分发时考虑 region |
| 序列化 | JsonUtility | NBT | 需要无 schema 演进 + 紧凑体积时上类 NBT |

---

## 六、推荐阅读顺序

1. **先读 izzel.io Ticket 文章**（30 分钟）→ 理解"加载是被动响应而非主动遍历"。
2. **再翻 Minestom 的 `Instance` / `Chunk` / `Generator`**（1 小时）→ 看一份没有历史包袱的最小 API。
3. **然后读 Fabric Wiki Worldgen**（1–2 小时）→ 弄清 Feature/Structure 的数据驱动配置长什么样。
4. **最后挑 PaperMC DeepWiki 中 `ChunkHolder` / `ChunkStatus` / `DistanceManager`**（按需）→ 看工业级实现细节。
5. **跳过 Anvil 二进制格式**，除非已经确定要做 region 文件，否则不是当下瓶颈。

---

## 七、与本仓库现有文档的关系

- [`README.md`](./README.md)：系统总览、目录、运行流程、扩展指南 —— **先读这个**。
- [`ITERATIONS.md`](./ITERATIONS.md)：迭代记录与 backlog。
- 本文档：**外部参考与设计借鉴**，不规定具体接口；落地项写入 ITERATIONS backlog。
