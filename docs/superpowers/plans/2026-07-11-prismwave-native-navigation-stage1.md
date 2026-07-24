# PrismWave Native Navigation Stage 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace only the handmade PrismWave sidebar with a native WinUI 3 `NavigationView` while preserving every existing route, page, queue overlay, and playback control.

**Architecture:** `ShellPage` becomes the owner of one native left `NavigationView`; its content remains the existing `Frame` plus `QueuePane`. `ShellViewModel.NavigateCommand` remains the single route authority, while `ShellPage` translates `NavigationViewItem.Tag` values and the native Settings event into the existing route strings.

**Tech Stack:** WinUI 3 / Windows App SDK 2.2, C# 13, XAML, xUnit source-contract test, Windows UI Automation.

## Global Constraints

- Modify only the navigation region during this stage.
- Do not modify `HomePage`, `TrendingBanner`, recommendation cards, or `BottomPlayerBar`.
- Use native `NavigationView`, `NavigationViewItem`, native pane toggle, native selected indicator, and native Settings item.
- Use `PaneDisplayMode="Left"`, `OpenPaneLength="220"`, and `CompactPaneLength="48"`.
- Keep routes `Home`, `Search`, `Library`, `Albums`, `Artists`, `Favorites`, and `Hits` unchanged.
- Localize the native Settings item content as `设置` while preserving its native icon and placement.
- Keep nested `TopPlaylist` and `AlbumDetail` routes visually selected as Home.
- Keep playback queue and bottom player behavior unchanged.
- Build, run at 1600 x 900, inspect expanded and collapsed states, and save `docs/ui-review/02-navigation-view.png`.
- Do not begin Stage 2 before Stage 1 screenshot review is accepted.

---

### Task 1: Add a failing native-navigation contract test

**Files:**
- Create: `tests/PrismWave.WinUI.Tests/ShellNavigationXamlTests.cs`

**Interfaces:**
- Consumes: repository `src/PrismWave.WinUI/Views/Shell/ShellPage.xaml`.
- Produces: source-level guarantees for the required native control and pane dimensions.

- [x] **Step 1: Add the failing test**

```csharp
namespace PrismWave_WinUI.Tests;

public sealed class ShellNavigationXamlTests
{
    [Fact]
    public void Shell_UsesNativeNavigationViewContract()
    {
        var xaml = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml"));

        Assert.Contains("<NavigationView", xaml, StringComparison.Ordinal);
        Assert.Contains("PaneDisplayMode=\"Left\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsPaneToggleButtonVisible=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsSettingsVisible=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenPaneLength=\"220\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CompactPaneLength=\"48\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<navigation:Sidebar", xaml, StringComparison.Ordinal);

        foreach (var route in new[] { "Home", "Search", "Library", "Albums", "Artists", "Favorites", "Hits" })
        {
            Assert.Contains($"Tag=\"{route}\"", xaml, StringComparison.Ordinal);
        }
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
```

- [x] **Step 2: Run the focused test and verify red state**

Run:

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter ShellNavigationXamlTests --verbosity minimal
```

Expected: FAIL because `ShellPage.xaml` still contains `<navigation:Sidebar` and no `<NavigationView>`.

---

### Task 2: Replace the handmade sidebar with native NavigationView

**Files:**
- Modify: `src/PrismWave.WinUI/Views/Shell/ShellPage.xaml`
- Modify: `src/PrismWave.WinUI/Views/Shell/ShellPage.xaml.cs`
- Preserve unchanged: `src/PrismWave.WinUI/Controls/Navigation/Sidebar.xaml`
- Preserve unchanged: `src/PrismWave.WinUI/Controls/Playback/BottomPlayerBar.xaml`

**Interfaces:**
- Consumes: `ShellViewModel.SelectedRoute`, `ShellViewModel.NavigateCommand`, and existing route strings.
- Produces: `AppNavigationView`, native Settings invocation, menu selection synchronization, and unchanged `ContentFrame` navigation.

- [x] **Step 1: Replace the shell's first-row layout**

Use a native `NavigationView` with these required properties:

```xml
<NavigationView
    x:Name="AppNavigationView"
    PaneDisplayMode="Left"
    PaneTitle="PrismWave"
    IsPaneOpen="True"
    IsPaneToggleButtonVisible="True"
    IsSettingsVisible="True"
    IsBackButtonVisible="Collapsed"
    OpenPaneLength="220"
    CompactPaneLength="48"
    SelectionChanged="AppNavigationView_SelectionChanged"
    ItemInvoked="AppNavigationView_ItemInvoked">
```

Add seven `NavigationViewItem` entries with Fluent `FontIcon` glyphs and exact route tags. Place the existing `ContentFrame` and `QueuePane` in `NavigationView.Content`. Keep `BottomPlayerBar` in Shell row 1 without modifying its bindings.

- [x] **Step 2: Route native selection through the existing ViewModel**

Add a selection handler that executes `App.Services.Shell.NavigateCommand` only when the selected item's `Tag` is a non-empty route. Use `ItemInvoked` and `NavigationViewItemInvokedEventArgs.IsSettingsInvoked` to execute `Settings`.

- [x] **Step 3: Synchronize route changes back to native selection**

When `Navigate(route)` runs, map `TopPlaylist` and `AlbumDetail` to `Home`, map `Settings` to `AppNavigationView.SettingsItem`, and select the primary item whose `Tag` matches other routes. Guard selection synchronization so it cannot recursively invoke navigation.

- [x] **Step 4: Run the focused contract test and verify green state**

Run:

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter ShellNavigationXamlTests --verbosity minimal
```

Expected: PASS.

- [x] **Step 5: Build the WinUI x64 app**

Run:

```powershell
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore --verbosity minimal
```

Expected: build succeeds with 0 errors.

---

### Task 3: Runtime visual and interaction acceptance

**Files:**
- Create: `docs/ui-review/02-navigation-view.png`

**Interfaces:**
- Consumes: built x64 packaged app.
- Produces: visible Stage 1 evidence at the fixed test size.

- [x] **Step 1: Launch the built app at 1600 x 900**

Run:

```powershell
dotnet run --project src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-build
```

- [x] **Step 2: Inspect expanded state**

Verify Home is selected, all seven items align vertically, Settings is fixed at the bottom, no item has an independent outlined card, and content begins outside the open 220 px pane.

- [x] **Step 3: Inspect collapsed state**

Invoke the native pane toggle and verify the pane is 48 px wide, icons remain centered, Settings remains centered, Home is not covered, and page content is not obscured.

- [x] **Step 4: Exercise navigation**

Select Home, Search, Library, Albums, Artists, Favorites, HITS, and Settings. Verify each route opens the same existing page and the native selected indicator follows the route.

- [x] **Step 5: Save and review screenshot**

Save the full expanded window to `docs/ui-review/02-navigation-view.png`. Compare it with `docs/ui-review/01-baseline.png`; if the native pane overlaps content, has double borders, or mis-centers Settings in compact mode, fix only Stage 1 files and repeat build/run/screenshot.

- [x] **Step 6: Run the complete unit test suite**

Run:

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --verbosity minimal
```

Expected: all tests pass.
