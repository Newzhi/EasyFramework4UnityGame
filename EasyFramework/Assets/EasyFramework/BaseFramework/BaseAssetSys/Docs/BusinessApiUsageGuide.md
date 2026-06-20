# 业务 API 调用指南（ABSystem_Beta）

> 入口：`BundleResLoader.Instance`  
> 句柄：`IAssetHandle`  
> 详细排期与能力边界：[BusinessApiAndCdnPlanning.md](./BusinessApiAndCdnPlanning.md)、[MainRoadmap.md](./MainRoadmap.md)  
> **引用计数附件**：常见写法逐步模拟见 **[RefCountAppendix.md](./RefCountAppendix.md)**。  
> **加载侧扩展**：`LoadGameObject` / `AssetReference`、Bundle **LRU 延迟卸包**（[§5.6](#bundle-lru-unload)）已实现；架构排期见 **[LoaderOptimizationPlan.md](./LoaderOptimizationPlan.md)**（引用计数 **Trace 日志**为调试能力，不在本文业务 API 范围）。

---

## 1. 初始化

| API | 说明 |
|-----|------|
| `BundleResLoader.Instance.EnsureReady()` | 懒加载 Catalogue 与 Bundle 根目录；可在首次 `Load` 前预热 |
| `BundleResLoader.Instance.Init(bundleRootPath, usePlatformSubfolder)` | 显式指定 AB 根路径；重复 Init 会打 Warning |
| `BundleResLoader.GetDefaultRuntimeBundleRoot()` | 默认 `StreamingAssets/{当前平台}/` |
| `BundleResLoader.Instance.GetCatalogue()` | 读取已加载清单（`buildMode`、`bundles[].resourcePriority` 等） |
| `BundleResLoader.Instance.IsCatalogueLoaded` | 清单是否已加载 |

**CDN 热更（阶段 C）**：打包时 `BuildSetting.cdnBaseUrl` 写入清单 `cdnBaseUrl`。`Init` / `EnsureReady` 时自动：

1. `DefaultBundlePathResolver`：查找顺序 **ABCache → StreamingAssets**  
2. `CdnCatalogueSyncService`：远程 `catalogueHash` 变化 → 缓存清单并重载  
3. `HttpRemoteBundleProvider`：本地无包时 NETCDN 下载（失败回退首包/本地 cache）

```csharp
// 一般无需手动 Init；首次 Load 会自动 EnsureReady
if (!BundleResLoader.Instance.EnsureReady())
{
    Debug.LogError("Catalogue init failed.");
    return;
}

// 常驻模块预热（不创建 Resource 句柄；UnloadAll 时对称 Release）
BundleResLoader.Instance.PreLoadBundles(new[] { "common.bundle", "ui.bundle" });
```

---

## 2. 同步加载

### 2.1 `Load<T>(loadPath)` — 业务主入口

- `loadPath`：清单 **简路径**，无扩展名。  
- `Resources/` 开头：走 Resources 分支，例 `Load<TextAsset>("Resources/ResourceSystemDesignGuide")`。  
- 失败返回 **`null`**，不增加引用计数。

```csharp
IAssetHandle handle = BundleResLoader.Instance.Load<GameObject>("UI/UIRoot");
if (handle == null) return;

GameObject prefab = handle.GetAsset<GameObject>();   // 仅取原型，不实例化
GameObject go = handle.Instantiate();                // 实例化
```

### 2.2 `LoadByAssetPath<T>(assetPath)` — 按工程完整路径

```csharp
IAssetHandle handle = BundleResLoader.Instance.LoadByAssetPath<Sprite>(
    "Assets/AssetBundle/Atlas/Role/Hog.png");
```

### 2.3 `LoadByBundle<T>(bundleName, assetName, assetPath?, loadPath?)` — 按包名

```csharp
IAssetHandle handle = BundleResLoader.Instance.LoadByBundle<Sprite>(
    "atlas.bundle",
    "Hog_Attack_000");
```

### 2.4 低频实例化 + 自动 `Release`（`LoadGameObject` / `AssetReference`）

面向 **偶尔生成、Destroy 即卸** 的 Prefab；**高频复用请用对象池**（§5.4），勿与同 `loadPath` 池混用。

| API | 说明 |
|-----|------|
| `LoadGameObject(loadPath)` | `Load` + `InstantiateAt` + `AssetReference.Bind`；`Destroy` 实例时自动 `Release` |
| `LoadGameObject(loadPath, pos, rot, parent?)` | 带位姿的实例化 |
| `LoadWithAutoUnLoad(loadPath)` | 等同 `LoadGameObject(loadPath)` |
| `LoadWithAutoUnLoadGeneric<T>(loadPath)` | 仅 `Load<T>` 返回句柄，**不**绑实例；须自行 `Release` |
| `LoadGameObjectAsync` / `LoadUniTaskAsynWithAutoUnLoad` | UniTask 版（当前 Yield 一帧后走同步逻辑） |

```csharp
// ✅ 低频 UI / 临时特效：Destroy(go) 即 Release，无需字段保存句柄
GameObject popup = BundleResLoader.Instance.LoadGameObject("UI/SubPanel");

// ✅ 指定位姿
GameObject fx = BundleResLoader.Instance.LoadGameObject(
    "FX/Hit", hitPos, Quaternion.identity, null);

// 手动绑定（与 LoadGameObject 内部一致）
IAssetHandle handle = BundleResLoader.Instance.Load<GameObject>(path);
GameObject go = handle.InstantiateAt(pos, rot, parent);
AssetReference.Bind(go, handle, path);
```

**`AssetReference`（`AbstractAssets/AssetReference.cs`）**

| API | 说明 |
|-----|------|
| `AssetReference.Bind(instance, handle, loadPathForTrace?)` | 绑定句柄；`OnDestroy` 时 `Release` 一次 |
| `HasHandle` / `LoadPath` | 是否仍持有句柄 / 追溯用路径 |
| `ReleaseBinding()` | 主动释放；之后 `OnDestroy` 不再 `Release` |

- **池化实例勿挂** `AssetReference`（句柄由 `PrefabPool` 持有）。  
- 与 `Load` + 字段 `Release` **二选一**，勿对同一 `Load` 又 `Bind` 又在模块里 `Release`（双 Release）。

---

## 3. 异步加载

当前异步 API 内部为 **`UniTask.Yield` 一帧 + 同步 `Load`**；须 `await` 或回调后再使用句柄。

### 3.1 `LoadUniTaskAsync<T>(loadPath)`

```csharp
IAssetHandle handle = await BundleResLoader.Instance.LoadUniTaskAsync<GameObject>("UI/UIRoot");
GameObject go = handle?.Instantiate();
```

### 3.2 `LoadGameObjectAsync` / `LoadUniTaskAsynWithAutoUnLoad`

```csharp
GameObject go = await BundleResLoader.Instance.LoadGameObjectAsync("UI/SubPanel");
GameObject go2 = await BundleResLoader.Instance.LoadUniTaskAsynWithAutoUnLoad("FX/Hit");
```

语义同 §2.4；失败返回 `null`。

### 3.3 `LoadUniTaskWithCallback<T>(loadPath, onComplete, onFailed, useUniTask)`

```csharp
BundleResLoader.Instance.LoadUniTaskWithCallback<GameObject>(
    "UI/UIRoot",
    onComplete: handle =>
    {
        if (handle == null) return;
        GameObject go = handle.Instantiate();
    },
    onFailed: err => Debug.LogError(err));
```

### 3.4 按路径 / 按 Bundle 的回调重载

```csharp
BundleResLoader.Instance.LoadByAssetPathUniTaskWithCallback<Sprite>(assetPath, onComplete, onFailed);
BundleResLoader.Instance.LoadByBundleUniTaskWithCallback<Sprite>(bundleName, assetName, onComplete, onFailed, assetPath: null);
```

- `useUniTask: false` 时改为同步 `Load` 并立即回调。

---

## 4. 句柄 `IAssetHandle`

| 成员 | 说明 |
|------|------|
| `GetAsset<T>()` | 取已加载资源（Sprite、Material、Prefab 原型等） |
| `Instantiate()` | 在原点实例化 GameObject |
| `InstantiateAt(pos, rot, parent)` | 指定位置 / 父节点实例化 |
| `Instance` | 等价于 **每次访问** 调用 `Instantiate()`；**勿重复读** |
| `Release()` | Ref -1；Ref 为 0 时卸载 Resource 占用（Bundle 走 LRU 延迟卸包） |

非池路径也可用 **`AssetReference`**（§2.4）在实例 `Destroy` 时自动 `Release`，等价于手动调用本表 `Release()`。

```csharp
IAssetHandle handle = BundleResLoader.Instance.Load<GameObject>("Model/Prefabs/tester");

GameObject a = handle.Instantiate();
GameObject b = handle.InstantiateAt(spawnPos, Quaternion.identity, parent);

Sprite icon = handle.GetAsset<Sprite>();  // 非 GameObject 资源
```

- `Release()` 在 Ref 已为 0 时为 **no-op**（重复调用安全）。  
- 同一路径多次 `Load` 命中缓存时返回 **同一句柄对象**，Ref 累加。

---

## 5. 卸载

### 5.1 `Unload(IAssetHandle resource, GameObject instance, Action<bool> onComplete)`

```csharp
BundleResLoader.Instance.Unload(handle, go);
BundleResLoader.Instance.Unload(handle, null);           // 仅 Release
BundleResLoader.Instance.Unload(null, go);             // 仅 Destroy
BundleResLoader.Instance.Unload(handle, go, ok => { }); // 可选回调
```

- 顺序：**先** `Destroy(instance)`（非 null），**再** `resource.Release()`。  
- 实例已在别处 `Destroy`：用 `handle?.Release()` 或 `Unload(handle, null)`。

### 5.2 `Release()`

```csharp
handle?.Release();
```

### 5.3 `UnloadAll()`

```csharp
BundleResLoader.Instance.UnloadAll();
```

- 进程级收尾（切场景 / 关游戏）；清空全部 Resource 缓存并 **`BundleManager.UnloadAll()`（立即卸全部 AB，绕过 LRU）**。  
- 与单资源 `Release` 分开使用，避免混用。
- **会先** `PrefabPoolManager.DeleteAllPools()`（销毁池内全部实例并对每个池 `Release` 句柄一次），再清 `resourceDic` 与 Bundle。

### 5.4 对象池（`PrefabPool` / `PrefabPoolManager`）

实现：`AssetPool/PrefabPool.cs`、`AssetPool/PrefabPoolManager.cs`、`AssetPool/PoolSceneRootsUtil.cs`。

**Lint（P1.5-4）**：`DEVELOPMENT_BUILD` 或定义 `VF_POOL_LOAD_LINT` 时，对已注册池的 `loadPath` 直接 `Load` 会 `LogWarning`，应统一经 `PrefabPoolManager`。

| API | 说明 |
|-----|------|
| `GetOrCreatPool(loadPath, poolRoot?, maxInactiveCapacity?)` | **当前 Active Scene** 内按 `loadPath` 去重共享；跨场景各一套池 |
| `TryGetPool(loadPath, out pool)` / `TryGetPool(loadPath, scene, out pool)` | 查询已注册池，不增加 refCount |
| `GetObj(loadPath)` / `GetObj(loadPath, pos, rot, parent?)` | PoolTemp 风格：从当前 Active Scene 池借出 |
| `RecycleObj(instance, loadPath)` | PoolTemp 风格：回收到当前 Active Scene 池 |
| `ReleasePoolShare(loadPath)` | 释放当前 Active Scene 下一次份额（refCount--） |
| `DeletePool(loadPath)` / `DeletePool(loadPath, scene)` | **强制删除**池（无视 refCount / 借出状态） |
| `DeleteAllPools()` | 销毁全部场景下已注册池 |
| `PrefabPool.GetObj` / `RecycleObj` | 借出 / 归还实例；**不改变**句柄 Ref |

**引用计数（池专用）**

| 阶段 | Resource Ref |
|------|----------------|
| 首次 `GetOrCreatPool` | `Load` +1，由池持有 |
| `GetObj` / `RecycleObj` 循环 | **不变** |
| `ReleasePoolShare` / `DeletePool` / `DeleteAllPools` | `Release` -1（每池一次，Delete 为强制） |
| `UnloadAll()` | 先 `DeleteAllPools`，再清其余 Resource |

**何时销毁池、释放引用**

| 时机 | 行为 |
|------|------|
| `ReleasePoolShare` | 当前 Active Scene 对应池 `refCount--`；归零时 TearDown |
| `DeletePool` | 强制 TearDown（无视 refCount / 借出） |
| `DeleteAllPools()` | 全部场景池强制 TearDown |
| `UnloadAll()` | 切场景 / 关游戏；池 + 全部 Resource + Bundle |
| 对局中 `GetObj`/`RecycleObj` | **不** 卸包、**不** Release 句柄 |

`UnloadAll` 之后勿再使用旧池或旧句柄；重新进场景需重新 `GetOrCreatPool`。

**场景隔离（同场景共享、跨场景分池）**

- 注册键：`Scene.handle` + `loadPath`；建池/卸池 API 默认针对 **调用时 `SceneManager.GetActiveScene()`**。
- 同场景多次 `GetOrCreatPool` 同一路径 → 同一 `PrefabPool`，`refCount++`。
- 不同场景同一 `loadPath` → 两套池、两棵 `PoolRuntime`（各场景内 `MoveGameObjectToScene`）。
- 场景卸载：`sceneUnloaded` 自动删除该场景全部池并清节点缓存；仍建议在切场景前 `UnloadAll()`。

约定：在**所属场景为 Active** 时调用建池/卸池（Single 切场景、Start 中建池均满足）。Additive 多场景若 Active 非所属场景，池会建到 Active 场景。

### 5.5 谁创建谁销毁（池所有权）

与 §6.3「生成方与卸载方一致」同理：**谁调用 `GetOrCreatPool`，谁负责对称 `ReleasePoolShare`**。

| 角色 | 允许 | 禁止 |
|------|------|------|
| **池所有者** | 谁 `GetOrCreatPool` 谁记 `refCount++`；`OnDestroy` 对称 `ReleasePoolShare` **一次** | 全局 PoolHost 集中建所有池；建池后无人对称释放 |
| **共享借用人** | `TryGetPool` 借用（**不**增 `refCount`）；只 `GetObj` / `RecycleObj` | 在池化实例 `OnDestroy` 卸共享池；误用 `GetOrCreatPool` 会多占一次 `refCount` |
| **场景流** | 切场景 / ExitGame **前** `UnloadAll()` + 销毁 `PoolRuntime` | 只 `LoadScene` 不卸池 → AB / 句柄泄漏 |

**推荐落地**

1. **不要**用全局 PoolHost 集中建池；谁要生成实例谁 `GetOrCreatPool`（或首次射击时懒加载）。  
2. 同一 Active Scene 同 `loadPath`：`GetOrCreatPool` 去重共享；额外借用方用 `TryGetPool`；每个曾 `GetOrCreatPool` 的组件 `OnDestroy` 各 `ReleasePoolShare` 一次。  
3. **池化敌人**（`RecycleObj` 回收）：实例 **不** `Destroy`，**不在**死亡时 `ReleasePoolShare`；份额由建池方 / 场景流 `UnloadAll` 收尾。  
4. **Direct 实例**（`Destroy` 死亡）：若该实例曾 `GetOrCreatPool` 子弹池，须在 `OnDestroy` 对称 `ReleasePoolShare`（`ownsBulletPoolShare` 标记）。  
5. 切场景 / ExitGame：`UnloadAll()`；`sceneUnloaded` 会兜底清理该场景池。

综合测试：`PlayerTest` + 各 `enemyTest` 各自 `GetOrCreatPool` 子弹池（共享 `refCount`）；`enemyManager` 仅敌人池；`ComprehensiveTestSceneFlow` 切场景收尾。

<a id="bundle-lru-unload"></a>

### 5.6 Bundle LRU 延迟卸载（业务无感）

业务 **只** 调用 `Release` / `Unload`；Bundle 容器何时从内存卸掉由框架按清单优先级调度，**无需**业务调用额外 API。

| 层级 | `Release` 后行为 |
|------|------------------|
| **Resource**（`AbstractResource`） | Ref=0 → **立即**清空原型引用、对称 `ReleaseBundle` |
| **Bundle**（`BundleManager`） | Ref=0 → 进入 **空闲队列**（Trace：`LruDefer`），保留 `AssetBundle` 一段时间 |
| **再次 Load 同包** | 命中空闲队列则 **复用**已加载 AB，不重复 `LoadFromFile` |
| **`UnloadAll`** | **立即**卸全部 Resource + Bundle（含空闲队列） |

**保留时长**（`BundleLruUnloadPolicy`，读清单 `bundles[].resourcePriority`）：

| `ResourcePriority` | Ref=0 后约保留 |
|--------------------|----------------|
| Critical | **永不** LRU 卸载（仅 `UnloadAll`） |
| High | 20s |
| Normal | 15s（Default/Detailed 打包默认） |
| Low | 10s |
| Optional | 5s |

- 空闲包总数超过 **32** 时，按「优先级低者优先、同优先级 LRU」强制淘汰（**不含 Critical**；Trace：`LruEvict` / `LruEvictCap`）。  
- **打包期**配置：自定义打包规则下可在 Editor 为每项设 `resourcePriority`；Default/Detailed 规则写入 `Normal`。详见 [CatalogueReference.md §P1-B](Assets/vFramework/BaseFramework/BaseAssetSys/Docs/CatalogueReference.md#resource-priority)。
- **注意**：Resource Ref=0 后若场上仍有实例引用包内资源，仍可能材质变粉；与是否 LRU 无关。**有活实例时不要 Release 到 0。**

---

<a id="sec-refcount-rules"></a>

## 6. 引用计数与规范用法

> **引用计数附件**：常见写法逐步模拟、三层计数变化表、Mermaid 链路与代码索引见 **[RefCountAppendix.md](./RefCountAppendix.md)**（本文 §6–§7 的规则与范例的补充追踪文档）。

### 6.1 规则速查

| 操作 | Resource Ref |
|------|----------------|
| `Load` 成功 ×1 | +1 |
| `Instantiate` ×N | 不变 |
| `Destroy` 实例 ×N | 不变 |
| `Release` ×1 | -1；为 0 时 Resource 卸载；对应 Bundle Ref=0 进入 LRU 空闲队列 |
| 同路径 `Load` ×N（缓存命中） | +N |

- 每次 **成功** 的 `Load` 最终要有 **一次** `Release` 或 `Unload(handle, …)`。  
- `Load` 返回 `null` → **不要** `Release`。  
- 句柄出作用域 **不会** 自动 Release。  
- Ref 为 0 后 Resource 立即卸原型；**Bundle 层** Ref=0 后进入 LRU 延迟卸载（按清单 `resourcePriority` 保留一段时间），`UnloadAll` 仍立即全卸。场上仍有依赖该资源的实例时会丢 Mesh/材质。**有活实例时不要 Release 到 0。**

### 6.2 必须遵守的写法

**① 保存句柄**

```csharp
// ✅ 字段 / 成员保存 IAssetHandle
IAssetHandle _handle;

// ❌ 只留 GameObject，无法 Release
GameObject _go = BundleResLoader.Instance.Load<GameObject>(path)?.Instantiate();
```

**② Load 与 Release 次数成对**

```csharp
// ✅ 一次 Load，一次 Release（模块 OnDestroy）
_handle = BundleResLoader.Instance.Load<GameObject>(path);
void OnDestroy() { _handle?.Release(); _handle = null; }

// ✅ N 次 Load，N 次 Release（每实例各绑一次 Load）
void Spawn() {
    var h = BundleResLoader.Instance.Load<GameObject>(path);
    go.GetComponent<X>().Bind(h);  // OnDestroy 里 h.Release()
}

// ❌ 一次 Load，多个实例各自 Release 同一句柄
_handle = Load(...);
for (...) Spawn();  // 子物体 OnDestroy 里 _handle.Release() → 第一个就 Ref=0
```

**③ 多实例 + 单次 Load：只 Release 一次**

```csharp
// ✅ Load 1 → Instantiate N → 模块 OnDestroy Release 1 次
_handle = BundleResLoader.Instance.Load<GameObject>(path);
for (int i = 0; i < n; i++) _list.Add(_handle.Instantiate());
void OnDestroy() {
    foreach (var go in _list) if (go) Destroy(go);
    _handle?.Release();
}

// ❌ 同一 _handle 在循环或子脚本里 Release 多次
```

**④ Destroy 实例 ≠ Release 资源**

```csharp
// ✅ 模块收尾时 Release
Destroy(go);
_handle?.Release();

// ❌ 只 Destroy，从不 Release → Ref 泄漏
Destroy(go);

// ❌ 只 Release，场上实例还在用 → 材质变粉
_handle?.Release();  // 实例仍在场景
```

**⑤ `Unload` 与 `Release` 不要重复减 Ref**

```csharp
// ✅ 二选一
BundleResLoader.Instance.Unload(_handle, _go);
// 或实例已 Destroy 时：
_handle?.Release();

// ❌ 对同一 Load 既 Unload 又 Release
BundleResLoader.Instance.Unload(_handle, _go);
_handle?.Release();  // Ref 多减一次
```

**⑥ 实例化用法**

```csharp
// ✅ 保存 Instantiate 返回值
_go = _handle.Instantiate();

// ✅ 仅要原型、不实例化
var prefab = _handle.GetAsset<GameObject>();

// ❌ 多次读 Instance（每次都会新建 GO）
var a = _handle.Instance;
var b = _handle.Instance;
```

**⑦ 异步 / 回调里也要保存句柄**

```csharp
// ✅
IAssetHandle _handle;
BundleResLoader.Instance.LoadUniTaskWithCallback<GameObject>(path,
    onComplete: h => { _handle = h; },
    onFailed: _ => { });

void OnDestroy() { _handle?.Release(); }

// ❌ 回调里 Instantiate 后未保存 h
onComplete: h => { Instantiate(); }  // 无法 Release
```

**⑧ 多资源：每个句柄单独 Release**

```csharp
void OnDestroy()
{
    BundleResLoader.Instance.Unload(_icon, null);
    BundleResLoader.Instance.Unload(_prefab, _instance);
    // 几个 Load 成功，就几次 Release（可合并进 Unload）
}
```

**⑨ `UnloadAll` 仅进程级收尾**

```csharp
// ✅ 切场景 / 关游戏
BundleResLoader.Instance.UnloadAll();

// ❌ 与单资源 Release 混用同一批资源的日常卸载逻辑
Load(...);
UnloadAll();  // 其它模块仍持有的句柄已失效
```

### 6.3 按场景选一种 Ref 模型（勿混用）

| 场景 | Load | Release |
|------|------|---------|
| 模块 / UI / 一两个实例 | 模块内各 `Load` 1 次 | 模块 `OnDestroy` 各 `Release` 1 次 |
| 同一 Prefab 多实例（低频） | 1 次 | 模块 `OnDestroy` **1 次** |
| 每实例 Destroy 里收尾 | 每 spawn `Load` 1 次 | 实例 `OnDestroy` 各 `Release` 1 次；或 `AssetReference.Bind`（§2.4） |
| 低频实例 Destroy 即卸 | `LoadGameObject` 1 次 | `Destroy` 实例即可（`AssetReference` 自动 `Release`） |
| Load 1 次 + 提前卸 AB | 1 次 + 活实例计数 | 计数为 0 时 **Release 1 次** |
| 对象池 | 建池方 `GetOrCreatPool`（共享时 `refCount++`）；借方 `TryGetPool` + `GetObj` | 每个建池方 `OnDestroy` `ReleasePoolShare` 一次；`refCount` 归零才 `Release`；切场景 `UnloadAll` 强制收尾；`RecycleObj` **不** Release |

选定一种后，**生成方与卸载方一致**：模块持句柄就由模块 Release；每实例 Bind 就由实例 Release；**池由谁 `GetOrCreatPool` 就由谁在退场时 `ReleasePoolShare` 一次**（见 §5.5）。

### 6.4 子脚本自控 Destroy 时

- 子脚本 **只** `Destroy(gameObject)`，**不要** `Release` 模块持有的共享句柄。  
- 若要求子脚本 `OnDestroy` 里 Release：spawn 时必须 **每次** `Load` 并把 **该次** 句柄 `Bind` 到实例（见 §7.5）。  
- 实例先于模块 Destroy 时，模块 `OnDestroy` 仍须 `Release`；GO 已为 null 时用 `Unload(handle, null)` 或 `handle?.Release()`。

---

<a id="sec-scenarios"></a>

## 7. 场景范例

### 7.1 模块持句柄 — 字段 + `OnDestroy`（推荐默认）

```csharp
IAssetHandle _prefab;
IAssetHandle _icon;
GameObject _instance;

void Start()
{
    _prefab = BundleResLoader.Instance.Load<GameObject>("Model/Prefabs/tester");
    _instance = _prefab?.Instantiate();
    _icon = BundleResLoader.Instance.Load<Sprite>("Icon/3");
}

void OnDestroy()
{
    BundleResLoader.Instance.Unload(_icon, null);
    BundleResLoader.Instance.Unload(_prefab, _instance);
}
```

### 7.2 低频一两个实例

```csharp
IAssetHandle _npcHandle;
GameObject _npc;

void Start()
{
    _npcHandle = BundleResLoader.Instance.Load<GameObject>("Model/Prefabs/Npc");
    _npc = _npcHandle?.Instantiate();
}

void OnDestroy()
{
    BundleResLoader.Instance.Unload(_npcHandle, _npc);
}
```

同一 Prefab 两个实例：`Load` 一次，`Instantiate` 两次，`OnDestroy` 里 Destroy 两个 GO 后 **`Release` 一次**。

### 7.3 多个不同 Prefab

```csharp
IAssetHandle _hNpc, _hItem, _hFx, _hUi;
GameObject _npc, _item, _fx, _ui;

void Start()
{
    _hNpc = BundleResLoader.Instance.Load<GameObject>("Model/Prefabs/Npc");
    _npc = _hNpc.Instantiate();
    _hItem = BundleResLoader.Instance.Load<GameObject>("Model/Prefabs/Item");
    _item = _hItem.Instantiate();
    _hFx = BundleResLoader.Instance.Load<GameObject>("FX/Hit");
    _fx = _hFx.Instantiate();
    _hUi = BundleResLoader.Instance.Load<GameObject>("UI/SubPanel");
    _ui = _hUi.Instantiate();
}

void OnDestroy()
{
    BundleResLoader.Instance.Unload(_hNpc, _npc);
    BundleResLoader.Instance.Unload(_hItem, _item);
    BundleResLoader.Instance.Unload(_hFx, _fx);
    BundleResLoader.Instance.Unload(_hUi, _ui);
}
```

子物体脚本只 `Destroy(gameObject)`，不要对共享句柄 `Release`。

### 7.4 同一 Prefab 多个实例 — 模块统一 Release

```csharp
IAssetHandle _handle;
readonly List<GameObject> _spawned = new();

void Start()
{
    _handle = BundleResLoader.Instance.Load<GameObject>("Model/Prefabs/Monster");
    for (int i = 0; i < 4; i++)
        _spawned.Add(_handle.Instantiate());
}

void OnDestroy()
{
    foreach (GameObject go in _spawned)
        if (go != null) Destroy(go);
    _spawned.Clear();
    _handle?.Release();
}
```

多个实例 **不要** 各自 `Release` 同一句柄。

### 7.5 每实例 `OnDestroy` 里 Release

**方式 A — `AssetReference`（推荐，与 `LoadGameObject` 一致）**

```csharp
IAssetHandle handle = BundleResLoader.Instance.Load<GameObject>("FX/Bullet");
GameObject go = handle.Instantiate();
AssetReference.Bind(go, handle, "FX/Bullet");
// go Destroy 时自动 Release；勿再手动 handle.Release()
```

**方式 B — 实例脚本自持句柄**

```csharp
// 生成方
IAssetHandle handle = BundleResLoader.Instance.Load<GameObject>("FX/Bullet");
GameObject go = handle.Instantiate();
go.GetComponent<Bullet>().Bind(handle);

// 实例脚本
IAssetHandle _handle;
public void Bind(IAssetHandle h) => _handle = h;
void OnDestroy()
{
    _handle?.Release();
    _handle = null;
}
```

### 7.6 Load 一次 + 活实例计数

```csharp
IAssetHandle _handle;
int _live;

void Spawn()
{
    if (_handle == null)
        _handle = BundleResLoader.Instance.Load<GameObject>("FX/Hit");
    _live++;
    var go = _handle.Instantiate();
    go.GetComponent<FxLife>().Bind(this);
}

internal void OnInstanceDestroyed()
{
    if (--_live > 0) return;
    _handle?.Release();
    _handle = null;
}
```

### 7.7 对象池（`PrefabPool` / `PrefabPoolManager`）

高频 Prefab（子弹、小怪等）用对象池：**Load 一次**，`Instantiate` 复用，**归还时不 Release**，池销毁时 **Release 一次**。API 总表见 §5.4。

#### 7.7.1 推荐入口：`PrefabPoolManager.Instance.GetOrCreatPool`（同场景共享）

同一 **Active Scene** 内同一 `loadPath` 只建一个池；跨场景自动分池：

```csharp
const string BulletPath = "Model/Prefabs/Bullet";

PrefabPool bulletPool = PrefabPoolManager.Instance.GetOrCreatPool(
    BulletPath,
    maxInactiveCapacity: 48);
```

- `poolRoot`：仅在**首次创建**时生效；默认 `PoolSceneRootsUtil.GetOrCreatePoolRoot(loadPath, activeScene)`。  
- `maxInactiveCapacity`：单份持有者闲置上限；共享时按 `refCount` 倍增；超出时 `RecycleObj` **直接 Destroy** 实例。  
- **懒增长**：`refCount++` 只抬闲置上限，**不**预 `Instantiate`；实例仅在 `GetObj` 且闲置队列为空时创建。

#### 7.7.2 生命周期

```
GetOrCreatPool（Active Scene） →  Load 一次，句柄归池；同场景 refCount++（不预建实例）
GetObj(pos, rot)               →  闲置复用或懒 Instantiate；SetActive(true)；parent 已忽略
RecycleObj(go)                 →  SetActive(false) + 回闲置队列，不 Release 句柄
ReleasePoolShare               →  当前 Active Scene refCount--
DeletePool                     →  强制删池（无视 refCount）
UnloadAll                      →  DeleteAllPools + Release 句柄
```

| 属性 / 方法 | 含义 |
|-------------|------|
| `ActiveCount` / `InactiveCount` | 已借出 / 闲置数量 |
| `CanReleaseShare` | 无未归还实例时可安全释放份额 |
| `MaxInactiveCapacity` | 闲置上限（0 不限） |

**安全约定**：池持有句柄后勿再对同一句柄 `Release`；退场前尽量 `RecycleObj` 全部借出实例；`UnloadAll` 强制销毁全部池。

#### 7.7.3 场景 Hierarchy（`PoolSceneRootsUtil`）

每个 **Active Scene** 各一棵：

```
PoolRuntime                    ← 每场景一个，MoveGameObjectToScene
├── Pool_Model_Prefabs_Bullet  ← 实例创建时挂一次，借还只 SetActive
└── Pool_Model_Prefabs_tester
```

切场景：`UnloadAll()` 或 `sceneUnloaded` 自动销毁该场景池并 `ClearCacheForScene`。

#### 7.7.4 射击与位姿

`GetObj(worldPosition, worldRotation)` 在开火帧设定位姿；复用实例也会重设。勿依赖 parent 归类。

```csharp
GameObject bullet = bulletPool.GetObj(firePoint.position, firePoint.rotation);
```

#### 7.7.5 谁创建谁销毁（范例）

```csharp
// —— PlayerTest：当前 Active Scene 内 GetOrCreatPool，OnDestroy ReleasePoolShare ——
void EnsureBulletPool()
{
    if (bulletPool != null) return;
    bulletPool = PrefabPoolManager.Instance.GetOrCreatPool(BulletPath, maxInactiveCapacity: 48);
    ownsBulletPoolShare = true;
}

void OnDestroy()
{
    if (ownsBulletPoolShare)
        PrefabPoolManager.Instance.ReleasePoolShare(BulletPath);
}

// —— enemyManager：Pooled 模式敌人池；Direct 模式 Load 预制体句柄 ——
if (IsPooledSpawnMode())
    enemyPool = PrefabPoolManager.Instance.GetOrCreatPool(EnemyPath, maxInactiveCapacity: 12);
else
    enemyPrefabHandle = BundleResLoader.Instance.Load<GameObject>(EnemyPath);
// OnDestroy：ReleasePoolShare(EnemyPath) 或 enemyPrefabHandle?.Release()

// —— enemyTest：首次射击 GetOrCreatPool 子弹池；Direct 死亡 Destroy → OnDestroy 释份额 ——
void EnsureBulletPool()
{
    if (bulletPool != null) return;
    bulletPool = PrefabPoolManager.Instance.GetOrCreatPool(BulletPath, maxInactiveCapacity: 48);
    ownsBulletPoolShare = true;
}
void OnDestroy()
{
    if (ownsBulletPoolShare)
        PrefabPoolManager.Instance.ReleasePoolShare(BulletPath);
}
// Pooled 敌人死亡走 RecycleObj，不 Destroy → 不触发上述 OnDestroy 释池

// —— 场景流：切场景 / ExitGame 前 ——
BundleResLoader.Instance.UnloadAll();
```

集成验证：`Assets/Test/comprehensiveTest`（`ComprehensiveTestSceneFlow`；见 `综合测试归档.md`）。

### 7.8 加载 Prefab 并替换贴图

```csharp
IAssetHandle prefab = BundleResLoader.Instance.Load<GameObject>("Model/Prefabs/tester");
GameObject go = prefab?.Instantiate();

IAssetHandle icon = BundleResLoader.Instance.Load<Sprite>("Icon/3");
Texture tex = icon?.GetAsset<Sprite>()?.texture;

if (go != null && tex != null)
{
    Material mat = go.GetComponentInChildren<Renderer>().material;
    mat.SetTexture("_BaseMap", tex);
}

// OnDestroy: Unload(icon, null); Unload(prefab, go);
```

### 7.9 跨包依赖

```csharp
IAssetHandle ui = BundleResLoader.Instance.Load<GameObject>("UI/UIRoot");
GameObject uiGo = ui?.Instantiate();
```

依赖包由清单 `bundles[]` 自动加载；须使用含依赖信息的打包清单（EditorTest 占位清单可能无 `bundles[]`）。

---

## 8. 其它注意

- 仅在主线程调用 `Load` / `Unload` / `LoadGameObject`。  
- `LoadUniTaskAsync` / `LoadGameObjectAsync` 当前为 Yield 一帧 + 同步 `Load`；加载失败时 `onFailed` / 判空后再决定是否重试。  
- **Bundle LRU**：单资源 `Release` 不保证 AB 立刻卸内存；短时间内重复 Load 同路径可能命中缓存（§5.6）。切场景务必 `UnloadAll()`。  
- **池路径**：业务侧对已在池中的 `loadPath` 应走 `PrefabPoolManager`，勿再 `Load` 同路径（否则多占 Resource Ref）。  
- Editor Play + 清单 `buildMode=EditorTest` 时在 Editor 内走 AssetDatabase；Player 走 AB，见 [ResLoader/README.md](../ResLoader/README.md)。  
- Ref 与范例细则见 **§6**；抄代码见 **§7**；逐步追踪见 **[RefCountAppendix.md](./RefCountAppendix.md)**。引用计数 **运行时 Trace** 为调试能力，见 [LoaderOptimizationPlan.md](./LoaderOptimizationPlan.md) §4，**不属于本文业务 API**。
