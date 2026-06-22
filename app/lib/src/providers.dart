import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'controllers/app_settings_controller.dart';
import 'controllers/hits_controller.dart';
import 'controllers/library_controller.dart';
import 'controllers/online_controller.dart';
import 'controllers/playback_controller.dart';
import 'services/online_media_cache_service.dart';
import 'state/app_settings_state.dart';
import 'state/hits_state.dart';
import 'state/library_state.dart';
import 'state/online_state.dart';
import 'state/playback_state.dart';

final appSettingsProvider =
    StateNotifierProvider<AppSettingsController, AppSettingsState>(
      (ref) => AppSettingsController(),
    );

final libraryProvider = StateNotifierProvider<LibraryController, LibraryState>(
  (ref) => LibraryController(
    debugLog: ref.read(playbackProvider.notifier).appendDeveloperLog,
  ),
);

final playbackProvider =
    StateNotifierProvider<PlaybackController, PlaybackState>((ref) {
      final controller = PlaybackController();
      ref.onDispose(controller.dispose);
      return controller;
    });

final hitsProvider =
    StateNotifierProvider.autoDispose<HitsController, HitsState>(
      (ref) => HitsController(
        readLibraryState: () => ref.read(libraryProvider),
        readPlaybackState: () => ref.read(playbackProvider),
        playbackController: ref.read(playbackProvider.notifier),
      ),
    );

final onlineProvider = StateNotifierProvider<OnlineController, OnlineState>((
  ref,
) {
  final controller = OnlineController(
    playbackController: ref.read(playbackProvider.notifier),
    readLibraryState: () => ref.read(libraryProvider),
    debugLog: ref.read(playbackProvider.notifier).appendDeveloperLog,
  );
  ref.onDispose(controller.dispose);
  return controller;
});

/// Process-wide singleton for the online cover cache.
///
/// Shared by `OnlineHomePanel`, the now-playing dock, and the full-play page so
/// a cover fetched once on the home page is reused everywhere — and so
/// playback chrome can render NetEase covers with the right headers (plain
/// `Image.network` sometimes 4xx's against NetEase CDNs).
final onlineCoverCacheProvider = Provider<OnlineMediaCacheService>((ref) {
  final service = OnlineMediaCacheService(
    debugLog: ref.read(playbackProvider.notifier).appendDeveloperLog,
  );
  ref.onDispose(service.dispose);
  return service;
});
