import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'package:path/path.dart' as p;

typedef OnlineMediaDebugLogger = void Function(String message, {bool force});

/// Lightweight cover-image cache for the online mode.
///
/// Independent of HITS to avoid the daily-edition cleanup logic. Audio is not
/// cached: most online audio URLs are already CDN-fronted and short-lived
/// streams that re-resolve cheaply.
class OnlineMediaCacheService {
  OnlineMediaCacheService({
    HttpClient? httpClient,
    OnlineMediaDebugLogger? debugLog,
  }) : _debugLog = debugLog,
       _httpClient =
           httpClient ?? (HttpClient()..connectionTimeout = _kConnectTimeout);

  static const Map<String, String> _baseRequestHeaders = <String, String>{
    'User-Agent':
        'PrismWave/Online (+https://github.com/shanbei2033/PrismWave)',
    'Accept': 'image/*,*/*;q=0.5',
  };
  static const int _maxCoverBytes = 6 * 1024 * 1024;
  static const Duration _kConnectTimeout = Duration(seconds: 4);
  static const Duration _kResponseTimeout = Duration(seconds: 7);

  final HttpClient _httpClient;
  final OnlineMediaDebugLogger? _debugLog;
  final Map<String, Future<Uint8List?>> _pendingCoverLoads =
      <String, Future<Uint8List?>>{};
  final Map<String, Uint8List> _coverMemoryCache = <String, Uint8List>{};

  Future<Uint8List?> loadCoverBytes({
    required String cacheKey,
    required String coverUrl,
  }) {
    final trimmed = coverUrl.trim();
    if (trimmed.isEmpty) {
      _debug('cover.skip -> reason=empty-url key=$cacheKey');
      return Future<Uint8List?>.value(null);
    }

    final memoryHit = _coverMemoryCache[cacheKey];
    if (memoryHit != null) {
      _debug('cover.memory-hit -> key=$cacheKey bytes=${memoryHit.length}');
      return Future<Uint8List?>.value(memoryHit);
    }

    final requestKey = '$cacheKey|$trimmed';
    final pending = _pendingCoverLoads[requestKey];
    if (pending != null) {
      _debug(
        'cover.pending-join -> key=$cacheKey url=${_describeUrl(trimmed)}',
      );
      return pending;
    }

    final future = _loadCoverInternal(cacheKey: cacheKey, coverUrl: trimmed)
        .then((bytes) {
          _pendingCoverLoads.remove(requestKey);
          if (bytes != null) {
            _coverMemoryCache[cacheKey] = bytes;
            _debug('cover.ready -> key=$cacheKey bytes=${bytes.length}');
          } else {
            _debug(
              'cover.failed -> key=$cacheKey url=${_describeUrl(trimmed)}',
              force: true,
            );
          }
          return bytes;
        });

    _pendingCoverLoads[requestKey] = future;
    return future;
  }

  Future<Uint8List?> _loadCoverInternal({
    required String cacheKey,
    required String coverUrl,
  }) async {
    final cacheFile = await _resolveCoverCacheFile(
      cacheKey: cacheKey,
      coverUrl: coverUrl,
    );
    if (cacheFile.existsSync()) {
      try {
        final bytes = await cacheFile.readAsBytes();
        // Anything we previously wrote to disk passed our intake checks at
        // download time, so trust the cached payload even if its first bytes
        // don't match our (incomplete) magic-byte whitelist. Image.memory's
        // own decoder is the final arbiter — its errorBuilder shows the
        // placeholder when the format isn't supported.
        if (bytes.isNotEmpty) {
          _debug(
            'cover.disk-hit -> key=$cacheKey bytes=${bytes.length} '
            'file=${cacheFile.path}',
          );
          return bytes;
        }
        _debug(
          'cover.disk-empty -> key=$cacheKey file=${cacheFile.path}',
          force: true,
        );
      } catch (error) {
        _debug(
          'cover.disk-read-error -> key=$cacheKey file=${cacheFile.path} '
          'error=$error',
          force: true,
        );
        // Fall through to network refetch.
      }
      try {
        await cacheFile.delete();
      } catch (error) {
        _debug(
          'cover.disk-delete-error -> key=$cacheKey file=${cacheFile.path} '
          'error=$error',
          force: true,
        );
        // Ignore cleanup errors.
      }
    }

    final candidates = _coverUrlCandidates(coverUrl);
    _debug(
      'cover.load-start -> key=$cacheKey candidates=${candidates.length} '
      'url=${_describeUrl(coverUrl)}',
    );
    for (final candidateUrl in candidates) {
      final bytes = await _downloadCover(candidateUrl);
      if (bytes == null) continue;
      await cacheFile.parent.create(recursive: true);
      await cacheFile.writeAsBytes(bytes, flush: true);
      _debug(
        'cover.disk-write -> key=$cacheKey bytes=${bytes.length} '
        'file=${cacheFile.path}',
      );
      return bytes;
    }

    return null;
  }

