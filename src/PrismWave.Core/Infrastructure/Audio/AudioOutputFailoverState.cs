using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Infrastructure.Audio;

public sealed class AudioOutputFailoverState
{
    private IReadOnlyList<AudioOutputRoute> _routes = Array.Empty<AudioOutputRoute>();
    private int _index;

    public AudioOutputFailoverState(string preferredModeId)
    {
        Reset(preferredModeId);
    }

    public string PreferredModeId { get; private set; } = AudioOutputPolicy.WasapiSharedId;
    public AudioOutputRoute ActiveRoute => _routes[_index];
    public string? FallbackReason { get; private set; }

    public void Reset(string preferredModeId)
    {
        PreferredModeId = AudioOutputPolicy.NormalizeModeId(preferredModeId);
        _routes = AudioOutputPolicy.BuildFallbackChain(PreferredModeId);
        _index = 0;
        FallbackReason = null;
    }

    public bool TryAdvance(string reason)
    {
        if (_index + 1 >= _routes.Count)
        {
            return false;
        }

        _index++;
        FallbackReason = reason;
        return true;
    }
}
