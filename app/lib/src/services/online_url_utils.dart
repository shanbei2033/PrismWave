// Shared URL utilities for the HITS / online modes.
//
// Some upstream sources (Deezer, iTunes) only expose 30-second preview clips
// that look like real audio URLs but truncate after ~30s. Both HITS and the
// online mode must reject these and fall back to the multi-provider resolver.

final RegExp kNonPlayableAudioUrlPattern = RegExp(
  r'(?:cdnt?-preview\.dzcdn\.net|audio-ssl\.itunes\.apple\.com|preview\.music\.apple\.com)',
);

bool isNonPlayableAudioUrl(String? url) {
  if (url == null) return false;
  final trimmed = url.trim();
  if (trimmed.isEmpty) return false;
  return kNonPlayableAudioUrlPattern.hasMatch(trimmed);
}
