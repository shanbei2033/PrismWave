using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Hits;

public sealed partial class HitsStatusViewModel : ObservableObject
{
    private readonly IHitsService _hitsService;
    private readonly IPlaybackService _playbackService;
    private readonly ISettingsService _settingsService;
    private HitsStatusKind _status;
    private string _statusLabel = "Idle";
    private string _description = "HITS has not been loaded.";
    private string _editionDate = string.Empty;
    private bool _isAvailable;
    private bool _isRefreshing;
    private bool _usingCache;
    private bool _isSessionActive;
    private bool _isPaused;
    private bool _isResynchronizing;
    private HitsScheduleItemModel? _currentTrack;
    private HitsScheduleItemModel? _nextTrack;
    private double _playbackOffsetSeconds;
    private double? _pendingSeekSeconds;
    private string? _pendingSeekTrackId;
    private int _pendingSeekAttempts;
    private DateTimeOffset _lastPendingSeekAttemptAt = DateTimeOffset.MinValue;
    private bool _isApplyingPendingSeek;

    public HitsStatusViewModel(
        IHitsService hitsService,
        IPlaybackService playbackService,
        ISettingsService settingsService)
    {
        _hitsService = hitsService;
        _playbackService = playbackService;
        _settingsService = settingsService;
        _hitsService.StateChanged += (_, _) => RefreshState();
        _playbackService.StateChanged += (_, _) => HandlePlaybackStateChanged();
        RefreshState();
    }

    public ObservableCollection<HitsScheduleItemModel> Tracks { get; } = new();

