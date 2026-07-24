# PrismWave Global Trending Stage 4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace only the `global-hot` horizontal song-card wall with a responsive WinUI ranking list while preserving the same tracks and play command.

**Architecture:** `HomeViewModel` projects the existing `global-hot` section into `GlobalTrendingTracks` and exposes all later sections through `FollowingSections`. A dedicated `TrendingSongList` control ranks the first ten tracks, splits 1-5 and 6-10 into two native ListViews, and moves the second list below the first when the control becomes narrow.

**Tech Stack:** WinUI 3 XAML, dependency properties, `INotifyCollectionChanged`, `ListView`, VisualStateManager, xUnit XAML/source-contract tests, Windows UI Automation.

## Constraints

- Modify only the global trending section and its UI projection.
- Preserve source data, order, duration, provider data, queue behavior, and `PlayHomeTrackCommand`.
- Do not modify Trending Hero, page header, navigation, later home sections, or bottom player.
- No `SongCard`, card wall, Canvas, negative Margin, fixed total width, or horizontal scroller in global trending.
- Show ranks 01-10, 48px covers, title, artist, duration, and a more command.
- Use two columns when the control is at least 900px wide and one ordered column below that width.
- Save the accepted runtime screenshot as `docs/ui-review/05-global-trending-list.png`.
- Stop before Stage 5.

---

### Task 1: Add failing ranking-list contracts

- [x] Add `TrendingSongListXamlTests.cs` covering the control structure, row content, responsive states, and forbidden card-wall patterns.
- [x] Assert `HomePage` mounts `TrendingSongList` and the old generic section renderer uses `FollowingSections`.
- [x] Run the focused tests and confirm they fail before implementation.

### Task 2: Add the isolated ViewModel projection

- [x] Add `GlobalTrendingTracks` without removing the canonical `Sections` collection used by queue lookup.
- [x] Add `FollowingSections` for every section except the selected `global-hot` source.
- [x] Populate both projections on every service refresh while preserving source order.

### Task 3: Build the native responsive ranking control

- [x] Add `TrendingSongList.xaml` and code-behind with `ItemsSource` and `PlayCommand` dependency properties.
- [x] Rank at most ten tracks and split 1-5 / 6-10.
- [x] Build two borderless native ListViews with 64px rows, 48px covers, ellipsized title/artist, duration, and more menu.
- [x] Attach Wide/Compact states to the direct `LayoutRoot`; move the right list below the left list below 900 control pixels.
- [x] Replace only the global-hot card section in `HomePage.xaml`.

### Task 4: Verify and visually accept Stage 4

- [x] Run focused tests and the x64 build with zero warnings/errors.
- [x] Launch Home at the default window with expanded navigation and inspect the two-column list.
- [x] Inspect a narrow window and confirm ordered single-column reflow without clipping.
- [x] Verify a row Hover state in the running app.
- [x] Save and inspect `05-global-trending-list.png`.
- [x] Run the complete test suite and stop before Stage 5.
