# PrismWave WinUI Playback Queue Overlay Design

## Scope

Phase one only: rebuild the playback queue. Do not change the local-library page, Home, FullPlay, NavigationView design, or the bottom player bar design. Phase two starts only after phase-one runtime acceptance.

## Proven root causes

1. `QueuePane.xaml` draws a fixed music glyph and never binds `TrackModel.CoverPath`, so real covers cannot render.
2. The playback service publishes position every 500 ms. Every notification calls `PlaybackViewModel.Refresh()`, which clears and repopulates `Queue`. This destroys ListView containers, cover state, scroll anchoring, and the native drag transaction.
3. Native reorder changes the view collection, but a progress notification restores the old service order before `DragItemsCompleted` can commit. Rows jump and drops appear to revert.
4. `PlaybackService.RemoveFromQueue` uses `RemoveAll`, so it is not an exact one-entry mutation.

Baseline screenshot: `docs/ui-review/library/01-current-playback-queue.png`.

## Design

`ShellPage` owns a top-level `QueueOverlay` in the main-content row. It is a sibling of the `NavigationView`, overlays rather than resizes the page, and remains below FullPlay. The overlay has a light dismiss backdrop and a right-aligned 344 DIP pane capped at 85% of available width.

Opening uses Composition `Translation.X` plus opacity for 240 ms. Closing uses 210 ms and collapses only after completion. An operation revision rejects stale completions from rapid toggles. Windows disabled animations and PrismWave low-effects mode apply final state immediately.

`PlaybackService.Queue` remains authoritative and exposes `QueueRevision`, which changes only for queue membership, order, or entry-content mutations. Progress, volume, and status notifications do not change it.

`PlaybackViewModel` retains `Queue` for FullPlay and adds `QueueItems` for the pane. Both are projections of the service queue. A new revision is reconciled incrementally by stable TrackId-plus-occurrence identity, reusing row objects and using Move/Insert/RemoveAt rather than Clear. Progress-only notifications update scalar playback state without touching either collection.

The native ListView reorders `QueueItems` during drag and commits exactly once on `DragItemsCompleted`. The service validates the order, increments its revision, and notifies without reloading `CurrentTrack`.

Each `PlaybackQueueItemViewModel` contains stable `EntryId`, track, one-based position, resolved cover, and current state. Rows use `StableCoverImage`, a fallback glyph only when the cover is missing, 48×48 covers, fixed height, sequence/title/artist/delete fields, a restrained current surface, and a separate playing indicator.

The header is localized (`播放队列`, `34 首`) with a 44×44 close button. The footer shows `列表循环`, `单曲循环`, `随机播放`, or `顺序播放`. Bottom button, close button, backdrop, and Escape dismiss the pane. Current state is not color-only. Native virtualization, wheel scrolling, reorder edge scrolling, and focus visuals remain intact.

## Verification

Automated tests cover unchanged collection identity on position ticks, incremental reconciliation, real reorder commit, structural revisions, single-item removal, real cover binding, native drag flags, overlay stacking, dismiss paths, responsive width, reduced motion, and animation timings.

Runtime uses at least 30 tracks and saves `02-playback-queue-open.png`, `03-playback-queue-dragging.png`, and `04-playback-queue-final.png`. The full test suite and x64 build must pass before the phase is presented for approval.
