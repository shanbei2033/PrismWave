using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Infrastructure.Audio;

public interface IPlaybackEngineFactory
{
    IPlaybackEngine Create(AudioOutputRoute route, string outputDevice);
}
