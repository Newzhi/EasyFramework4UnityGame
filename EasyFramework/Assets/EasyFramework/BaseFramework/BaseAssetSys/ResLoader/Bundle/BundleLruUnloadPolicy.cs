using UnityEngine;

/// <summary>
/// Bundle 层 LRU 延迟卸载策略：Ref 归零后不立即 <c>Unload</c>，按清单 <see cref="ResourcePriority"/> 与空闲时长淘汰。
/// </summary>
public static class BundleLruUnloadPolicy
{
    /// <summary>Ref=0 且仍驻留内存的空闲包上限；超出时按 LRU + 优先级强制淘汰（不含 Critical）。</summary>
    public const int MaxIdleBundles = 32;

    /// <summary>Critical 不参与 grace / 超上限淘汰，仅 <see cref="BundleManager.UnloadAll"/> 可卸。</summary>
    public static bool IsNeverUnload(int resourcePriority)
    {
        return (ResourcePriority)Mathf.Clamp(resourcePriority, 0, 4) == ResourcePriority.Critical;
    }

    /// <summary>按优先级返回 Ref=0 后的保留秒数（<see cref="ResourcePriority"/> 越小保留越久）。</summary>
    public static float GetGraceSeconds(int resourcePriority)
    {
        if (IsNeverUnload(resourcePriority))
            return float.PositiveInfinity;

        switch ((ResourcePriority)Mathf.Clamp(resourcePriority, 0, 4))
        {
            case ResourcePriority.High:
                return 20f;
            case ResourcePriority.Normal:
                return 15f;
            case ResourcePriority.Low:
                return 10f;
            case ResourcePriority.Optional:
                return 5f;
            default:
                return 15f;
        }
    }
}
