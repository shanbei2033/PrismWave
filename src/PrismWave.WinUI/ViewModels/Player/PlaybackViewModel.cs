using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.ViewModels.Player;

public sealed partial class PlaybackViewModel : ObservableObject
{
    private static readonly Regex LyricsOffsetInputPattern = new(
        @"^[+-]?\d+(?:\.\d)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IPlaybackService _playbackService;
    private readonly ILyricsService _lyricsService;
    private readonly ICoverService? _coverService;
    private readonly ILibraryService? _libraryService;
    private TrackModel? _currentTrack;
    private string? _lyricsTrackId;
    private int _lyricsRevision;
    private CancellationTokenSource? _lyricsCancellationSource;
    private bool _isPlaying;
    private bool _isLoading;
    private bool _isLyricsLoading;
    private string _modeLabel = "列表循环";
    private string _modeGlyph = "\uE8EE";
    private double _volume = 0.78;
    private double _positionSeconds;
    private double _durationSeconds;
    private string? _error;
    private string? _currentCoverPath;
    private PlaybackStatus _status;
    private int _currentLyricIndex = -1;
    private string _lyricsSource = "local";
    private string _lyricsProvider = "local";
    private string _lyricsStatus = "No lyrics available";
    private double _lyricsOffsetSeconds;
    private bool _isManualScrolling;
    private int _selectedLyricIndex = -1;
    private bool _lyricsPresentationUpdatesActive;
    private double _lyricsPresentationPositionSeconds;
    private long _observedQueueRevision = long.MinValue;
    private bool _isQueueReorderActive;

    public PlaybackViewModel(
        IPlaybackService playbackService,
        ILyricsService lyricsService,
        ICoverService? coverService = null,
        ILibraryService? libraryService = null)
    {
        _playbackService = playbackService;
        _lyricsService = lyricsService;
        _coverService = coverService;
        _libraryService = libraryService;
        _playbackService.StateChanged += (_, _) => Refresh();
        if (_coverService is not null)
        {
            _coverService.CoverChanged += CoverService_CoverChanged;
        }

        if (_libraryService is not null)
        {
            _libraryService.LibraryChanged += LibraryService_LibraryChanged;
        }

        Refresh();
    }

    public TrackModel? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            if (SetProperty(ref _currentTrack, value))
            {
                RefreshFavoriteState();
                NotifyLyricsEmptyStateChanged();
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(ToggleGlyph));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsLyricsLoading
    {
        get => _isLyricsLoading;
        private set
        {
            if (SetProperty(ref _isLyricsLoading, value))
            {
                NotifyLyricsEmptyStateChanged();
            }
        }
    }

    public string ModeLabel
    {
        get => _modeLabel;
        private set => SetProperty(ref _modeLabel, value);
    }

    public string ModeGlyph
    {
        get => _modeGlyph;
        private set => SetProperty(ref _modeGlyph, value);
    }

    public double Volume
    {
        get => _volume;
        private set => SetProperty(ref _volume, value);
    }

    public double PositionSeconds
    {
        get => _positionSeconds;
        private set
        {
            if (SetProperty(ref _positionSeconds, value))
            {
                OnPropertyChanged(nameof(PositionLabel));
            }
        }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            if (SetProperty(ref _durationSeconds, value))
            {
                OnPropertyChanged(nameof(DurationLabel));
            }
        }
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public PlaybackStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusLabel));
            }
        }
    }

    public int CurrentLyricIndex
    {
        get => _currentLyricIndex;
        private set
        {
            if (SetProperty(ref _currentLyricIndex, value))
            {
                OnPropertyChanged(nameof(CurrentLyricText));
            }
        }
    }

    public LyricsTransitionKind CurrentLyricTransitionKind { get; private set; } = LyricsTransitionKind.Initial;

    public string LyricsSource
    {
        get => _lyricsSource;
        private set
        {
            if (SetProperty(ref _lyricsSource, value))
            {
                OnPropertyChanged(nameof(LyricsSourceLabel));
            }
        }
    }

    public string LyricsProvider
    {
        get => _lyricsProvider;
        private set => SetProperty(ref _lyricsProvider, value);
    }

    public string LyricsStatus
    {
        get => _lyricsStatus;
        private set => SetProperty(ref _lyricsStatus, value);
    }

    public double LyricsOffsetSeconds
    {
        get => _lyricsOffsetSeconds;
        private set
        {
            if (SetProperty(ref _lyricsOffsetSeconds, value))
            {
                OnPropertyChanged(nameof(LyricsOffsetLabel));
            }
        }
    }

    public bool IsManualScrolling
    {
        get => _isManualScrolling;
        private set => SetProperty(ref _isManualScrolling, value);
    }

    public int SelectedLyricIndex
    {
        get => _selectedLyricIndex;
        private set => SetProperty(ref _selectedLyricIndex, value);
    }

    public ObservableCollection<TrackModel> Queue { get; } = new();
    public ObservableCollection<PlaybackQueueItemViewModel> QueueItems { get; } = new();
    public ObservableCollection<LyricLineDisplayModel> Lyrics { get; } = new();

    public string CurrentTitle => CurrentTrack?.Title ?? "未选择歌曲";
    public string CurrentArtist => CurrentTrack?.Artist ?? "--";
    public string CurrentAlbum => CurrentTrack?.Album ?? string.Empty;
    public string CurrentSubtitle => CurrentTrack is null ? "--" : $"{CurrentTrack.Artist} - {CurrentTrack.Album}";
    public string? CurrentCoverPath => _currentCoverPath;
    public bool CanFavoriteCurrentTrack => _libraryService is not null &&
                                           CurrentTrack is { IsRemote: false } track &&
                                           !string.IsNullOrWhiteSpace(track.Path);
    public string CurrentFavoriteGlyph => IsCurrentTrackFavorite ? "\uEB52" : "\uEB51";
    public bool HasTrack => CurrentTrack is not null;
    public bool HasQueue => QueueItems.Count > 0;
    public string QueueCountLabel => $"{QueueItems.Count} 首";
    public bool HasLyrics => Lyrics.Count > 0;
    public bool ShowLyricsEmptyState => !IsLyricsLoading && Lyrics.Count == 0;
    public string LyricsEmptyMessage => CurrentTrack is null
        ? "选择一首歌曲后显示歌词"
        : "暂未找到歌词";
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public string ToggleGlyph => IsPlaying ? "\uE769" : "\uE768";
    public string PositionLabel => FormatTime(PositionSeconds);
    public string DurationLabel => DurationSeconds > 0 ? FormatTime(DurationSeconds) : CurrentTrack?.Duration ?? "--:--";
    public string StatusLabel => Status switch
    {
        PlaybackStatus.Resolving => "正在解析音源",
        PlaybackStatus.Buffering => "正在缓冲",
        PlaybackStatus.Failed => Error ?? "无法播放",
        _ => string.Empty
    };
    public string CurrentLyricText => CurrentLyricIndex >= 0 && CurrentLyricIndex < Lyrics.Count
        ? Lyrics[CurrentLyricIndex].Text
        : string.Empty;
    public string LyricsSourceLabel => LyricsSource == "online" ? "Online" : "Local";
    public string LyricsOffsetLabel => Math.Abs(LyricsOffsetSeconds) < 0.001
        ? "0.0 s"
        : $"{LyricsOffsetSeconds:+0.0;-0.0} s";
    public double EffectiveLyricsPositionSeconds => Math.Max(0, PositionSeconds - LyricsOffsetSeconds);
    public string? WindowsDsdOutputModeLabel => _playbackService.WindowsDsdOutputModeLabel;
    public string? WindowsDsdActiveDeviceName => _playbackService.WindowsDsdActiveDeviceName;
    public string? WindowsDsdFallbackReason => _playbackService.WindowsDsdFallbackReason;

    [RelayCommand]
    private void TogglePlayPause()
    {
        _playbackService.TogglePlayPause();
    }

    [RelayCommand]
    private void Previous()
    {
        _playbackService.Previous();
    }

    [RelayCommand]
    private void Next()
    {
        _playbackService.Next();
    }

    [RelayCommand]
    private void CycleMode()
    {
        _playbackService.CycleMode();
    }

    [RelayCommand(CanExecute = nameof(CanToggleCurrentFavorite))]
    private async Task ToggleCurrentFavoriteAsync()
    {
        if (_libraryService is null || CurrentTrack is null || !CanFavoriteCurrentTrack)
        {
            return;
        }

        await _libraryService.ToggleFavoriteAsync(CurrentTrack);
        RefreshFavoriteState();
    }

    [RelayCommand]
    private void RemoveFromQueue(TrackModel track)
    {
        _playbackService.RemoveFromQueue(track);
    }

    [RelayCommand]
    private void PlayQueueTrack(TrackModel track)
    {
        _playbackService.PlayFromQueue(track);
    }

    [RelayCommand]
    private void ClearQueue()
    {
        _playbackService.ClearQueue();
    }

    [RelayCommand]
    private async Task ToggleLyricsSourceAsync()
    {
        if (CurrentTrack is null)
        {
            return;
        }

        var previous = LyricsSource;
        var next = LyricsSource == "local" ? "online" : "local";
        await _lyricsService.SetPreferredSourceAsync(CurrentTrack, next);
        if (!await LoadLyricsAsync(
                CurrentTrack,
                ++_lyricsRevision,
                next,
                preserveExistingOnEmpty: true))
        {
            await _lyricsService.SetPreferredSourceAsync(CurrentTrack, previous);
            LyricsSource = previous;
            LyricsStatus = $"No {next} lyrics available";
        }
    }

    [RelayCommand]
    private async Task ReloadOnlineLyricsAsync()
    {
        if (CurrentTrack is null)
        {
            return;
        }

        var previous = LyricsSource;
        await _lyricsService.SetPreferredSourceAsync(CurrentTrack, "online");
        if (!await LoadLyricsAsync(
                CurrentTrack,
                ++_lyricsRevision,
                "online",
                forceOnline: true,
                preserveExistingOnEmpty: true))
        {
            await _lyricsService.SetPreferredSourceAsync(CurrentTrack, previous);
            LyricsSource = previous;
            LyricsStatus = "Online lyrics unavailable";
        }
    }

    [RelayCommand]
    private Task IncreaseLyricsOffsetAsync()
    {
        return SetLyricsOffsetAsync(LyricsOffsetSeconds + 0.2);
    }

    [RelayCommand]
    private Task DecreaseLyricsOffsetAsync()
    {
        return SetLyricsOffsetAsync(LyricsOffsetSeconds - 0.2);
    }

    [RelayCommand]
    private Task ResetLyricsOffsetAsync()
    {
        return SetLyricsOffsetAsync(0);
    }

    public Task<IReadOnlyList<LyricsSearchResultModel>> SearchOnlineLyricsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        return CurrentTrack is null
            ? Task.FromResult<IReadOnlyList<LyricsSearchResultModel>>(Array.Empty<LyricsSearchResultModel>())
            : _lyricsService.SearchOnlineLyricsAsync(CurrentTrack, query, cancellationToken);
    }

    public async Task<bool> ApplyLyricsSearchResultAsync(
        LyricsSearchResultModel result,
        CancellationToken cancellationToken = default)
    {
        var track = CurrentTrack;
        if (track is null)
        {
            return false;
        }

        var revision = ++_lyricsRevision;
        _lyricsCancellationSource?.Cancel();
        _lyricsCancellationSource?.Dispose();
        var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _lyricsCancellationSource = operationCancellation;
        IsLyricsLoading = true;
        LyricsStatus = $"Loading {result.Provider} lyrics...";
        try
        {
            var document = await _lyricsService.LoadSearchResultAsync(
                track,
                result,
                operationCancellation.Token);
            if (revision != _lyricsRevision || CurrentTrack?.Id != track.Id || document.IsEmpty)
            {
                return false;
            }

            ApplyLyricsDocument(document);
            return true;
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (revision == _lyricsRevision)
            {
                IsLyricsLoading = false;
            }
        }
    }

    public void PersistQueueOrder()
    {
        CompleteQueueReorder();
    }

    public void BeginQueueReorder()
    {
        _isQueueReorderActive = true;
    }

    public void CompleteQueueReorder()
    {
        _isQueueReorderActive = false;
        _playbackService.ReorderQueue(QueueItems.Select(item => item.Track).ToArray());
    }

    public void SetVolume(double value)
    {
        _playbackService.SetVolume(value);
    }

    public void Seek(double seconds)
    {
        var position = Math.Max(0, seconds);
        _playbackService.Seek(position);
        PositionSeconds = position;
        _lyricsPresentationPositionSeconds = position;
        UpdateCurrentLyric(
            Math.Max(0, position - LyricsOffsetSeconds),
            LyricsTransitionKind.Rapid);
    }

    public void BeginManualLyricsBrowse()
    {
        if (IsManualScrolling)
        {
            return;
        }

        IsManualScrolling = true;
        SelectedLyricIndex = -1;
        foreach (var line in Lyrics)
        {
            line.IsManualBrowsing = true;
        }
    }

    public void EndManualLyricsBrowse(
        LyricsTransitionKind transitionKind = LyricsTransitionKind.Natural)
    {
        IsManualScrolling = false;
        SelectedLyricIndex = -1;
        foreach (var line in Lyrics)
        {
            line.TransitionKind = transitionKind;
            line.IsManualBrowsing = false;
        }
    }

    public void SeekToLyric(int index)
    {
        if (index < 0 || index >= Lyrics.Count)
        {
            return;
        }

        SelectedLyricIndex = index;
        var playbackPosition = Math.Max(0, Lyrics[index].TimeSeconds + LyricsOffsetSeconds);
        _playbackService.Seek(playbackPosition);
        PositionSeconds = playbackPosition;
        _lyricsPresentationPositionSeconds = playbackPosition;
        EndManualLyricsBrowse(LyricsTransitionKind.Rapid);
        UpdateCurrentLyric(
            Math.Max(0, playbackPosition - LyricsOffsetSeconds),
            LyricsTransitionKind.Rapid);
    }

    public void UpdateLyricsPresentationPosition(double positionSeconds)
    {
        _lyricsPresentationPositionSeconds = Math.Max(0, positionSeconds);
        UpdateCurrentLyric(
            Math.Max(0, _lyricsPresentationPositionSeconds - LyricsOffsetSeconds),
            LyricsTransitionKind.Natural);
    }

    public void BeginLyricsPresentationUpdates()
    {
        _lyricsPresentationUpdatesActive = true;
        _lyricsPresentationPositionSeconds = PositionSeconds;
        UpdateCurrentLyric(
            Math.Max(0, _lyricsPresentationPositionSeconds - LyricsOffsetSeconds),
            LyricsTransitionKind.Initial);
    }

    public void EndLyricsPresentationUpdates()
    {
        _lyricsPresentationUpdatesActive = false;
        UpdateCurrentLyric(null, LyricsTransitionKind.Rapid);
    }

    public async Task<bool> ApplyLyricsOffsetAsync(string input)
    {
        var normalized = input.Trim();
        if (CurrentTrack is null
            || !LyricsOffsetInputPattern.IsMatch(normalized)
            || !double.TryParse(
                normalized,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return false;
        }

        await SetLyricsOffsetAsync(value);
        return true;
    }

    private async Task SetLyricsOffsetAsync(double value)
    {
        if (CurrentTrack is null)
        {
            return;
        }

        await _lyricsService.SetOffsetSecondsAsync(CurrentTrack, value);
        LyricsOffsetSeconds = _lyricsService.GetOffsetSeconds(CurrentTrack);
        UpdateCurrentLyric(
            _lyricsPresentationUpdatesActive
                ? Math.Max(0, _lyricsPresentationPositionSeconds - LyricsOffsetSeconds)
                : null,
            LyricsTransitionKind.Rapid);
    }

    private void Refresh()
    {
        var previousTrackId = CurrentTrack?.Id;
        CurrentTrack = _playbackService.CurrentTrack;
        var trackChanged = !string.Equals(previousTrackId, CurrentTrack?.Id, StringComparison.Ordinal);
        if (CurrentTrack?.Id != _lyricsTrackId)
        {
            _lyricsTrackId = CurrentTrack?.Id;
            LyricsOffsetSeconds = CurrentTrack is null ? 0 : _lyricsService.GetOffsetSeconds(CurrentTrack);
            LyricsSource = CurrentTrack is null ? "local" : _lyricsService.GetPreferredSource(CurrentTrack);
            _ = LoadLyricsAsync(CurrentTrack, ++_lyricsRevision);
        }

        IsPlaying = _playbackService.IsPlaying;
        IsLoading = _playbackService.IsLoading;
        Status = _playbackService.Status;
        Volume = _playbackService.Volume;
        PositionSeconds = _playbackService.PositionSeconds;
        DurationSeconds = _playbackService.DurationSeconds;
        Error = _playbackService.Error;
        ModeLabel = _playbackService.Mode switch
        {
            PlaybackMode.Single => "单曲循环",
            PlaybackMode.Shuffle => "随机播放",
            _ => "列表循环"
        };
        ModeGlyph = _playbackService.Mode switch
        {
            PlaybackMode.Single => "\uE8ED",
            PlaybackMode.Shuffle => "\uE8B1",
            _ => "\uE8EE"
        };

        var queueRevision = _playbackService.QueueRevision;
        if (!_isQueueReorderActive && queueRevision != _observedQueueRevision)
        {
            SynchronizeQueueCollections(_playbackService.Queue);
            _observedQueueRevision = queueRevision;
        }

        RefreshQueueCurrentState();

        if (!_lyricsPresentationUpdatesActive || trackChanged)
        {
            if (trackChanged)
            {
                _lyricsPresentationPositionSeconds = PositionSeconds;
            }

            UpdateCurrentLyric(
                _lyricsPresentationUpdatesActive
                    ? Math.Max(0, _lyricsPresentationPositionSeconds - LyricsOffsetSeconds)
                    : null,
                trackChanged ? LyricsTransitionKind.Initial : LyricsTransitionKind.Natural);
        }
        RefreshCurrentCoverPath();
        OnPropertyChanged(nameof(HasQueue));
        OnPropertyChanged(nameof(QueueCountLabel));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentArtist));
        OnPropertyChanged(nameof(CurrentAlbum));
        OnPropertyChanged(nameof(CurrentSubtitle));
        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(ToggleGlyph));
        OnPropertyChanged(nameof(DurationLabel));
        OnPropertyChanged(nameof(WindowsDsdOutputModeLabel));
        OnPropertyChanged(nameof(WindowsDsdActiveDeviceName));
        OnPropertyChanged(nameof(WindowsDsdFallbackReason));
    }

    private void CoverService_CoverChanged(object? sender, CoverChangedEventArgs e)
    {
        for (var index = 0; index < Queue.Count; index++)
        {
            var queuedTrack = Queue[index];
            if (string.Equals(queuedTrack.Id, e.TrackId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(queuedTrack.Path, e.TrackPath, StringComparison.OrdinalIgnoreCase)
                || TrackCoverIdentity.Matches(
                    queuedTrack.Title,
                    queuedTrack.Artist,
                    e.Title,
                    e.Artist))
            {
                Queue[index] = queuedTrack with { CoverPath = e.CoverPath };
            }
        }

        foreach (var item in QueueItems)
        {
            var queuedTrack = item.Track;
            if (string.Equals(queuedTrack.Id, e.TrackId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(queuedTrack.Path, e.TrackPath, StringComparison.OrdinalIgnoreCase)
                || TrackCoverIdentity.Matches(
                    queuedTrack.Title,
                    queuedTrack.Artist,
                    e.Title,
                    e.Artist))
            {
                var updated = queuedTrack with { CoverPath = e.CoverPath };
                item.Update(updated, item.Position, e.CoverPath, item.IsCurrent);
            }
        }

        if (CurrentTrack is not null
            && (string.Equals(CurrentTrack.Id, e.TrackId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(CurrentTrack.Path, e.TrackPath, StringComparison.OrdinalIgnoreCase)
                || TrackCoverIdentity.Matches(
                    CurrentTrack.Title,
                    CurrentTrack.Artist,
                    e.Title,
                    e.Artist)))
        {
            RefreshCurrentCoverPath();
        }
    }

    private void RefreshCurrentCoverPath()
    {
        var next = CurrentTrack is null
            ? null
            : _coverService?.ResolveCoverPath(CurrentTrack) ?? CurrentTrack.CoverPath;
        if (string.Equals(_currentCoverPath, next, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentCoverPath = next;
        OnPropertyChanged(nameof(CurrentCoverPath));
    }

    private void SynchronizeQueueCollections(IReadOnlyList<TrackModel> desiredTracks)
    {
        SynchronizeTrackQueue(desiredTracks);

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < desiredTracks.Count; index++)
        {
            var track = desiredTracks[index];
            var occurrence = occurrences.TryGetValue(track.Id, out var count) ? count : 0;
            occurrences[track.Id] = occurrence + 1;
            var entryId = $"{track.Id}\u001f{occurrence}";
            var existingIndex = FindQueueItemIndex(entryId, index);
            PlaybackQueueItemViewModel item;
            if (existingIndex < 0)
            {
                item = new PlaybackQueueItemViewModel(
                    entryId,
                    track,
                    index + 1,
                    ResolveQueueCoverPath(track),
                    IsCurrentTrack(track));
                QueueItems.Insert(index, item);
            }
            else
            {
                if (existingIndex != index)
                {
                    QueueItems.Move(existingIndex, index);
                }

                item = QueueItems[index];
                item.Update(
                    track,
                    index + 1,
                    ResolveQueueCoverPath(track),
                    IsCurrentTrack(track));
            }
        }

        while (QueueItems.Count > desiredTracks.Count)
        {
            QueueItems.RemoveAt(QueueItems.Count - 1);
        }
    }

    private void SynchronizeTrackQueue(IReadOnlyList<TrackModel> desiredTracks)
    {
        for (var index = 0; index < desiredTracks.Count; index++)
        {
            var desired = desiredTracks[index];
            var existingIndex = FindTrackIndex(desired.Id, index);
            if (existingIndex < 0)
            {
                Queue.Insert(index, desired);
                continue;
            }

            if (existingIndex != index)
            {
                Queue.Move(existingIndex, index);
            }

            if (!Equals(Queue[index], desired))
            {
                Queue[index] = desired;
            }
        }

        while (Queue.Count > desiredTracks.Count)
        {
            Queue.RemoveAt(Queue.Count - 1);
        }
    }

    private int FindQueueItemIndex(string entryId, int startIndex)
    {
        for (var index = startIndex; index < QueueItems.Count; index++)
        {
            if (string.Equals(QueueItems[index].EntryId, entryId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private int FindTrackIndex(string trackId, int startIndex)
    {
        for (var index = startIndex; index < Queue.Count; index++)
        {
            if (string.Equals(Queue[index].Id, trackId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void RefreshQueueCurrentState()
    {
        for (var index = 0; index < QueueItems.Count; index++)
        {
            var item = QueueItems[index];
            item.Update(
                item.Track,
                index + 1,
                item.CoverPath,
                IsCurrentTrack(item.Track));
        }
    }

    private string? ResolveQueueCoverPath(TrackModel track) =>
        _coverService?.ResolveCoverPath(track) ?? track.CoverPath;

    private bool IsCurrentTrack(TrackModel track) =>
        string.Equals(track.Id, CurrentTrack?.Id, StringComparison.Ordinal);

    private bool CanToggleCurrentFavorite()
    {
        return CanFavoriteCurrentTrack;
    }

    private bool IsCurrentTrackFavorite => CurrentTrack is not null &&
                                           _libraryService?.Favorites.Any(track =>
                                               string.Equals(
                                                   track.Path,
                                                   CurrentTrack.Path,
                                                   StringComparison.OrdinalIgnoreCase)) == true;

    private void LibraryService_LibraryChanged(object? sender, EventArgs e)
    {
        RefreshFavoriteState();
    }

    private void RefreshFavoriteState()
    {
        OnPropertyChanged(nameof(CanFavoriteCurrentTrack));
        OnPropertyChanged(nameof(CurrentFavoriteGlyph));
        ToggleCurrentFavoriteCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> LoadLyricsAsync(
        TrackModel? track,
        int revision,
        string? sourceOverride = null,
        bool forceOnline = false,
        bool preserveExistingOnEmpty = false)
    {
        _lyricsCancellationSource?.Cancel();
        _lyricsCancellationSource?.Dispose();
        _lyricsCancellationSource = new CancellationTokenSource();
        var cancellationToken = _lyricsCancellationSource.Token;
        if (!preserveExistingOnEmpty)
        {
            Lyrics.Clear();
            CurrentLyricIndex = -1;
            OnPropertyChanged(nameof(HasLyrics));
            NotifyLyricsEmptyStateChanged();
        }

        if (track is null)
        {
            LyricsStatus = "No track selected";
            return false;
        }

        IsLyricsLoading = true;
        LyricsStatus = "Loading lyrics...";
        try
        {
            var document = await _lyricsService.LoadLyricsDocumentAsync(
                track,
                sourceOverride,
                forceOnline,
                cancellationToken);
            if (revision != _lyricsRevision)
            {
                return false;
            }

            if (document.IsEmpty && preserveExistingOnEmpty)
            {
                return false;
            }

            ApplyLyricsDocument(document);
            if (!document.IsEmpty
                && !document.HasTimedSegments
                && document.SelectionKind == LyricsSelectionKind.Auto
                && string.Equals(document.Source, "online", StringComparison.OrdinalIgnoreCase))
            {
                _ = TryUpgradeWordSyncedLyricsAsync(track, revision, cancellationToken);
            }

            return !document.IsEmpty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            if (revision == _lyricsRevision)
            {
                LyricsStatus = $"Lyrics unavailable · {exception.Message}";
            }

            return false;
        }
        finally
        {
            if (revision == _lyricsRevision)
            {
                IsLyricsLoading = false;
            }
        }
    }

    private async Task TryUpgradeWordSyncedLyricsAsync(
        TrackModel track,
        int revision,
        CancellationToken cancellationToken)
    {
        try
        {
            var upgraded = await _lyricsService.TryLoadWordSyncedLyricsDocumentAsync(
                track,
                cancellationToken);
            if (upgraded is null
                || upgraded.IsEmpty
                || !upgraded.HasTimedSegments
                || revision != _lyricsRevision
                || CurrentTrack?.Id != track.Id)
            {
                return;
            }

            ApplyLyricsDocument(upgraded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StartupLog.Write(
                $"lyrics.online.word-upgrade.failed: track=\"{track.Title}\", error={exception.Message}");
        }
    }

    private void ApplyLyricsDocument(LyricsDocumentModel document)
    {
        Lyrics.Clear();
        CurrentLyricIndex = -1;
        LyricsSource = document.Source;
        LyricsProvider = document.Provider;
        foreach (var line in document.Lines)
        {
            Lyrics.Add(new LyricLineDisplayModel(line));
        }

        LyricsStatus = document.IsEmpty
            ? "No lyrics available"
            : $"{document.Lines.Count} lines · {document.Provider}";
        OnPropertyChanged(nameof(HasLyrics));
        NotifyLyricsEmptyStateChanged();
        UpdateCurrentLyric(
            _lyricsPresentationUpdatesActive
                ? Math.Max(0, _lyricsPresentationPositionSeconds - LyricsOffsetSeconds)
                : null,
            LyricsTransitionKind.Initial);
    }

    private void NotifyLyricsEmptyStateChanged()
    {
        OnPropertyChanged(nameof(ShowLyricsEmptyState));
        OnPropertyChanged(nameof(LyricsEmptyMessage));
    }

    private void UpdateCurrentLyric(
        double? presentationPositionSeconds = null,
        LyricsTransitionKind transitionKind = LyricsTransitionKind.Natural)
    {
        if (Lyrics.Count == 0)
        {
            CurrentLyricIndex = -1;
            return;
        }

        var effectivePosition = presentationPositionSeconds ?? EffectiveLyricsPositionSeconds;
        var nextIndex = LyricsTimeline.FindActiveIndex(Lyrics, effectivePosition);

        if (CurrentLyricIndex != nextIndex)
        {
            CurrentLyricTransitionKind = transitionKind;
            for (var index = 0; index < Lyrics.Count; index++)
            {
                var line = Lyrics[index];
                line.TransitionKind = transitionKind;
                line.IsCurrent = index == nextIndex;
                line.DistanceFromCurrent = Math.Abs(index - nextIndex);
                if (!line.IsCurrent)
                {
                    line.WordProgress = 0;
                }
            }

            CurrentLyricIndex = nextIndex;
        }

        Lyrics[nextIndex].WordProgress = Lyrics[nextIndex].Segments.Count > 0
            ? CalculateKaraokeProgress(Lyrics[nextIndex].Segments, effectivePosition)
            : CalculateFallbackLineProgress(nextIndex, effectivePosition);
    }

    private double CalculateFallbackLineProgress(int lineIndex, double positionSeconds)
    {
        var start = Lyrics[lineIndex].TimeSeconds;
        var end = lineIndex + 1 < Lyrics.Count
            ? Lyrics[lineIndex + 1].TimeSeconds
            : start + 3;
        var duration = end - start;
        if (duration <= 0)
        {
            return positionSeconds >= start ? 1 : 0;
        }

        var progress = Math.Clamp((positionSeconds - start) / duration, 0, 1);
        return progress * progress * (3 - (2 * progress));
    }

    private static double CalculateKaraokeProgress(
        IReadOnlyList<LyricSegmentModel> segments,
        double positionSeconds)
    {
        if (segments.Count == 0)
        {
            return 0;
        }

        var totalLength = segments.Sum(segment => CountPaintableCharacters(segment.Text));
        var completed = 0d;
        foreach (var segment in segments)
        {
            var segmentLength = CountPaintableCharacters(segment.Text);
            if (positionSeconds >= segment.EndSeconds)
            {
                completed += segmentLength;
                continue;
            }

            if (positionSeconds > segment.StartSeconds)
            {
                var duration = Math.Max(0.001, segment.EndSeconds - segment.StartSeconds);
                completed += segmentLength * Math.Clamp((positionSeconds - segment.StartSeconds) / duration, 0, 1);
            }

            break;
        }

        return totalLength <= 0 ? 0 : completed / totalLength;
    }

    private static int CountPaintableCharacters(string text)
    {
        return Math.Max(1, text.Count(character => !char.IsWhiteSpace(character)));
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return "00:00";
        }

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }
}
