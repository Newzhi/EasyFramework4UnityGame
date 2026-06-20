# ResLoader 模块说明

> 路径：`BaseAssetSys/ResLoader/`  
> 运行时加载侧；与打包侧 `Editor/` + `BundleRuleConfig/` 通过 **二进制资源清单（`catalog.bytes`）** 衔接。

---

## 加载侧架构图（业务无感知）

```mermaid
flowchart TD
    Biz["业务 BundleResLoader.Load loadPath"]
    BRL["BundleResLoader 门面 Ref 缓存"]
    AR["AbstractResource"]
    Router["AssetRouter RouteAssetSource 自动选源"]
    P1["AbBundleProvider"]
    P2["EditorResourcesProvider"]
    P3["ResourcesProvider"]
    P4["CdnBundleProvider NETCDN"]
    BM["BundleManager"]
    ADB["AssetDatabase"]
    RL["Resources.Load"]
    Resolver["IBundlePathResolver"]

    Biz --> BRL --> AR
    AR --> Router
    Router --> P1 --> BM
    Router --> P2 --> ADB
    Router --> P3 --> RL
    Router --> P4 --> Resolver --> BM
```

| 层 | 对业务可见 | 职责 |
|----|------------|------|
| `BundleResLoader` | 是 | 唯一入口；Catalogue 查表；`IAssetHandle`；Ref |
| `AssetRouter` | **否** | 根据 path / Catalogue / 环境 **自动** `RouteAssetSource` |
| `IAssetProvider` | 否 | 各来源 Load / Release 实现 |

业务始终：`BundleResLoader.Instance.Load<T>("path")`，**不**直接选 `AssetSource` 或 NETCDN。

---

## 分层与子目录

```text
业务代码
    ▼
Business/          BundleResLoader     业务 API、Resource 缓存、懒 Init
    ▼
AbstractAssets/    AbstractResource    Resource 层 Ref（模块外，见同级目录）
    ▼
Router/            AssetRouter         四源路由 + Provider
    ├─ ABUNDLE / RESOURCES / EDITORRESOURCES / NETCDN
    ▼
Bundle/            BundleManager       .bundle 容器、依赖 Acquire、路径解析
Catalogue/         CatalogueReader     读清单 entries / bundles[]
ContentPackage/    ContentPackageService  DLC/Mod 挂载（IContentPackageGate）
```

| 目录 | 文件 | 职责 |
|------|------|------|
| `Business/` | `BundleResLoader.cs` | 单例入口：`Load` / `LoadUniTaskAsync` / `UnloadAll` |
| `Bundle/` | `BundleManager.cs` | Bundle Ref、`AcquireBundleWithDependencies`、LRU |
| `Bundle/` | `DefaultBundlePathResolver.cs` | 多 bundles 根（cache → 首包）；递归查找 |
| `Catalogue/` | `CatalogueReader.cs` | 读 `catalog.bytes`；Editor 可回退工程内 `AssetCatalog.bytes` |
| `Catalogue/` | `AssetCatalogBinaryCodec.cs` | 二进制编解码（定义在 `BundleRuleConfig/Catalogue/`） |
| `Catalogue/` | `StreamingAssetsIO.cs` | Android `jar:` 等 StreamingAssets 读二进制/文本 |
| `ContentPackage/` | `ContentPackageService.cs` | DLC `Merge` / `Unmerge` |
| `Cdn/` | `CdnCatalogueSyncService.cs` | 远程 `catalog.bytes` 热更 |
| `Router/` | `AssetRouter.cs` | `RouteAssetSource` + `Load` / `Release` |

---

## AssetRouter 路由（业务无感）

| 源 | 条件 |
|----|------|
| `RESOURCES` | `loadPath` 以 `Resources/` 开头 |
| `EDITORRESOURCES` | Editor Play 且 Catalogue `buildMode == EditorTest` |
| `NETCDN` | 非 EditorTest 且本地无 bundle |
| `ABUNDLE` | 默认 |

---

## 与打包侧的边界

| 打包侧产出 | 加载侧消费 |
|------------|------------|
| `{平台根}/Base/Bundles/*.bundle` | `BundleManager.AcquireBundle` |
| `{平台根}/Base/Version/catalog.bytes` | `CatalogueReader.LoadFromBundleRoot` |
| `BundleRuleConfig/Catalogue/AssetCatalog.bytes` | Editor 无 StreamingAssets 时 `LoadFromProjectCatalogue` |
| `buildMode` 写入清单 | `AssetRouter` 决定是否 Editor 模拟 |
| DLC `catalog.fragment.bytes` | `ContentPackageService.TryMount` → `CatalogueReader.Merge` |

路径工具 `BundlePlatformPaths` 在 **`BundleRuleConfig/`**（构建与运行时共用）。

---

## 详细设计

[LoaderDesignGuide.md](./LoaderDesignGuide.md)

## 相关文档

- [Docs/CatalogueReference.md](../Docs/CatalogueReference.md)  
- [Docs/MainRoadmap.md](../Docs/MainRoadmap.md)  
- [Docs/BusinessApiAndCdnPlanning.md](../Docs/BusinessApiAndCdnPlanning.md)  
- [AbstractAssets/README.md](../AbstractAssets/README.md)
