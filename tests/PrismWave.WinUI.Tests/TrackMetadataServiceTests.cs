using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class TrackMetadataServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"PrismWaveMetadataTests-{Guid.NewGuid():N}");

    public TrackMetadataServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task SaveAsync_WritesAndReloadsAllTagFields()
    {
        var path = CreateMinimalMp3();
        var service = new TrackMetadataService();

        var save = await service.SaveAsync(path, new TrackMetadataModel(
            "New Title", "Artist A;Artist B", "New Album", "Album Artist",
            2003, "Pop", "[00:01.00]Hello", null, true));
        Assert.Equal(TrackMetadataSaveResult.Success, save);

        var loaded = await service.LoadAsync(path);
        Assert.Equal("New Title", loaded.Title);
        Assert.Equal("Artist A", loaded.Artist);
        Assert.Equal("New Album", loaded.Album);
        Assert.Equal("Album Artist", loaded.AlbumArtist);
        Assert.Equal(2003u, loaded.Year);
        Assert.Equal("Pop", loaded.Genre);
        Assert.Equal("[00:01.00]Hello", loaded.Lyrics);
        Assert.True(loaded.IsWritable);
    }

    [Fact]
    public async Task SaveAsync_EmbedsAndRemovesCoverPicture()
    {
        var path = CreateMinimalMp3();
        var coverPath = Path.Combine(_tempDirectory, $"cover-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(coverPath, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4]);
        var service = new TrackMetadataService();

        var save = await service.SaveAsync(
            path,
            new TrackMetadataModel("T", "A", "B", "C", 0, "G", null, null, true),
            newCoverImagePath: coverPath);
        Assert.Equal(TrackMetadataSaveResult.Success, save);

        var withCover = await service.LoadAsync(path);
        Assert.NotNull(withCover.EmbeddedCoverBytes);
        Assert.Equal(8, withCover.EmbeddedCoverBytes!.Length);

        var remove = await service.SaveAsync(
            path,
            new TrackMetadataModel("T", "A", "B", "C", 0, "G", null, null, true),
            removeCover: true);
        Assert.Equal(TrackMetadataSaveResult.Success, remove);

        var withoutCover = await service.LoadAsync(path);
        Assert.Null(withoutCover.EmbeddedCoverBytes);
    }

    [Theory]
    [InlineData(".aac")]
    [InlineData(".dsf")]
    [InlineData(".dff")]
    public async Task UnsupportedFormats_AreRejectedAsReadOnly(string extension)
    {
        var path = Path.Combine(_tempDirectory, $"sample-{Guid.NewGuid():N}{extension}");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var service = new TrackMetadataService();

        var loaded = await service.LoadAsync(path);
        Assert.False(loaded.IsWritable);

        var save = await service.SaveAsync(
            path,
            new TrackMetadataModel("T", "A", "B", "C", 0, "G", null, null, true));
        Assert.Equal(TrackMetadataSaveResult.UnsupportedFormat, save);
    }

    [Fact]
    public async Task SaveAsync_ReportsFileLockedForReadOnlyFile()
    {
        var path = CreateMinimalMp3();
        var service = new TrackMetadataService();
        await service.SaveAsync(path, new TrackMetadataModel("T", "A", "B", "C", 0, "G", null, null, true));

        var originalAttributes = File.GetAttributes(path);
        File.SetAttributes(path, originalAttributes | FileAttributes.ReadOnly);
        try
        {
            var save = await service.SaveAsync(
                path,
                new TrackMetadataModel("Locked", "A", "B", "C", 0, "G", null, null, true));
            Assert.Equal(TrackMetadataSaveResult.FileLocked, save);
        }
        finally
        {
            File.SetAttributes(path, originalAttributes);
        }
    }

    private string CreateMinimalMp3()
    {
        var path = Path.Combine(_tempDirectory, $"sample-{Guid.NewGuid():N}.mp3");
        using var stream = File.Create(path);
        // Minimal ID3v2.3 header with an empty tag body.
        stream.Write([(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 0]);
        // Several MPEG-1 Layer III frames (128kbps / 44.1kHz) so TagLib can build properties.
        var frame = new byte[417];
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        frame[2] = 0x90;
        frame[3] = 0x64;
        for (var index = 0; index < 8; index++)
        {
            stream.Write(frame);
        }

        return path;
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_tempDirectory))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_tempDirectory, recursive: true);
    }
}
