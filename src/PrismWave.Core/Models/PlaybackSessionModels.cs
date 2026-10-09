namespace PrismWave_WinUI.Models;

public sealed record PlaybackSessionSnapshot(
    TrackModel? Track,
    IReadOnlyList<TrackModel> Queue,
    PlaybackMode Mode,
    double PositionSeconds,
    double DurationSeconds,
    bool ShouldResume);
