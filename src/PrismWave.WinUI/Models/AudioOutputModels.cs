namespace PrismWave_WinUI.Models;

public enum AudioOutputRoute
{
    Mpv,
    WasapiShared,
    WasapiExclusive
}

public sealed record AudioOutputModeOptionModel(
    string Id,
    string DisplayName,
    string Description);

public sealed record AudioOutputDeviceOptionModel(
    string Id,
    string DisplayName);

public static class AudioOutputPolicy
{
    public const string CompatibilityId = "compatibility";
    public const string WasapiSharedId = "wasapi_shared";
    public const string WasapiExclusiveId = "wasapi_exclusive";

    public static IReadOnlyList<AudioOutputModeOptionModel> Options { get; } =
    [
        new(CompatibilityId, "MPV（自动）", "由 MPV 自动选择可用的音频输出。"),
        new(WasapiSharedId, "WASAPI 共享", "默认模式，可与其他应用同时播放。"),
        new(WasapiExclusiveId, "WASAPI 独占", "独占设备；失败后依次回退到共享和 MPV。")
    ];

    public static string NormalizeModeId(string? value) =>
        value?.Trim().ToLowerInvariant().Replace('-', '_') switch
        {
            CompatibilityId => CompatibilityId,
            WasapiExclusiveId or "wasapiexclusive" => WasapiExclusiveId,
            WasapiSharedId or "wasapishared" => WasapiSharedId,
            _ => WasapiSharedId
        };

    public static IReadOnlyList<AudioOutputRoute> BuildFallbackChain(string? value) =>
        NormalizeModeId(value) switch
        {
            WasapiExclusiveId =>
                [AudioOutputRoute.WasapiExclusive, AudioOutputRoute.WasapiShared, AudioOutputRoute.Mpv],
            CompatibilityId => [AudioOutputRoute.Mpv],
            _ => [AudioOutputRoute.WasapiShared, AudioOutputRoute.Mpv]
        };

    public static string GetRouteDisplayName(AudioOutputRoute route) => route switch
    {
        AudioOutputRoute.WasapiExclusive => "WASAPI 独占",
        AudioOutputRoute.WasapiShared => "WASAPI 共享",
        _ => "MPV（自动）"
    };
}
