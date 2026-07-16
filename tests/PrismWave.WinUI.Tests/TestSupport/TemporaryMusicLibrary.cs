using System.Text;

namespace PrismWave_WinUI.Tests.TestSupport;

internal sealed class TemporaryMusicLibrary : IDisposable
{
    public TemporaryMusicLibrary()
    {
        Root = Path.Combine(Path.GetTempPath(), "PrismWave.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(string relativePath, byte[]? contents = null)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents ?? [0x00, 0x01, 0x02, 0x03]);
        return path;
    }

    public string CreateWave(
        string relativePath,
        string? title = null,
        string? artist = null,
        string? album = null)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0u);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        WriteChunk(writer, "fmt ", CreatePcmFormat());
        if (title is not null || artist is not null || album is not null)
        {
            using var listStream = new MemoryStream();
            using (var listWriter = new BinaryWriter(listStream, Encoding.UTF8, leaveOpen: true))
            {
                listWriter.Write(Encoding.ASCII.GetBytes("INFO"));
                WriteInfo(listWriter, "INAM", title);
                WriteInfo(listWriter, "IART", artist);
                WriteInfo(listWriter, "IPRD", album);
            }

            WriteChunk(writer, "LIST", listStream.ToArray());
        }

        WriteChunk(writer, "data", new byte[8820]);
        writer.Flush();
        stream.Position = 4;
        writer.Write((uint)(stream.Length - 8));
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
        }
    }

    private static byte[] CreatePcmFormat()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(44100u);
        writer.Write(88200u);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        return stream.ToArray();
    }

    private static void WriteInfo(BinaryWriter writer, string id, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var bytes = Encoding.Default.GetBytes(value + "\0");
        WriteChunk(writer, id, bytes);
    }

    private static void WriteChunk(BinaryWriter writer, string id, byte[] bytes)
    {
        writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
        if ((bytes.Length & 1) != 0)
        {
            writer.Write((byte)0);
        }
    }
}
