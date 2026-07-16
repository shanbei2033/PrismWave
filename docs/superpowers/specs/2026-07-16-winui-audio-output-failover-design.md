# WinUI Audio Output Failover Design

**Date:** 2026-07-16

**Status:** Approved

## Problem

The WinUI player currently configures one long-lived `MpvPlaybackEngine` after `mpv_initialize` and treats `MPV_EVENT_FILE_LOADED` as successful playback. With the full libmpv runtime, embedded album art is selected as video and opens a separate mpv window. Subsequent local loads can remain in `Buffering` because file parsing, audio-output startup, and active playback are represented by one event and one engine context.

Direct probing of the bundled libmpv with the real local track `爱与诚.m4a` confirmed that MPV automatic output, WASAPI shared, and WASAPI exclusive can all reach `MPV_EVENT_PLAYBACK_RESTART`. The codec runtime and local file are therefore not the root cause.

## Goals

- Play local MP3, AAC, M4A, MP4, WAV, FLAC, OGG, APE, WMA, DSF, and DFF files without opening a separate mpv window.
- Expose three understandable output modes: MPV, WASAPI shared, and WASAPI exclusive.
- Default to WASAPI shared.
- Fall back from WASAPI shared to MPV.
- Fall back from WASAPI exclusive to WASAPI shared and then MPV.
- Keep the selected mode as the user's preference while separately reporting the active route.
- Preserve the current track, queue, position, volume, and play/pause intent when rebuilding the engine.
- Prevent events from a retired engine or stale load from mutating current playback state.

## Non-goals

- Replacing libmpv with a second decoder.
- Adding ASIO or changing the existing DSD-specific BASS path.
- Changing online provider resolution or candidate matching.
- Persisting the temporary fallback route as the user's preferred mode.
- Maintaining multiple live mpv instances as hot standbys.

## Output Modes and Fallback Policy

The persisted identifiers remain compatible with existing settings:

| User-facing mode | Persisted ID | libmpv configuration | Fallback chain |
| --- | --- | --- | --- |
| MPV | `compatibility` | mpv automatic audio output; exclusive disabled | MPV |
| WASAPI shared | `wasapi_shared` | `ao=wasapi`, `audio-exclusive=no` | WASAPI shared → MPV |
| WASAPI exclusive | `wasapi_exclusive` | `ao=wasapi`, `audio-exclusive=yes`, `wasapi-exclusive-buffer=50000` | WASAPI exclusive → WASAPI shared → MPV |

Missing or invalid settings migrate to `wasapi_shared`. A fallback changes only the active route. A later track starts from the user's preferred route again unless that route is already known to be unavailable for the current engine session.

## Engine Lifecycle

`MpvPlaybackEngine` will receive an immutable route when constructed. All options that determine output and window creation are applied before `mpv_initialize`.

Every route applies:

- `terminal=no`
- `sub-auto=no`
- `cover-art-auto=no`
- `audio-display=no`
- `video=no`
- `force-window=no`
- `cache-secs=12`
- `cache-on-disk=no`
- `audio-client-name=PrismWave`

The compatibility route does not force `ao`; mpv performs its normal automatic selection. WASAPI routes force `ao=wasapi`. A selected `audio-device` is applied before initialization, with `auto` as the safe default.

Changing output settings or falling back creates a replacement engine, subscribes it to events, restores the playback snapshot, and only then disposes the retired engine. Engine generations and load revisions guard every callback.

## Playback State Model

File parsing and audible playback become separate states:

- `MPV_EVENT_FILE_LOADED`: metadata and duration are available; UI remains `Buffering`.
- `MPV_EVENT_PLAYBACK_RESTART`: the audio pipeline has started; UI becomes `Playing` or `Paused` according to the requested intent.
- `MPV_EVENT_END_FILE` with an error: classify and either advance the route fallback or publish failure.
- Local startup watchdog: if a local file does not reach playback restart within five seconds, advance the route fallback.

Online network failures keep using the existing candidate recovery policy. Only errors classified as audio-output failures use the audio-route fallback chain. The local watchdog applies only to local tracks so slow online startup is not misclassified as an output failure.

The first successful playback restart cancels the watchdog and records the active route. A late event from a retired engine or stale revision is logged and ignored.

## Recovery Snapshot

An engine rebuild captures:

- current track and queue
- current queue index
- latest position
- volume
- autoplay or paused intent
- current load revision

For a mode change during playback, the replacement loads the same track and seeks after playback restart. For a startup failure before playback begins, fallback reloads at zero. For a failure after playback began, existing remote recovery behavior remains responsible for online resume; local output fallback resumes from the last sampled position.

## Settings UI

The Playback settings page replaces raw identifiers with display models containing a localized name and description. It shows:

- preferred output mode
- active output route
- a short fallback reason when the active route differs from the preference

The existing setting IDs and JSON shape remain unchanged. Selecting a different mode triggers a controlled engine rebuild instead of mutating the initialized mpv instance.

## Testing Strategy

Pure tests cover route normalization and fallback chains. Engine structure tests lock pre-initialize audio-only options and event handling. A fake engine factory drives `PlaybackService` tests for rebuild, snapshot restoration, stale callback rejection, timeout fallback, and final failure.

The existing bundled-libmpv integration probe is extended to verify the real E-AC-3 fixture reaches playback restart in all three routes without video output. Device-dependent fallback is tested with fakes; manual Demo acceptance exercises the actual Windows output device.

## Acceptance Criteria

1. Clicking a local track never opens an mpv-owned window.
2. The default route is WASAPI shared.
3. Shared failure automatically retries with MPV.
4. Exclusive failure retries shared, then MPV.
5. `file-loaded` alone does not clear the buffering indicator.
6. `playback-restart` clears buffering and starts position updates.
7. Rapid local track changes cannot leave the player waiting on a retired load.
8. A mode change preserves track, queue, position, volume, and paused/playing intent.
9. Settings show the preferred and active route using readable labels.
10. Full tests and the x64 WinUI build pass without warnings or errors.
