# PrismWave Genre Explorer Stage 6 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace every remaining Home card strip with one lightweight native explorer while preserving all channel and genre sections and their playback queues.

**Architecture:** `HomeViewModel` projects `world-charts` and `audius-trending` into `ChannelSections`, and `style-*` into `GenreSections`, while retaining canonical `Sections`. A dedicated `GenreExplorer` turns each section into a small icon/text/count command and lays entries out with an adaptive `ItemsRepeater` uniform grid.

**Tech Stack:** WinUI 3 XAML, dependency properties, `INotifyCollectionChanged`, `ItemsRepeater`, `UniformGridLayout`, native Buttons, xUnit XAML/source contract tests, Windows UI Automation.

## Constraints

- Modify only the remaining discovery/genre region and its ViewModel projection.
- Preserve every `world-charts`, `audius-trending`, and `style-*` section and its source track order.
- Clicking an entry plays its first track through the existing command, which resolves the complete canonical section queue.
- Do not modify Hero, ranking, editorial, navigation, or bottom player.
- No album covers, card strips, random colors, outlined item cards, Canvas, negative Margin, horizontal scrolling, or fixed total width.
- Entries use one restrained accent icon, title, and track count with native Hover/Pressed states.
- Layout wraps automatically as window width changes.
- Save accepted runtime screenshot as `docs/ui-review/07-genre-explore.png`.
- Stop before Stage 7.

---

### Task 1: Add failing explorer contracts

- [x] Add `GenreExplorerXamlTests.cs` for heading hierarchy, repeaters, lightweight command template, wrapping layout, and forbidden card patterns.
- [x] Assert Home mounts `GenreExplorer` after editorial and removes the generic `FollowingSections` card renderer.
- [x] Assert ViewModel projects channels and `style-*` genres while retaining canonical `Sections`.
- [x] Run focused tests and confirm red state.

### Task 2: Add channel and genre projections

- [x] Add observable `ChannelSections` and `GenreSections` collections.
- [x] Populate channels from `world-charts` and `audius-trending` in source order.
- [x] Populate genres from all `style-*` sections in source order.
- [x] Remove the now-unused `FollowingSections` projection without changing canonical queue lookup.

### Task 3: Build the lightweight native explorer

- [x] Add `GenreExplorer.xaml` and code-behind with channel/genre source and play-command dependency properties.
- [x] Observe both source collections and project section title, count, icon glyph, subtitle, and first track.
- [x] Render channel and genre entries as transparent 46px native Buttons inside wrapping `ItemsRepeater` layouts.
- [x] Mount the explorer after editorial and delete the old generic card-strip XAML.

### Task 4: Verify and visually accept Stage 6

- [x] Run focused tests and x64 build with zero warnings/errors.
- [x] Launch Home, scroll to explorer, and inspect default wrapping and restrained Hover state.
- [x] Inspect a narrow window and confirm entries reflow without clipping or horizontal scrolling.
- [x] Save and inspect `07-genre-explore.png`.
- [x] Run the complete test suite and stop before Stage 7.
