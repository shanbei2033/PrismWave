namespace PrismWave_WinUI.Infrastructure.Navigation;

public sealed class BackNavigationPageCache<TPage>
    where TPage : class
{
    private readonly Stack<Entry> _entries = new();

    public int Count => _entries.Count;

    public void Push(string route, TPage page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(page);
        _entries.Push(new Entry(route, page));
    }

    public bool TryPeek(string route, out TPage? page)
    {
        if (_entries.TryPeek(out var entry) &&
            string.Equals(entry.Route, route, StringComparison.Ordinal))
        {
            page = entry.Page;
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

        _entries.Pop();
        return true;
    }

    public void Clear() => _entries.Clear();

    private readonly record struct Entry(string Route, TPage Page);
}
