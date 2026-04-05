# PrismWave Handoff Memory

## Project Overview
- Project root: `F:\Project\PrismWave`
- Main app root: `F:\Project\PrismWave\app`
- Platform focus: Windows desktop Flutter player
- UI style: glassmorphism / acrylic / borderless custom window controls
- Audio backend: `just_audio` + local fork of `just_audio_media_kit` + `libmpv`
- Current shell/environment used recently: PowerShell on Windows

## User Preferences
- User wants the assistant to actually implement changes, not just discuss them.
- After meaningful code changes, user expects a fresh compiled demo/build.
- User is sensitive to "you changed it but did not rebuild".
- User prefers warm, direct collaboration and quick iteration.
- User often tests with developer mode logs and expects log-based debugging.
- User wants release artifacts in Windows-friendly formats.
- User asked for concise release notes.

## Current Product State
- This is considered the first PrismWave release by the user.
- Release version naming requested by user: `R010`
- User wanted an installable Windows setup, not just a portable zip.

## Key Features Already Implemented
- Borderless acrylic/glass main window
- Custom top bar with integrated minimize / maximize / close
- Main sections:
  - Library
  - Albums
  - Artists
  - Favorites
- Settings panel integrated as a page-style glass panel instead of a native modal
- FullPlay page
  - Separate page feel
  - Lyrics display
  - Local / online / search lyrics tooling
  - Background blur based on current cover
- Local music playback
- Output mode switching:
  - Compatibility
  - WASAPI Shared
  - WASAPI Exclusive
- Audio output device selection
- Developer mode with realtime log file + console window
- Online cover search and replacement
- Multilingual UI:
  - Simplified Chinese
  - Traditional Chinese
  - English

## Important Playback / Audio Decisions
- Native low-level loop mode is intentionally kept at `off`.
- Actual playback behavior is controlled in Dart via `PlaybackMode` and `PlaybackStrategy`.
- This means logs showing `loopMode=off` do not necessarily indicate a bug.
- Playback mode logic should follow:
  - `loop`: auto-next in current playlist with wraparound
  - `single`: manual next/prev behave like loop, but auto-ended playback repeats current track
  - `shuffle`: manual and auto-next both pick a different random track in current playlist
  - one-track playlist: always restart current track regardless of mode

## Important Files
- Main audio routing and playback logic:
  - `app/lib/src/controllers/playback_controller.dart`
- Playback index strategy:
  - `app/lib/src/domain/playback_strategy.dart`
- Native mpv/media_kit bridge:
  - `app/third_party/just_audio_media_kit/lib/mediakit_player.dart`
  - `app/third_party/just_audio_media_kit/lib/just_audio_media_kit.dart`
- App startup / audio backend configuration:
  - `app/lib/main.dart`
- Settings and top-bar quote logic:
  - `app/lib/src/controllers/app_settings_controller.dart`
  - `app/lib/src/services/quote_service.dart`
  - `app/lib/src/ui/window_top_bar.dart`
- Main page UI:
  - `app/lib/src/ui/main_page.dart`
- FullPlay UI:
  - `app/lib/src/ui/fullplay_page.dart`
- Language strings:
  - `app/lib/src/i18n/app_strings.dart`
- Windows installer script:
  - `installer/PrismWaveSetup.iss`

## Important Recent Fixes

### 1. Audio device recovery
- Problem:
  - selected WASAPI device could configure successfully but fail at real playback open
  - app could end up in `idle` with no sound
- Fix:
  - added staged audio route recovery in `playback_controller.dart`
  - fallback chain:
    - selected device -> `auto`
    - WASAPI Exclusive -> WASAPI Shared
    - WASAPI Shared -> Compatibility

### 2. `.lrc` sidecar false playback failure
- Problem:
  - mpv tried to auto-open same-name `.lrc`
  - failure to open external `.lrc` was treated as fatal playback error
- Fix:
  - disabled mpv sidecar subtitle auto-load in `main.dart` using:
    - `sub-auto = no`
  - ignored benign `.lrc` external-file open errors in `mediakit_player.dart`

### 3. English top-bar quote support
- Problem:
  - English UI still showed Chinese "quote / hitokoto" content
- Fix:
  - language-aware quote fetching in `quote_service.dart`
  - English now uses English quote source
  - quote cache is separated by language in `app_settings_controller.dart`
  - fallback quote text is also language-specific

### 4. Playback mode icon refresh
- Problem:
  - clicking playback mode changed controller state, but icon did not visually update
