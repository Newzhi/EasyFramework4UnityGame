# 加载侧优化计划（Loader Optimization Plan）

> **状态**：设计文档；**排期/TODO 唯一来源** → [MainRoadmap.md](./MainRoadmap.md) §4 P1.5。  
> **关联**：[BusinessApiUsageGuide.md](./BusinessApiUsageGuide.md)、[RefCountAppendix.md](./RefCountAppendix.md)  
> **日志**：`BaseLogSys/AssetRefTraceLogger.cs`（首版已接入 Resource / Bundle / Pool 关键路径）

---

## 1. 目标

| # | 方向 | 说明 |
|---|------|------|
| 1 | **自动管理 Handle（TEngine / AssetReference）** | 非池化：`Load` + `Instantiate` 后 GO 挂绑定组件，`Destroy` 自动 `Release`；业务 API 可隐藏句柄 |
| 2 | **统一对象池持 Handle** | 池化资源 **只允许** 经 `PrefabPoolManager` 创建/释放 Provider 引用；`GetObj`/`RecycleObj` 不动 Ref |
| 3 | **引用计数追溯日志** | `AssetRefTraceLogger` 统一打点，便于泄漏与 AB 过早卸载排查 |

与 [RefCountAppendix.md](./RefCountAppendix.md) 三层计数模型一致：**① Resource Ref / ② Bundle Ref / ③ Pool refCount**。

---

## 2. 自动管理 Handle（对标 TEngine AssetsReference）

### 2.1 原则

```text
非池化：GameObject → AssetReference → IAssetHandle → AbstractResource.Ref
池化：  PrefabPool → IAssetHandle（实例不持 Handle、不 Release）
```

- **Ref 永远绑 Provider/Handle**，不绑 `GameObject` 生死（避免父死子活导致 AB 被卸）。
- **池内实例禁止** 挂 `AssetReference` 或在 `OnDestroy` 里 `Release`（会与 `TearDown` 冲突）。

### 2.2 API（已实现）

| API | 用途 |
|-----|------|
| `AssetReference.Bind(go, handle)` | 挂到实例；`OnDestroy` → `Release` 一次 |
| `BundleResLoader.LoadGameObject(loadPath)` | `Load` + `Instantiate` + 自动 `Bind` |
| `LoadGameObjectAsync` / `LoadWithAutoUnLoad` | 同上（异步 Yield） |
| `AssetReference.ReleaseBinding()` | 主动释放，避免重复 Release |

### 2.3 与 YooAsset / TEngine 差异

| 项 | YooAsset / TEngine | 本框架现状 | 计划 |
|----|-------------------|------------|------|
| 每次 Load 返回句柄 | 新建 `AssetHandle` | 同一 `AbstractResource`，`Ref++` | 可选：薄 Handle 包装（P2） |
| 非池自动 Release | `AssetsReference` | ✅ `AssetReference` + `LoadGameObject` |
| 池持 Handle | Pool 内部 | ✅ `PrefabPool` | 强化：业务禁止对池路径直接 `Load` |

详见对话与设计附件中对 TEngine 的对照分析；实现以 **双路径（池 / 非池）** 为准，不混用。

---

## 3. 统一对象池管理 Handle（当前 + 优化）

### 3.1 当前（已实现）

```text
PrefabPoolManager
  GetOrCreatPool → CreatePool → BundleResLoader.Load（① Ref=1）
  ReleasePoolShare / DeletePool → PrefabPool.TearDown → prefabHandle.Release()
  GetObj / RecycleObj → 仅 GameObject 借还
```

- 注册键：`Scene.handle + loadPath`
- 多模块共享：`refCount`（③），对称 `ReleasePoolShare`

### 3.2 优化方向（待做）

| 项 | 内容 |
|----|------|
| **唯一入口** | 池化 prefab **不得** 业务侧 `BundleResLoader.Load`；只走 `PrefabPoolManager` |
| **门面收敛** | `PoolManager.GetObj(path)` / `RecycleObj(go, path)` 与 `GetOrCreatPool` 文档统一 |
| **可选集中份额** | 如 `BulletPoolService` 独占一次 `GetOrCreatPool`，全局 `TryGetPool`（减少多模块对称卸池负担） |
| **IPooledObject** | 实例 `Recycle()` 封装，避免误 `Destroy` |

### 3.3 正确关系（敌人 + 子弹）

```text
EnemyPool ──Handle──► Enemy.prefab
BulletPool ──Handle──► Bullet.prefab   （与 Enemy 独立）

Enemy 死亡 RecycleObj → 不卸 BulletPool
Bullet RecycleObj     → 不 Release Handle
```

---

## 4. 引用计数追溯日志（AssetRefTraceLogger）

### 4.1 位置与职责

