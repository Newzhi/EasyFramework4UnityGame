using UnityEditor;

/// <summary>
/// 将 <see cref="BundleCompressionMode"/> 与执行模式映射为 Unity <see cref="BuildAssetBundleOptions"/>。
/// </summary>
public static class BuildAssetBundleOptionsFactory
{
    public static BuildAssetBundleOptions Resolve(BuildSetting setting, BundleBuildExecutionMode executionMode)
    {
        BuildAssetBundleOptions options = BuildAssetBundleOptions.None;

        if (setting != null)
        {
            switch (setting.compressionMode)
            {
                case BundleCompressionMode.LZ4Chunk:
                    options |= BuildAssetBundleOptions.ChunkBasedCompression;
                    break;
                case BundleCompressionMode.Uncompressed:
                    options |= BuildAssetBundleOptions.UncompressedAssetBundle;
                    break;
                case BundleCompressionMode.LZMA:
                default:
                    break;
            }
        }

        if (executionMode == BundleBuildExecutionMode.FullOverwrite)
            options |= BuildAssetBundleOptions.ForceRebuildAssetBundle;

        return options;
    }
}