- Fix:
  - mode buttons in `main_page.dart` and `fullplay_page.dart` now use explicit mode-based keys plus `AnimatedSwitcher`
  - this forces icon rebuild and makes visual switching visible across all languages

### 5. Near-end decode error handling for FLAC and similar edge cases
- Problem:
  - some tracks, especially FLAC, threw `Error decoding audio.` near the end
  - app treated this as decode recovery instead of track completion
  - result: after changing playback mode, auto-ended behavior did not follow the selected mode
- Fix:
  - in `playback_controller.dart`, near-end decode errors can now be treated as pseudo-completion
  - this path logs:
    - `decode completion decision -> ...`
    - `decode completion -> treating as auto next. ...`
  - then calls `next(fromAutoEnded: true)` so the selected `PlaybackMode` is respected

## Build / Packaging Notes
- Flutter Windows release build usually succeeds in about 70-90 seconds.
- A previous impression of "stuck build" turned out not to be a real deadlock.
- A verbose build on 2026-03-29 completed successfully in about 79 seconds.

## Current Release Artifacts
- Portable zip:
  - `F:\Project\PrismWave\dist\PrismWave-windows-release.zip`
- Old setup:
  - `F:\Project\PrismWave\dist\PrismWave-Setup-1.0.0.exe`
- Latest requested setup:
  - `F:\Project\PrismWave\dist\PrismWave-Setup-R010.exe`

## Setup Packaging Rules Requested By User
- Setup file should be branded as release `R010`
- User requested installable setup, not "demo"
- Latest installer behavior:
  - default install path prefers `D:\PrismWave`
  - if `D:` does not exist, fallback to `C:\Program Files\PrismWave`
- Installer script path:
  - `installer/PrismWaveSetup.iss`
- Inno Setup compiler path on this machine:
  - `C:\Users\shanbei2033\AppData\Local\Programs\Inno Setup 6\ISCC.exe`

## Git / Sync State
- GitHub repo:
  - `https://github.com/shanbei2033/PrismWave`
- Branch:
  - `main`
- Latest pushed commit at time of writing:
  - `c682c42 chore: add windows setup packaging script`

## Important Current Local State
- `installer/PrismWaveSetup.iss` is modified locally after the last pushed commit.
- Reason:
  - local script was updated again to use version `R010`
  - local script was updated to prefer installing to `D:\PrismWave`, fallback to `C:\Program Files\PrismWave`
- `dist/` is untracked locally and contains generated release artifacts.
- If another AI takes over and wants GitHub to reflect the exact latest installer behavior, it should:
  - review `installer/PrismWaveSetup.iss`
  - commit the current local changes
  - push again

## Current Local Git Status At Time Of Writing
- expected:
  - modified: `installer/PrismWaveSetup.iss`
  - untracked: `dist/`

## Useful Commands
- Analyze:
  - `..\tools\flutter\bin\flutter.bat analyze`
- Build Windows release:
  - `..\tools\flutter\bin\flutter.bat build windows --release`
- Build verbose if diagnosing build time:
  - `..\tools\flutter\bin\flutter.bat build windows --release -v`
- Build setup:
  - `"C:\Users\shanbei2033\AppData\Local\Programs\Inno Setup 6\ISCC.exe" "F:\Project\PrismWave\installer\PrismWaveSetup.iss"`

## Release Notes Requested By User
- User asked for concise English release notes.
- Suggested text:

`Title`
`PrismWave R010`

`Release note`
`First public release of PrismWave.`
`- Glass-style Windows desktop player UI`
`- Library, Albums, Artists, and Favorites`
`- FullPlay page with lyrics display`
`- Local lyrics, online lyrics, and lyric search`
`- WASAPI Exclusive / Shared / Compatibility modes`
`- Audio device selection and developer log mode`
`- Windows setup installer`

## If Another AI Takes Over
- Do not assume a playback bug is caused by native loop mode logs showing `off`.
- Check `PlaybackMode` in Dart logic first.
- For playback-end issues on FLAC:
  - inspect `decode completion` path in `playback_controller.dart`
- For sidecar lyric playback errors:
  - inspect `sub-auto = no` and benign `.lrc` error ignore logic
- For build issues:
  - verify whether build is actually hanging or just still running
  - verbose build proved the pipeline works
- For release work:
  - latest desired installer is `PrismWave-Setup-R010.exe`
  - verify whether installer script local change has been committed before pushing
