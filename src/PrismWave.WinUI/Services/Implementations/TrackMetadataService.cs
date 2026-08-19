using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using TagFile = TagLib.File;
using TagLib;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class TrackMetadataService : ITrackMetadataService
{
    private static readonly HashSet<string> WritableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".mp4", ".flac", ".ogg", ".wma", ".wav", ".ape"
    };

    public Task<TrackMetadataModel> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isWritable = IsWritableFormat(path);
        try
        {
            using var file = TagFile.Create(path);
            var cover = file.Tag.Pictures is { Length: > 0 } pictures
                ? pictures[0].Data.Data
                : null;
            return Task.FromResult(new TrackMetadataModel(
                file.Tag.Title ?? string.Empty,
                file.Tag.FirstPerformer ?? string.Empty,
                file.Tag.Album ?? string.Empty,
                file.Tag.FirstAlbumArtist ?? string.Empty,
                file.Tag.Year,
                file.Tag.FirstGenre ?? string.Empty,
                file.Tag.Lyrics ?? string.Empty,
                cover is { Length: > 0 } ? cover : null,
                isWritable));
        }
        catch (Exception exception) when (exception is IOException
            or SystemException
            or UnsupportedFormatException
            or CorruptFileException)
        {
            // 读失败时仍返回可展示的基础模型（如 DSD/损坏文件），仅标记不可写。
            return Task.FromResult(new TrackMetadataModel(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                string.Empty,
                string.Empty,
                null,
                isWritable));
        }
    }

    public async Task<TrackMetadataSaveResult> SaveAsync(
        string path,
        TrackMetadataModel metadata,
        string? newCoverImagePath = null,
        bool removeCover = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!IsWritableFormat(path))
        {
            return TrackMetadataSaveResult.UnsupportedFormat;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var file = TagFile.Create(path);
            file.Tag.Title = NullIfEmpty(metadata.Title);
            file.Tag.Performers = SplitValues(metadata.Artist);
            file.Tag.Album = NullIfEmpty(metadata.Album);
            file.Tag.AlbumArtists = SplitValues(metadata.AlbumArtist);
            file.Tag.Year = metadata.Year;
            file.Tag.Genres = SplitValues(metadata.Genre);
            file.Tag.Lyrics = NullIfEmpty(metadata.Lyrics);

            if (!string.IsNullOrWhiteSpace(newCoverImagePath) && System.IO.File.Exists(newCoverImagePath))
            {
                file.Tag.Pictures = new IPicture[] { new Picture(newCoverImagePath) };
            }
            else if (removeCover)
            {
                file.Tag.Pictures = Array.Empty<IPicture>();
            }

            await Task.Run(() => file.Save(), cancellationToken).ConfigureAwait(false);
            return TrackMetadataSaveResult.Success;
        }
        catch (IOException)
        {
            return TrackMetadataSaveResult.FileLocked;
        }
        catch (UnauthorizedAccessException)
        {
            // 只读属性/无写入权限同样属于无法写入，按占用类提示。
            return TrackMetadataSaveResult.FileLocked;
        }
        catch (Exception exception) when (exception is UnsupportedFormatException
            or CorruptFileException)
        {
            return TrackMetadataSaveResult.UnsupportedFormat;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or SystemException)
        {
            return TrackMetadataSaveResult.Failed;
        }
    }

    public static bool IsWritableFormat(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && WritableExtensions.Contains(extension);
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string[] SplitValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(item => item.Length > 0)
            .ToArray();
    }
}
