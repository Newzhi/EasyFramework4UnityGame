using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Bundle 文件 SHA256 / CRC32 计算（Editor 写清单与运行时下载校验共用）。
/// </summary>
public static class BundleIntegrityUtil
{
    public static string ComputeFileSha256(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return string.Empty;

        using (var sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(filePath))
        {
            byte[] hash = sha.ComputeHash(stream);
            return ToHex(hash);
        }
    }

    public static uint ComputeFileCrc32(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return 0;

        byte[] bytes = File.ReadAllBytes(filePath);
        return ComputeCrc32(bytes);
    }

    public static string ComputeTextSha256(string text)
    {
        if (text == null)
            return string.Empty;

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return ComputeBytesSha256(bytes);
    }

    public static string ComputeBytesSha256(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        using (var sha = SHA256.Create())
            return ToHex(sha.ComputeHash(bytes));
    }

    public static uint ComputeCrc32(byte[] data)
    {
        if (data == null || data.Length == 0)
            return 0;

        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB88320u & ~((crc & 1) - 1));
        }

        return ~crc;
    }

    static string ToHex(byte[] hash)
    {
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
            sb.Append(b.ToString("x2"));

        return sb.ToString();
    }
}