- **目录**：`Assets/vFramework/BaseFramework/BaseLogSys/AssetRefTraceLogger.cs`
- **职责**：统一格式输出 ① Resource / ② Bundle / ③ Pool 变化及关键生命周期事件
- **首版**：`Debug.Log` + 内存环形缓冲（`DumpRecent`）；Editor / `DEVELOPMENT_BUILD` 默认开启

### 4.2 日志格式（示例）

**用途**：检查 Resource / Bundle / Pool 三层引用计数是否正常。

**人类可读（Editor / Console）**：

```text
[AssetRefTrace][RefCountCheck][Resource] op=3 for=Model/Prefabs/Bullet resRef=1 reason=AddReference
[AssetRefTrace][RefCountCheck][Scope] op=3 for=Model/Prefabs/Bullet main=model.bundle bundles=atlas.bundle>background.bundle>icon.bundle reason=BundleAcquireBegin
[AssetRefTrace][RefCountCheck][Bundle] op=3 for=Model/Prefabs/Bullet main=model.bundle bundle=atlas.bundle role=Dep bundleRef=1 delta=+1 reason=AcquireBundle(new)
[AssetRefTrace][RefCountCheck][Pool] share=1 resRef=1 delta=+1 reason=Initialize
```

**真机 JSONL**（非 Editor，每条一行）：

- 目录：`{persistentDataPath}/vFramework/AssetRefTrace/Logs/ref_trace_yyyyMMdd_HHmmss.jsonl`
- 首行 header：`purpose=AssetRefCountCheck`，`schema=v1-ref-trace`，`note=引用计数是否正常校验`
- 条目字段：`opId`、`forLoadPath`、`mainBundle`、`bundleRole`（Dep/Main/Release）、`resourceRef`、`bundleRef`、`poolShareRef`、`acquiredBundles`（`>` 连接加载顺序）

### 4.3 接入点

| 模块 | 打点 |
|------|------|
| `AbstractResource` | `AddReference` / `ReduceReference` / `UnLoad` / 首次 `LoadAsset` |
| `BundleManager` | `AcquireBundle` / `ReleaseBundle` / `LruDefer` / `LruEvict` |
| `PrefabPool` | `Initialize` / `RegisterShare` / `ReleaseShare` / `TearDown` |

### 4.4 后续扩展

- 与 `ComprehensiveTestLogger` 字段对齐（`schema=v2-ref-holders-max`）
- 泄漏检测：进程结束时 `DumpRecent` + Resource/Bundle 非零 Ref 告警
- Release 包可选接入 `DebugLogger` 统一真机目录（当前 JSONL 已独立落盘）

---

## 4.5 Bundle LRU 延迟卸载（✅ 已实现）

| 项 | 说明 |
|----|------|
| 触发 | `ReleaseBundle` 使 Ref=0 → 进入空闲队列，**不**立即 `Unload(true)` |
| 优先级 | 读清单 `bundles[].resourcePriority`；`Critical` 保留最久，`Optional` 最先淘汰 |
| 策略类 | `ResLoader/Bundle/BundleLruUnloadPolicy.cs`（grace 秒数 + `MaxIdleBundles=32`） |
| 淘汰时机 | 每次 `Acquire`/`Release` 尝试淘汰；可选 `BundleManager.TickLruUnload()` |
| 强制全卸 | `UnloadAll` / `Init` 仍立即卸载全部包 |

Trace：`Reason=LruDefer`（Ref 归零入队）、`LruEvict` / `LruEvictCap`（真正 Unload）。

---

## 5. 实施排期

**唯一来源**：[MainRoadmap.md §4 P1.5](./MainRoadmap.md#p15--加载侧优化与-b-2--c-并行) — **不在本文维护状态列**；完成/进行中只改主路线。

| 代号 | 内容（摘要） |
|------|----------------|
| P1.5-1 | 本文档 + Trace 规范 |
| P1.5-2 | `AssetRefTraceLogger` |
| P1.5-3 | `AssetReference` + `LoadGameObject` |
| P1.5-4 | 池路径禁止业务直接 `Load` |

---

## 6. 业务侧过渡指南

| 场景 | 现在 | 优化后 |
|------|------|--------|
| 高频子弹/怪 | `PrefabPoolManager.GetOrCreatPool` + `GetObj`/`RecycleObj` | 不变；加 Trace 日志排查 |
| 低频 UI/NPC | `LoadGameObject` / `LoadWithAutoUnLoad`；Destroy 自动 Release | 勿与模块字段 `Release` 混用 |
| 切场景 | `UnloadAll` | 不变；Trace 应看到全部 TearDown / UnLoad |

计数规则仍以 [RefCountAppendix.md](./RefCountAppendix.md) 为准；运行时对照 **Trace 日志** 与 **附录表格**。

---

*文档维护：加载侧重构时只更新设计章节（§1–§4、§6）；排期与 TODO 见 [MainRoadmap.md](./MainRoadmap.md)。*
