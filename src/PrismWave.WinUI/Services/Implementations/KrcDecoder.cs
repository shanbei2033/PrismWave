using System.IO.Compression;
using System.Text;

namespace PrismWave_WinUI.Services.Implementations;

public static class KrcDecoder
{
    private static readonly byte[] Key = "@Gaw]GtVKn@jRW!An"u8.ToArray();

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
