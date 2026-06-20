using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// AssetCatalog 二进制编解码（魔数 VCAT，格式版本 1）。Editor 写入与运行时读取共用。
/// </summary>
public static class AssetCatalogBinaryCodec
{
    public const int FormatVersion = 1;
    public const string Magic = "VCAT";

    public static byte[] Serialize(AssetCatalog catalog)
    {
        if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));

        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteCatalog(writer, catalog);
            return stream.ToArray();
        }
    }

    public static bool TryDeserialize(byte[] data, out AssetCatalog catalog)
    {
        catalog = null;
        if (data == null || data.Length < Magic.Length + 2)
            return false;

        if (!HasBinaryMagic(data))
            return false;

        try
        {
            using (var stream = new MemoryStream(data, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            {
                catalog = ReadCatalog(reader);
                return catalog != null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AssetCatalogBinaryCodec] Deserialize failed: " + ex.Message);
            catalog = null;
            return false;
        }
    }

    public static bool TryLoadFromPath(string path, out AssetCatalog catalog)
    {
        catalog = null;
        if (string.IsNullOrEmpty(path))
            return false;

        byte[] data = ReadAllBytes(path);
        if (data == null || data.Length == 0)
            return false;

        return TryDeserialize(data, out catalog);
    }

    public static bool WriteToFile(string path, AssetCatalog catalog)
    {
        if (string.IsNullOrEmpty(path) || catalog == null)
            return false;

        catalog.catalogueHash = string.Empty;
        byte[] body = Serialize(catalog);
        catalog.catalogueHash = BundleIntegrityUtil.ComputeBytesSha256(body);
        byte[] finalBytes = Serialize(catalog);

        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(path, finalBytes);
        return true;
    }

    public static string ComputeCatalogueHash(AssetCatalog catalog)
    {
        if (catalog == null)
            return string.Empty;

        string previous = catalog.catalogueHash;
        catalog.catalogueHash = string.Empty;
        byte[] body = Serialize(catalog);
        catalog.catalogueHash = previous;
        return BundleIntegrityUtil.ComputeBytesSha256(body);
    }

    public static bool HasBinaryMagic(byte[] data)
    {
        if (data == null || data.Length < Magic.Length)
            return false;

        for (int i = 0; i < Magic.Length; i++)
        {
            if (data[i] != Magic[i])
                return false;
        }

        return true;
    }

    static byte[] ReadAllBytes(string path)
    {
        if (StreamingAssetsIO.IsNonFileProtocolPath(path))
            return StreamingAssetsIO.ReadAllBytes(path);

        if (!File.Exists(path))
            return null;

        return File.ReadAllBytes(path);
    }

    static void WriteCatalog(BinaryWriter writer, AssetCatalog catalog)
    {
        writer.Write(Encoding.UTF8.GetBytes(Magic));
        writer.Write((ushort)FormatVersion);

        WriteString(writer, catalog.version);
        writer.Write(catalog.buildNumber);
        WriteString(writer, catalog.platform);
        WriteString(writer, catalog.buildMode);
        WriteString(writer, catalog.packingRule);
        WriteString(writer, catalog.bundleRoot);
        WriteString(writer, catalog.resourceRoot);
        WriteString(writer, catalog.buildId);
        WriteString(writer, catalog.catalogueHash ?? string.Empty);
        WriteString(writer, catalog.compressionMode);
        WriteString(writer, catalog.cdnBaseUrl);

        WriteEntries(writer, catalog.entries);
        WriteBundles(writer, catalog.bundles);
    }

    static AssetCatalog ReadCatalog(BinaryReader reader)
    {
        byte[] magic = reader.ReadBytes(Magic.Length);
        if (magic.Length != Magic.Length)
            return null;

        for (int i = 0; i < Magic.Length; i++)
        {
            if (magic[i] != Magic[i])
                throw new InvalidDataException("Invalid catalogue magic");
        }

        ushort version = reader.ReadUInt16();
        if (version != FormatVersion)
            throw new InvalidDataException("Unsupported catalogue format version: " + version);

        var catalog = new AssetCatalog
        {
            version = ReadString(reader),
            buildNumber = reader.ReadInt32(),
            platform = ReadString(reader),
            buildMode = ReadString(reader),
            packingRule = ReadString(reader),
            bundleRoot = ReadString(reader),
            resourceRoot = ReadString(reader),
            buildId = ReadString(reader),
            catalogueHash = ReadString(reader),
            compressionMode = ReadString(reader),
            cdnBaseUrl = ReadString(reader),
            entries = ReadEntries(reader),
            bundles = ReadBundles(reader)
        };

        return catalog;
    }

    static void WriteEntries(BinaryWriter writer, AssetCatalogEntry[] entries)
    {
        if (entries == null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(entries.Length);
        foreach (AssetCatalogEntry entry in entries)
        {
            if (entry == null)
            {
                WriteString(writer, null);
                WriteString(writer, null);
                WriteString(writer, null);
                continue;
            }

            WriteString(writer, entry.assetPath);
            WriteString(writer, entry.bundleName);
            WriteString(writer, entry.assetName);
        }
    }

    static AssetCatalogEntry[] ReadEntries(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0)
            return null;

        var entries = new AssetCatalogEntry[count];
        for (int i = 0; i < count; i++)
        {
            entries[i] = new AssetCatalogEntry
            {
                assetPath = ReadString(reader),
                bundleName = ReadString(reader),
                assetName = ReadString(reader)
            };
        }

        return entries;
    }

    static void WriteBundles(BinaryWriter writer, BundleCatalogInfo[] bundles)
    {
        if (bundles == null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(bundles.Length);
        foreach (BundleCatalogInfo info in bundles)
        {
            if (info == null)
            {
                WriteString(writer, null);
                writer.Write(0);
                writer.Write(0L);
                WriteString(writer, null);
                writer.Write(0u);
                WriteStringArray(writer, null);
                WriteStringArray(writer, null);
                continue;
            }

            WriteString(writer, info.bundleName);
            writer.Write(info.resourcePriority);
            writer.Write(info.sizeBytes);
            WriteString(writer, info.fileHash);
            writer.Write(info.crc32);
            WriteStringArray(writer, info.dependencies);
            WriteStringArray(writer, info.dependenciesAll);
        }
    }

    static BundleCatalogInfo[] ReadBundles(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0)
            return null;

        var bundles = new BundleCatalogInfo[count];
        for (int i = 0; i < count; i++)
        {
            bundles[i] = new BundleCatalogInfo
            {
                bundleName = ReadString(reader),
                resourcePriority = reader.ReadInt32(),
                sizeBytes = reader.ReadInt64(),
                fileHash = ReadString(reader),
                crc32 = reader.ReadUInt32(),
                dependencies = ReadStringArray(reader),
                dependenciesAll = ReadStringArray(reader)
            };
        }

        return bundles;
    }

    static void WriteStringArray(BinaryWriter writer, string[] values)
    {
        if (values == null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(values.Length);
        foreach (string value in values)
            WriteString(writer, value);
    }

    static string[] ReadStringArray(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0)
            return null;

        var values = new string[count];
        for (int i = 0; i < count; i++)
            values[i] = ReadString(reader);

        return values;
    }

    static void WriteString(BinaryWriter writer, string value)
    {
        if (value == null)
        {
            writer.Write(-1);
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        if (bytes.Length > 0)
            writer.Write(bytes);
    }

    static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0)
            return null;

        if (length == 0)
            return string.Empty;

        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException("Unexpected end of catalogue string");

        return Encoding.UTF8.GetString(bytes);
    }
}
