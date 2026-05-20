import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'package:path/path.dart' as p;

import '../models/hits_manifest.dart';
import 'hits_audio_resolver_service.dart';

class HitsMediaCacheService {
  HitsMediaCacheService({HttpClient? httpClient})
    : _httpClient =
          httpClient ??
          (HttpClient()..connectionTimeout = const Duration(seconds: 8));

  static const Map<String, String> _requestHeaders = <String, String>{
    'User-Agent': 'PrismWave/1.0.0 (+https://github.com/shanbei2033/PrismWave)',
    'Accept': '*/*',
  };
  static const int _maxCoverBytes = 6 * 1024 * 1024;
  static const int _maxAudioBytes = 80 * 1024 * 1024;

  final HttpClient _httpClient;
  final Map<String, Future<String?>> _pendingAudioPrefetch =
      <String, Future<String?>>{};
  final Map<String, Future<Uint8List?>> _pendingCoverLoads =
      <String, Future<Uint8List?>>{};
  final Map<String, Uint8List?> _coverMemoryCache = <String, Uint8List?>{};

  Future<String?> cachedAudioPlaybackUrl({
    required HitsScheduleTrack track,
    required HitsResolvedAudioSource source,
  }) async {
    final file = await _resolveAudioCacheFile(track: track, source: source);
    if (!file.existsSync()) {
      return null;
    }
    return Uri.file(file.path).toString();
  }

  Future<String?> prefetchAudio({
    required HitsScheduleTrack track,
    required HitsResolvedAudioSource source,
  }) async {
    final cacheKey = track.stationTrackId;
    final existing = _pendingAudioPrefetch[cacheKey];
    if (existing != null) {
      return existing;
    }

    final future = _downloadAudioToCache(track: track, source: source).then((
      playbackUrl,
    ) {
      _pendingAudioPrefetch.remove(cacheKey);
      return playbackUrl;
    });

    _pendingAudioPrefetch[cacheKey] = future;
    return future;
  }

  Future<Uint8List?> loadCoverBytes(HitsScheduleTrack track) {
    final coverUrl = track.coverUrl?.toString().trim() ?? '';
    if (coverUrl.isEmpty) {
      return Future<Uint8List?>.value(null);
    }
    return loadCoverBytesFromUrl(
      cacheKey: track.stationTrackId,
      coverUrl: coverUrl,
    );
  }

  Future<Uint8List?> loadCoverBytesFromUrl({
    required String cacheKey,
    required String coverUrl,
  }) {
    final trimmedUrl = coverUrl.trim();
    if (trimmedUrl.isEmpty) {
      return Future<Uint8List?>.value(null);
    }

    final cachedBytes = _coverMemoryCache[cacheKey];
    if (cachedBytes != null) {
      return Future<Uint8List?>.value(cachedBytes);
    }

    final requestKey = _coverRequestKey(cacheKey: cacheKey, coverUrl: trimmedUrl);
    final pending = _pendingCoverLoads[requestKey];
    if (pending != null) {
      return pending;
    }

    final future = _loadCoverBytesInternal(
      cacheKey: cacheKey,
      coverUrl: trimmedUrl,
    ).then((bytes) {
      _pendingCoverLoads.remove(requestKey);
      if (bytes != null) {
        _coverMemoryCache[cacheKey] = bytes;
      }
      return bytes;
    });

    _pendingCoverLoads[requestKey] = future;
    return future;
  }

  Future<void> prefetchCover(HitsScheduleTrack track) async {
    await loadCoverBytes(track);
  }

