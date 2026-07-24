# PrismWave Page Header Stage 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refine only the Home page title row into a clear native WinUI header with a safe, responsive refresh action.

**Architecture:** Preserve the existing two-row `HomePage` root. Name and constrain row 0 as `PageHeader`, keep the content `ScrollViewer` in row 1, and use the native default `Button` visual states for refresh without changing any command or Home content control.

**Tech Stack:** WinUI 3 XAML, C#, xUnit XML contract test, Windows UI Automation.

## Global Constraints

- Modify only `HomePage.xaml` plus its source-contract test.
- Do not modify `TrendingBanner`, song sections, navigation, or playback controls.
- Keep `RefreshHomeCommand` and `IsRefreshing` bindings unchanged.
- Keep header height at 40 logical pixels and refresh at 40 x 40.
- Use `Margin="0,18,24,12"` so the action retains a 24-pixel right safety area.
- Use native Button hover/pressed visuals; do not specify custom `Background`, `BorderBrush`, or `BorderThickness` on refresh.
- Keep the page header in row 0 and the vertical `ScrollViewer` in row 1.
- Build, run at 1600 x 900, save `docs/ui-review/03-page-header.png`, and stop before Stage 3.

---

### Task 1: Add a failing page-header XAML contract test

**Files:**
- Create: `tests/PrismWave.WinUI.Tests/HomePageHeaderXamlTests.cs`

**Interfaces:**
- Consumes: `src/PrismWave.WinUI/Views/Home/HomePage.xaml`.
- Produces: automated row, spacing, button-size, and native-style guarantees.

- [x] **Step 1: Add the failing test**

```csharp
using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class HomePageHeaderXamlTests
{
    [Fact]
    public void HomeHeader_UsesIndependentNativeLayoutContract()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Home", "HomePage.xaml"));
        var header = FindByAutomationId(document, "HomePageHeader");
        var refresh = FindByAutomationId(document, "HomeRefreshButton");
        var contentScroll = document.Descendants().Single(element =>
            element.Name.LocalName == "ScrollViewer" &&
            element.Attribute("Grid.Row")?.Value == "1");

        Assert.Equal("0", header.Attribute("Grid.Row")?.Value);
        Assert.Equal("40", header.Attribute("Height")?.Value);
        Assert.Equal("0,18,24,12", header.Attribute("Margin")?.Value);
        Assert.Equal("40", refresh.Attribute("Width")?.Value);
        Assert.Equal("40", refresh.Attribute("Height")?.Value);
        Assert.Equal("{Binding RefreshHomeCommand}", refresh.Attribute("Command")?.Value);
        Assert.Null(refresh.Attribute("Background"));
        Assert.Null(refresh.Attribute("BorderBrush"));
        Assert.Null(refresh.Attribute("BorderThickness"));
        Assert.Equal("1", contentScroll.Attribute("Grid.Row")?.Value);
    }

    private static XElement FindByAutomationId(XDocument document, string automationId)
    {
        return Assert.Single(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == automationId);
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

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter HomePageHeaderXamlTests --verbosity minimal
```

Expected: FAIL because the current Header and refresh button do not expose the required automation IDs or native-style contract.

---

### Task 2: Refine only the Home page header XAML

**Files:**
- Modify: `src/PrismWave.WinUI/Views/Home/HomePage.xaml`
- Preserve unchanged: `src/PrismWave.WinUI/Controls/Home/TrendingBanner.xaml`
- Preserve unchanged: `src/PrismWave.WinUI/Controls/Playback/BottomPlayerBar.xaml`

**Interfaces:**
- Consumes: existing `RefreshHomeCommand` and `IsRefreshing` bindings.
- Produces: `HomePageHeader` and `HomeRefreshButton` automation boundaries.

- [x] **Step 1: Name and align the Header**

Set row 0 explicitly, add `AutomationProperties.AutomationId="HomePageHeader"`, retain `Height="40"`, and use `Margin="0,18,24,12"`.

- [x] **Step 2: Restore native refresh visuals**

Add `AutomationProperties.AutomationId="HomeRefreshButton"`, retain 40 x 40 and zero padding, increase the Fluent refresh icon to 16px, and remove custom background/border/corner overrides.

- [x] **Step 3: Verify green state and build**

Run the focused test, then:

```powershell
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore --verbosity minimal
```

Expected: focused test passes and build has 0 errors.

---

### Task 3: Runtime screenshot acceptance

**Files:**
- Create: `docs/ui-review/03-page-header.png`

- [x] **Step 1: Launch at 1600 x 900, Home, pane expanded**

- [x] **Step 2: Verify title and refresh vertical centers align**

- [x] **Step 3: Verify refresh has at least 24 logical pixels before the page edge and does not intersect the vertical scroll bar**

- [x] **Step 4: Verify the Hero starts below the Header with natural spacing and was not modified**

- [x] **Step 5: Save and visually inspect `03-page-header.png`**

- [x] **Step 6: Run the complete test suite and stop before Stage 3**
