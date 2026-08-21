using System.IO.Compression;
using System.Text;

namespace PrismWave_WinUI.Services.Implementations;

public static class KrcDecoder
{
    // 2026-08 酷狗升级后的新密钥（16 字节，来源：Pure-music krc_decryptor.dart）。
    // 旧密钥 "@Gaw]GtVKn@jRW!An" 已失效。
    private static readonly byte[] Key =
    {
        0x40, 0x47, 0x61, 0x77, 0x5E, 0x32, 0x74, 0x47,
        0x51, 0x36, 0x31, 0x2D, 0xCE, 0xD2, 0x6E, 0x69
    };

    public static string? Decrypt(string? base64Content)
    {
        if (string.IsNullOrWhiteSpace(base64Content))
        {
            return null;
        }

        try
        {
            var encrypted = Convert.FromBase64String(base64Content.Trim());
            if (encrypted.Length <= 4 || !IsKrcHeader(encrypted))
            {
                return null;
            }

            var payload = new byte[encrypted.Length - 4];
            for (var index = 0; index < payload.Length; index++)
            {
                payload[index] = (byte)(encrypted[index + 4] ^ Key[index % Key.Length]);
            }

            using var input = new MemoryStream(payload, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(zlib, new UTF8Encoding(false, false));
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is FormatException
            or InvalidDataException
            or IOException)
        {
            return null;
        }
    }

    private static bool IsKrcHeader(byte[] value)
    {
        return value[0] == (byte)'k'
            && value[1] == (byte)'r'
            && value[2] == (byte)'c'
            && value[3] == (byte)'1';
    }
}
