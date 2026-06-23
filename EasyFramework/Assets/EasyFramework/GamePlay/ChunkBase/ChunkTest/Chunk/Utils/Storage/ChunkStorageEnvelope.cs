using System;

/// <summary>
/// 区块存档文件 Envelope：magic + version + serializerKind + payloadLength + body。
/// Legacy：前 4 字节非 CHKT 时整文件视为无头 JSON。
/// </summary>
public static class ChunkStorageEnvelope
{
    public const int Magic = 0x43484B54; // "CHKT"
    public const byte CurrentVersion = 1;
    public const int HeaderSize = 10;

    public static byte[] Write(byte serializerKind, byte[] payloadBytes)
    {
        payloadBytes ??= Array.Empty<byte>();
        var file = new byte[HeaderSize + payloadBytes.Length];
        WriteInt32BigEndian(file, 0, Magic);
        file[4] = CurrentVersion;
        file[5] = serializerKind;
        WriteInt32BigEndian(file, 6, payloadBytes.Length);
        if (payloadBytes.Length > 0)
        {
            Buffer.BlockCopy(payloadBytes, 0, file, HeaderSize, payloadBytes.Length);
        }

        return file;
    }

    public static bool TryRead(
        byte[] fileBytes,
        out byte serializerKind,
        out byte[] payloadBytes,
        out bool isLegacy)
    {
        serializerKind = ChunkStorageSerializerKind.Json;
        payloadBytes = null;
        isLegacy = false;

        if (fileBytes is null || fileBytes.Length == 0)
        {
            return false;
        }

        if (fileBytes.Length < 4 || ReadInt32BigEndian(fileBytes, 0) != Magic)
        {
            isLegacy = true;
            payloadBytes = fileBytes;
            return true;
        }

        if (fileBytes.Length < HeaderSize)
        {
            return false;
        }

        byte version = fileBytes[4];
        if (version != CurrentVersion)
        {
            return false;
        }

        serializerKind = fileBytes[5];
        int payloadLength = ReadInt32BigEndian(fileBytes, 6);
        if (payloadLength < 0 || HeaderSize + payloadLength > fileBytes.Length)
        {
            return false;
        }

        if (payloadLength == 0)
        {
            payloadBytes = Array.Empty<byte>();
            return true;
        }

        payloadBytes = new byte[payloadLength];
        Buffer.BlockCopy(fileBytes, HeaderSize, payloadBytes, 0, payloadLength);
        return true;
    }

    private static int ReadInt32BigEndian(byte[] buffer, int offset)
    {
        return (buffer[offset] << 24)
               | (buffer[offset + 1] << 16)
               | (buffer[offset + 2] << 8)
               | buffer[offset + 3];
    }

    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}

public static class ChunkStorageSerializerKind
{
    public const byte Json = 0;
    public const byte HeightmapBinary = 1;
}
