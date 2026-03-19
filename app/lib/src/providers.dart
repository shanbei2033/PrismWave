import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'controllers/app_settings_controller.dart';
import 'controllers/library_controller.dart';
import 'controllers/playback_controller.dart';
import 'state/app_settings_state.dart';
import 'state/library_state.dart';
import 'state/playback_state.dart';

final appSettingsProvider =
    StateNotifierProvider<AppSettingsController, AppSettingsState>(
      (ref) => AppSettingsController(),
    );

final libraryProvider = StateNotifierProvider<LibraryController, LibraryState>(
  (ref) => LibraryController(),
);

final playbackProvider =
    StateNotifierProvider<PlaybackController, PlaybackState>((ref) {
      final controller = PlaybackController();
      ref.onDispose(controller.dispose);
      return controller;
    });
