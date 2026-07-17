# WinUI Local Music Library Table Design

## Goal

Make the WinUI local library a focused music browsing surface: a compact header, wide search, fixed table header and virtualized song rows. Keep the current NavigationView, dark theme, and bottom player unchanged.

## Context

`LibraryPage` currently allocates a permanent 260-DIP folder card next to a card-like track list. The page already owns the correct data flow: `LibraryViewModel.VisibleTracks` is the filtered, reorderable list, and `LibraryFolderManagerViewModel` is the shared folder command/state owner. The redesign must move presentation only; it must not create another playback queue or another folder-state source.

## Options considered

1. Keep the folder rail and tighten its spacing. This preserves the current hierarchy but continues to waste the width that the track table needs. Rejected.
2. Use a compact Flyout for folders. It is quick for one path, but long paths, scan errors and destructive remove actions need more stable space. Rejected.
3. Use a lightweight `ContentDialog` for folder management. The page remains song-first while the existing shared manager continues to show paths, availability, scan progress, add, remove and rescan actions. Chosen.

## Layout

`LibraryPage` becomes one vertical Grid:

1. compact title/count and right-aligned actions (`Folders`, `Add folder`, `Rescan`, `Play all`);
2. a full-width native `AutoSuggestBox` with search glyph;
3. scan-error `InfoBar`, only when needed;
4. a fixed table header;
5. one virtualized `ListView` filling the remaining height.

Track rows remain 64 DIP high with a 44-DIP cover. Columns are cover, title, artist, album/source, duration, favorite, and more. The three text columns use proportional widths and single-line trimming, so narrow windows retain stable column alignment without a DataTemplate layout switch. Rows use transparent backgrounds and small hover/current-playback emphasis rather than cards.

## Folder management

`LibraryFoldersDialog` binds to the existing singleton `LibraryFolderManagerViewModel`. It shows a derived folder entry for each configured path: path, availability/status and count of scanned tracks below that root. It reuses the existing add, remove and whole-library rescan commands. Settings continues to use the existing status collection unchanged.

## Data and interaction

Search remains live and only reconstructs `VisibleTracks`; it does not touch playback. The table uses the existing `TrackModel` instances, existing item click, context actions, favorite command, reorder persistence and removal dialogs. `LibraryViewModel` exposes the currently playing track id and updates it only when the playback identity changes, so the table can show the active row without reacting to position updates.

## Verification

Structural tests will prove that the permanent folder panel and metric pills are gone, that the dialog owns the shared folder manager, and that the header/list album columns remain paired without conditional-template jumps. View-model tests will prove current-track identity and per-folder counts. After build and full test suite, the app will be opened at roughly 1600x900 to inspect the library, open/close folder management, search, play a row, scroll and open the phase-one queue overlay.

## Constraints

- Do not modify Home, FullPlay, NavigationView or bottom playback bar.
- Do not create a second queue, a second folder manager or a permanent folder rail.
- Do not use Canvas positioning or negative-margin layout fixes.
- Preserve local playback, reorder, favorite, context menu and deletion behavior.
