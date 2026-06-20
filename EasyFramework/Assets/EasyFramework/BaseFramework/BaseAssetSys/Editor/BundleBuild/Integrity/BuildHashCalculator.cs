using System.IO;

/// <summary>
/// Editor 侧包装 <see cref="BundleIntegrityUtil"/>，供 Manifest 与 Catalogue 写入。
/// </summary>
public static class BuildHashCalculator
{
    public static string ComputeFileSha256(string filePath) => BundleIntegrityUtil.ComputeFileSha256(filePath);

    public static uint ComputeFileCrc32(string filePath) => BundleIntegrityUtil.ComputeFileCrc32(filePath);

    public static string ComputeTextSha256(string text) => BundleIntegrityUtil.ComputeTextSha256(text);

    public static string ComputeBytesSha256(byte[] bytes) => BundleIntegrityUtil.ComputeBytesSha256(bytes);

    public static uint ComputeCrc32(byte[] data) => BundleIntegrityUtil.ComputeCrc32(data);
}
