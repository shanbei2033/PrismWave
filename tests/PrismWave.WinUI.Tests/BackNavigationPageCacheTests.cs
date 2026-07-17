using PrismWave_WinUI.Infrastructure.Navigation;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class BackNavigationPageCacheTests
{
    [Fact]
    public void Cache_UsesRouteMatchedLifoOrder()
    {
        var cache = new BackNavigationPageCache<object>();
        var home = new object();
        var playlist = new object();

        cache.Push("Home", home);
        cache.Push("TopPlaylist", playlist);

        Assert.False(cache.TryPeek("Home", out _));
        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryPeek("TopPlaylist", out var peeked));
        Assert.Same(playlist, peeked);
        Assert.True(cache.TryPop("TopPlaylist", out var popped));
        Assert.Same(playlist, popped);
        Assert.True(cache.TryPeek("Home", out peeked));
        Assert.Same(home, peeked);
    }

    [Fact]
    public void Clear_RemovesEveryCachedPage()
    {
        var cache = new BackNavigationPageCache<object>();
        cache.Push("Home", new object());
        cache.Push("AlbumDetail", new object());

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryPeek("AlbumDetail", out _));
    }
}
