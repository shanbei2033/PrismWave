namespace PrismWave_WinUI.Models;

public sealed record OnlineProviderTrackModel(
    string Provider,
    string ProviderTrackId,
    string Title,
    string Artist,
    string Album,
    double DurationSeconds,
    string? CoverUrl = null,
    string? DirectAudioUrl = null,
    bool RequiresVip = false)
{
    public string Descriptor =>
        $"online://{Provider.ToLowerInvariant()}/{Uri.EscapeDataString(ProviderTrackId)}";

    public string ProviderLabel => Provider.ToLowerInvariant() switch
    {
        "audius" => "Audius",
        "netease" => "NetEase",
        "kuwo" => "Kuwo",
        "migu" => "Migu",
        "qq" => "QQ Music",
        "kugou" => "Kugou",
        "taihe" => "Taihe",
        _ => Provider
    };
}
