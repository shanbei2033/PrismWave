namespace PrismWave_WinUI.Infrastructure.Navigation;

public sealed class BackNavigationPageCache<TPage>
    where TPage : class
{
    private const int MaxCacheDepth = 2;

    private readonly LinkedList<Entry> _entries = new();

    public int Count => _entries.Count;

    public void Push(string route, TPage page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(page);
        _entries.AddFirst(new Entry(route, page));

        while (_entries.Count > MaxCacheDepth)
        {
            _entries.RemoveLast();
        }
    }

    public bool TryPeek(string route, out TPage? page)
    {
        if (_entries.First is not null &&
            string.Equals(_entries.First.Value.Route, route, StringComparison.Ordinal))
        {
            page = _entries.First.Value.Page;
            return true;
        }

        page = null;
        return false;
    }

    public bool TryPop(string route, out TPage? page)
    {
        if (!TryPeek(route, out page))
        {
            return false;
        }

        _entries.RemoveFirst();
        return true;
    }

    public void Clear() => _entries.Clear();

    private readonly record struct Entry(string Route, TPage Page);
}
