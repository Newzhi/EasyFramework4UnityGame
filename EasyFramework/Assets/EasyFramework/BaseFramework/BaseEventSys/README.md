# BaseEventSys

> 路径：`BaseFramework/BaseEventSys/`  
> 命名空间：`BaseFramework.BaseEventSys`

> **免责声明**  
> 本模块及整份 vFramework 文档，都是作者小时候不懂事写着玩的练习作品，**不是**成熟商业框架，API 命名、边界划分随时可能改。  
> 若你在正式项目里直接照搬，后果自负；当学习笔记看就好。

---

## 1. 在架构中的位置

BaseEventSys 属于 **BaseFramework（基础架构层）**，与 `BaseGameRoot`、`BaseFSM` 同级，供上层 **BaseLayer / HotUpdateLayer** 做跨模块、低频通知。

```text
HotUpdateLayer / BaseLayer
        │  RegisterEvent / SentEvent（解耦通知）
        ▼
BaseEventSys（GameEventBus + IGameEvent）
        │
        ▼
不依赖 Unity 场景、不替代 GameRoot IOC / Proxy 数据流
```

| 适合用事件 | 不适合用事件 |
|------------|--------------|
| 流程切换（如 `GameFlowChangedEvent`） | 每帧战斗逻辑、弹道、伤害结算 |
| UI / 音频 / 日志旁路刷新 | 网络包解析进 Model（走 Proxy） |
| 调试监听（如综合测试里的射击、死亡日志） | 需要严格调用顺序的核心玩法链 |

宏观架构见 [FrameworkDesign.md](../../Docs/Overview/FrameworkDesign.md) §4.2；异步与主线程约定见同文档 §4.5（与事件无关，勿混在本 README 里当「事件用法」）。

---

## 2. 职责

| 组件 | 职责 |
|------|------|
| **IGameEvent** | 事件标记接口；所有可发布类型必须实现 |
| **GameEventBus** | 静态、类型安全的全局总线：`RegisterEvent` / `DeRegisterEvent` / `SentEvent` |

特点（按当前实现）：

- 按 `Type` 分桶，`List<Delegate>` 保存订阅，**同一 handler 重复注册只保留一份**
- `SentEvent` 发布时对当前订阅列表做**快照**；发布过程中新订阅者**收不到**本次事件
- 单个 handler 抛异常会被捕获并 `LogError`，**不中断**后续 handler
- 支持嵌套发布（事件 A 里再 `SentEvent` B），用按深度分配的缓冲避免 `ToArray()` 分配
- Editor 进入 Play Mode 时自动 `ClearAll()`，减轻域重载后残留订阅

---

## 3. 目录结构

```text
BaseEventSys/
├── Interface/
│   └── IGameEvent.cs          # 事件标记接口
├── Impt/
│   └── GameEventBus.cs        # 总线实现
└── README.md
```

业务事件类型**不要**写在本目录，应放在各自模块的 `Events/` 下，例如：

- `BaseGameRoot/GameFlow/Events/GameFlowChangedEvent.cs`
- `HotUpdateLayer/.../Events/`（规划）
- 测试：`Assets/Test/comprehensiveTest/Scripts/Events/`

---

## 4. 基本用法

### 4.1 定义事件

推荐 **`readonly struct`** + 轻量字段（值类型、Id、坐标等），避免在事件里长期塞 `GameObject` / `Transform`：

```csharp
using BaseFramework.BaseEventSys;

namespace MyGame.Events
{
    public readonly struct WaveStartedEvent : IGameEvent
    {
        public int WaveIndex { get; }
        public WaveStartedEvent(int waveIndex) => WaveIndex = waveIndex;
    }
}
```

框架内已有范例：`GameFlowChangedEvent`（流程 `FromStateId` / `ToStateId` / `UserData`）。

### 4.2 订阅与退订

在 `OnEnable` 订阅、`OnDisable` 退订（或 Module `Init` / `Dispose` 成对出现）：

```csharp
using BaseFramework.BaseEventSys;
using UnityEngine;

public class WaveHudListener : MonoBehaviour
{
    void OnEnable()
    {
        GameEventBus.RegisterEvent<WaveStartedEvent>(OnWaveStarted);
    }

    void OnDisable()
    {
        GameEventBus.DeRegisterEvent<WaveStartedEvent>(OnWaveStarted);
    }

    void OnWaveStarted(WaveStartedEvent e)
    {
        Debug.Log($"Wave {e.WaveIndex} started");
    }
}
```

### 4.3 发布

在「状态已确定」之后再发，避免订阅者读到半初始化数据：

```csharp
GameEventBus.SentEvent(new WaveStartedEvent(waveIndex));
```

`GameFlowService` 在 `ChangeState` 成功后会发布 `GameFlowChangedEvent`，可供 UI、存档策略等订阅。

---

## 5. API 一览

| 方法 | 说明 |
|------|------|
| `RegisterEvent<T>(Action<T>)` | 订阅；重复注册同一 delegate 无效 |
| `DeRegisterEvent<T>(Action<T>)` | 退订 |
| `SentEvent<T>(T)` | 发布（主线程调用） |
| `ClearAll()` | 清空全部订阅；测试场景切换时可手动调用 |
| `ClearByType<T>()` | 清空某一事件类型的全部订阅 |
| `GetSubscriberCount<T>()` | 调试：当前订阅者数量 |
| `EventTypeCount` | 调试：当前有订阅者的事件类型数 |

> 说明：`FrameworkDesign.md` 里写的 `Subscribe` / `Publish` 是设计用语；**当前代码**实际方法名为 `RegisterEvent` / `DeRegisterEvent` / `SentEvent`。

---

## 6. 注意事项

1. **必须成对退订**  
   总线是静态全局，忘记 `DeRegisterEvent` 是典型泄漏来源（对象已销毁仍被回调）。

2. **默认主线程**  
   `SentEvent` 应在 Unity 主线程调用；订阅方通常会碰 UI / `UnityEngine.Object`。从线程池回调里要先切回主线程再发事件。

3. **不要当数据总线**  
   事件只传「发生了什么」的摘要，不要把大数组、每帧坐标流塞进总线。高频局内消息优先 Proxy + Model 或直接调用。

4. **慎用 Unity 引用**  
   若必须传 `GameObject`，订阅方只做当帧只读使用，不要写进静态缓存。更稳的是传 `entityId`、格子坐标等可重建信息。

5. **避免事件风暴 / 递归环**  
   A 的处理里再发 A，或链式触发大量事件，会导致难排查的调用栈与性能尖峰。

6. **与 GameRoot 的关系**  
   `GameEventBus` **不是** `IGameModule`，不参与 `ModuleManager` 生命周期；需要时在 Bootstrap 或场景边界手动 `ClearAll()`（测试里 `ComprehensiveTestSceneFlow` 即如此）。

---

## 7. 相关文档

| 文档 | 内容 |
|------|------|
| [FrameworkDesign.md](../../Docs/Overview/FrameworkDesign.md) | 三层架构、BaseEventSys 在 §4.2 |
| [ProjectGoals.md](../../Docs/Overview/ProjectGoals.md) | 框架定位与事件适用场景 |
| [BaseGameRoot/README.md](../BaseGameRoot/README.md) | 入口与 Module 调度 |
| [GameFlowApi.md](../BaseGameRoot/GameFlow/GameFlowApi.md) | 宏观流程与 `GameFlowChangedEvent` |