    public HitsStatusKind Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                NotifyPresentationStateChanged();
            }
        }
    }

    public string StatusLabel
    {
        get => _statusLabel;
        private set => SetProperty(ref _statusLabel, value);
    }

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public string EditionDate
    {
        get => _editionDate;
        private set => SetProperty(ref _editionDate, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                NotifyPresentationStateChanged();
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                NotifyPresentationStateChanged();
            }
        }
    }

    public bool UsingCache
    {
        get => _usingCache;
        private set
        {
            if (SetProperty(ref _usingCache, value))
            {
                OnPropertyChanged(nameof(CacheLabel));
            }
        }
    }

    public bool IsSessionActive
    {
        get => _isSessionActive;
        private set
        {
            if (SetProperty(ref _isSessionActive, value))
            {
                NotifyPresentationStateChanged();
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                NotifyPresentationStateChanged();
            }
        }
    }

    public HitsScheduleItemModel? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            if (SetProperty(ref _currentTrack, value))
            {
                OnPropertyChanged(nameof(HasCurrentTrack));
                OnPropertyChanged(nameof(CurrentTrackTitle));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplayArtist));
                OnPropertyChanged(nameof(DisplayAlbum));
                OnPropertyChanged(nameof(CurrentCoverPath));
                NotifyPresentationStateChanged();
            }
        }
    }

    public HitsScheduleItemModel? NextTrack
    {
        get => _nextTrack;
        private set
        {
            if (SetProperty(ref _nextTrack, value))
            {
                OnPropertyChanged(nameof(NextTrackLabel));
            }
        }
    }

    public double PlaybackOffsetSeconds
    {
        get => _playbackOffsetSeconds;
        private set
        {
            if (SetProperty(ref _playbackOffsetSeconds, value))
            {
                OnPropertyChanged(nameof(PlaybackOffsetLabel));
            }
        }
    }

    public bool HasCurrentTrack => CurrentTrack is not null;
    public string CurrentTrackTitle => CurrentTrack?.Track.Title ?? "No programme on air";
    public string NextTrackLabel => NextTrack is null
        ? "No upcoming track"
        : $"Next · {NextTrack.Track.Title} · {NextTrack.StartAt:HH:mm} UTC";
    public string PlaybackOffsetLabel => $"Live +{TimeSpan.FromSeconds(Math.Max(0, PlaybackOffsetSeconds)):mm\\:ss}";
    public string CacheLabel => UsingCache ? "Cached schedule" : "Live schedule";
    public bool IsLive => IsSessionActive && IsAvailable && !IsPaused && CurrentTrack is not null;
    public bool ShowLiveDot => IsLive;
    public bool IsConnecting => IsRefreshing || _isResynchronizing;
    public bool CanToggleLivePlayback => IsSessionActive && IsAvailable && CurrentTrack is not null && !IsConnecting;
    public string BroadcastStateLabel => IsPaused
        ? "PAUSED"
        : IsLive ? "LIVE" : IsConnecting ? "CONNECTING" : StatusLabel.ToUpperInvariant();
    public string CoverHintGlyph => IsPaused ? "\uE768" : "\uE769";
    public string CoverAutomationName => IsPaused ? "Resume HITS live radio" : "Pause HITS live radio";
    public string DisplayTitle => CurrentTrack?.Track.Title ?? "HITS is off air";
    public string DisplayArtist => CurrentTrack?.Track.Artist ?? Description;
    public string DisplayAlbum => CurrentTrack?.Track.Album ?? string.Empty;
    public string? CurrentCoverPath => CurrentTrack?.Track.CoverPath;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return _hitsService.RefreshAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    public void Tick()
    {
        _hitsService.UpdatePosition(DateTimeOffset.UtcNow);
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        return _hitsService.RefreshAsync(DateTimeOffset.UtcNow);
    }

    [RelayCommand]
    private async Task PrepareHitsSessionAsync()
    {
        if (!IsAvailable || CurrentTrack is null)
        {
            return;
        }

        var settings = _settingsService.Current;
        if (!string.Equals(settings.AudioOutputMode, "wasapi_shared", StringComparison.Ordinal))
        {
            await _settingsService.SaveAsync(settings with { AudioOutputMode = "wasapi_shared" });
        }

        IsSessionActive = true;
        IsPaused = false;
        SyncPlayback(forceReload: true);
    }

    [RelayCommand(CanExecute = nameof(CanToggleLivePlayback))]
    private void ToggleLivePlayback()
    {
        if (IsPaused)
        {
            _isResynchronizing = true;
            IsPaused = false;
            _playbackService.TogglePlayPause();
            _hitsService.UpdatePosition(DateTimeOffset.UtcNow);
            _isResynchronizing = false;
            NotifyPresentationStateChanged();
            SyncPlayback(forceReload: true);
            return;
        }

        IsPaused = true;
        ClearPendingSeek();
        _playbackService.TogglePlayPause();
    }

    [RelayCommand]
    private async Task PlayTrackAsync(HitsScheduleItemModel item)
    {
        var settings = _settingsService.Current;
        if (!string.Equals(settings.AudioOutputMode, "wasapi_shared", StringComparison.Ordinal))
        {
            await _settingsService.SaveAsync(settings with { AudioOutputMode = "wasapi_shared" });
        }

        IsSessionActive = true;
        SetPendingSeek(
            item.Track.Id,
            item.Contains(DateTimeOffset.UtcNow)
                ? Math.Max(0, (DateTimeOffset.UtcNow - item.StartAt).TotalSeconds)
                : 0);
        _playbackService.Play(item.Track, Tracks.Select(track => track.Track).ToList());
    }

    private void RefreshState()
    {
        var state = _hitsService.Current;
        Status = state.Status;
        StatusLabel = state.StatusLabel;
        Description = state.Description;
        EditionDate = state.EditionDate;
        IsAvailable = state.IsAvailable;
        IsRefreshing = state.IsRefreshing;
        UsingCache = state.UsingCache;
        CurrentTrack = state.CurrentTrack;
        NextTrack = state.NextTrack;
        PlaybackOffsetSeconds = state.PlaybackOffsetSeconds;

        if (Tracks.Count != state.Tracks.Count
            || !Tracks.Select(item => item.StationTrackId).SequenceEqual(state.Tracks.Select(item => item.StationTrackId)))
        {
            Tracks.Clear();
            foreach (var track in state.Tracks)
            {
                Tracks.Add(track);
            }
        }

        SyncPlayback(forceReload: false);
    }

    private void SyncPlayback(bool forceReload)
    {
        if (!IsSessionActive || IsPaused || _isResynchronizing)
        {
            return;
        }

        if (Status != HitsStatusKind.Ready || CurrentTrack is null)
        {
            ClearPendingSeek();
            _playbackService.Stop();
            return;
        }

        var playbackTrack = _playbackService.CurrentTrack;
        if (forceReload || playbackTrack?.Id != CurrentTrack.Track.Id)
        {
            SetPendingSeek(CurrentTrack.Track.Id, PlaybackOffsetSeconds);
            _playbackService.Play(CurrentTrack.Track);
            return;
        }

        if (!_playbackService.IsLoading
            && Math.Abs(_playbackService.PositionSeconds - PlaybackOffsetSeconds) >= 1.4)
        {
            _playbackService.Seek(PlaybackOffsetSeconds);
        }
    }

    private void HandlePlaybackStateChanged()
    {
        ApplyPendingSeek();
        OnPropertyChanged(nameof(IsSessionActive));
        NotifyPresentationStateChanged();
    }

    private void ApplyPendingSeek()
    {
        if (_isApplyingPendingSeek)
        {
            return;
        }

        if (_pendingSeekSeconds is not double target
            || _playbackService.IsLoading
            || _playbackService.CurrentTrack is not { } playbackTrack
            || !string.Equals(playbackTrack.Id, _pendingSeekTrackId, StringComparison.Ordinal))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_pendingSeekAttempts > 0
            && now - _lastPendingSeekAttemptAt < TimeSpan.FromMilliseconds(300))
        {
            return;
        }

        if (_pendingSeekAttempts > 0
            && Math.Abs(_playbackService.PositionSeconds - target) < 1.4)
        {
            ClearPendingSeek();
            return;
        }

        if (_pendingSeekAttempts >= 12)
        {
            ClearPendingSeek();
            return;
        }

        _pendingSeekAttempts++;
        _lastPendingSeekAttemptAt = now;
        _isApplyingPendingSeek = true;
        try
        {
            _playbackService.Seek(target);
        }
        finally
        {
            _isApplyingPendingSeek = false;
        }
    }

    private void SetPendingSeek(string trackId, double seconds)
    {
        _pendingSeekTrackId = trackId;
        _pendingSeekSeconds = Math.Max(0, seconds);
        _pendingSeekAttempts = 0;
        _lastPendingSeekAttemptAt = DateTimeOffset.MinValue;
    }

    private void ClearPendingSeek()
    {
        _pendingSeekTrackId = null;
        _pendingSeekSeconds = null;
        _pendingSeekAttempts = 0;
        _lastPendingSeekAttemptAt = DateTimeOffset.MinValue;
    }

    private void NotifyPresentationStateChanged()
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(ShowLiveDot));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(CanToggleLivePlayback));
        OnPropertyChanged(nameof(BroadcastStateLabel));
        OnPropertyChanged(nameof(CoverHintGlyph));
        OnPropertyChanged(nameof(CoverAutomationName));
        ToggleLivePlaybackCommand.NotifyCanExecuteChanged();
    }
}
