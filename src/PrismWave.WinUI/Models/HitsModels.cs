namespace PrismWave_WinUI.Models;

public enum HitsStatusKind
{
    Idle,
    Loading,
    Ready,
    OffAir,
    Standby,
    NoNetwork,
    CloudTimeout,
    Unavailable
}

public sealed record HitsTimeWindowModel(
    string Label,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt)
{
    public bool Contains(DateTimeOffset value) => value >= StartAt && value < EndAt;
}

public sealed record HitsScheduleItemModel(
    int Slot,
    string StationTrackId,
    string Window,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    TrackModel Track)
{
    public bool Contains(DateTimeOffset value) => value >= StartAt && value < EndAt;
    public string AirTimeLabel => $"{StartAt:HH:mm} - {EndAt:HH:mm} UTC";
}

public sealed record HitsStateSnapshot(
    HitsStatusKind Status,
    string Description,
    string EditionDate,
    DateTimeOffset CurrentUtcTime,
    IReadOnlyList<HitsScheduleItemModel> Tracks,
    HitsScheduleItemModel? CurrentTrack,
    HitsScheduleItemModel? NextTrack,
    double PlaybackOffsetSeconds,
    bool UsingCache,
    bool IsRefreshing,
    string? Error = null)
{
    public bool IsAvailable => Status == HitsStatusKind.Ready && CurrentTrack is not null;
    public string StatusLabel => Status switch
    {
        HitsStatusKind.Loading => "Loading",
        HitsStatusKind.Ready => "On air",
        HitsStatusKind.OffAir => "Off air",
        HitsStatusKind.Standby => "Standby",
        HitsStatusKind.NoNetwork => "No network",
        HitsStatusKind.CloudTimeout => "Cloud timeout",
        HitsStatusKind.Unavailable => "Unavailable",
        _ => "Idle"
    };

    public static HitsStateSnapshot Idle { get; } = new(
        HitsStatusKind.Idle,
        "HITS has not been loaded.",
        string.Empty,
        DateTimeOffset.UtcNow,
        Array.Empty<HitsScheduleItemModel>(),
        null,
        null,
        0,
        false,
        false);
}
