library just_audio_media_kit;

import 'package:media_kit/media_kit.dart';

Future<String?> getProperty(Player player, String key) async {
  if (player.platform is! NativePlayer) return null;
  return (player.platform as NativePlayer).getProperty(key);
}
