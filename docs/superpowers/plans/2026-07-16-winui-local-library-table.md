# WinUI Local Music Library Table Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the permanent local-library folder rail with a responsive, virtualized song table and an on-demand folder-management dialog.

**Architecture:** Keep `LibraryViewModel` as the sole track-list owner and `LibraryFolderManagerViewModel` as the shared folder-state owner. Add only a small derived folder-row model and dialog presentation; reuse the existing scanner, folder picker, playback commands and deletion dialogs.

**Tech Stack:** WinUI 3 XAML, CommunityToolkit.Mvvm, xUnit, XML structural tests.

## Global Constraints

- Only local music library presentation and its directly required view-model state may change in this phase.
- Keep existing WinUI NavigationView and bottom playback bar unchanged.
- Use the existing `ListView` virtualization; do not use Canvas or negative margins.
- Preserve `VisibleTracks`, playback, favorite, reorder, context-menu and source-removal behavior.

---

### Task 1: Lock the presentation contract with failing tests

**Files:**
- Modify: `tests/PrismWave.WinUI.Tests/LocalLibraryStructureTests.cs`
- Test: `tests/PrismWave.WinUI.Tests/LocalLibraryStructureTests.cs`

**Interfaces:**
- Consumes: `LibraryPage.xaml` and the planned `LibraryFoldersDialog.xaml`.
- Produces: structural constraints for the header, dialog and responsive columns.

- [ ] **Step 1: Write failing structural tests**

Add tests that require `LibraryPage.xaml` to contain `AutoSuggestBox`, `TableHeader`, a `TracksList` bound to `VisibleTracks`, paired `AlbumHeader`/`AlbumCell` adaptive setters, and `OpenFolderManager_Click`; require no `MetricPill`, no `ItemsSource="{Binding LibraryFolders.Folders}"`, and no `Grid.ColumnDefinitions` entry with `Width="260"`. Add a dialog test requiring `LibraryFoldersDialog.xaml` to bind `FolderEntries`, add/remove/rescan commands, status, count and availability.

- [ ] **Step 2: Run the test class and verify red**

Run: `dotnet test tests\\PrismWave.WinUI.Tests\\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~LocalLibraryStructureTests`

Expected: failures identify the missing table structure and dialog files.

- [ ] **Step 3: Keep the tests unchanged until the corresponding XAML exists**

Do not weaken the missing-structure assertions; later tasks satisfy them through production XAML.

### Task 2: Add stable playback and folder-entry presentation state

**Files:**
- Create: `src/PrismWave.WinUI/ViewModels/Library/LibraryFolderEntryViewModel.cs`
- Modify: `src/PrismWave.WinUI/ViewModels/Library/LibraryFolderManagerViewModel.cs`
- Modify: `src/PrismWave.WinUI/ViewModels/Library/LibraryViewModel.cs`
- Create: `tests/PrismWave.WinUI.Tests/LibraryViewModelTests.cs`
- Modify: `tests/PrismWave.WinUI.Tests/LibraryFolderManagerViewModelTests.cs`

**Interfaces:**
- Produces `LibraryFolderManagerViewModel.FolderEntries` containing `Path`, `TrackCount`, `IsAvailable`, `StatusText` and `Error`.
- Produces `LibraryViewModel.CurrentTrackId` synchronized only with playback identity changes.

- [ ] **Step 1: Write failing view-model tests**

Add a folder-manager test with a root and child-path tracks which asserts the entry count includes only tracks inside the normalized root. Add a library-view-model test that raises playback state for a new track then a position-only state event and asserts `CurrentTrackId` changes once while `VisibleTracks` remains intact.

- [ ] **Step 2: Run the targeted tests and verify red**

Run: `dotnet test tests\\PrismWave.WinUI.Tests\\PrismWave.WinUI.Tests.csproj --no-restore --filter "FullyQualifiedName~LibraryFolderManagerViewModelTests|FullyQualifiedName~LibraryViewModelTests"`

Expected: missing `FolderEntries` and `CurrentTrackId` members.

- [ ] **Step 3: Implement the smallest presentation models**

Create `LibraryFolderEntryViewModel` as a read-only presentation object. Rebuild `FolderEntries` only in the manager's existing `Refresh`, using full-path containment with case-insensitive comparison. In `LibraryViewModel`, listen to `IPlaybackService.StateChanged`, compare the current id with the stored id, and call `SetProperty` only when the identity differs.

- [ ] **Step 4: Run targeted tests and verify green**

Run the command from Step 2.

Expected: all selected tests pass.

### Task 3: Move folder management into a dialog

**Files:**
- Create: `src/PrismWave.WinUI/Views/Dialogs/LibraryFoldersDialog.xaml`
- Create: `src/PrismWave.WinUI/Views/Dialogs/LibraryFoldersDialog.xaml.cs`
- Modify: `src/PrismWave.WinUI/Views/Library/LibraryPage.xaml.cs`
- Test: `tests/PrismWave.WinUI.Tests/LocalLibraryStructureTests.cs`

