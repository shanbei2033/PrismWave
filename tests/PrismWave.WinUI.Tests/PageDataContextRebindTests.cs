using Xunit;

namespace PrismWave_WinUI.Tests;

/// <summary>
/// Regression guard for the "home skeleton after back navigation" bug:
/// nested navigation caches page instances (BackNavigationPageCache) and
/// restores them without re-running the constructor, while Unloaded clears
/// DataContext. Every page using the Unloaded null-out pattern must also
/// re-bind DataContext on Loaded, otherwise the restored page renders with
/// broken bindings (empty skeleton).
/// </summary>
public sealed class PageDataContextRebindTests
{
    public static TheoryData<string, string[], string> PagesWithViewModelProperty =>
        new()
        {
            { "Views/Home/HomePage.xaml.cs", new[] { "src", "PrismWave.WinUI" }, "Home" },
            { "Views/Home/TopPlaylistPage.xaml.cs", new[] { "src", "PrismWave.WinUI" }, "Home" },
            { "Views/Home/AlbumDetailPage.xaml.cs", new[] { "src", "PrismWave.WinUI" }, "Home" },
            { "Views/Library/LibraryPage.xaml.cs", new[] { "src", "PrismWave.WinUI" }, "Library" },
            { "Views/Library/ArtistsPage.xaml.cs", new[] { "src", "PrismWave.WinUI" }, "Artists" },
            { "Views/Library/ArtistDetailPage.xaml.cs", new[] { "src", "PrismWave.WinUI" }, "Artists" },
            { "Views/Library/FavoritesPage.xaml.cs", new[] { "src", "PrismWave.WinUI" }, "Favorites" },
            { "Views/Search/SearchPage.xaml.cs", new[] { "src", "PrismWave.WinUI" }, "Search" }
        };

    [Theory]
    [MemberData(nameof(PagesWithViewModelProperty))]
    public void PagesWithUnloadedClear_RebindDataContextOnLoaded(
        string pagePath,
        string[] repositorySegments,
        string viewModelProperty)
    {
        var codeBehind = File.ReadAllText(FindRepositoryFile(
            repositorySegments.Append(pagePath).ToArray()));

        Assert.Contains(
            $"Unloaded += (_, _) => DataContext = null;",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            $"Loaded += (_, _) => DataContext = App.Services.{viewModelProperty};",
            codeBehind,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository source file.");
    }
}
