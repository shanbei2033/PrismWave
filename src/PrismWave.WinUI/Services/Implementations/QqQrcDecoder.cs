using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PrismWave_WinUI.Services.Implementations;

public static class QqQrcDecoder
{
    private static readonly byte[] Key =
    {
        0x21, 0x40, 0x23, 0x29, 0x28, 0x2A, 0x24, 0x25,
        0x31, 0x32, 0x33, 0x5A, 0x58, 0x43, 0x21, 0x40,
        0x21, 0x40, 0x23, 0x29, 0x28, 0x4E, 0x48, 0x4C
    };

    public static string? Decrypt(string encryptedLyrics)
    {
        var value = encryptedLyrics.Trim();
        if (value.Length == 0 || value.Length % 16 != 0)
        {
            return null;
        }

        try
        {
            var encrypted = Convert.FromHexString(value);
            using var tripleDes = TripleDES.Create();
            tripleDes.Mode = CipherMode.ECB;
            tripleDes.Padding = PaddingMode.None;
            tripleDes.Key = Key;
            using var decryptor = tripleDes.CreateDecryptor();
            var compressed = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            using var input = new MemoryStream(compressed, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(zlib, new UTF8Encoding(false, false));
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is FormatException
            or CryptographicException
            or InvalidDataException
            or IOException)
        {
            return null;
        }
    }
}
