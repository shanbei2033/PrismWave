# PrismWave

[中文说明](./README_zh.md)

PrismWave is a Windows local music player built with Flutter.

## Features

- Local music library scan with folder management
- Library / Albums / Artists / Favorites views with drag-to-reorder
- Bottom playback bar + full-screen play page
- Playback queue with drag-to-reorder
- Playback modes: list loop, single repeat, shuffle
- Audio output modes: compatibility, WASAPI shared, WASAPI exclusive
- Lyrics: local, online search & cache, word-by-word, QQ QRC decode
- HITS radio mode: schedule-based online playback with 9 audio providers, cover & lyrics caching, prefetch
- Windows DSD backend via BASS/BASSDSD/BASSASIO FFI
- Developer mode with live playback logs

## Stack

- Flutter (3.29.3)
- Riverpod
- just_audio + just_audio_media_kit (media_kit / MPV)
- BASS / BASSDSD / BASSASIO (DSD playback)
- Windows desktop

## Project layout

```text
PrismWave/
  app/                   Flutter application
  native/windows_dsd/    BASS/BASSDSD/BASSASIO native libraries
  installer/             Inno Setup installer script
  tools/flutter/         Bundled Flutter SDK 
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

The playback backend is `just_audio + media_kit + MPV`.

Available output modes on Windows:

- Compatibility
- WASAPI Shared
- WASAPI Exclusive

## HITS mode

HITS is a radio-style mode that plays scheduled online content:

- Pulls schedule from the `prismwave-hits` repository
- Resolves audio from 9 providers (Bilibili, YouTube, Audius, NetEase, Kuwo, Migu, QQ Music, Kugou)
- Caches covers, lyrics, and audio locally
- Prefetches upcoming tracks in the background

## Developer mode

When developer mode is enabled, PrismWave opens a live log window and writes playback logs to:

```text
C:\Users\<YourUser>\AppData\Local\PrismWave\logs\
```

## Acknowledgements

- [QQMusicDecoder](https://github.com/WXRIW/QQMusicDecoder): helped verify the QQ `QRC` word-by-word lyrics pipeline, especially the decrypt and decompress steps required before parsing lyric content.
- [LDDC](https://github.com/chenmozhijin/LDDC): provided useful reference for timed / word-by-word lyric parsing details, format tolerance, and edge-case handling during the PrismWave lyrics adaptation work.

## License

GPL-3.0
