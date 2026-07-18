using System.Buffers.Binary;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class AppIconAssetTests
{
    [Fact]
    public void Master_IsHighResolutionFlatArtwork()
    {
        var assets = FindRepositoryDirectory("src", "PrismWave.WinUI", "Assets");
        var (width, height) = ReadPngSize(Path.Combine(assets, "AppIconMaster.png"));
        var source = File.ReadAllText(Path.Combine(assets, "AppIconMaster.svg"));

        Assert.Equal((1024, 1024), (width, height));
        Assert.Equal(
            (1024, 1024),
            ReadPngSize(Path.Combine(FindRepositoryDirectory("assets"), "logo.png")));
        Assert.Contains("fill=\"#1e292f\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filter", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gradient", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ico_ContainsNativeSmallAndLargeFrames()
    {
        var path = Path.Combine(
            FindRepositoryDirectory("src", "PrismWave.WinUI", "Assets"),
            "AppIcon.ico");
        var bytes = File.ReadAllBytes(path);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));
        var sizes = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var sizeByte = bytes[6 + (index * 16)];
            sizes.Add(sizeByte == 0 ? 256 : sizeByte);
        }

        Assert.Equal(new[] { 16, 24, 32, 48, 64, 128, 256 }, sizes.OrderBy(size => size));
    }

    [Fact]
    public void UnplatedTaskbarAssets_AreRenderedAtNativeSizes()
    {
        var assets = FindRepositoryDirectory("src", "PrismWave.WinUI", "Assets");

        Assert.Equal(
            (24, 24),
            ReadPngSize(Path.Combine(assets, "Square44x44Logo.targetsize-24_altform-unplated.png")));
        Assert.Equal(
            (48, 48),
            ReadPngSize(Path.Combine(assets, "Square44x44Logo.targetsize-48_altform-lightunplated.png")));
    }

    private static (int Width, int Height) ReadPngSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 24);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        return (
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    private static string FindRepositoryDirectory(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository directory.");
    }
}
