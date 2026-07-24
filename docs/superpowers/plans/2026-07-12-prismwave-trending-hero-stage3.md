# PrismWave Trending Hero Stage 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform only the current Trending banner into an immersive, responsive WinUI media Hero using the same recommendation data, artwork, and commands.

**Architecture:** `TrendingBanner.xaml` keeps one backdrop image and Acrylic veil, but replaces the fixed card and 2x2 repeater with a responsive two-column Hero. `TrendingBanner.xaml.cs` projects the existing observable `Tracks` collection into four cover URL dependency properties so four native XAML image layers can overlap inside a Grid without Canvas or negative margins.

**Tech Stack:** WinUI 3 XAML, dependency properties, `INotifyCollectionChanged`, xUnit XML/source contract tests, Windows UI Automation.

## Global Constraints

- Modify only `TrendingBanner.xaml`, `TrendingBanner.xaml.cs`, and a dedicated source-contract test.
- Do not modify `HomePage`, global trending songs, NavigationView, or BottomPlayerBar.
- Preserve `Title`, `Subtitle`, `BackdropUrl`, `Tracks`, `PlayCommand`, and `OpenRequested` public behavior.
- Reuse the existing backdrop and first four real recommendation covers.
- Add visible `TRENDING`, `TOP 100`, one concise description, generation subtitle, Play All, and existing detail action.
- Do not use Canvas, negative Margin, negative Translation, a regular cover grid, colorful gradients, or glow effects.
- Remove the obvious 1px Hero outline and use moderate 12px rounding.
- Use a 220px minimum Hero height; switch the collage from the Hero control's own width so shell and pane width are accounted for.
- Build and inspect at 1600 x 900 and 1280 x 720; save `04-trending-hero.png` and `04-trending-hero-1280.png`.
- Stop before Stage 4.

---

### Task 1: Add failing Hero structure and cover-projection tests

**Files:**
- Create: `tests/PrismWave.WinUI.Tests/TrendingHeroXamlTests.cs`

- [x] **Step 1: Add XAML contract test**

Parse `TrendingBanner.xaml` and assert:

```csharp
Assert.Equal("220", hero.Attribute("MinHeight")?.Value);
Assert.Equal("0", hero.Attribute("BorderThickness")?.Value);
Assert.Equal("12", hero.Attribute("CornerRadius")?.Value);
Assert.Contains(document.Descendants(), element => element.Attribute("Text")?.Value == "TOP 100");
Assert.Contains(document.Descendants(), element => element.Attribute("AutomationProperties.AutomationId")?.Value == "HeroCoverCollage");
Assert.Equal(4, document.Descendants().Count(element =>
    element.Attribute("AutomationProperties.AutomationId")?.Value?.StartsWith("HeroCover", StringComparison.Ordinal) == true &&
    element.Name.LocalName == "Image"));
Assert.DoesNotContain("<ItemsRepeater", source, StringComparison.Ordinal);
Assert.DoesNotContain("<UniformGridLayout", source, StringComparison.Ordinal);
Assert.DoesNotContain("<Canvas", source, StringComparison.Ordinal);
Assert.DoesNotContain("Translation=\"0,-", source, StringComparison.Ordinal);
Assert.DoesNotContain("Margin=\"-", source, StringComparison.Ordinal);
```

- [x] **Step 2: Add code-behind projection contract test**

Read `TrendingBanner.xaml.cs` and assert it declares `CoverOneUrl` through `CoverFourUrl`, subscribes to `INotifyCollectionChanged.CollectionChanged`, and updates the slots from the existing `Tracks` sequence.

- [x] **Step 3: Run focused tests and verify red state**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter TrendingHeroXamlTests --verbosity minimal
```

Expected: FAIL because the current Hero is 166px high and still contains a 2x2 `ItemsRepeater`.

---

### Task 2: Implement the four-cover projection

**Files:**
- Modify: `src/PrismWave.WinUI/Controls/Home/TrendingBanner.xaml.cs`

- [x] **Step 1: Register four cover URL dependency properties**

Expose nullable `CoverOneUrl`, `CoverTwoUrl`, `CoverThreeUrl`, and `CoverFourUrl` values for ElementName binding.

- [x] **Step 2: Observe the existing Tracks collection**

Give `TracksProperty` a change callback. Unsubscribe the old `INotifyCollectionChanged`, subscribe the new collection, and refresh all four slots whenever collection contents change.

- [x] **Step 3: Project the same real cover data**

Enumerate `Tracks.OfType<HomeTrackModel>()`, select non-empty `CoverUrl` values, keep distinct URLs, take four, and assign missing slots as null.

---

### Task 3: Replace the card banner with an immersive Hero

**Files:**
- Modify: `src/PrismWave.WinUI/Controls/Home/TrendingBanner.xaml`

- [x] **Step 1: Create the Hero surface**

Use a `Border` named `TrendingHero` with `MinHeight="220"`, `BorderThickness="0"`, `CornerRadius="12"`, subdued backdrop opacity, and the existing neutral Acrylic veil.

Constrain the backdrop image itself to `Height="220"` so its source pixel dimensions cannot inflate the Hero's desired height inside the page StackPanel.

- [x] **Step 2: Build the left information hierarchy**

Use a two-column Grid with 28/24px padding. Show `TRENDING`, `TOP 100`, the bound title, a concise Chinese description, the bound generation subtitle, and existing primary/secondary commands. Remove the negative Translation.

- [x] **Step 3: Build a non-Canvas overlapping cover collage**

Place four differently sized Borders in the same `HeroCoverCollage` Grid using horizontal/vertical alignment, positive margins, restrained opacity, small rotations, and increasing `Canvas.ZIndex`-independent `Grid` draw order. Bind each image to one projected URL.

- [x] **Step 4: Preserve compact behavior**

Handle `SizeChanged` on the Hero control and call `VisualStateManager.GoToState`. Use Wide at 720 control pixels and above; Compact below 720.

Attach the VisualStateGroup to the UserControl's direct `LayoutRoot` Grid so `GoToState(this, ...)` can resolve the states.

Compact must collapse the collage, set `CoverColumn.Width` to 0, and reduce title size. Wide restores the collage, the 320px cover column, and the 44px title.

Do not set a local `Visibility` value on `CoverCollage`; visibility is owned exclusively by the two VisualState setters.

- [x] **Step 5: Run focused tests and build**

Run focused tests, then:

```powershell
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore --verbosity minimal
```

Expected: tests pass and build has 0 errors.

---

### Task 4: Runtime visual acceptance

**Files:**
- Create: `docs/ui-review/04-trending-hero.png`
- Create: `docs/ui-review/04-trending-hero-1280.png`

- [x] **Step 1: Launch 1600 x 900, Home, pane expanded**
- [x] **Step 2: Verify the Hero is the strongest page focus without looking like an outlined card or web ad**
- [x] **Step 3: Verify all text and actions are readable and the four covers remain inside Hero bounds**
- [x] **Step 4: Resize to 1280 x 720 and verify the collage hides without text/button overlap**
- [x] **Step 5: Restore 1600 x 900, save both screenshots, and inspect the saved PNG files**
- [x] **Step 6: Run the complete test suite and stop before Stage 4**
