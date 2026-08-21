namespace PrismWave_WinUI.Models;

public sealed record TrackMetadataModel(
    string Title,
    string Artist,
    string Album,
    string AlbumArtist,
    uint Year,
    string Genre,
    string Lyrics,
    byte[]? EmbeddedCoverBytes,
    bool IsWritable);

public enum TrackMetadataSaveResult
{
    Success,
    FileLocked,
    UnsupportedFormat,
    Failed
}