  Future<String?> _downloadAudioToCache({
    required HitsScheduleTrack track,
    required HitsResolvedAudioSource source,
  }) async {
    final targetFile = await _resolveAudioCacheFile(track: track, source: source);
    if (targetFile.existsSync()) {
      return Uri.file(targetFile.path).toString();
    }

    final tempFile = File('${targetFile.path}.download');
    await tempFile.parent.create(recursive: true);
    if (tempFile.existsSync()) {
      await tempFile.delete();
    }

    try {
      final uri = Uri.parse(source.playbackUrl);
      final request = await _httpClient
          .getUrl(uri)
          .timeout(const Duration(seconds: 10));
      _applyHeaders(request);
      final extraHeaders = source.playbackHeaders;
      if (extraHeaders != null) {
        extraHeaders.forEach(request.headers.set);
      }
      final response = await request.close().timeout(
        const Duration(seconds: 20),
      );

      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return null;
      }
      if (response.contentLength > _maxAudioBytes) {
        await response.drain<void>();
        return null;
      }

      final sink = tempFile.openWrite();
      var totalBytes = 0;
      await for (final chunk in response) {
        totalBytes += chunk.length;
        if (totalBytes > _maxAudioBytes) {
          await sink.close();
          if (tempFile.existsSync()) {
            await tempFile.delete();
          }
          return null;
        }
        sink.add(chunk);
      }
      await sink.close();

      if (!tempFile.existsSync() || tempFile.lengthSync() <= 0) {
        return null;
      }

      if (targetFile.existsSync()) {
        await targetFile.delete();
      }
      await tempFile.rename(targetFile.path);
      return Uri.file(targetFile.path).toString();
    } catch (_) {
      if (tempFile.existsSync()) {
        await tempFile.delete();
      }
      return null;
    }
  }

  Future<Uint8List?> _loadCoverBytesInternal({
    required String cacheKey,
    required String coverUrl,
  }) async {
    final cacheFile = await _resolveCoverCacheFile(
      cacheKey: cacheKey,
      coverUrl: coverUrl,
    );
    if (cacheFile.existsSync()) {
      try {
        final cachedBytes = await cacheFile.readAsBytes();
        if (cachedBytes.isNotEmpty &&
            _looksLikeImagePayload(
              cachedBytes,
              mimeType: _mimeTypeFromExtension(cacheFile.path),
            )) {
          return cachedBytes;
        }
      } catch (_) {
        // Fall through to remote fetch.
      }

      try {
        await cacheFile.delete();
      } catch (_) {
        // Ignore cache cleanup failures and continue with remote fetch.
      }
    }

    try {
      final request = await _httpClient
          .getUrl(Uri.parse(coverUrl))
          .timeout(const Duration(seconds: 8));
      _applyHeaders(request);
      final response = await request.close().timeout(
        const Duration(seconds: 12),
      );
      if (response.statusCode < 200 || response.statusCode >= 300) {
        await response.drain<void>();
        return null;
      }

      final mimeType = response.headers.contentType?.mimeType.toLowerCase() ?? '';

      final builder = BytesBuilder(copy: false);
      await for (final chunk in response) {
        builder.add(chunk);
        if (builder.length > _maxCoverBytes) {
          return null;
        }
      }

      final bytes = builder.takeBytes();
      if (bytes.isEmpty) {
        return null;
      }
      if (!_looksLikeImagePayload(bytes, mimeType: mimeType)) {
        return null;
      }

      await cacheFile.parent.create(recursive: true);
      await cacheFile.writeAsBytes(bytes, flush: true);
      return bytes;
    } catch (_) {
      return null;
    }
  }

  Future<File> _resolveAudioCacheFile({
    required HitsScheduleTrack track,
    required HitsResolvedAudioSource source,
  }) async {
    final root = await _resolveCacheRootDirectory();
    final extension = _normalizeExtension(
      source.suggestedFileExtension,
      fallback: '.audio',
    );
    final fileName = '${_sanitizeFileName(track.stationTrackId)}$extension';
    return File(p.join(root.path, 'audio', _editionKey(track), fileName));
  }

  Future<File> _resolveCoverCacheFile({
    required String cacheKey,
    required String coverUrl,
  }) async {
    final root = await _resolveCacheRootDirectory();
    final extension = _normalizeExtension(
      p.extension(Uri.tryParse(coverUrl)?.path ?? coverUrl),
      fallback: '.img',
    );
    final editionKey = _editionKeyFromCacheKey(cacheKey);
    final urlKey = _stableHash(coverUrl);
    final fileName = '${_sanitizeFileName(cacheKey)}_$urlKey$extension';
    return File(p.join(root.path, 'covers', editionKey, fileName));
  }

  Future<Directory> _resolveCacheRootDirectory() async {
    final current = Directory.current.absolute;
    final currentSegments = p.split(current.path);
    final prismWaveIndex = currentSegments.lastIndexWhere(
      (segment) => segment.toLowerCase() == 'prismwave',
    );
    if (prismWaveIndex >= 0) {
      final repoRoot = p.joinAll(currentSegments.take(prismWaveIndex + 1));
      return Directory(p.join(p.dirname(repoRoot), 'PrismWave_HITS_Cache'));
    }

    final executableDir = File(Platform.resolvedExecutable).parent.path;
    return Directory(p.join(p.dirname(executableDir), 'PrismWave_HITS_Cache'));
  }

  String _editionKey(HitsScheduleTrack track) {
    final match = RegExp(r'^\d{4}-\d{2}-\d{2}').stringMatch(track.stationTrackId);
    return match ?? 'unknown';
  }

  String _editionKeyFromCacheKey(String cacheKey) {
    final match = RegExp(r'^\d{4}-\d{2}-\d{2}').stringMatch(cacheKey);
    return match ?? 'unknown';
  }

  String _sanitizeFileName(String value) {
    final sanitized = value.replaceAll(RegExp(r'[^a-zA-Z0-9._-]+'), '_');
    return sanitized.isEmpty ? 'hits_asset' : sanitized;
  }

  String _normalizeExtension(String? value, {required String fallback}) {
    final candidate = (value ?? '').trim().toLowerCase();
    if (candidate.isEmpty) {
      return fallback;
    }
    if (!candidate.startsWith('.') || candidate.length > 8) {
      return fallback;
    }
    return candidate;
  }

  String _coverRequestKey({
    required String cacheKey,
    required String coverUrl,
  }) {
    return '$cacheKey::${_stableHash(coverUrl)}';
  }

  String _stableHash(String input) {
    var hash = 0xcbf29ce484222325;
    for (final codeUnit in input.codeUnits) {
      hash ^= codeUnit;
      hash = (hash * 0x100000001b3) & 0x7fffffffffffffff;
    }
    return hash.toRadixString(16);
  }

  void _applyHeaders(HttpClientRequest request) {
    _requestHeaders.forEach(request.headers.set);
  }

  String _mimeTypeFromExtension(String path) {
    final extension = p.extension(path).toLowerCase();
    switch (extension) {
      case '.jpg':
      case '.jpeg':
        return 'image/jpeg';
      case '.png':
        return 'image/png';
      case '.gif':
        return 'image/gif';
      case '.webp':
        return 'image/webp';
      default:
        return '';
    }
  }

  bool _looksLikeImagePayload(Uint8List bytes, {required String mimeType}) {
    if (mimeType.startsWith('image/')) {
      return true;
    }

    if (bytes.length >= 3 &&
        bytes[0] == 0xFF &&
        bytes[1] == 0xD8 &&
        bytes[2] == 0xFF) {
      return true;
    }
    if (bytes.length >= 8 &&
        bytes[0] == 0x89 &&
        bytes[1] == 0x50 &&
        bytes[2] == 0x4E &&
        bytes[3] == 0x47 &&
        bytes[4] == 0x0D &&
        bytes[5] == 0x0A &&
        bytes[6] == 0x1A &&
        bytes[7] == 0x0A) {
      return true;
    }
    if (bytes.length >= 6 &&
        bytes[0] == 0x47 &&
        bytes[1] == 0x49 &&
        bytes[2] == 0x46 &&
        bytes[3] == 0x38) {
      return true;
    }
    if (bytes.length >= 12 &&
        bytes[0] == 0x52 &&
        bytes[1] == 0x49 &&
        bytes[2] == 0x46 &&
        bytes[3] == 0x46 &&
        bytes[8] == 0x57 &&
        bytes[9] == 0x45 &&
        bytes[10] == 0x42 &&
        bytes[11] == 0x50) {
      return true;
    }

    return false;
  }

  void dispose() {
    _httpClient.close(force: true);
  }
}
