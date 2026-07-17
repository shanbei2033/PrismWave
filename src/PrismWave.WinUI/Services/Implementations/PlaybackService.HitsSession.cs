using PrismWave_WinUI.Infrastructure.Audio;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed partial class PlaybackService
{
    private readonly TransientPlaybackSessionGuard _hitsSessionGuard = new();
    private EventHandler? _hitsStateChanged;
    private TrackModel? _hitsTrack;
    private long _hitsRevision;
    private bool _hitsIsLoading;
    private bool _hitsIsPlaying;
    private double _hitsPositionSeconds;
    private double _hitsDurationSeconds;
    private string? _hitsError;
    private string? _primaryWindowsDsdOutputModeLabelDuringHits;
    private string? _primaryWindowsDsdActiveDeviceNameDuringHits;
    private string? _primaryWindowsDsdFallbackReasonDuringHits;
    private string _primaryAudioOutputModeLabelDuringHits = string.Empty;
    private string? _primaryAudioOutputFallbackReasonDuringHits;

    private bool IsHitsSessionActive => _hitsSessionGuard.IsCurrent(_hitsRevision);

    bool IHitsPlaybackSession.IsActive => IsHitsSessionActive;
    TrackModel? IHitsPlaybackSession.CurrentTrack => _hitsTrack;
    double IHitsPlaybackSession.PositionSeconds => _hitsPositionSeconds;
    double IHitsPlaybackSession.DurationSeconds => _hitsDurationSeconds;
    bool IHitsPlaybackSession.IsLoading => _hitsIsLoading;
    bool IHitsPlaybackSession.IsPlaying => _hitsIsPlaying;
    string? IHitsPlaybackSession.Error => _hitsError;

    event EventHandler? IHitsPlaybackSession.StateChanged
    {
        add => _hitsStateChanged += value;
        remove => _hitsStateChanged -= value;
    }

    long IHitsPlaybackSession.Begin() => BeginHitsSession();
    void IHitsPlaybackSession.Play(TrackModel track) => PlayHitsTrack(track);
    void IHitsPlaybackSession.Pause() => PauseHitsPlayback();
    void IHitsPlaybackSession.Resume() => ResumeHitsPlayback();
    void IHitsPlaybackSession.Seek(double seconds) => SeekHitsPlayback(seconds);
    void IHitsPlaybackSession.Stop() => StopHitsPlayback(clearTrack: true);
    void IHitsPlaybackSession.End() => EndHitsSession();

    private long BeginHitsSession()
    {
        if (IsHitsSessionActive)
        {
            return _hitsRevision;
        }

        CapturePrimaryOutputPresentation();
        var snapshot = CapturePrimaryPlaybackSession();
        var revision = _hitsSessionGuard.Begin(snapshot);
        _hitsRevision = revision;
        _mpvLoadEventGuard.Invalidate();
        ResetHitsPlaybackState();

        CancelPendingLoad();
        _mpvHost.Engine.Stop();
        _dsdEngine.Stop();

        var settings = _settingsService.Current;
        _mpvHost.ResetPreference("wasapi_shared", settings.AudioOutputDevice);
        NotifyHits(revision);
        return revision;
    }

    private void PlayHitsTrack(TrackModel track)
    {
        var revision = _hitsRevision;
        if (!_hitsSessionGuard.IsCurrent(revision))
        {
            return;
        }

        _mpvLoadEventGuard.Invalidate();
        _hitsTrack = null;
        _hitsIsLoading = false;
        _hitsIsPlaying = false;
        _mpvHost.Engine.Stop();

        _hitsTrack = track;
        _hitsIsLoading = true;
        _hitsIsPlaying = false;
        _hitsPositionSeconds = 0;
        _hitsDurationSeconds = Math.Max(0, track.DurationSeconds);
        _hitsError = null;

        var sourceKey = OnlinePlaybackCandidateKey.Create(track);
        var loadContext = _mpvLoadEventGuard.BeginLoad(
            checked((int)revision),
            sourceKey,
            autoplay: true);
        if (!_mpvHost.Engine.Load(
            track,
            Volume,
            autoplay: true,
            loadContext.Sequence,
            loadContext.SourceKey,
            out var error))
        {
            SetHitsPlaybackFailed(
                revision,
                error ?? _mpvHost.Engine.Error ?? "The audio source could not be opened.");
            return;
        }

        NotifyHits(revision);
    }

    private void PauseHitsPlayback()
    {
        var revision = _hitsRevision;
        if (!_hitsSessionGuard.IsCurrent(revision) || _hitsTrack is null)
        {
            return;
        }

        _mpvHost.Engine.Pause();
        _hitsIsPlaying = false;
        NotifyHits(revision);
    }

    private void ResumeHitsPlayback()
    {
        var revision = _hitsRevision;
        if (!_hitsSessionGuard.IsCurrent(revision) || _hitsTrack is null)
        {
            return;
        }

        _mpvHost.Engine.Play();
        _hitsIsPlaying = _mpvHost.Engine.IsPlaying;
        _hitsError = _mpvHost.Engine.Error;
        NotifyHits(revision);
    }

    private void SeekHitsPlayback(double seconds)
    {
        var revision = _hitsRevision;
        if (!_hitsSessionGuard.IsCurrent(revision) || _hitsTrack is null)
        {
            return;
        }

        var duration = _hitsDurationSeconds > 0
            ? _hitsDurationSeconds
            : _mpvHost.Engine.DurationSeconds;
        var clamped = duration > 0
            ? Math.Clamp(seconds, 0, duration)
            : Math.Max(0, seconds);
        _mpvHost.Engine.Seek(clamped);
        _hitsPositionSeconds = clamped;
        NotifyHits(revision);
    }

    private void StopHitsPlayback(bool clearTrack)
    {
        var revision = _hitsRevision;
        if (!_hitsSessionGuard.IsCurrent(revision))
        {
            return;
        }

        _mpvLoadEventGuard.Invalidate();
        if (clearTrack)
        {
            _hitsTrack = null;
        }

        _hitsIsLoading = false;
        _hitsIsPlaying = false;
        _hitsPositionSeconds = 0;
        _hitsDurationSeconds = clearTrack ? 0 : _hitsDurationSeconds;
        _hitsError = null;
        _mpvHost.Engine.Stop();
        NotifyHits(revision);
    }

    private void EndHitsSession()
    {
        var revision = _hitsRevision;
        if (!_hitsSessionGuard.IsCurrent(revision)) return;
        StopHitsPlayback(clearTrack: true);
        if (!_hitsSessionGuard.TryEnd(revision, out var snapshot) || snapshot is null) return;
        var settings = _settingsService.Current;
        _mpvHost.ResetPreference(settings.AudioOutputMode, settings.AudioOutputDevice);
        RestorePrimaryPlaybackSession(snapshot);
    }

    private PlaybackSessionSnapshot CapturePrimaryPlaybackSession()
    {
        var position = PositionSeconds;
        var duration = DurationSeconds;
        if (CurrentTrack is not null)
        {
            if (_usingDsdBackend)
            {
                position = Math.Max(position, _dsdEngine.PositionSeconds);
                duration = Math.Max(duration, _dsdEngine.DurationSeconds);
            }
            else
            {
                position = Math.Max(position, _mpvHost.Engine.PositionSeconds);
                duration = Math.Max(duration, _mpvHost.Engine.DurationSeconds);
            }
        }

        return new PlaybackSessionSnapshot(
            CurrentTrack,
            _queue.ToArray(),
            Mode,
            Math.Max(0, position),
            Math.Max(0, duration),
            IsPlaying || Status is PlaybackStatus.Resolving or PlaybackStatus.Buffering);
    }

    private void CapturePrimaryOutputPresentation()
    {
        _primaryWindowsDsdOutputModeLabelDuringHits = _usingDsdBackend
            ? _dsdEngine.OutputModeLabel
            : null;
        _primaryWindowsDsdActiveDeviceNameDuringHits = _usingDsdBackend
            ? _dsdEngine.ActiveDeviceName
            : null;
        _primaryWindowsDsdFallbackReasonDuringHits =
            _windowsDsdFallbackReason ?? _dsdEngine.FallbackReason;
        _primaryAudioOutputModeLabelDuringHits = _mpvHost.ActiveRouteLabel;
        _primaryAudioOutputFallbackReasonDuringHits = _mpvHost.FallbackReason;
    }

    private void RestorePrimaryPlaybackSession(PlaybackSessionSnapshot snapshot)
    {
        CurrentTrack = snapshot.Track;
        _queue.Clear();
        _queue.AddRange(snapshot.Queue);
        Mode = snapshot.Mode;
        PositionSeconds = Math.Max(0, snapshot.PositionSeconds);
        DurationSeconds = Math.Max(0, snapshot.DurationSeconds);
        Error = null;

        if (CurrentTrack is null)
        {
            CancelPendingLoad();
            _mpvHost.Engine.Stop();
            _dsdEngine.Stop();
            _usingDsdBackend = false;
            IsLoading = false;
            IsPlaying = false;
            Status = PlaybackStatus.Idle;
            Notify();
            return;
        }

        _remoteRecoveryPolicy.BeginTrack(CurrentTrack.Id);
        _pendingRecoverySeekSeconds = snapshot.PositionSeconds > 0
            ? snapshot.PositionSeconds
            : null;
        LoadCurrentTrack(
            snapshot.ShouldResume,
            preserveRecoverySeek: true);
        Notify();
    }

    private long CaptureHitsCallbackRevision() =>
        IsHitsSessionActive ? _hitsRevision : 0;

    private bool TryHandleHitsPlaybackStarted(
        PlaybackLoadEventArgs args,
        long revision)
    {
        if (!_hitsSessionGuard.IsCurrent(revision) || _hitsTrack is null)
        {
            return false;
        }

        var sourceKey = OnlinePlaybackCandidateKey.Create(_hitsTrack);
        if (!_mpvLoadEventGuard.TryAccept(
            args.LoadSequence,
            args.SourceKey,
            checked((int)revision),
            sourceKey,
            out _))
        {
            return false;
        }

        _hitsIsLoading = false;
        _hitsIsPlaying = _mpvHost.Engine.IsPlaying;
        _hitsError = null;
        _hitsPositionSeconds = Math.Max(0, _mpvHost.Engine.PositionSeconds);
        if (_mpvHost.Engine.DurationSeconds > 0)
        {
            _hitsDurationSeconds = _mpvHost.Engine.DurationSeconds;
        }

        NotifyHits(revision);
        return true;
    }

    private bool TryHandleHitsPlaybackFailed(
        string message,
        long loadSequence,
        string sourceKey,
        long revision)
    {
        if (!_hitsSessionGuard.IsCurrent(revision) || _hitsTrack is null)
        {
            return false;
        }

        var currentSourceKey = OnlinePlaybackCandidateKey.Create(_hitsTrack);
        if (!_mpvLoadEventGuard.TryAccept(
            loadSequence,
            sourceKey,
            checked((int)revision),
            currentSourceKey,
            out _))
        {
            return false;
        }

        SetHitsPlaybackFailed(
            revision,
            string.IsNullOrWhiteSpace(message)
                ? "The audio source could not be opened."
                : message);
        return true;
    }

    private bool TryHandleHitsPlaybackEnded(long revision)
    {
        if (!_hitsSessionGuard.IsCurrent(revision) || _hitsTrack is null)
        {
            return false;
        }

        _mpvLoadEventGuard.Invalidate();
        _hitsIsLoading = false;
        _hitsIsPlaying = false;
        _hitsPositionSeconds = _hitsDurationSeconds > 0
            ? _hitsDurationSeconds
            : Math.Max(0, _mpvHost.Engine.PositionSeconds);
        NotifyHits(revision);
        return true;
    }

    private bool TryRefreshHitsPosition(long revision)
    {
        if (!_hitsSessionGuard.IsCurrent(revision) || _hitsTrack is null)
        {
            return false;
        }

        _hitsPositionSeconds = Math.Max(0, _mpvHost.Engine.PositionSeconds);
        if (_mpvHost.Engine.DurationSeconds > 0)
        {
            _hitsDurationSeconds = _mpvHost.Engine.DurationSeconds;
        }

        if (!_hitsIsLoading)
        {
            _hitsIsPlaying = _mpvHost.Engine.IsPlaying;
        }

        if (!string.IsNullOrWhiteSpace(_mpvHost.Engine.Error))
        {
            _hitsIsLoading = false;
            _hitsIsPlaying = false;
            _hitsError = _mpvHost.Engine.Error;
        }

        NotifyHits(revision);
        return true;
    }

    private void SetHitsPlaybackFailed(long revision, string message)
    {
        if (!_hitsSessionGuard.IsCurrent(revision))
        {
            return;
        }

        _hitsIsLoading = false;
        _hitsIsPlaying = false;
        _hitsError = message;
        NotifyHits(revision);
    }

    private void ResetHitsPlaybackState()
    {
        _hitsTrack = null;
        _hitsIsLoading = false;
        _hitsIsPlaying = false;
        _hitsPositionSeconds = 0;
        _hitsDurationSeconds = 0;
        _hitsError = null;
    }

    private void NotifyHits(long revision)
    {
        Dispatch(() =>
        {
            if (_hitsSessionGuard.IsCurrent(revision))
            {
                _hitsStateChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }
}
