namespace PrismWave_WinUI.Models;

public sealed record OnlineAudioCacheStatus(
    string DirectoryPath,
    long CurrentBytes,
    long MaximumBytes,
    int FileCount,
    bool IsAtCapacity)
{
    public double CurrentGigabytes => CurrentBytes / 1024d / 1024d / 1024d;
    public double MaximumGigabytes => MaximumBytes / 1024d / 1024d / 1024d;
}
