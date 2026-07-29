using PrismWave_WinUI.Models;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Infrastructure.Audio;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed partial class PlaybackService : IPlaybackService, IHitsPlaybackSession, IDisposable
{
    private readonly List<TrackModel> _queue = new();
    private readonly ISettingsService _settingsService;
    private readonly IOnlinePlaybackResolver _onlinePlaybackResolver;
    private readonly IOnlineAudioCache _onlineAudioCache;
    private readonly MpvPlaybackEngineHost _mpvHost;
    private readonly RemotePlaybackRecoveryPolicy _remoteRecoveryPolicy = new();
    private readonly PlaybackLoadEventGuard _mpvLoadEventGuard = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _positionTimer;
    private readonly Random _random = new();
    private int _loadRevision;
    private CancellationTokenSource? _loadCancellationSource;
    private CancellationTokenSource? _localStartupCancellationSource;
    private double? _pendingRecoverySeekSeconds;
    private bool _positionResetPending;

    public TrackModel? CurrentTrack { get; private set; }
    public IReadOnlyList<TrackModel> Queue => _queue;
    public long QueueRevision { get; private set; }
    public PlaybackMode Mode { get; private set; } = PlaybackMode.Loop;
    public PlaybackStatus Status { get; private set; } = PlaybackStatus.Idle;
    public double Volume { get; private set; } = 0.78;
    public double PositionSeconds { get; private set; }
    public double DurationSeconds { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsPlaying { get; private set; }
    public string? Error { get; private set; }
    public string ActiveAudioOutputModeLabel => IsHitsSessionActive
        ? _primaryAudioOutputModeLabelDuringHits
        : _mpvHost.ActiveRouteLabel;
    public string? AudioOutputFallbackReason => IsHitsSessionActive
        ? _primaryAudioOutputFallbackReasonDuringHits
        : _mpvHost.FallbackReason;
    public event EventHandler? StateChanged;

    public PlaybackService(
        ISettingsService settingsService,
        IOnlinePlaybackResolver onlinePlaybackResolver,
        IOnlineAudioCache onlineAudioCache,
        IPlaybackEngineFactory? playbackEngineFactory = null)
    {
        _settingsService = settingsService;
        _onlinePlaybackResolver = onlinePlaybackResolver;
        _onlineAudioCache = onlineAudioCache;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var settings = _settingsService.Current;
        _mpvHost = new MpvPlaybackEngineHost(
            playbackEngineFactory ?? new MpvPlaybackEngineFactory(),
            settings.AudioOutputMode,
            settings.AudioOutputDevice);
        _mpvHost.PlaybackEnded += (_, _) =>
        {
            var hitsRevision = CaptureHitsCallbackRevision();
            Dispatch(() => HandleMediaEnded(hitsRevision));
        };
        _mpvHost.PlaybackStarted += (_, args) =>
        {
            var hitsRevision = CaptureHitsCallbackRevision();
            Dispatch(() => HandlePlaybackStarted(args, hitsRevision));
        };
        _mpvHost.PlaybackFailed += (_, args) =>
        {
            var hitsRevision = CaptureHitsCallbackRevision();
            Dispatch(() => HandleMediaFailed(
                args.Message,
                args.LoadSequence,
                args.SourceKey,
                hitsRevision));
        };
        _mpvHost.StateChanged += (_, _) =>
        {
            var hitsRevision = CaptureHitsCallbackRevision();
            Dispatch(() => RefreshHostStateWhenReady(hitsRevision));
        };
        _settingsService.SettingsChanged += (_, _) => Dispatch(ApplyAudioSettings);

        _positionTimer = _dispatcherQueue.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
        _positionTimer.Tick += (_, _) => RefreshPosition();
        _positionTimer.Start();
    }

    public void Play(TrackModel track, IReadOnlyList<TrackModel>? queue = null)
    {
        if (IsHitsSessionActive)
        {
            return;
        }

        if (CurrentTrack?.Id == track.Id
            && Status is PlaybackStatus.Resolving or PlaybackStatus.Buffering)
        {
            StartupLog.Write($"playback.play.coalesced: title=\"{track.Title}\", status={Status}");
            return;
        }

        StartupLog.Write($"playback.play: title=\"{track.Title}\", provider={track.Provider}, remote={track.IsRemote}");
        CurrentTrack = track;
        _remoteRecoveryPolicy.BeginTrack(track.Id);
        Error = null;
        IsLoading = true;
        IsPlaying = false;
        PositionSeconds = 0;
        DurationSeconds = track.DurationSeconds;
        _queue.Clear();
        _queue.AddRange(queue is { Count: > 0 } ? queue : new[] { track });
        AdvanceQueueRevision();
        LoadCurrentTrack(autoplay: true);
        Notify();
    }

    public void Stop()
    {
        if (IsHitsSessionActive)
        {
            return;
        }

        CancelPendingLoad();
        _mpvHost.Engine.Stop();
        CurrentTrack = null;
        if (_queue.Count > 0)
        {
            _queue.Clear();
            AdvanceQueueRevision();
        }
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
        if (IsHitsSessionActive)
        {
            return;
        }

        if (CurrentTrack is null)
        {
            return;
        }

        if (IsPlaying)
        {
            StartupLog.Write($"playback.pause: title=\"{CurrentTrack.Title}\"");
            _mpvHost.Engine.Pause();
        }
        else
        {
            StartupLog.Write($"playback.resume: title=\"{CurrentTrack.Title}\"");
            _mpvHost.Engine.Play();
        }

        RefreshPlayerState();
        Notify();
    }

    public void Next()
    {
        if (IsHitsSessionActive)
        {
            return;
        }

        Move(1);
    }

    public void Previous()
    {
        if (IsHitsSessionActive)
        {
            return;
        }

        Move(-1);
    }

    public void CycleMode()
    {
        if (IsHitsSessionActive)
        {
            return;
        }

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
        if (IsHitsSessionActive)
        {
            return;
        }

        Volume = Math.Clamp(volume, 0, 1);
        _mpvHost.Engine.SetVolume(Volume);
        Notify();
    }

    public void Seek(double seconds)
    {
        if (IsHitsSessionActive)
        {
            return;
        }

        if (CurrentTrack is null)
        {
            return;
        }

        var duration = DurationSeconds > 0 ? DurationSeconds : _mpvHost.Engine.DurationSeconds;
        var clamped = duration > 0 ? Math.Clamp(seconds, 0, duration) : Math.Max(0, seconds);
        _mpvHost.Engine.Seek(clamped);

        PositionSeconds = clamped;
        Notify();
    }

    public void PlayFromQueue(TrackModel track)
    {
        if (IsHitsSessionActive)
        {
            return;
        }

        var queuedTrack = _queue.FirstOrDefault(item => item.Id == track.Id);
        if (queuedTrack is null)
        {
            return;
        }

        CurrentTrack = queuedTrack;
        _remoteRecoveryPolicy.BeginTrack(queuedTrack.Id);
        StartupLog.Write($"queue.select: index={_queue.IndexOf(queuedTrack)}, title=\"{queuedTrack.Title}\"");
        Error = null;
        PositionSeconds = 0;
        IsPlaying = false;
        LoadCurrentTrack(autoplay: true);
        Notify();
    }

    public void AddToQueue(TrackModel track)
    {
        if (IsHitsSessionActive)
        {
            return;
        }

        if (_queue.Any(item => string.Equals(item.Id, track.Id, StringComparison.Ordinal)))
        {
            return;
        }

        _queue.Add(track);
        AdvanceQueueRevision();
        StartupLog.Write($"queue.add: title=\"{track.Title}\", index={_queue.Count - 1}");
        Notify();
    }

    public void PlayNext(TrackModel track)
    {
        if (IsHitsSessionActive)
        {
            return;
        }

        _queue.RemoveAll(item => string.Equals(item.Id, track.Id, StringComparison.Ordinal));
        var currentIndex = CurrentTrack is null
            ? -1
            : _queue.FindIndex(item => string.Equals(item.Id, CurrentTrack.Id, StringComparison.Ordinal));
        _queue.Insert(Math.Clamp(currentIndex + 1, 0, _queue.Count), track);
        AdvanceQueueRevision();
        StartupLog.Write($"queue.play-next: title=\"{track.Title}\", after={currentIndex}");
        Notify();
    }

    public void ReorderQueue(IReadOnlyList<TrackModel> tracks)
    {
        if (IsHitsSessionActive)
        {
            return;
        }

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

        if (_queue.SequenceEqual(tracks))
        {
            return;
        }

        _queue.Clear();
        _queue.AddRange(tracks);
        AdvanceQueueRevision();
        StartupLog.Write($"queue.reorder: count={_queue.Count}");
        Notify();
    }

    public void RemoveFromQueue(TrackModel track)
    {
        if (IsHitsSessionActive)
        {
            return;
        }

        StartupLog.Write($"queue.remove: title=\"{track.Title}\", current={CurrentTrack?.Id == track.Id}");
        var removeIndex = _queue.FindIndex(item => item.Id == track.Id);
        if (removeIndex < 0)
        {
            return;
        }

        _queue.RemoveAt(removeIndex);
        AdvanceQueueRevision();
        if (CurrentTrack?.Id == track.Id)
        {
            CancelPendingLoad();
            CurrentTrack = _queue.FirstOrDefault();
            if (CurrentTrack is null)
            {
                _mpvHost.Engine.Stop();
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
        if (IsHitsSessionActive)
        {
            return;
        }

        var hadQueue = _queue.Count > 0;
        _queue.Clear();
        if (hadQueue)
        {
            AdvanceQueueRevision();
        }
        CurrentTrack = null;
        CancelPendingLoad();
        _mpvHost.Engine.Stop();
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
        if (IsHitsSessionActive)
        {
            return;
        }

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
        PositionSeconds = 0;
        IsPlaying = false;
        LoadCurrentTrack(autoplay: true);
        Notify();
    }

    private void LoadCurrentTrack(bool autoplay, bool preserveRecoverySeek = false)
    {
        if (CurrentTrack is null)
        {
            return;
        }

        // Release references from previous track before loading new one
        GC.Collect(0, GCCollectionMode.Optimized, blocking: false);

        var recoverySeek = preserveRecoverySeek ? _pendingRecoverySeekSeconds : null;
        CancelPendingLoad();
        if (preserveRecoverySeek)
        {
            _pendingRecoverySeekSeconds = recoverySeek;
        }
        ResetPreferredAudioRouteForNewTrack();
        _loadCancellationSource = new CancellationTokenSource();
        var cancellationToken = _loadCancellationSource.Token;
        var revision = _loadRevision;

        if (CurrentTrack.IsRemote)
        {
            var cachedTrack = _onlineAudioCache.TryGetCachedTrack(CurrentTrack);
            if (cachedTrack is not null)
            {
                CurrentTrack = cachedTrack;
                ReplaceQueuedTrack(cachedTrack);
            }
        }

        if (NeedsOnlineResolution(CurrentTrack))
        {
            IsLoading = true;
            IsPlaying = false;
            Status = PlaybackStatus.Resolving;
            Error = null;
            PositionSeconds = 0;
            DurationSeconds = CurrentTrack.DurationSeconds;
            _mpvHost.Engine.Stop();
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
        CancelLocalStartupWatchdog();
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
        if (!_mpvHost.Engine.Load(
            track,
            Volume,
            autoplay,
            loadContext.Sequence,
            loadContext.SourceKey,
            out var error))
        {
            Error = error ?? _mpvHost.Engine.Error;
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
        if (!track.IsRemote)
        {
            ArmLocalStartupWatchdog(track, loadContext, _mpvHost.Generation);
        }
        // libmpv can continue exposing the previous item's time-pos until the
        // new file-loaded event is delivered. Publishing that value here makes
        // a cache hit (and ordinary track changes) appear to retain the old
        // seek position. Keep the explicit zero set above while buffering.
        Notify();
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
                _ = _onlineAudioCache.CacheAsync(track, resolved, CancellationToken.None);
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
        if (index >= 0 && !Equals(_queue[index], track))
        {
            _queue[index] = track;
            AdvanceQueueRevision();
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

    private void HandleMediaEnded(long hitsRevision = 0)
    {
        if (TryHandleHitsPlaybackEnded(hitsRevision))
        {
            return;
        }

        if (hitsRevision != 0 || IsHitsSessionActive)
        {
            return;
        }

        StartupLog.Write($"playback.ended: title=\"{CurrentTrack?.Title}\", mode={Mode}");
        if (Mode == PlaybackMode.Single)
        {
            PositionSeconds = 0;
            IsPlaying = false;
            LoadCurrentTrack(autoplay: true);
            Notify();
            return;
        }

        Next();
    }

    private void HandlePlaybackStarted(PlaybackLoadEventArgs args, long hitsRevision = 0)
    {
        if (TryHandleHitsPlaybackStarted(args, hitsRevision))
        {
            return;
        }

        if (hitsRevision != 0 || IsHitsSessionActive)
        {
            return;
        }

        if (CurrentTrack is null)
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

        CancelLocalStartupWatchdog();
        IsLoading = false;
        IsPlaying = _mpvHost.Engine.IsPlaying;
        Error = null;
        Status = IsPlaying ? PlaybackStatus.Playing : PlaybackStatus.Paused;
        _remoteRecoveryPolicy.MarkOpened(CurrentTrack.Id);
        if (_pendingRecoverySeekSeconds is double resumePosition)
        {
            _pendingRecoverySeekSeconds = null;
            _mpvHost.Engine.Seek(resumePosition);
            PositionSeconds = resumePosition;
            StartupLog.Write(
                $"playback.remote.resume-seek: provider={CurrentTrack.Provider}, candidate={OnlinePlaybackCandidateKey.Create(CurrentTrack)}, attempt={_remoteRecoveryPolicy.SourceAttemptCount}, position={resumePosition:0.###}");
        }
        else
        {
            PositionSeconds = 0;
            _positionResetPending = true;
        }
        StartupLog.Write(
            $"playback.mpv.started: title=\"{CurrentTrack.Title}\", status={Status}, route={_mpvHost.ActiveRoute}, source={DescribeSource(CurrentTrack.PlaybackSource)}");
        // Do not sample time-pos in the same callback that acknowledges the
        // new media. Some backends update it one message later, so the first
        // regular timer sample is the earliest reliable position. A recovery
        // seek was already applied above and must not be overwritten either.
        Notify();
    }

    private void HandleMediaFailed(
        string message,
        long loadSequence,
        string sourceKey,
        long hitsRevision = 0)
    {
        if (TryHandleHitsPlaybackFailed(
            message,
            loadSequence,
            sourceKey,
            hitsRevision))
        {
            return;
        }

        if (hitsRevision != 0 || IsHitsSessionActive)
        {
            return;
        }

        if (CurrentTrack is null)
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

        CancelLocalStartupWatchdog();
        var failureKind = OnlinePlaybackFailureClassifier.Classify(message);
        if (!failedTrack.IsRemote || failureKind == OnlinePlaybackFailureKind.AudioOutput)
        {
            if (TryFallbackAudioOutput(message, failedTrack, loadContext.Autoplay))
            {
                return;
            }

            SetPlaybackFailed(
                string.IsNullOrWhiteSpace(message)
                    ? "The audio source could not be opened."
                    : message);
            return;
        }

        var recoveryAction = _remoteRecoveryPolicy.DecideFailure(
            failedTrack.Id,
            failedTrack.IsRemote,
            failureKind);
        var candidateKey = OnlinePlaybackCandidateKey.Create(failedTrack);
        var resumePosition = Math.Max(PositionSeconds, _mpvHost.Engine.PositionSeconds);
        StartupLog.Write(
            $"playback.remote.failure: kind={failureKind}, provider={failedTrack.Provider}, candidate={candidateKey}, attempt={_remoteRecoveryPolicy.SourceAttemptCount}, action={recoveryAction}, source={DescribeSource(failedTrack.PlaybackSource)}");

        if (recoveryAction is RemotePlaybackRecoveryAction.ResolveNextSource
            or RemotePlaybackRecoveryAction.ResolveNextSourceAndResume)
        {
            _onlineAudioCache.Invalidate(failedTrack);
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
        // When all recovery attempts are exhausted for remote tracks, show a user-friendly message
        var isSourceExhausted = failedTrack.IsRemote
            && failureKind == OnlinePlaybackFailureKind.Source
            && recoveryAction == RemotePlaybackRecoveryAction.None;
        SetPlaybackFailed(
            isSourceExhausted
                ? "歌曲无法获取音源"
                : string.IsNullOrWhiteSpace(message)
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

    private bool TryFallbackAudioOutput(
        string reason,
        TrackModel track,
        bool autoplay)
    {
        var resumePosition = Math.Max(PositionSeconds, _mpvHost.Engine.PositionSeconds);
        if (!_mpvHost.TryFallback(reason))
        {
            StartupLog.Write(
                $"playback.output.exhausted: preferred={_mpvHost.PreferredModeId}, active={_mpvHost.ActiveRoute}, title=\"{track.Title}\"");
            return false;
        }

        CancelPendingLoad();
        _pendingRecoverySeekSeconds = resumePosition > 0 ? resumePosition : null;
        StartupLog.Write(
            $"playback.output.fallback: preferred={_mpvHost.PreferredModeId}, active={_mpvHost.ActiveRoute}, title=\"{track.Title}\", resume={(resumePosition > 0 ? "yes" : "no")}, reason={reason}");
        LoadMpvTrack(track, autoplay);
        Notify();
        return true;
    }

    private void ArmLocalStartupWatchdog(
        TrackModel track,
        PlaybackLoadContext loadContext,
        long engineGeneration)
    {
        CancelLocalStartupWatchdog();
        _localStartupCancellationSource = new CancellationTokenSource();
        _ = WatchLocalStartupAsync(
            _loadRevision,
            loadContext.Sequence,
            loadContext.SourceKey,
            engineGeneration,
            track,
            loadContext.Autoplay,
            _localStartupCancellationSource.Token);
    }

    private async Task WatchLocalStartupAsync(
        int revision,
        long loadSequence,
        string sourceKey,
        long engineGeneration,
        TrackModel track,
        bool autoplay,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            Dispatch(() =>
            {
                var currentSourceKey = CurrentTrack is null
                    ? null
                    : OnlinePlaybackCandidateKey.Create(CurrentTrack);
                if (revision != _loadRevision
                    || engineGeneration != _mpvHost.Generation
                    || CurrentTrack?.Id != track.Id
                    || !_mpvLoadEventGuard.TryAccept(
                        loadSequence,
                        sourceKey,
                        _loadRevision,
                        currentSourceKey,
                        out _))
                {
                    return;
                }

                const string timeoutMessage = "Local playback did not start within 5 seconds.";
                StartupLog.Write(
                    $"playback.local.start-timeout: revision={revision}, route={_mpvHost.ActiveRoute}, title=\"{track.Title}\"");
                if (!TryFallbackAudioOutput(timeoutMessage, track, autoplay))
                {
                    SetPlaybackFailed(timeoutMessage);
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void CancelLocalStartupWatchdog()
    {
        var cancellation = _localStartupCancellationSource;
        _localStartupCancellationSource = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private MpvPlaybackSnapshot? CaptureMpvPlaybackSnapshot()
    {
        if (CurrentTrack is null
            || NeedsOnlineResolution(CurrentTrack))
        {
            return null;
        }

        return new MpvPlaybackSnapshot(
            CurrentTrack,
            Math.Max(PositionSeconds, _mpvHost.Engine.PositionSeconds),
            IsPlaying || Status == PlaybackStatus.Buffering);
    }

    private void RestoreMpvPlaybackSnapshot(MpvPlaybackSnapshot snapshot)
    {
        _pendingRecoverySeekSeconds = snapshot.PositionSeconds > 0
            ? snapshot.PositionSeconds
            : null;
        LoadMpvTrack(snapshot.Track, snapshot.Autoplay);
    }

    private void ApplyAudioSettings()
    {
        if (IsHitsSessionActive)
        {
            return;
        }

        var settings = _settingsService.Current;
        var snapshot = CaptureMpvPlaybackSnapshot();
        if (snapshot is not null)
        {
            CancelPendingLoad();
        }

        if (!_mpvHost.ResetPreference(settings.AudioOutputMode, settings.AudioOutputDevice))
        {
            return;
        }

        StartupLog.Write(
            $"playback.output.preference: preferred={_mpvHost.PreferredModeId}, active={_mpvHost.ActiveRoute}, device={_mpvHost.OutputDevice}");
        if (snapshot is not null)
        {
            RestoreMpvPlaybackSnapshot(snapshot);
        }

        Notify();
    }

    private void ResetPreferredAudioRouteForNewTrack()
    {
        var settings = _settingsService.Current;
        if (_mpvHost.ResetPreference(settings.AudioOutputMode, settings.AudioOutputDevice))
        {
            StartupLog.Write(
                $"playback.output.reset: preferred={_mpvHost.PreferredModeId}, active={_mpvHost.ActiveRoute}, device={_mpvHost.OutputDevice}");
            Notify();
        }
    }

    private void SetPlaybackFailed(string message)
    {
        CancelLocalStartupWatchdog();
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
        IsPlaying = _mpvHost.Engine.IsPlaying;
        IsLoading = false;
        Error = _mpvHost.Engine.Error;
        Status = Error is not null
            ? PlaybackStatus.Failed
            : IsPlaying
                ? PlaybackStatus.Playing
                : PlaybackStatus.Paused;
        RefreshPosition();
    }

    private void RefreshHostStateWhenReady(long hitsRevision = 0)
    {
        if (TryRefreshHitsPosition(hitsRevision))
        {
            return;
        }

        if (hitsRevision != 0 || IsHitsSessionActive)
        {
            return;
        }

        if (CurrentTrack is not null
            && Status is not PlaybackStatus.Resolving and not PlaybackStatus.Buffering)
        {
            RefreshPlayerState();
        }
    }

    private void RefreshPosition()
    {
        var hitsRevision = CaptureHitsCallbackRevision();
        if (TryRefreshHitsPosition(hitsRevision) || hitsRevision != 0)
        {
            return;
        }

        if (CurrentTrack is null)
        {
            return;
        }

        if (Status is PlaybackStatus.Resolving or PlaybackStatus.Buffering)
        {
            return;
        }

        if (_positionResetPending)
        {
            _positionResetPending = false;
            PositionSeconds = 0;
            Notify();
            return;
        }

        var enginePosition = Math.Max(0, _mpvHost.Engine.PositionSeconds);
        var positionChanged = Math.Abs(PositionSeconds - enginePosition) > 0.3;
        if (PositionSeconds < 1 && enginePosition > 5)
        {
            return;
        }

        PositionSeconds = enginePosition;
        var naturalDuration = _mpvHost.Engine.DurationSeconds;
        if (naturalDuration > 0)
        {
            DurationSeconds = naturalDuration;
        }

        if (DurationSeconds > 0 && PositionSeconds > DurationSeconds + 1)
        {
            PositionSeconds = 0;
        }

        if (positionChanged)
        {
            Notify();
        }
    }

    private void CancelPendingLoad()
    {
        CancelLocalStartupWatchdog();
        _loadRevision++;
        _pendingRecoverySeekSeconds = null;
        _positionResetPending = false;
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

    public void Dispose()
    {
        _positionTimer.Stop();
        CancelPendingLoad();
        _mpvHost.Dispose();
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
        if (IsHitsSessionActive)
        {
            return;
        }

        Dispatch(() =>
        {
            if (!IsHitsSessionActive)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void AdvanceQueueRevision()
    {
        QueueRevision++;
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

    private sealed record MpvPlaybackSnapshot(
        TrackModel Track,
        double PositionSeconds,
        bool Autoplay);
}
