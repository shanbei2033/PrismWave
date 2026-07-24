# PrismWave Player Bar Stage 7 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the existing bottom player layout so transport controls, progress, track information, favorite action, and volume remain aligned and non-overlapping across supported window widths.

**Architecture:** Keep `BottomPlayerBar` as the single shell-level playback control and preserve all existing playback commands. Use a balanced three-column outer Grid, a dedicated three-row center Grid, and control-width visual states driven by the control's actual width. Extend `PlaybackViewModel` only with the current-track favorite state and command needed by the left region.

**Tech Stack:** WinUI 3 XAML, CommunityToolkit.Mvvm, dependency properties, visual states, xUnit XAML/source and ViewModel tests, Windows UI Automation.

## Global Constraints

- Modify only the bottom player and the minimum ViewModel wiring required for its favorite button.
- Preserve playback, seek, volume, queue, full-play, and audio-engine behavior.
- Keep player height in the required 120-140px range; use the existing 132px token.
- Controls and progress must occupy separate Grid rows with a 10px spacer.
- Wide layout uses three balanced `*` columns; compact layouts hide secondary controls instead of overlapping.
- Main play/pause is the only accent-filled transport button.
- Do not use Canvas, ZIndex, negative Margin, clipping, or a fixed total width.
- Save the accepted runtime screenshot as `docs/ui-review/08-player-bar.png` and stop before Stage 8.

---

### Task 1: Add failing player-bar contracts

**Files:**
- Create: `tests/PrismWave.WinUI.Tests/BottomPlayerBarXamlTests.cs`
- Create: `tests/PrismWave.WinUI.Tests/PlaybackViewModelFavoriteTests.cs`

- [x] Assert the three balanced named columns and the center `Auto,10,Auto` row contract.
- [x] Assert exact button sizes, transparent secondary buttons, accent primary button, time labels, seek slider, volume region, and favorite button.
- [x] Assert `Wide`, `Medium`, and `Compact` states and forbidden layout patterns.
- [x] Assert local current tracks can toggle favorite state while remote tracks cannot.
- [x] Run focused tests and confirm they fail for the missing layout and favorite behavior.

### Task 2: Add current-track favorite state

**Files:**
- Modify: `src/PrismWave.WinUI/ViewModels/Player/PlaybackViewModel.cs`
- Modify: `src/PrismWave.WinUI/Infrastructure/AppServices.cs`

- [x] Accept optional `ILibraryService` without breaking existing test constructors.
- [x] Expose `CurrentFavoriteGlyph`, `CanFavoriteCurrentTrack`, and `ToggleCurrentFavoriteCommand`.
- [x] Re-evaluate favorite state when playback or library state changes.
- [x] Pass the application library service into the singleton playback ViewModel.
- [x] Run focused favorite tests and confirm green state.

### Task 3: Rebuild the bottom player layout

**Files:**
- Modify: `src/PrismWave.WinUI/Controls/Playback/BottomPlayerBar.xaml`
- Modify: `src/PrismWave.WinUI/Controls/Playback/BottomPlayerBar.xaml.cs`

- [x] Change the outer layout to named left, center, and right `*` columns.
- [x] Add cover, ellipsized title/subtitle/error, and a centered 36px favorite command to the left region.
- [x] Place 36/40/52/40/36px transport buttons in center row 0 and time/seek/time in row 2.
- [x] Keep ordinary controls transparent and reserve accent fill for play/pause.
- [x] Add the right-aligned volume icon and slider with outer edge safety padding.
- [x] Apply `Wide`, `Medium`, and `Compact` states at 1120px and 760px control-width thresholds.
- [x] Run focused XAML tests and x64 build with zero warnings/errors.

### Task 4: Run and visually accept Stage 7

- [x] Launch the latest x64 build on Home with the same demo data.
- [x] Inspect the full player at the default desktop size and verify visual centering and separate progress row.
- [x] Inspect a compact window and confirm secondary controls hide without overlap.
- [x] Exercise play/pause, seek, queue, and favorite availability without changing playback services.
- [x] Save and inspect `docs/ui-review/08-player-bar.png`.
- [x] Run the full test suite, leave the demo running, and stop before Stage 8.
