using PrismWave_WinUI.Models;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Infrastructure.Audio;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class PlaybackService : IPlaybackService
{
    private readonly List<TrackModel> _queue = new();
    private readonly ISettingsService _settingsService;
    private readonly IOnlinePlaybackResolver _onlinePlaybackResolver;
    private readonly MpvPlaybackEngine _mpvEngine;
    private readonly RemotePlaybackRecoveryPolicy _remoteRecoveryPolicy = new();
    private readonly PlaybackLoadEventGuard _mpvLoadEventGuard = new();
    private readonly WindowsDsdPlaybackEngine _dsdEngine = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _positionTimer;
    private readonly Random _random = new();
    private IReadOnlyList<WindowsDsdDeviceModel> _windowsDsdDevices = new[] { WindowsDsdDeviceModel.Automatic };
    private int _loadRevision;
    private CancellationTokenSource? _loadCancellationSource;
    private double? _pendingRecoverySeekSeconds;
    private bool _usingDsdBackend;
    private string? _windowsDsdFallbackReason;

    public TrackModel? CurrentTrack { get; private set; }
    public IReadOnlyList<TrackModel> Queue => _queue;
    public PlaybackMode Mode { get; private set; } = PlaybackMode.Loop;
    public PlaybackStatus Status { get; private set; } = PlaybackStatus.Idle;
    public double Volume { get; private set; } = 0.78;
    public double PositionSeconds { get; private set; }
    public double DurationSeconds { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsPlaying { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<WindowsDsdDeviceModel> WindowsDsdDevices => _windowsDsdDevices;
    public bool WindowsDsdAvailable => _dsdEngine.IsAvailable;
    public string? WindowsDsdOutputModeLabel => _usingDsdBackend ? _dsdEngine.OutputModeLabel : null;
    public string? WindowsDsdActiveDeviceName => _usingDsdBackend ? _dsdEngine.ActiveDeviceName : null;
    public string? WindowsDsdFallbackReason => _windowsDsdFallbackReason ?? _dsdEngine.FallbackReason;
    public event EventHandler? StateChanged;

    public PlaybackService(ISettingsService settingsService, IOnlinePlaybackResolver onlinePlaybackResolver)
    {
        _settingsService = settingsService;
        _onlinePlaybackResolver = onlinePlaybackResolver;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var settings = _settingsService.Current;
        _mpvEngine = new MpvPlaybackEngine(
            AudioOutputPolicy.BuildFallbackChain(settings.AudioOutputMode)[0],
            settings.AudioOutputDevice);
        _mpvEngine.PlaybackEnded += (_, _) => Dispatch(HandleMediaEnded);
        _mpvEngine.PlaybackStarted += (_, args) => Dispatch(() => HandlePlaybackStarted(args));
        _mpvEngine.PlaybackFailed += (_, args) => Dispatch(() => HandleMediaFailed(
            args.Message,
            args.LoadSequence,
            args.SourceKey));
        _mpvEngine.StateChanged += (_, _) => Dispatch(() =>
        {
            if (CurrentTrack is not null
                && !_usingDsdBackend
                && Status is not PlaybackStatus.Resolving and not PlaybackStatus.Buffering)
            {
                RefreshPlayerState();
            }
        });
        _dsdEngine.PlaybackEnded += (_, _) => Dispatch(HandleMediaEnded);

        _positionTimer = _dispatcherQueue.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
        _positionTimer.Tick += (_, _) => RefreshPosition();
        _positionTimer.Start();
    }

    public void Play(TrackModel track, IReadOnlyList<TrackModel>? queue = null)
    {
        if (CurrentTrack?.Id == track.Id
            && Status is PlaybackStatus.Resolving or PlaybackStatus.Buffering)
        {
            StartupLog.Write($"playback.play.coalesced: title=\"{track.Title}\", status={Status}");
            return;
        }

        StartupLog.Write($"playback.play: title=\"{track.Title}\", provider={track.Provider}, remote={track.IsRemote}, dsd={track.IsDsd}");
        CurrentTrack = track;
        _remoteRecoveryPolicy.BeginTrack(track.Id);
        Error = null;
        IsLoading = true;
        IsPlaying = false;
        PositionSeconds = 0;
        DurationSeconds = track.DurationSeconds;
        _queue.Clear();
        _queue.AddRange(queue is { Count: > 0 } ? queue : new[] { track });
        LoadCurrentTrack(autoplay: true);
        Notify();
    }

    public void Stop()
    {
        CancelPendingLoad();
        _mpvEngine.Stop();
        _dsdEngine.Stop();
        _usingDsdBackend = false;
        CurrentTrack = null;
        _queue.Clear();
        PositionSeconds = 0;
        DurationSeconds = 0;
        IsLoading = false;
        IsPlaying = false;
        Error = null;
        Status = PlaybackStatus.Idle;
        StartupLog.Write("playback.stop");
        Notify();
    }

    public void TogglePlayPause()
    {
        if (CurrentTrack is null)
        {
            return;
        }

        if (IsPlaying)
        {
            StartupLog.Write($"playback.pause: title=\"{CurrentTrack.Title}\"");
            if (_usingDsdBackend)
            {
                _dsdEngine.Pause();
                IsPlaying = false;
                Status = PlaybackStatus.Paused;
                Notify();
                return;
            }

            _mpvEngine.Pause();
        }
        else
        {
            StartupLog.Write($"playback.resume: title=\"{CurrentTrack.Title}\"");
            if (_usingDsdBackend)
            {
                _dsdEngine.Resume();
                IsPlaying = true;
                Status = PlaybackStatus.Playing;
                Notify();
                return;
            }

            _mpvEngine.Play();
        }

        RefreshPlayerState();
        Notify();
    }

    public void Next()
    {
        Move(1);
    }

    public void Previous()
    {
        Move(-1);
    }

    public void CycleMode()
    {
        Mode = Mode switch
        {
            PlaybackMode.Loop => PlaybackMode.Single,
            PlaybackMode.Single => PlaybackMode.Shuffle,
            _ => PlaybackMode.Loop
        };
        StartupLog.Write($"queue.mode: {Mode}");
        Notify();
    }

    public void SetVolume(double volume)
    {
        Volume = Math.Clamp(volume, 0, 1);
        _mpvEngine.SetVolume(Volume);
        _dsdEngine.SetVolume(Volume);
        Notify();
    }

    public void Seek(double seconds)
    {
        if (CurrentTrack is null)
        {
            return;
        }

        var duration = DurationSeconds > 0 ? DurationSeconds : _mpvEngine.DurationSeconds;
        var clamped = duration > 0 ? Math.Clamp(seconds, 0, duration) : Math.Max(0, seconds);
        if (_usingDsdBackend)
        {
            _dsdEngine.Seek(clamped);
        }
        else
        {
            _mpvEngine.Seek(clamped);
        }

        PositionSeconds = clamped;
        Notify();
    }

    public void PlayFromQueue(TrackModel track)
    {
        var queuedTrack = _queue.FirstOrDefault(item => item.Id == track.Id);
        if (queuedTrack is null)
        {
            return;
        }

        CurrentTrack = queuedTrack;
        _remoteRecoveryPolicy.BeginTrack(queuedTrack.Id);
        StartupLog.Write($"queue.select: index={_queue.IndexOf(queuedTrack)}, title=\"{queuedTrack.Title}\"");
        Error = null;
        LoadCurrentTrack(autoplay: true);
        Notify();
    }

    public void ReorderQueue(IReadOnlyList<TrackModel> tracks)
    {
        if (tracks.Count != _queue.Count)
        {
            return;
        }

        var expected = _queue
            .GroupBy(track => track.Id)
            .ToDictionary(group => group.Key, group => group.Count());
        var received = tracks
            .GroupBy(track => track.Id)
            .ToDictionary(group => group.Key, group => group.Count());
        if (expected.Count != received.Count
            || expected.Any(pair => !received.TryGetValue(pair.Key, out var count) || count != pair.Value))
        {
            return;
        }

        _queue.Clear();
        _queue.AddRange(tracks);
        StartupLog.Write($"queue.reorder: count={_queue.Count}");
        Notify();
    }

    public void RemoveFromQueue(TrackModel track)
    {
        StartupLog.Write($"queue.remove: title=\"{track.Title}\", current={CurrentTrack?.Id == track.Id}");
        _queue.RemoveAll(item => item.Id == track.Id);
        if (CurrentTrack?.Id == track.Id)
        {
            CancelPendingLoad();
            CurrentTrack = _queue.FirstOrDefault();
            if (CurrentTrack is null)
            {
                _mpvEngine.Stop();
                _dsdEngine.Stop();
                _usingDsdBackend = false;
                IsPlaying = false;
                IsLoading = false;
                Status = PlaybackStatus.Idle;
                PositionSeconds = 0;
                DurationSeconds = 0;
            }
            else
            {
                _remoteRecoveryPolicy.BeginTrack(CurrentTrack.Id);
                LoadCurrentTrack(autoplay: IsPlaying);
            }
        }

        Notify();
    }

    public void ClearQueue()
    {
        _queue.Clear();
        CurrentTrack = null;
        CancelPendingLoad();
        _mpvEngine.Stop();
        _dsdEngine.Stop();
        _usingDsdBackend = false;
        IsPlaying = false;
        IsLoading = false;
        Error = null;
        Status = PlaybackStatus.Idle;
        PositionSeconds = 0;
        DurationSeconds = 0;
        StartupLog.Write("queue.clear");
        Notify();
    }

    private void Move(int delta)
    {
        if (_queue.Count == 0 || CurrentTrack is null)
        {
            return;
        }

        var index = _queue.FindIndex(track => track.Id == CurrentTrack.Id);
        if (index < 0)
        {
            index = 0;
        }

        var next = Mode == PlaybackMode.Shuffle && _queue.Count > 1
            ? NextShuffleIndex(index)
            : (index + delta + _queue.Count) % _queue.Count;
        CurrentTrack = _queue[next];
        _remoteRecoveryPolicy.BeginTrack(CurrentTrack.Id);
        StartupLog.Write($"queue.move: index={next}, title=\"{CurrentTrack.Title}\", mode={Mode}");
        Error = null;
        LoadCurrentTrack(autoplay: true);
        Notify();
    }

    private void LoadCurrentTrack(bool autoplay)
    {
        if (CurrentTrack is null)
        {
            return;
        }

        CancelPendingLoad();
        _loadCancellationSource = new CancellationTokenSource();
        var cancellationToken = _loadCancellationSource.Token;
        var revision = _loadRevision;
        if (CurrentTrack.IsDsd)
        {
            _mpvEngine.Stop();
            if (_dsdEngine.Play(
                CurrentTrack.Path,
                Volume,
                _settingsService.Current.WindowsDsdDevice,
                out var dsdError))
            {
                _usingDsdBackend = true;
                _windowsDsdFallbackReason = _dsdEngine.FallbackReason;
                if (!autoplay)
                {
                    _dsdEngine.Pause();
                }
                StartupLog.Write($"playback.dsd.loaded: {CurrentTrack.Path}");
                Error = null;
                IsLoading = false;
                IsPlaying = autoplay;
                Status = autoplay ? PlaybackStatus.Playing : PlaybackStatus.Paused;
                DurationSeconds = _dsdEngine.DurationSeconds;
                Notify();
                return;
            }

            _usingDsdBackend = false;
            _windowsDsdFallbackReason = DescribeDsdFallback(dsdError);
            StartupLog.Write($"playback.dsd.fallback: {CurrentTrack.Path}: {dsdError}");
            LoadMpvTrack(CurrentTrack, autoplay);
            return;
        }

        _usingDsdBackend = false;
        _windowsDsdFallbackReason = null;

        if (NeedsOnlineResolution(CurrentTrack))
        {
            IsLoading = true;
            IsPlaying = false;
            Status = PlaybackStatus.Resolving;
            Error = null;
            PositionSeconds = 0;
            DurationSeconds = CurrentTrack.DurationSeconds;
            _mpvEngine.Stop();
            Notify();
            _ = ResolveAndLoadCurrentTrackAsync(CurrentTrack, autoplay, revision, cancellationToken);
            return;
        }

        if (CurrentTrack.IsRemote && _remoteRecoveryPolicy.SourceAttemptCount == 0)
        {
            _remoteRecoveryPolicy.BeginSourceAttempt(
                CurrentTrack.Id,
                OnlinePlaybackCandidateKey.Create(CurrentTrack),
                CurrentTrack.PlaybackSource);
        }

        LoadMpvTrack(CurrentTrack, autoplay);
    }

    private void LoadMpvTrack(TrackModel track, bool autoplay)
    {
        _dsdEngine.Stop();
        _usingDsdBackend = false;
        IsLoading = true;
        IsPlaying = false;
        Status = PlaybackStatus.Buffering;
        PositionSeconds = 0;
        DurationSeconds = track.DurationSeconds;
        var sourceKey = OnlinePlaybackCandidateKey.Create(track);
        var loadContext = _mpvLoadEventGuard.BeginLoad(
            _loadRevision,
            sourceKey,
            autoplay);
        if (!_mpvEngine.Load(
            track,
            Volume,
            autoplay,
            loadContext.Sequence,
            loadContext.SourceKey,
            out var error))
        {
            Error = error ?? _mpvEngine.Error;
            var failureKind = OnlinePlaybackFailureClassifier.Classify(Error);
            StartupLog.Write(
                $"playback.mpv.load-failed: kind={failureKind}, provider={track.Provider}, candidate={OnlinePlaybackCandidateKey.Create(track)}, attempt={_remoteRecoveryPolicy.SourceAttemptCount}, source={DescribeSource(track.PlaybackSource)}");
            HandleMediaFailed(
                Error ?? "The audio source could not be opened.",
                loadContext.Sequence,
                loadContext.SourceKey);
            return;
        }

        Error = null;
        StartupLog.Write($"playback.mpv.source-set: source={DescribeSource(track.PlaybackSource)}, autoplay={autoplay}");
        RefreshPosition();
    }

    private async Task ResolveAndLoadCurrentTrackAsync(
        TrackModel track,
        bool autoplay,
        int revision,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            StartupLog.Write($"online.resolve.start: revision={revision}, track={track.Id}, title=\"{track.Title}\"");
            var resolved = await _onlinePlaybackResolver.ResolveAsync(track, cancellationToken);
            Dispatch(() =>
            {
                if (revision != _loadRevision || CurrentTrack?.Id != track.Id)
                {
                    return;
                }

                if (resolved is null)
                {
                    Error = $"No playable URL resolved for {track.Provider}.";
                    StartupLog.Write($"online.resolve.failed: provider={track.Provider}, title=\"{track.Title}\"");
                    IsLoading = false;
                    IsPlaying = false;
                    Status = PlaybackStatus.Failed;
                    Notify();
                    return;
                }

                var candidateKey = resolved.CandidateKey
                    ?? OnlinePlaybackCandidateKey.Create(
                        resolved.Provider,
                        resolved.ProviderTrackId,
                        resolved.PlaybackUrl);
                if (!_remoteRecoveryPolicy.BeginSourceAttempt(
                    track.Id,
                    candidateKey,
                    resolved.PlaybackUrl))
                {
                    SetPlaybackFailed("The resolved online source was already attempted.");
                    return;
                }

                var resolvedTrack = OnlinePlaybackTrack.ApplyResolution(track, resolved);
                ReplaceQueuedTrack(resolvedTrack);
                CurrentTrack = resolvedTrack;
                stopwatch.Stop();
                StartupLog.Write(
                    $"online.resolve.success: revision={revision}, provider={resolved.Provider}, candidate={candidateKey}, attempt={resolved.Attempt}, elapsed={stopwatch.ElapsedMilliseconds}ms, source={DescribeSource(resolved.PlaybackUrl)}");
                LoadMpvTrack(resolvedTrack, autoplay);
                Notify();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StartupLog.Write($"online.resolve.cancelled: revision={revision}, track={track.Id}, title=\"{track.Title}\"");
        }
        catch (Exception exception)
        {
            Dispatch(() =>
            {
                if (revision != _loadRevision || CurrentTrack?.Id != track.Id)
                {
                    return;
                }

                StartupLog.Write(
                    $"online.resolve.error: provider={track.Provider}, revision={revision}, errorType={exception.GetType().Name}");
                SetPlaybackFailed(exception.Message);
            });
        }
    }

    private void ReplaceQueuedTrack(TrackModel track)
    {
        var index = _queue.FindIndex(item => item.Id == track.Id);
        if (index >= 0)
        {
            _queue[index] = track;
        }
    }

    private static bool NeedsOnlineResolution(TrackModel track)
    {
        if (!track.IsRemote)
        {
            return false;
        }

        if (!Uri.TryCreate(track.PlaybackSource, UriKind.Absolute, out var uri))
        {
            return true;
        }

        return uri.Scheme != Uri.UriSchemeHttp
            && uri.Scheme != Uri.UriSchemeHttps
            && uri.Scheme != Uri.UriSchemeFile;
    }

    private void HandleMediaEnded()
    {
        StartupLog.Write($"playback.ended: title=\"{CurrentTrack?.Title}\", mode={Mode}");
        if (Mode == PlaybackMode.Single)
        {
            Seek(0);
            if (_usingDsdBackend)
            {
                _dsdEngine.Resume();
            }
            else
            {
                _mpvEngine.Play();
            }
            return;
        }

        Next();
    }

    private void HandlePlaybackStarted(PlaybackLoadEventArgs args)
    {
        if (CurrentTrack is null || _usingDsdBackend)
        {
            return;
        }

        var currentSourceKey = OnlinePlaybackCandidateKey.Create(CurrentTrack);
        if (!_mpvLoadEventGuard.TryAccept(
            args.LoadSequence,
            args.SourceKey,
            _loadRevision,
            currentSourceKey,
            out _))
        {
            StartupLog.Write(
                $"playback.mpv.opened-stale: revision={_loadRevision}, callbackSource={args.SourceKey}, currentSource={currentSourceKey}");
            return;
        }

        IsLoading = false;
        IsPlaying = _mpvEngine.IsPlaying;
        Error = null;
        Status = IsPlaying ? PlaybackStatus.Playing : PlaybackStatus.Paused;
        _remoteRecoveryPolicy.MarkOpened(CurrentTrack.Id);
        if (_pendingRecoverySeekSeconds is double resumePosition)
        {
            _pendingRecoverySeekSeconds = null;
            _mpvEngine.Seek(resumePosition);
            PositionSeconds = resumePosition;
            StartupLog.Write(
                $"playback.remote.resume-seek: provider={CurrentTrack.Provider}, candidate={OnlinePlaybackCandidateKey.Create(CurrentTrack)}, attempt={_remoteRecoveryPolicy.SourceAttemptCount}, position={resumePosition:0.###}");
        }
        StartupLog.Write(
            $"playback.mpv.opened: title=\"{CurrentTrack.Title}\", status={Status}, source={DescribeSource(CurrentTrack.PlaybackSource)}");
        RefreshPosition();
    }

    private void HandleMediaFailed(
        string message,
        long loadSequence,
        string sourceKey)
    {
        if (CurrentTrack is null || _usingDsdBackend)
        {
            return;
        }

        var failedTrack = CurrentTrack;
        var currentSourceKey = OnlinePlaybackCandidateKey.Create(failedTrack);
        if (!_mpvLoadEventGuard.TryAccept(
            loadSequence,
            sourceKey,
            _loadRevision,
            currentSourceKey,
            out var loadContext))
        {
            StartupLog.Write(
                $"playback.mpv.failure-stale: revision={_loadRevision}, callbackSource={sourceKey}, currentSource={currentSourceKey}");
            return;
        }

        var failureKind = OnlinePlaybackFailureClassifier.Classify(message);
        var recoveryAction = _remoteRecoveryPolicy.DecideFailure(
            failedTrack.Id,
            failedTrack.IsRemote,
            failureKind);
        var candidateKey = OnlinePlaybackCandidateKey.Create(failedTrack);
        var resumePosition = Math.Max(PositionSeconds, _mpvEngine.PositionSeconds);
        StartupLog.Write(
            $"playback.remote.failure: kind={failureKind}, provider={failedTrack.Provider}, candidate={candidateKey}, attempt={_remoteRecoveryPolicy.SourceAttemptCount}, action={recoveryAction}, source={DescribeSource(failedTrack.PlaybackSource)}");

        if (recoveryAction == RemotePlaybackRecoveryAction.RetryAudioOutput)
        {
            IsLoading = true;
            IsPlaying = false;
            Error = null;
            Status = PlaybackStatus.Buffering;
            StartupLog.Write(
                $"playback.output.retry: provider={failedTrack.Provider}, candidate={candidateKey}, attempt={_remoteRecoveryPolicy.SourceAttemptCount}");
            Notify();
            LoadMpvTrack(failedTrack, loadContext.Autoplay);
            return;
        }

        if (recoveryAction is RemotePlaybackRecoveryAction.ResolveNextSource
            or RemotePlaybackRecoveryAction.ResolveNextSourceAndResume)
        {
            _onlinePlaybackResolver.InvalidatePlaybackUrl(failedTrack.PlaybackSource);
            BeginRemoteSourceRecovery(
                failedTrack,
                recoveryAction == RemotePlaybackRecoveryAction.ResolveNextSourceAndResume
                    ? resumePosition
                    : null,
                loadContext.Autoplay);
            return;
        }

        _pendingRecoverySeekSeconds = null;
        SetPlaybackFailed(
            string.IsNullOrWhiteSpace(message)
                ? "The audio source could not be opened."
                : message);
    }

    private void BeginRemoteSourceRecovery(
        TrackModel failedTrack,
        double? resumePosition,
        bool autoplay)
    {
        CancelPendingLoad();
        _loadCancellationSource = new CancellationTokenSource();
        var cancellationToken = _loadCancellationSource.Token;
        var revision = _loadRevision;
        var descriptorTrack = failedTrack with
        {
            PlaybackUrl = null,
            PlaybackHeaders = null
        };
        var exclusions = _remoteRecoveryPolicy.Exclusions;
        var attempt = _remoteRecoveryPolicy.SourceAttemptCount + 1;
        IsLoading = true;
        IsPlaying = false;
        Error = null;
        Status = PlaybackStatus.Resolving;
        StartupLog.Write(
            $"playback.remote.resolve-next: revision={revision}, provider={failedTrack.Provider}, excludedCandidates={exclusions.CandidateKeys.Count}, excludedUrls={exclusions.NormalizedPlaybackUrls.Count}, attempt={attempt}, resume={(resumePosition.HasValue ? "yes" : "no")}");
        Notify();
        _ = ResolveAndLoadRecoveryAsync(
            descriptorTrack,
            exclusions,
            attempt,
            resumePosition,
            autoplay,
            revision,
            cancellationToken);
    }

    private async Task ResolveAndLoadRecoveryAsync(
        TrackModel track,
        OnlinePlaybackExclusions exclusions,
        int attempt,
        double? resumePosition,
        bool autoplay,
        int revision,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await _onlinePlaybackResolver.ResolveNextAsync(
                track,
                exclusions,
                attempt,
                cancellationToken);
            Dispatch(() =>
            {
                if (revision != _loadRevision || CurrentTrack?.Id != track.Id)
                {
                    return;
                }

                if (resolved is null)
                {
                    SetPlaybackFailed("No additional online source is available.");
                    return;
                }

                var candidateKey = resolved.CandidateKey
                    ?? OnlinePlaybackCandidateKey.Create(
                        resolved.Provider,
                        resolved.ProviderTrackId,
                        resolved.PlaybackUrl);
                if (!_remoteRecoveryPolicy.BeginSourceAttempt(
                    track.Id,
                    candidateKey,
                    resolved.PlaybackUrl))
                {
                    SetPlaybackFailed("The next online source was already attempted.");
                    return;
                }

                var resolvedTrack = OnlinePlaybackTrack.ApplyResolution(track, resolved);
                ReplaceQueuedTrack(resolvedTrack);
                CurrentTrack = resolvedTrack;
                _pendingRecoverySeekSeconds = resumePosition;
                StartupLog.Write(
                    $"playback.remote.resolve-next-ready: revision={revision}, provider={resolved.Provider}, candidate={candidateKey}, attempt={resolved.Attempt}, resume={(resumePosition.HasValue ? "yes" : "no")}, source={DescribeSource(resolved.PlaybackUrl)}");
                LoadMpvTrack(resolvedTrack, autoplay);
                Notify();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StartupLog.Write(
                $"playback.remote.resolve-next-cancelled: revision={revision}, attempt={attempt}");
        }
        catch (Exception exception)
        {
            Dispatch(() =>
            {
                if (revision != _loadRevision || CurrentTrack?.Id != track.Id)
                {
                    return;
                }

                StartupLog.Write(
                    $"playback.remote.resolve-next-error: revision={revision}, attempt={attempt}, errorType={exception.GetType().Name}");
                SetPlaybackFailed(exception.Message);
            });
        }
    }

    private void SetPlaybackFailed(string message)
    {
        _pendingRecoverySeekSeconds = null;
        IsLoading = false;
        IsPlaying = false;
        Error = message;
        Status = PlaybackStatus.Failed;
        Notify();
    }

    private int NextShuffleIndex(int currentIndex)
    {
        if (_queue.Count <= 1)
        {
            return currentIndex;
        }

        var next = currentIndex;
        while (next == currentIndex)
        {
            next = _random.Next(_queue.Count);
        }

        return next;
    }

    private void RefreshPlayerState()
    {
        IsPlaying = _mpvEngine.IsPlaying;
        IsLoading = false;
        Error = _mpvEngine.Error;
        Status = Error is not null
            ? PlaybackStatus.Failed
            : IsPlaying
                ? PlaybackStatus.Playing
                : PlaybackStatus.Paused;
        RefreshPosition();
    }

    private void RefreshPosition()
    {
        if (CurrentTrack is null)
        {
            return;
        }

        if (_usingDsdBackend)
        {
            PositionSeconds = Math.Max(0, _dsdEngine.PositionSeconds);
            if (_dsdEngine.DurationSeconds > 0)
            {
                DurationSeconds = _dsdEngine.DurationSeconds;
            }

            Notify();
            return;
        }

        PositionSeconds = Math.Max(0, _mpvEngine.PositionSeconds);
        var naturalDuration = _mpvEngine.DurationSeconds;
        if (naturalDuration > 0)
        {
            DurationSeconds = naturalDuration;
        }

        Notify();
    }

    public async Task RefreshWindowsDsdDevicesAsync()
    {
        try
        {
            var devices = await Task.Run(_dsdEngine.ListAvailableDevices);
            _windowsDsdDevices = devices;
        }
        catch (Exception exception)
        {
            _windowsDsdDevices = new[] { WindowsDsdDeviceModel.Automatic };
            StartupLog.Write("windows.dsd.deviceEnumerationFailed", exception);
        }

        Notify();
    }

    private string DescribeDsdFallback(string? error)
    {
        if (!WindowsDsdAvailable)
        {
            return "BASS/BASSDSD/BASSASIO runtime is unavailable; using mpv fallback.";
        }

        if (_windowsDsdDevices.Count <= 1)
        {
            return "No ASIO output device is available; using mpv fallback.";
        }

        return string.IsNullOrWhiteSpace(error)
            ? "The Windows DSD backend could not start; using mpv fallback."
            : $"{error} Using mpv fallback.";
    }

    private void CancelPendingLoad()
    {
        _loadRevision++;
        _pendingRecoverySeekSeconds = null;
        _mpvLoadEventGuard.Invalidate();
        var cancellation = _loadCancellationSource;
        _loadCancellationSource = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private static string DescribeSource(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out uri))
        {
            return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
        }

        return Path.GetFileName(source);
    }

    private void Notify()
    {
        Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    private void Dispatch(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
    }
}