  Future<Uint8List?> _downloadCover(String coverUrl) async {
    try {
      final request = await _httpClient
          .getUrl(Uri.parse(coverUrl))
          .timeout(_kConnectTimeout);
      _requestHeadersForCover(coverUrl).forEach(request.headers.set);
      final response = await request.close().timeout(_kResponseTimeout);
      if (response.statusCode < 200 || response.statusCode >= 300) {
        final statusCode = response.statusCode;
        await response.drain<void>();
        _debug(
          'cover.http-status -> status=$statusCode url=${_describeUrl(coverUrl)}',
          force: true,
        );
        return null;
      }
      final mimeType =
          response.headers.contentType?.mimeType.toLowerCase() ?? '';
      final claimsImage = mimeType.startsWith('image/');

      final builder = BytesBuilder(copy: false);
      await for (final chunk in response) {
        builder.add(chunk);
        if (builder.length > _maxCoverBytes) {
          _debug(
            'cover.too-large -> bytes>$_maxCoverBytes '
            'url=${_describeUrl(coverUrl)}',
            force: true,
          );
          return null;
        }
      }
      final bytes = builder.takeBytes();
      if (bytes.isEmpty) {
        _debug(
          'cover.empty-response -> mime=$mimeType url=${_describeUrl(coverUrl)}',
          force: true,
        );
        return null;
      }

      // Accept the payload if either (a) the server claims it's an image, or
      // (b) the magic bytes match one of our known formats. The whitelist is
      // intentionally a fallback, not a gate — Last.fm/Audius CDNs sometimes
      // serve AVIF/HEIC/SVG that we want to display even though the bytes
      // don't start with PNG/JPEG/GIF/WEBP markers.
      if (!claimsImage && !_looksLikeImage(bytes)) {
        _debug(
          'cover.not-image -> mime=$mimeType bytes=${bytes.length} '
          'url=${_describeUrl(coverUrl)}',
          force: true,
        );
        return null;
      }

      _debug(
        'cover.download-ok -> mime=$mimeType bytes=${bytes.length} '
        'url=${_describeUrl(coverUrl)}',
      );
      return bytes;
    } on TimeoutException catch (error) {
      _debug(
        'cover.timeout -> url=${_describeUrl(coverUrl)} error=$error',
        force: true,
      );
      return null;
    } on SocketException catch (error) {
      _debug(
        'cover.socket-error -> url=${_describeUrl(coverUrl)} error=$error',
        force: true,
      );
      return null;
    } on FormatException catch (error) {
      _debug(
        'cover.bad-url -> url=${_describeUrl(coverUrl)} error=$error',
        force: true,
      );
      return null;
    } catch (error) {
      _debug(
        'cover.download-error -> url=${_describeUrl(coverUrl)} error=$error',
        force: true,
      );
      return null;
    }
  }

  List<String> _coverUrlCandidates(String coverUrl) {
    final uri = Uri.tryParse(coverUrl);
    if (uri == null) return <String>[coverUrl];
    final candidates = <String>[coverUrl];
    final host = uri.host.toLowerCase();

    if ((host.endsWith('music.126.net') || host.endsWith('music.163.com')) &&
        !uri.queryParameters.containsKey('param')) {
      candidates.insert(
        0,
        uri
            .replace(
              queryParameters: <String, String>{
                ...uri.queryParameters,
                'param': '512y512',
              },
            )
            .toString(),
      );
    }

    if (host == 'api.deezer.com') {
      final segments = uri.pathSegments;
      final albumIndex = segments.indexOf('album');
      if (albumIndex >= 0 && albumIndex + 1 < segments.length) {
        final albumId = segments[albumIndex + 1];
        candidates.insert(
          0,
          'https://e-cdns-images.dzcdn.net/images/cover/$albumId/500x500-000000-80-0-0.jpg',
        );
      }
    }

    return candidates.toSet().toList(growable: false);
  }

