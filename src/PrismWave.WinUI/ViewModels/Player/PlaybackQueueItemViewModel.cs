using CommunityToolkit.Mvvm.ComponentModel;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.ViewModels.Player;

public sealed class PlaybackQueueItemViewModel : ObservableObject
{
    private TrackModel _track;
    private int _position;
    private string? _coverPath;
    private bool _isCurrent;

    public PlaybackQueueItemViewModel(
        string entryId,
        TrackModel track,
        int position,
        string? coverPath,
        bool isCurrent)
    {
        EntryId = entryId;
        _track = track;
        _position = position;
        _coverPath = coverPath;
        _isCurrent = isCurrent;
    }

    public string EntryId { get; }

    public TrackModel Track
    {
        get => _track;
        private set
        {
            if (SetProperty(ref _track, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Artist));
            }
        }
    }

    public int Position
    {
        get => _position;
        private set
        {
            if (SetProperty(ref _position, value))
            {
                OnPropertyChanged(nameof(PositionLabel));
            }
        }
    }

    public string PositionLabel => Position.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string Title => Track.Title;
    public string Artist => Track.Artist;

    public string? CoverPath
    {
        get => _coverPath;
        private set => SetProperty(ref _coverPath, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        private set => SetProperty(ref _isCurrent, value);
    }

    public void Update(TrackModel track, int position, string? coverPath, bool isCurrent)
    {
        Track = track;
        Position = position;
        CoverPath = coverPath;
        IsCurrent = isCurrent;
    }
}
