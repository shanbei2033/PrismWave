# WinUI Playback Queue Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a stable, cover-aware, animated right-side playback queue overlay without interrupting playback.

**Architecture:** Add a structural queue revision to the authoritative playback service, incrementally reconcile stable queue row view models, and host the existing pane as a shell-level Composition overlay.

**Tech Stack:** C# 13, .NET 10, WinUI 3, CommunityToolkit.Mvvm, WinUI Composition, xUnit.

## Global Constraints

- Phase one only; no local-library page edits.
- No Home, FullPlay, NavigationView, or bottom-player-bar redesign.
- Native ListView reorder; no Canvas, negative margins, or whole-collection replacement.
- Open 240 ms, close 210 ms, width 344 DIP capped at 85%.
- Finish with full tests, x64 build, live interaction checks, and inspected 1600×900-class screenshots.

---

### Task 1: Stable queue projection

**Files:**
- Create: `src/PrismWave.WinUI/ViewModels/Player/PlaybackQueueItemViewModel.cs`
- Create: `tests/PrismWave.WinUI.Tests/PlaybackQueueSynchronizationTests.cs`
- Modify: `src/PrismWave.WinUI/Services/Contracts/IPlaybackService.cs`
- Modify: `src/PrismWave.WinUI/ViewModels/Player/PlaybackViewModel.cs`
- Modify: `tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj`

**Interfaces:** Produces `QueueRevision`, `QueueItems`, `BeginQueueReorder()`, and `CompleteQueueReorder()`.

- [ ] Write tests proving position publications preserve row identity and emit no Reset, external revisions reuse stable rows, covers resolve, current state updates, and drag completion commits real order while preserving current track.
- [ ] Run the focused filter and observe failure because the API is absent.
- [ ] Implement the row model and TrackId-plus-occurrence incremental Move/Insert/RemoveAt reconciler. Commit reorder once with `ReorderQueue(QueueItems.Select(item => item.Track).ToArray())`.
- [ ] Run the focused filter and observe all tests pass.

### Task 2: Revision service mutations

**Files:**
- Create: `tests/PrismWave.WinUI.Tests/PlaybackQueueServiceStructureTests.cs`
- Modify: `src/PrismWave.WinUI/Services/Implementations/PlaybackService.cs`

**Interfaces:** Produces monotonic revision changes for queue replacement, reorder, one-item removal, clear, and resolved-track replacement.

- [ ] Write tests requiring one revision helper, forbidding `_queue.RemoveAll`, and proving reorder does not call `LoadCurrentTrack`.
- [ ] Run the focused filter and observe failure.
- [ ] Implement revisioned mutations and exact single-item removal.
- [ ] Run Tasks 1–2 filters and observe all tests pass.

### Task 3: Real-cover QueuePane

**Files:**
- Create: `tests/PrismWave.WinUI.Tests/PlaybackQueueXamlTests.cs`
- Modify: `src/PrismWave.WinUI/Controls/Playback/QueuePane.xaml`
- Modify: `src/PrismWave.WinUI/Controls/Playback/QueuePane.xaml.cs`

**Interfaces:** Consumes `QueueItems`; produces `CloseRequested` and drag lifecycle calls.

- [ ] Write XAML tests for real 48×48 `StableCoverImage`, localized header/count/mode, close button, fixed row fields, current indicator, and native reorder flags.
- [ ] Run the focused filter and observe failure.
- [ ] Implement the Fluent row and bind drag start/completion to the view model.
- [ ] Run queue filters and observe all tests pass.

### Task 4: Animated shell overlay

**Files:**
- Create: `tests/PrismWave.WinUI.Tests/PlaybackQueueOverlayTests.cs`
- Modify: `src/PrismWave.WinUI/ViewModels/Shell/ShellViewModel.cs`
- Modify: `src/PrismWave.WinUI/Views/Shell/ShellPage.xaml`
- Modify: `src/PrismWave.WinUI/Views/Shell/ShellPage.xaml.cs`

**Interfaces:** Produces `CloseQueuePaneCommand`, shell-level overlay, backdrop/Escape dismissal, responsive sizing, and revision-safe 240/210 ms Composition transitions.

- [ ] Write structural tests proving the overlay is a shell sibling rather than page content and code handles motion, width, focus, dismissal, and stale completions.
- [ ] Run the focused filter and observe failure.
- [ ] Implement the overlay and reduced-motion path without changing page columns.
- [ ] Run overlay, shell navigation, and shell view-model filters and observe all tests pass.

### Task 5: Runtime acceptance

**Files:**
- Create: `docs/ui-review/library/02-playback-queue-open.png`
- Create: `docs/ui-review/library/03-playback-queue-dragging.png`
- Create: `docs/ui-review/library/04-playback-queue-final.png`

- [ ] Run `dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore`.
- [ ] Run `dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore`.
- [ ] Use 30+ real tracks to test open/close/rapid toggle/Escape/backdrop, scrolling and edge drag, first→middle, middle→last, last→first, current-track drag, current/non-current delete, reopen persistence, and playback continuity.
- [ ] Capture and inspect the three screenshots for covers, clipping, localization, current state, fixed row heights, overlay stacking, and unchanged page width.
- [ ] Run `git diff --check` and `git status --short`, then present phase-one evidence and wait for explicit approval before phase two.
