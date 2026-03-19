# PrismWave

PrismWave is a Windows local music player built with Flutter.

This repository contains the first usable demo version. The current build focuses on getting the desktop experience, library structure, playback flow, output modes, and lyrics view working end to end.

## Current features

- Local library scan
- Library / Albums / Artists / Favorites views
- Search and favorite management
- Bottom playback bar
- Full Play page with synced embedded lyrics
- Playback modes: list, single repeat, shuffle
- Audio output modes: compatibility, WASAPI shared, WASAPI exclusive
- Developer mode with live playback logs and local log files

## Stack

- Flutter
- Riverpod
- just_audio
- media_kit / MPV
- Windows desktop

## Project layout

```text
PrismWave/
  app/               Flutter application
  native/rust_core/  Reserved Rust audio core workspace
  backups/           Local backups
  dev.md             Product and architecture notes
  step.md            Development workflow
```

## Run

If Flutter is already available in your environment:

```powershell
cd app
flutter pub get
flutter run -d windows
```

If you want to use the bundled local Flutter toolchain:

```powershell
cd app
..\tools\flutter\bin\flutter.bat pub get
..\tools\flutter\bin\flutter.bat run -d windows
```

## Build

```powershell
cd app
..\tools\flutter\bin\flutter.bat build windows --release
```

Release output:

```text
app/build/windows/x64/runner/Release/prismwave_demo.exe
```

## Audio notes

The current demo uses `just_audio + media_kit + MPV` as the playback backend.

Available output modes on Windows:

- Compatibility
- WASAPI Shared
- WASAPI Exclusive

Track switching behavior is controlled by the app layer using the current playlist context, current index, and playback mode.

## Developer mode

When developer mode is enabled, PrismWave opens a live log window and writes playback logs to:

```text
C:\Users\<YourUser>\AppData\Local\PrismWave\logs\
```

This is mainly used for playback errors, output mode diagnostics, and auto-switch debugging.

## License

GPL-3.0
