using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Infrastructure.Audio;

public sealed class TransientPlaybackSessionGuard
{
    private long _revision;
    public long ActiveRevision { get; private set; }
    public PlaybackSessionSnapshot? Snapshot { get; private set; }

    public long Begin(PlaybackSessionSnapshot snapshot)
    {
        if (ActiveRevision != 0)
        {
            return ActiveRevision;
        }

        Snapshot = snapshot with { Queue = snapshot.Queue.ToArray() };
        ActiveRevision = ++_revision;
        return ActiveRevision;
    }

    public bool IsCurrent(long revision) => revision != 0 && revision == ActiveRevision;

    public bool TryEnd(long revision, out PlaybackSessionSnapshot? snapshot)
    {
        snapshot = null;
        if (!IsCurrent(revision) || Snapshot is null)
        {
            return false;
        }

        snapshot = Snapshot;
        Snapshot = null;
        ActiveRevision = 0;
        _revision++;
        return true;
    }
}
