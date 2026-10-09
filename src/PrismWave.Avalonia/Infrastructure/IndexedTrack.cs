using PrismWave_WinUI.Models;

namespace PrismWave.Avalonia.Infrastructure;

/// <summary>
/// 曲目行包装：携带 1 起始序号，供专辑/艺术家详情页展示（对齐 WinUI ContainerContentChanging 的序号行为）。
/// </summary>
public sealed record IndexedTrack(int Index, TrackModel Track)
{
    public string IndexLabel => Index.ToString("00");

    public static IReadOnlyList<IndexedTrack> Wrap(IEnumerable<TrackModel> tracks)
    {
        var list = new List<IndexedTrack>();
        var index = 1;
        foreach (var track in tracks)
        {
            list.Add(new IndexedTrack(index++, track));
        }

        return list;
    }
}
