import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:path/path.dart' as p;

import '../models/hits_manifest.dart';

const String kHitsLatestManifestUrl = String.fromEnvironment(
  'PRISMWAVE_HITS_MANIFEST_URL',
  defaultValue:
      'https://raw.githubusercontent.com/shanbei2033/prismwave-hits/main/latest.json',
);

enum HitsManifestErrorKind { noNetwork, cloudTimeout, unavailable, invalidPayload }

class HitsManifestException implements Exception {
  const HitsManifestException(this.kind, [this.message]);

  final HitsManifestErrorKind kind;
  final String? message;

  @override
  String toString() => message ?? 'HitsManifestException($kind)';
}

class HitsManifestBundle {
  const HitsManifestBundle({
    required this.latestManifest,
    required this.schedule,
    required this.usedCache,
  });

  final HitsLatestManifest latestManifest;
  final HitsSchedule schedule;
  final bool usedCache;
}

class HitsManifestService {
  HitsManifestService({HttpClient? httpClient})
    : _httpClient =
          httpClient ??
          (HttpClient()..connectionTimeout = const Duration(seconds: 6));

  final HttpClient _httpClient;

  Uri get latestManifestUri => Uri.parse(kHitsLatestManifestUrl);

  Future<HitsManifestBundle> loadActiveBundle({DateTime? nowUtc}) async {
    final currentUtc = (nowUtc ?? DateTime.now()).toUtc();
    try {
      final latestText = await _fetchText(
        _withCacheBust(latestManifestUri),
        timeout: const Duration(seconds: 5),
      );
      final latestManifest = HitsLatestManifest.fromJson(
        _decodeJsonObject(latestText, label: 'latest manifest'),
      );
      await _writeCacheFile('latest.json', latestText);

      final schedule = await _loadPreferredSchedule(
        latestManifest: latestManifest,
        nowUtc: currentUtc,
      );

      return HitsManifestBundle(
        latestManifest: latestManifest,
        schedule: schedule,
        usedCache: false,
      );
    } on HitsManifestException {
      final cachedBundle = await _loadCachedBundle(nowUtc: currentUtc);
      if (cachedBundle != null) {
        return cachedBundle;
      }
      rethrow;
    } on FormatException catch (error) {
      final cachedBundle = await _loadCachedBundle(nowUtc: currentUtc);
      if (cachedBundle != null) {
        return cachedBundle;
      }
      throw HitsManifestException(
        HitsManifestErrorKind.invalidPayload,
        error.message,
      );
    }
  }

  Future<HitsManifestBundle> loadBestAvailable({DateTime? nowUtc}) async {
    final currentUtc = (nowUtc ?? DateTime.now()).toUtc();

    final cached = await _loadCachedBundle(nowUtc: currentUtc);
    if (cached != null) {
      return cached;
    }

    return loadActiveBundle(nowUtc: nowUtc);
  }

  Future<HitsManifestBundle?> _loadCachedBundle({required DateTime nowUtc}) async {
    final latestFile = await _resolveCacheFile('latest.json');
    if (!latestFile.existsSync()) return null;

    try {
      final latestManifest = HitsLatestManifest.fromJson(
        _decodeJsonObject(
          await latestFile.readAsString(),
          label: 'cached latest manifest',
        ),
      );
      final todayEdition = _isoDate(nowUtc);
      final candidateFileNames = <String>[
        '$todayEdition.json',
        '${latestManifest.activeEditionDate}.json',
      ];

      for (final fileName in candidateFileNames.toSet()) {
        final scheduleFile = await _resolveCacheFile(fileName);
        if (!scheduleFile.existsSync()) {
          continue;
        }
        final schedule = HitsSchedule.fromJson(
          _decodeJsonObject(
            await scheduleFile.readAsString(),
            label: 'cached schedule',
          ),
        );
        return HitsManifestBundle(
          latestManifest: latestManifest,
          schedule: schedule,
          usedCache: true,
        );
      }
    } catch (_) {
      return null;
    }

    return null;
  }

  Future<HitsSchedule> _loadPreferredSchedule({
    required HitsLatestManifest latestManifest,
    required DateTime nowUtc,
  }) async {
    final todayEdition = _isoDate(nowUtc);
    final candidateUris = <Uri>[
      if (todayEdition != latestManifest.activeEditionDate)
        latestManifest.scheduleUrlForEditionDate(todayEdition),
      latestManifest.scheduleUrl,
    ];

    HitsManifestException? lastError;
    for (final uri in candidateUris) {
      try {
        final scheduleText = await _fetchText(
          _withCacheBust(uri),
          timeout: const Duration(seconds: 6),
        );
        final schedule = HitsSchedule.fromJson(
          _decodeJsonObject(scheduleText, label: 'daily schedule'),
        );
        await _writeCacheFile('${schedule.editionDate}.json', scheduleText);
        return schedule;
      } on HitsManifestException catch (error) {
        lastError = error;
      } on FormatException {
        rethrow;
      }
    }

    throw lastError ??
        const HitsManifestException(HitsManifestErrorKind.unavailable);
  }

