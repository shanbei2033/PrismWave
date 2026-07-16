using System.Security.Cryptography;
using System.Text;

namespace PrismWave_WinUI.Models;

public static class TrackCoverIdentity
{
    public static string CreateKey(string title, string artist)
    {
        var normalizedTitle = Normalize(title);
        var normalizedArtist = Normalize(artist);
        if (normalizedTitle.Length == 0 || normalizedArtist.Length == 0)
        {
            return string.Empty;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{normalizedTitle}\n{normalizedArtist}"));
        return $"identity:v2:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    public static bool Matches(string leftTitle, string leftArtist, string rightTitle, string rightArtist)
    {
        var left = CreateKey(leftTitle, leftArtist);
        return left.Length > 0 && string.Equals(
            left,
            CreateKey(rightTitle, rightArtist),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return string.Join(
                ' ',
                value.Normalize(NormalizationForm.FormKC)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }
}

public sealed record CoverSearchResultModel(
    string Id,
    string Title,
    string Artist,
    string Album,
    string ThumbnailUrl,
    string FullImageUrl,
    string Source,
    int Score = 0)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Album : Title;
    public string DisplaySubtitle => string.Join(
        " · ",
        new[] { Artist, Album }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string SourceLabel => Source.ToLowerInvariant() switch
    {
        "apple" => "Apple Music",
        "deezer" => "Deezer",
        "musicbrainz" => "MusicBrainz",
        _ => Source
    };
    public string ScoreLabel => Score > 0 ? $"Match {Score}" : SourceLabel;
}

public sealed class CoverChangedEventArgs : EventArgs
{
    public CoverChangedEventArgs(
        string trackId,
        string trackPath,
        string coverPath,
        string title = "",
        string artist = "")
    {
        TrackId = trackId;
        TrackPath = trackPath;
        CoverPath = coverPath;
        Title = title;
        Artist = artist;
    }

    public string TrackId { get; }
    public string TrackPath { get; }
    public string CoverPath { get; }
    public string Title { get; }
    public string Artist { get; }
}
