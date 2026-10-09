namespace PrismWave_WinUI.Services.Implementations;

internal readonly record struct PlaybackLoadContext(
    long Sequence,
    int Revision,
    string SourceKey,
    bool Autoplay);

internal sealed class PlaybackLoadEventGuard
{
    private long _nextSequence;
    private PlaybackLoadContext? _expected;

    public PlaybackLoadContext BeginLoad(
        int revision,
        string sourceKey,
        bool autoplay)
    {
        var context = new PlaybackLoadContext(
            Interlocked.Increment(ref _nextSequence),
            revision,
            sourceKey,
            autoplay);
        _expected = context;
        return context;
    }

    public bool TryAccept(
        long sequence,
        string sourceKey,
        int currentRevision,
        string? currentSourceKey,
        out PlaybackLoadContext accepted)
    {
        if (_expected is PlaybackLoadContext expected
            && expected.Sequence == sequence
            && expected.Revision == currentRevision
            && string.Equals(expected.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(expected.SourceKey, currentSourceKey, StringComparison.OrdinalIgnoreCase))
        {
            accepted = expected;
            return true;
        }

        accepted = default;
        return false;
    }

    public void Invalidate()
    {
        _expected = null;
    }
}
