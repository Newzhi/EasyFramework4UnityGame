# AbstractAssets 模块说明

> 路径：`BaseAssetSys/AbstractAssets/`  
> 对外接口：`IAssetHandle.cs`、`AssetReference.cs`；内部实现：`AbstractResource.cs`

---

## 职责

- 封装 **单个 AB 内资源** 的加载结果与 **Resource 层引用计数**。
- 业务 **不** 直接 `AssetBundle.LoadAsset` / `Unload`，也不直接依赖 `AbstractResource`；通过 `IAssetHandle` + `Release()` 与 `BundleManager` 联动。

---

## 引用计数

| 操作 | 行为 |
|------|------|
| `Load` 命中缓存 | `AddReference()`，Ref+1 |
| `Release()` | Ref-1；Ref=0 时 `UnLoad()` |
| `UnLoad()` | 清空 asset 引用、对称 `ReleaseBundle(依赖+主包)`（Bundle Ref=0 走 LRU 延迟卸包）、触发 `onUnLoad` 回调 |

`onUnLoad` 由 `BundleResLoader` 注册，用于从 `resourceDic` 移除缓存项（见加载器说明 §4）。

---

## 业务 API 对应

| 设计基线需求 | 本模块 |
|--------------|--------|
| §5 卸载单个资源 | ✅ `Release()` |
| §5 Instantiate | ✅ `Instantiate()`（实例 Destroy 与 Release 无关） |
| 自动 Release（非池） | ✅ `AssetReference` + `BundleResLoader.LoadGameObject` |

---

## 已知限制

- 实例 `Destroy` 与 `Release` 分离：模块持句柄则模块 `Release`；实例自持用 `LoadGameObject` + `AssetReference`。
- **池化实例禁止挂 `AssetReference`**（句柄由 `PrefabPool` 管理）。
- DestroyInstance / AutoUnload 为 **延后项**，见 [Docs/MainRoadmap.md](../Docs/MainRoadmap.md) §4。

---

## 相关文档

- [ResLoader/README.md](../ResLoader/README.md)
- [ResLoader/LoaderDesignGuide.md](../ResLoader/LoaderDesignGuide.md)  
- [Docs/MainRoadmap.md](../Docs/MainRoadmap.md)  
- [Docs/BusinessApiAndCdnPlanning.md](../Docs/BusinessApiAndCdnPlanning.md)
