using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Infrastructure.Audio;

public sealed class MpvPlaybackEngineFactory : IPlaybackEngineFactory
{
    public IPlaybackEngine Create(AudioOutputRoute route, string outputDevice) =>
        new MpvPlaybackEngine(route, outputDevice);
}