**Interfaces:**
- Consumes `App.Services.LibraryFolders`.
- Produces `OpenFolderManager_Click`, which assigns `XamlRoot` and awaits a `LibraryFoldersDialog`.

- [ ] **Step 1: Keep Task 1 dialog assertions failing**

The dialog assertion must fail before this task because neither file nor `FolderEntries` binding exists.

- [ ] **Step 2: Create the dialog**

Use a `ContentDialog` with `DataContext = App.Services.LibraryFolders`, title `Music folders`, a compact status/progress header, add/rescan buttons, an `ItemsRepeater` or `ListView` bound to `FolderEntries`, and row fields for path, `TrackCount`, availability/status and remove command. Keep the close button as the primary dialog action.

- [ ] **Step 3: Add page open handling**

Add one `async` click handler that constructs the dialog, assigns the page `XamlRoot`, and awaits `ShowAsync`. Do not add a picker or scanner in code-behind.

- [ ] **Step 4: Run structural tests and verify green for the dialog contract**

Run: `dotnet test tests\\PrismWave.WinUI.Tests\\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~LocalLibraryStructureTests`

Expected: dialog and shared-command assertions pass; page-layout assertions can remain red until Task 4.

### Task 4: Replace the page rail with the responsive song table

**Files:**
- Modify: `src/PrismWave.WinUI/Views/Library/LibraryPage.xaml`
- Modify: `tests/PrismWave.WinUI.Tests/LocalLibraryStructureTests.cs`

**Interfaces:**
- Consumes `VisibleTracks`, `CurrentTrackId`, `LibraryFolders` commands and existing code-behind events.
- Produces paired `TableHeader`, `TracksList`, `AlbumHeader` and `AlbumCell` elements with proportional column widths and no conditional row-template layout state.

- [ ] **Step 1: Implement the page Grid**

Replace the current 260/* split with a single Grid using rows for compact header, `AutoSuggestBox`, conditional error, table header and fill-height list. Replace metric pills with a text count. Retain add, rescan and play-all commands, and add the folder-dialog button.

- [ ] **Step 2: Implement the table header and rows**

Keep one `ListView` bound to `VisibleTracks`; set `SelectionMode="Single"`, `SelectedValuePath="Id"`, and bind `SelectedValue` to `CurrentTrackId`. Give header and row matching Grid columns: 44-DIP cover, flexible title, artist, album, right-aligned duration, favorite and more. Retain the existing context-menu handlers, favorite handler, item click and reorder completion. Add a 64-DIP row style with transparent baseline and Fluent hover/selection behavior.

- [ ] **Step 3: Keep proportional columns stable at narrow widths**

Use matching proportional title, artist and album columns in the header and rows, with single-line trimming for every text cell. Do not use a DataTemplate-level adaptive state that can remeasure realized rows while the user scrolls.

- [ ] **Step 4: Run the structural tests and verify green**

Run: `dotnet test tests\\PrismWave.WinUI.Tests\\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~LocalLibraryStructureTests`

Expected: all local-library structure assertions pass.

### Task 5: Full verification and visual acceptance

**Files:**
- Modify if needed after visual inspection: the Task 2–4 files only.

- [ ] **Step 1: Run full automated verification**

Run:
`dotnet test tests\\PrismWave.WinUI.Tests\\PrismWave.WinUI.Tests.csproj --no-restore`

Then:
`dotnet build src\\PrismWave.WinUI\\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore`

Then:
`git diff --check`

Expected: tests pass; build has zero warnings/errors; diff check has no whitespace errors.

- [ ] **Step 2: Run the application and inspect it near 1600x900**

Open the local library. Check header compactness, search, stable table columns, hover/current row, long-title trimming, list scrolling and bottom-bar clearance. Open/close folders; test add/rescan/remove controls without retaining a permanent rail. Start a song, then search and scroll; verify playback stays active. Open the phase-one queue overlay to verify it still covers rather than compresses the table.

- [ ] **Step 3: Capture and inspect screenshots**

Save or display `06-local-library-final.png` and `07-final-library-with-queue.png` under `docs/ui-review/library/` when the runtime capture tooling permits file output. Inspect the actual rendered result before declaring the phase complete.

- [ ] **Step 4: Commit after verification**

Run:
`git add src/PrismWave.WinUI/Views/Library src/PrismWave.WinUI/Views/Dialogs/LibraryFoldersDialog.* src/PrismWave.WinUI/ViewModels/Library tests/PrismWave.WinUI.Tests docs/superpowers`

`git commit -m "feat(winui): redesign local music library table"`

Then merge the verified branch locally into `WinUI` only after the user has approved the full UI result.