  Future<String> _fetchText(Uri uri, {required Duration timeout}) async {
    try {
      final request = await _httpClient.getUrl(uri).timeout(timeout);
      request.headers.set(
        HttpHeaders.userAgentHeader,
        'PrismWave/HITS (+https://github.com/shanbei2033/PrismWave)',
      );
      request.headers.set(HttpHeaders.acceptHeader, 'application/json');
      final response = await request.close().timeout(timeout);
      final body = await utf8.decoder.bind(response).join();

      if (response.statusCode >= 200 && response.statusCode < 300) {
        return body;
      }
      if (response.statusCode == 404) {
        throw const HitsManifestException(HitsManifestErrorKind.unavailable);
      }
      if (response.statusCode >= 500) {
        throw HitsManifestException(
          HitsManifestErrorKind.cloudTimeout,
          'Remote service returned ${response.statusCode}',
        );
      }
      throw HitsManifestException(
        HitsManifestErrorKind.unavailable,
        'Remote service returned ${response.statusCode}',
      );
    } on TimeoutException {
      throw const HitsManifestException(HitsManifestErrorKind.cloudTimeout);
    } on SocketException {
      final hasNetwork = await _probeGeneralConnectivity();
      throw HitsManifestException(
        hasNetwork
            ? HitsManifestErrorKind.cloudTimeout
            : HitsManifestErrorKind.noNetwork,
      );
    } on HttpException {
      throw const HitsManifestException(HitsManifestErrorKind.cloudTimeout);
    }
  }

  Future<bool> _probeGeneralConnectivity() async {
    final probeClient = HttpClient()
      ..connectionTimeout = const Duration(milliseconds: 900);
    try {
      final request = await probeClient
          .getUrl(Uri.https('www.qq.com', '/'))
          .timeout(const Duration(milliseconds: 900));
      request.headers.set(HttpHeaders.userAgentHeader, 'PrismWave Hits Probe');
      final response = await request.close().timeout(
        const Duration(milliseconds: 900),
      );
      await response.drain<void>();
      return response.statusCode >= 200 && response.statusCode < 400;
    } catch (_) {
      return false;
    } finally {
      probeClient.close(force: true);
    }
  }

  Map<String, dynamic> _decodeJsonObject(String raw, {required String label}) {
    final sanitized = raw.replaceFirst('\uFEFF', '').trim();
    final decoded = jsonDecode(sanitized);
    if (decoded is! Map<String, dynamic>) {
      throw FormatException('Unexpected $label payload');
    }
    return decoded;
  }

  Future<void> _writeCacheFile(String fileName, String content) async {
    final file = await _resolveCacheFile(fileName);
    await file.parent.create(recursive: true);
    await file.writeAsString(content, flush: true);
  }

  Future<File> _resolveCacheFile(String fileName) async {
    final directory = await _resolveCacheDirectory();
    return File(p.join(directory.path, fileName));
  }

  Uri _withCacheBust(Uri uri) {
    final queryParameters = <String, String>{
      ...uri.queryParameters,
      '_pw': DateTime.now().toUtc().microsecondsSinceEpoch.toString(),
    };
    return uri.replace(queryParameters: queryParameters);
  }

  Future<Directory> _resolveCacheDirectory() async {
    final localAppData = Platform.environment['LOCALAPPDATA'];
    if (localAppData != null && localAppData.isNotEmpty) {
      return Directory(p.join(localAppData, 'PrismWave', 'hits_manifest_cache'));
    }

    final userProfile = Platform.environment['USERPROFILE'];
    if (userProfile != null && userProfile.isNotEmpty) {
      return Directory(
        p.join(userProfile, 'Documents', 'PrismWave', 'hits_manifest_cache'),
      );
    }

    return Directory(p.join(Directory.current.path, 'hits_manifest_cache'));
  }

  String _isoDate(DateTime value) {
    final utc = value.toUtc();
    final year = utc.year.toString().padLeft(4, '0');
    final month = utc.month.toString().padLeft(2, '0');
    final day = utc.day.toString().padLeft(2, '0');
    return '$year-$month-$day';
  }

  void dispose() {
    _httpClient.close(force: true);
  }
}
