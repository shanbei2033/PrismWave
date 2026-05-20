import 'dart:async';
import 'dart:io';

import '../services/hits_manifest_service.dart';

enum HitsAvailability { available, unavailable }

class HitsAvailabilityResolver {
  HitsAvailabilityResolver._();

  static final Uri _manifestProbeUri = Uri.parse(kHitsLatestManifestUrl);

  static Future<HitsAvailability> resolve() async {
    final firstAttempt = await _canReach(
      _manifestProbeUri,
      timeout: const Duration(milliseconds: 2200),
    );
    if (firstAttempt) {
      return HitsAvailability.available;
    }

    await Future<void>.delayed(const Duration(milliseconds: 280));
    final secondAttempt = await _canReach(
      _manifestProbeUri,
      timeout: const Duration(milliseconds: 2200),
    );
    return secondAttempt
        ? HitsAvailability.available
        : HitsAvailability.unavailable;
  }

  static Future<bool> _canReach(Uri uri, {required Duration timeout}) async {
    final client = HttpClient()..connectionTimeout = timeout;
    try {
      final request = await client.getUrl(_withCacheBust(uri)).timeout(timeout);
      request.followRedirects = true;
      request.headers.set(HttpHeaders.userAgentHeader, 'PrismWave Hits Probe');
      final response = await request.close().timeout(timeout);
      await response.drain<void>();
      return response.statusCode >= 200 && response.statusCode < 400;
    } on TimeoutException {
      return false;
    } on SocketException {
      return false;
    } on HttpException {
      return false;
    } finally {
      client.close(force: true);
    }
  }

  static Uri _withCacheBust(Uri uri) {
    final queryParameters = <String, String>{
      ...uri.queryParameters,
      '_pw': DateTime.now().toUtc().microsecondsSinceEpoch.toString(),
    };
    return uri.replace(queryParameters: queryParameters);
  }
}
