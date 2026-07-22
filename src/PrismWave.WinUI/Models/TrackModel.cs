namespace PrismWave_WinUI.Models;

public sealed record TrackModel(
    string Id,
    string Path,
    string Title,
    string Artist,
    string Album,
    string Duration,
    string? CoverPath,
    bool IsRemote = false,
    string Provider = "Local",
    string? PlaybackUrl = null,
    IReadOnlyDictionary<string, string>? PlaybackHeaders = null,
    double DurationSeconds = 0,
    bool IsFavorite = false,
    int BitrateKbps = 0,
    int SampleRateHz = 0,
    int Channels = 0,
    long FileSizeBytes = 0,
    string? Codec = null,
    string? OnlineCandidateKey = null,
    string? OnlineProviderTrackId = null)
{
    public string PlaybackSource => IsRemote && !string.IsNullOrWhiteSpace(PlaybackUrl)
        ? PlaybackUrl.Trim()
        : Path;

    public string FileName => string.IsNullOrWhiteSpace(Path) ? Title : System.IO.Path.GetFileName(Path);

    public string FavoriteGlyph => IsFavorite ? "\uEB52" : "\uEB51";

    public string BitrateLabel => BitrateKbps > 0 ? $"{BitrateKbps} kbps" : "Unknown";

    public string SampleRateLabel => SampleRateHz > 0
        ? $"{SampleRateHz / 1000d:0.##} kHz"
        : "Unknown";

    public string ChannelLabel => Channels switch
    {
        1 => "Mono",
        2 => "Stereo",
        > 2 => $"{Channels} channels",
        _ => "Unknown"
    };

    public string FileSizeLabel
    {
        get
        {
            if (FileSizeBytes <= 0)
            {
                return "Unknown";
            }

            var megabytes = FileSizeBytes / 1024d / 1024d;
            return megabytes >= 1024
                ? $"{megabytes / 1024d:0.##} GB"
                : $"{megabytes:0.##} MB";
        }
    }
}