  Future<File> _resolveCoverCacheFile({
    required String cacheKey,
    required String coverUrl,
  }) async {
    final root = await _resolveCacheDir();
    final urlExt = p.extension(Uri.tryParse(coverUrl)?.path ?? coverUrl);
    final extension = _normalizeExtension(urlExt, fallback: '.img');
    final urlHash = _stableHash(coverUrl);
    final keyHash = _stableHash(cacheKey);
    final fileName = '${keyHash}_$urlHash$extension';
    return File(p.join(root.path, 'covers', fileName));
  }

  Future<Directory> _resolveCacheDir() async {
    final localAppData = Platform.environment['LOCALAPPDATA'];
    if (localAppData != null && localAppData.isNotEmpty) {
      return Directory(p.join(localAppData, 'PrismWave', 'online_cache'));
    }
    final userProfile = Platform.environment['USERPROFILE'];
    if (userProfile != null && userProfile.isNotEmpty) {
      return Directory(
        p.join(userProfile, 'Documents', 'PrismWave', 'online_cache'),
      );
    }
    return Directory(p.join(Directory.current.path, 'online_cache'));
  }

  bool _looksLikeImage(Uint8List bytes) {
    if (bytes.length < 4) return false;
    // PNG
    if (bytes[0] == 0x89 &&
        bytes[1] == 0x50 &&
        bytes[2] == 0x4E &&
        bytes[3] == 0x47) {
      return true;
    }
    // JPEG
    if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
    // GIF
    if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true;
    // WEBP (RIFF....WEBP)
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

  String _normalizeExtension(String raw, {required String fallback}) {
    final cleaned = raw.trim().toLowerCase();
    if (cleaned.isEmpty) return fallback;
    final candidate = cleaned.startsWith('.') ? cleaned : '.$cleaned';
    if (candidate.length > 6 || !RegExp(r'^\.[a-z0-9]+$').hasMatch(candidate)) {
      return fallback;
    }
    return candidate;
  }

  Map<String, String> _requestHeadersForCover(String coverUrl) {
    final uri = Uri.tryParse(coverUrl);
    final host = uri?.host.toLowerCase() ?? '';
    if (host.endsWith('music.126.net') || host.endsWith('music.163.com')) {
      return <String, String>{
        ..._baseRequestHeaders,
        'Referer': 'https://music.163.com/',
      };
    }
    if (host.endsWith('dmhmusic.com') || host.endsWith('taihe.com')) {
      return <String, String>{
        ..._baseRequestHeaders,
        'Referer': 'https://music.taihe.com/',
      };
    }
    return _baseRequestHeaders;
  }

  String _stableHash(String input) {
    var hash = 0;
    for (final code in input.codeUnits) {
      hash = (hash * 31 + code) & 0x7FFFFFFF;
    }
    return hash.toRadixString(16).padLeft(8, '0');
  }

  void recordDecodeFailure({
    required String cacheKey,
    required String? coverUrl,
    required Object error,
  }) {
    _debug(
      'cover.decode-error -> key=$cacheKey url=${_describeUrl(coverUrl ?? '')} '
      'error=$error',
      force: true,
    );
  }

  void _debug(String message, {bool force = false}) {
    _debugLog?.call('online.$message', force: force);
  }

  String _describeUrl(String url) {
    final uri = Uri.tryParse(url);
    if (uri == null || uri.host.isEmpty) return '<invalid>';
    final path = uri.path.length > 48
        ? '${uri.path.substring(0, 48)}...'
        : uri.path;
    return '${uri.scheme}://${uri.host}$path';
  }

  void dispose() {
    _httpClient.close(force: true);
    _coverMemoryCache.clear();
    _pendingCoverLoads.clear();
  }
}
