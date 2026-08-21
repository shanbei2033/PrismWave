using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Infrastructure;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;

namespace PrismWave_WinUI.ViewModels.Library;

public sealed partial class TrackEditorViewModel : ObservableObject, IDisposable
{
    private readonly ITrackMetadataService _metadataService;
    private readonly ILibraryService _libraryService;
    private readonly IPlaybackService _playbackService;
    private readonly ICoverService? _coverService;

    private TrackModel? _track;
    private TrackMetadataModel? _loadedMetadata;
    private TrackMetadataModel? _originalMetadata;
    private bool _suppressDirtyCheck;

    public TrackEditorViewModel(
        ITrackMetadataService metadataService,
        ILibraryService libraryService,
        IPlaybackService playbackService,
        ICoverService? coverService = null)
    {
        _metadataService = metadataService;
        _libraryService = libraryService;
        _playbackService = playbackService;
        _coverService = coverService;
        _playbackService.StateChanged += PlaybackService_StateChanged;
    }

    public event EventHandler? SaveCompleted;

    [ObservableProperty]
    private TrackModel? currentTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InfoLine))]
    private string title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InfoLine))]
    private string artist = string.Empty;

    [ObservableProperty]
    private string album = string.Empty;

    [ObservableProperty]
    private string albumArtist = string.Empty;

    [ObservableProperty]
    private string year = string.Empty;

    [ObservableProperty]
    private string genre = string.Empty;

    [ObservableProperty]
    private string lyrics = string.Empty;

    [ObservableProperty]
    private byte[]? coverBytes;

    [ObservableProperty]
    private string? pendingCoverImagePath;

    [ObservableProperty]
    private bool removeCoverRequested;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private bool statusIsError;

    [ObservableProperty]
    private bool hasChanges;

    [ObservableProperty]
    private bool isFormatWritable = true;

    public ObservableCollection<string> ReadOnlyInfo { get; } = [];

    public string InfoLine => CurrentTrack is null
        ? string.Empty
        : $"{Title} · {Artist}";

    public bool IsPlayingThisTrack => CurrentTrack is not null
        && _playbackService.CurrentTrack is { } playing
        && string.Equals(
            playing.Path,
            CurrentTrack.Path,
            StringComparison.OrdinalIgnoreCase);

    public bool CanEditNow => CurrentTrack is not null
        && IsFormatWritable
        && !IsPlayingThisTrack;

    public bool CanSave => CanEditNow && HasChanges && !IsBusy;

    public string? LockedReason
    {
        get
        {
            if (CurrentTrack is null)
            {
                return null;
            }

            if (!IsFormatWritable)
            {
                return "该格式不支持写入元数据，以下信息仅供查看。";
            }

            if (IsPlayingThisTrack)
            {
                return "正在播放该歌曲（暂停中同样占用文件），请停止播放或切换歌曲后再修改。";
            }

            return null;
        }
    }

    public async Task LoadAsync(TrackModel track)
    {
        _track = track;
        _suppressDirtyCheck = true;
        CurrentTrack = track;
        IsFormatWritable = TrackMetadataService.IsWritableFormat(track.Path);
        PendingCoverImagePath = null;
        RemoveCoverRequested = false;
        StatusMessage = null;
        StatusIsError = false;
        IsBusy = true;
        HasChanges = false;

        ReadOnlyInfo.Clear();
        ReadOnlyInfo.Add($"格式：{track.Codec ?? System.IO.Path.GetExtension(track.Path).TrimStart('.').ToUpperInvariant()}");
        ReadOnlyInfo.Add($"码率：{track.BitrateLabel}");
        ReadOnlyInfo.Add($"采样率：{track.SampleRateLabel}");
        ReadOnlyInfo.Add($"声道：{track.ChannelLabel}");
        ReadOnlyInfo.Add($"大小：{track.FileSizeLabel}");
        ReadOnlyInfo.Add($"文件：{track.Path}");

        var metadata = await _metadataService.LoadAsync(track.Path);
        _loadedMetadata = metadata;
        _originalMetadata = metadata;
        Title = metadata.Title;
        Artist = metadata.Artist;
        Album = metadata.Album;
        AlbumArtist = metadata.AlbumArtist;
        Year = metadata.Year > 0 ? metadata.Year.ToString() : string.Empty;
        Genre = metadata.Genre;
        Lyrics = metadata.Lyrics;

        // 优先显示嵌入封面；无嵌入封面时通过 CoverService 解析当前使用的封面
        // （含自定义封面覆盖），确保编辑页始终可见当前封面。
        CoverBytes = metadata.EmbeddedCoverBytes;
        if (CoverBytes is null)
        {
            var resolvedCoverPath = _coverService?.ResolveCoverPath(track) ?? track.CoverPath;
            StartupLog.Write($"track.editor.cover.fallback: track=\"{track.Title}\", embedded=null, resolvedPath=\"{resolvedCoverPath}\"");
            if (!string.IsNullOrWhiteSpace(resolvedCoverPath))
            {
                try
                {
                    CoverBytes = await System.IO.File.ReadAllBytesAsync(resolvedCoverPath);
                }
                catch (IOException)
                {
                }
            }
        }

        StartupLog.Write($"track.editor.cover.final: track=\"{track.Title}\", embeddedBytes={metadata.EmbeddedCoverBytes?.Length ?? 0}, coverBytes={CoverBytes?.Length ?? 0}");

        IsBusy = false;
        _suppressDirtyCheck = false;
        RefreshLockState();
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(InfoLine));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (CurrentTrack is null || !CanEditNow || IsBusy)
        {
            return;
        }

        var year = uint.TryParse(Year.Trim(), out var parsedYear) ? parsedYear : 0;
        var edits = new TrackMetadataModel(
            Title.Trim(),
            Artist.Trim(),
            Album.Trim(),
            AlbumArtist.Trim(),
            year,
            Genre.Trim(),
            Lyrics,
            CoverBytes,
            IsFormatWritable);

        IsBusy = true;
        StatusMessage = null;
        var result = await _metadataService.SaveAsync(
            CurrentTrack.Path,
            edits,
            PendingCoverImagePath,
            RemoveCoverRequested);
        if (result == TrackMetadataSaveResult.Success)
        {
            // 局部刷新该单曲，避免全库重扫导致长时间无响应；曲目不在库时才退回全量扫描。
            var refreshed = await _libraryService.RefreshTrackAsync(CurrentTrack);
            if (!refreshed)
            {
                await _libraryService.RescanAsync();
            }

            StatusIsError = false;
            StatusMessage = "已保存并刷新曲库。";
            SaveCompleted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            StatusIsError = true;
            StatusMessage = result switch
            {
                TrackMetadataSaveResult.FileLocked => "写入失败：文件被占用或无写入权限，请停止播放后重试。",
                TrackMetadataSaveResult.UnsupportedFormat => "写入失败：该格式不支持写入元数据。",
                _ => "写入失败，请检查文件权限后重试。"
            };
        }

        IsBusy = false;
    }

    /// <summary>重置封面为源文件的嵌入封面（清除待选封面并重读嵌入封面字节）。</summary>
    public async Task ResetCoverAsync()
    {
        if (!CanEditNow || IsBusy || CurrentTrack is null)
        {
            return;
        }

        PendingCoverImagePath = null;
        RemoveCoverRequested = false;
        await ReloadCoverFromSourceAsync();
    }

    /// <summary>重读源文件的嵌入封面字节并刷新预览（不清除待选封面状态）。</summary>
    public async Task ReloadCoverFromSourceAsync()
    {
        if (CurrentTrack is null)
        {
            return;
        }

        var metadata = await _metadataService.LoadAsync(CurrentTrack.Path);
        CoverBytes = metadata.EmbeddedCoverBytes;
    }

    /// <summary>应用封面选择结果（文件选择器由页面层调用，保持 VM 可测）。</summary>
    public async Task ApplyCoverSelectionAsync(string coverFilePath)
    {
        if (!CanEditNow || IsBusy || string.IsNullOrWhiteSpace(coverFilePath))
        {
            return;
        }

        PendingCoverImagePath = coverFilePath;
        RemoveCoverRequested = false;
        try
        {
            CoverBytes = await System.IO.File.ReadAllBytesAsync(coverFilePath);
        }
        catch (IOException)
        {
        }

        EvaluateHasChanges();
    }

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(InfoLine));
        EvaluateHasChanges();
    }

    partial void OnArtistChanged(string value)
    {
        OnPropertyChanged(nameof(InfoLine));
        EvaluateHasChanges();
    }

    partial void OnAlbumChanged(string value) => EvaluateHasChanges();

    partial void OnAlbumArtistChanged(string value) => EvaluateHasChanges();

    partial void OnYearChanged(string value) => EvaluateHasChanges();

    partial void OnGenreChanged(string value) => EvaluateHasChanges();

    partial void OnLyricsChanged(string value) => EvaluateHasChanges();

    partial void OnCurrentTrackChanged(TrackModel? value) => RefreshLockState();

    partial void OnIsFormatWritableChanged(bool value) => RefreshLockState();

    private void PlaybackService_StateChanged(object? sender, EventArgs e) => RefreshLockState();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void RefreshLockState()
    {
        OnPropertyChanged(nameof(IsPlayingThisTrack));
        OnPropertyChanged(nameof(CanEditNow));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(LockedReason));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void EvaluateHasChanges()
    {
        if (_suppressDirtyCheck || _originalMetadata is null)
        {
            return;
        }

        uint.TryParse(Year.Trim(), out var year);
        HasChanges = !string.Equals(Title.Trim(), _originalMetadata.Title, StringComparison.Ordinal)
            || !string.Equals(Artist.Trim(), _originalMetadata.Artist, StringComparison.Ordinal)
            || !string.Equals(Album.Trim(), _originalMetadata.Album, StringComparison.Ordinal)
            || !string.Equals(AlbumArtist.Trim(), _originalMetadata.AlbumArtist, StringComparison.Ordinal)
            || year != _originalMetadata.Year
            || !string.Equals(Genre.Trim(), _originalMetadata.Genre, StringComparison.Ordinal)
            || !string.Equals(Lyrics, _originalMetadata.Lyrics, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(PendingCoverImagePath)
            || RemoveCoverRequested;
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _playbackService.StateChanged -= PlaybackService_StateChanged;
    }
}
