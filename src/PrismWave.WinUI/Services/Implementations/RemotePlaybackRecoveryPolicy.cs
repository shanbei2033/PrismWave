using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Implementations;

internal enum RemotePlaybackRecoveryAction
{
    None,
    RetryAudioOutput,
    ResolveNextSource,
    ResolveNextSourceAndResume
}

internal sealed class RemotePlaybackRecoveryPolicy
{
    private const int MaxOpeningSourceAttempts = 3;

    private readonly HashSet<string> _sourceAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excludedCandidateKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excludedPlaybackUrls = new(StringComparer.Ordinal);
    private string? _trackId;
    private string? _currentCandidateKey;
    private string? _currentPlaybackUrl;
    private bool _currentSourceOpened;
    private bool _openingRecoveryScheduled;
    private bool _audioOutputRetryScheduled;
    private bool _postOpenRecoveryScheduled;
    private bool _postOpenRecoveryInProgress;

    public IReadOnlyCollection<string> ExcludedCandidateKeys => _excludedCandidateKeys;

    public OnlinePlaybackExclusions Exclusions => new(
        _excludedCandidateKeys,
        _excludedPlaybackUrls);

    public int SourceAttemptCount => _sourceAttempts.Count;

    public void BeginTrack(string trackId)
    {
        _trackId = trackId;
        _currentCandidateKey = null;
        _currentPlaybackUrl = null;
        _currentSourceOpened = false;
        _openingRecoveryScheduled = false;
        _audioOutputRetryScheduled = false;
        _postOpenRecoveryScheduled = false;
        _postOpenRecoveryInProgress = false;
        _sourceAttempts.Clear();
        _excludedCandidateKeys.Clear();
        _excludedPlaybackUrls.Clear();
    }

    public bool BeginSourceAttempt(string trackId, string candidateKey)
    {
        return BeginSourceAttempt(trackId, candidateKey, playbackUrl: null);
    }

    public bool BeginSourceAttempt(
        string trackId,
        string candidateKey,
        string? playbackUrl)
    {
        if (!string.Equals(_trackId, trackId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(candidateKey)
            || !_sourceAttempts.Add(candidateKey.Trim()))
        {
            return false;
        }

        _currentCandidateKey = candidateKey.Trim();
        _currentPlaybackUrl = OnlinePlaybackExclusions.NormalizePlaybackUrl(playbackUrl);
        _currentSourceOpened = false;
        _openingRecoveryScheduled = false;
        return true;
    }

    public void MarkOpened(string trackId)
    {
        if (string.Equals(_trackId, trackId, StringComparison.Ordinal))
        {
            _currentSourceOpened = true;
            _postOpenRecoveryInProgress = false;
        }
    }

    public RemotePlaybackRecoveryAction DecideFailure(
        string trackId,
        bool isRemote,
        OnlinePlaybackFailureKind failureKind)
    {
        if (!isRemote || !string.Equals(_trackId, trackId, StringComparison.Ordinal))
        {
            return RemotePlaybackRecoveryAction.None;
        }

        if (failureKind == OnlinePlaybackFailureKind.AudioOutput)
        {
            if (_audioOutputRetryScheduled)
            {
                return RemotePlaybackRecoveryAction.None;
            }

            _audioOutputRetryScheduled = true;
            return RemotePlaybackRecoveryAction.RetryAudioOutput;
        }

        if (failureKind != OnlinePlaybackFailureKind.Source
            || string.IsNullOrWhiteSpace(_currentCandidateKey))
        {
            return RemotePlaybackRecoveryAction.None;
        }

        _excludedCandidateKeys.Add(_currentCandidateKey);
        if (_currentPlaybackUrl is not null)
        {
            _excludedPlaybackUrls.Add(_currentPlaybackUrl);
        }
        if (_currentSourceOpened)
        {
            if (_postOpenRecoveryScheduled)
            {
                return RemotePlaybackRecoveryAction.None;
            }

            _postOpenRecoveryScheduled = true;
            _postOpenRecoveryInProgress = true;
            return RemotePlaybackRecoveryAction.ResolveNextSourceAndResume;
        }

        if (_postOpenRecoveryInProgress
            || _openingRecoveryScheduled
            || _sourceAttempts.Count >= MaxOpeningSourceAttempts)
        {
            return RemotePlaybackRecoveryAction.None;
        }

        _openingRecoveryScheduled = true;
        return RemotePlaybackRecoveryAction.ResolveNextSource;
    }
}
