namespace PrismWave_WinUI.Models;

public sealed record WindowsDsdDeviceModel(
    string Id,
    string Name,
    string Driver,
    int InputChannels,
    int OutputChannels,
    bool SupportsNativeDsd)
{
    public string DisplayName => Id == "auto"
        ? "Automatic (first available ASIO device)"
        : $"{Name} · {(SupportsNativeDsd ? "Native DSD" : "DoP")}";

    public static WindowsDsdDeviceModel Automatic { get; } = new(
        "auto",
        "Automatic",
        string.Empty,
        0,
        0,
        false);
}
