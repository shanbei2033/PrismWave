using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class PlaybackQueueServiceStructureTests
{
    [Fact]
    public void Service_ExposesOneStructuralQueueRevisionPath()
    {
        var source = ReadPlaybackService();

        Assert.Contains("public long QueueRevision { get; private set; }", source, StringComparison.Ordinal);
        Assert.Contains("private void AdvanceQueueRevision()", source, StringComparison.Ordinal);
        Assert.Contains("QueueRevision++;", ExtractMethod(source, "AdvanceQueueRevision"), StringComparison.Ordinal);
        Assert.Contains("AdvanceQueueRevision();", ExtractMethod(source, "ReorderQueue"), StringComparison.Ordinal);
        Assert.Contains("AdvanceQueueRevision();", ExtractMethod(source, "ClearQueue"), StringComparison.Ordinal);
        Assert.Contains("AdvanceQueueRevision();", ExtractMethod(source, "ReplaceQueuedTrack"), StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveFromQueue_RemovesOneEntryInsteadOfEveryMatchingId()
    {
        var method = ExtractMethod(ReadPlaybackService(), "RemoveFromQueue");

        Assert.DoesNotContain("RemoveAll", method, StringComparison.Ordinal);
        Assert.Contains("RemoveAt", method, StringComparison.Ordinal);
        Assert.Contains("AdvanceQueueRevision();", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ReorderQueue_DoesNotReloadCurrentTrack()
    {
        var method = ExtractMethod(ReadPlaybackService(), "ReorderQueue");

        Assert.DoesNotContain("LoadCurrentTrack", method, StringComparison.Ordinal);
        Assert.Contains("_queue.AddRange(tracks);", method, StringComparison.Ordinal);
    }

    private static string ReadPlaybackService() => File.ReadAllText(FindRepositoryFile(
        "src", "PrismWave.WinUI", "Services", "Implementations", "PlaybackService.cs"));

    private static string ExtractMethod(string source, string methodName)
    {
        var signatureIndex = source.IndexOf($"public void {methodName}(", StringComparison.Ordinal);
        if (signatureIndex < 0)
        {
            signatureIndex = source.IndexOf($"private void {methodName}(", StringComparison.Ordinal);
        }

        Assert.True(signatureIndex >= 0, $"Could not find method {methodName}.");
        var openingBrace = source.IndexOf('{', signatureIndex);
        Assert.True(openingBrace >= 0, $"Could not find opening brace for {methodName}.");
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[openingBrace..(index + 1)];
            }
        }

        throw new InvalidDataException($"Could not find closing brace for {methodName}.");
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository source file.");
    }
}
