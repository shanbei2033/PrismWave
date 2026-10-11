using Avalonia.Media.Imaging;

namespace PrismWave.Avalonia.Controls.Media;

/// <summary>
/// 进程级解码封面位图 LRU 缓存。
/// key = source|maxEdge，按位图字节数预算淘汰（w*h*4 估算）。
/// 仅在 UI 线程访问（StableCoverImage 的加载完成回调均在 UI 线程），无需加锁。
/// 缓存持有位图所有权：淘汰项交由 GC 终结器释放原生内存，StableCoverImage 不再自行 Dispose。
/// </summary>
public static class CoverBitmapCache
{
    private const long MaxBytes = 128L * 1024 * 1024;

    private sealed class Entry(string key, Bitmap bitmap, long bytes)
    {
        public readonly string Key = key;
        public readonly Bitmap Bitmap = bitmap;
        public readonly long Bytes = bytes;
    }

    private static readonly Dictionary<string, LinkedListNode<Entry>> Map = new(StringComparer.Ordinal);
    private static readonly LinkedList<Entry> Lru = new();
    private static long _bytes;

    public static bool TryGet(string source, int maxEdge, out Bitmap? bitmap)
    {
        var key = BuildKey(source, maxEdge);
        if (!Map.TryGetValue(key, out var node))
        {
            bitmap = null;
            return false;
        }

        Lru.Remove(node);
        Lru.AddFirst(node);
        bitmap = node.Value.Bitmap;
        return true;
    }

    public static void Put(string source, int maxEdge, Bitmap bitmap)
    {
        var key = BuildKey(source, maxEdge);
        if (Map.TryGetValue(key, out var existing))
        {
            Lru.Remove(existing);
            _bytes -= existing.Value.Bytes;
            Map.Remove(key);
        }

        var bytes = EstimateBytes(bitmap);
        var node = new LinkedListNode<Entry>(new Entry(key, bitmap, bytes));
        Lru.AddFirst(node);
        Map[key] = node;
        _bytes += bytes;
        EvictOverBudget();
    }

    private static void EvictOverBudget()
    {
        while (_bytes > MaxBytes && Lru.Last is { } oldest)
        {
            Lru.RemoveLast();
            Map.Remove(oldest.Value.Key);
            _bytes -= oldest.Value.Bytes;
            // 不主动 Dispose：若仍有 Image 控件引用该位图，立即释放会导致渲染异常；交由 GC 终结器回收。
        }
    }

    private static long EstimateBytes(Bitmap bitmap) =>
        (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;

    private static string BuildKey(string source, int maxEdge) => $"{maxEdge}|{source}";
}
