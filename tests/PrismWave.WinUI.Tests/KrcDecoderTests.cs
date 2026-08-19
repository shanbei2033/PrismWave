using System.IO.Compression;
using System.Text;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class KrcDecoderTests
{
    [Fact]
    public void Decrypt_RestoresPlaintextFromEncodedKrc()
    {
        const string plain = "[offset:0]\n[0,3000]你<0,300,0>好<300,400,0>";

        var encoded = EncodeKrc(plain);

        Assert.Equal(plain, KrcDecoder.Decrypt(encoded));
    }

    [Fact]
    public void Decrypt_ReturnsNullForInvalidInput()
    {
        Assert.Null(KrcDecoder.Decrypt(null));
        Assert.Null(KrcDecoder.Decrypt(string.Empty));
        Assert.Null(KrcDecoder.Decrypt("not-valid-base64!!!"));
    }

    [Fact]
    public void Decrypt_ReturnsNullWhenMagicHeaderMissing()
    {
        var bytes = Convert.FromBase64String(EncodeKrc("[0,1000]x<0,100,0>"));
        bytes[0] = (byte)'x';

        Assert.Null(KrcDecoder.Decrypt(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Decrypt_ReturnsNullWhenPayloadCorrupted()
    {
        var bytes = Convert.FromBase64String(EncodeKrc("[0,1000]x<0,100,0>"));
        bytes[^1] ^= 0xFF;

        Assert.Null(KrcDecoder.Decrypt(Convert.ToBase64String(bytes)));
    }

    private static string EncodeKrc(string plain)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Encoding.UTF8.GetBytes(plain));
        }

        var key = "@Gaw]GtVKn@jRW!An"u8.ToArray();
        var payload = compressed.ToArray();
        var encrypted = new byte[4 + payload.Length];
        encrypted[0] = (byte)'k';
        encrypted[1] = (byte)'r';
        encrypted[2] = (byte)'c';
        encrypted[3] = (byte)'1';
        for (var index = 0; index < payload.Length; index++)
        {
            encrypted[4 + index] = (byte)(payload[index] ^ key[index % key.Length]);
        }

        return Convert.ToBase64String(encrypted);
    }
}
