import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_acrylic/flutter_acrylic.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:just_audio_media_kit/just_audio_media_kit.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:window_manager/window_manager.dart';

import 'src/app.dart';
import 'src/models/audio_output_mode.dart';
import 'src/services/local_audio_metadata_service.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await _configureAudioBackendFromSettings();
  JustAudioMediaKit.ensureInitialized(
    android: false,
    iOS: false,
    macOS: false,
    linux: false,
    windows: true,
  );
  runApp(const ProviderScope(child: PrismWaveApp()));
  unawaited(_completePlatformBootstrap());
}

Future<void> _completePlatformBootstrap() async {
  try {
    await _configureWindow();
  } catch (_) {
    // Keep startup resilient if window styling fails on a specific machine.
  }

  unawaited(initializeLocalAudioMetadataBackend());
}

Future<void> _setWindowsAcrylicEffect() async {
  try {
    await Window.setEffect(
      effect: WindowEffect.acrylic,
      color: const Color(0x14101828),
      dark: true,
    );
  } catch (_) {
    await Window.setEffect(
      effect: WindowEffect.aero,
      color: Colors.transparent,
      dark: true,
    );
  }
}

Future<void> _configureAudioBackendFromSettings() async {
  final prefs = await SharedPreferences.getInstance();
  final mode = AudioOutputMode.fromId(prefs.getString(kPrefAudioOutputMode));

  JustAudioMediaKit.title = 'PrismWave';
  JustAudioMediaKit.nativeMpvProperties = const <String, String>{
    'sub-auto': 'no',
  };
  switch (mode) {
    case AudioOutputMode.compatibility:
      JustAudioMediaKit.preferWasapi = false;
      JustAudioMediaKit.preferWasapiExclusive = false;
      JustAudioMediaKit.fallbackToWasapiShared = false;
      return;
    case AudioOutputMode.wasapiShared:
      JustAudioMediaKit.preferWasapi = true;
      JustAudioMediaKit.preferWasapiExclusive = false;
      JustAudioMediaKit.fallbackToWasapiShared = true;
      return;
    case AudioOutputMode.wasapiExclusive:
      JustAudioMediaKit.preferWasapi = true;
      JustAudioMediaKit.preferWasapiExclusive = true;
      JustAudioMediaKit.fallbackToWasapiShared = true;
      return;
  }
}

Future<void> _configureWindow() async {
  await Window.initialize();
  await windowManager.ensureInitialized();
  await windowManager.setBackgroundColor(Colors.transparent);
  await windowManager.setMinimumSize(const Size(980, 620));
  await windowManager.setResizable(true);
  await windowManager.setTitle('PrismWave');
  await windowManager.setTitleBarStyle(
    TitleBarStyle.hidden,
    windowButtonVisibility: false,
  );
  if (Platform.isWindows) {
    await windowManager.setAsFrameless();
    await Window.hideWindowControls();
  }
  await windowManager.show();
  await windowManager.focus();
  if (Platform.isWindows) {
    unawaited(_setWindowsAcrylicEffect());
  }
}
