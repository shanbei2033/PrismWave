import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:prismwave_demo/src/models/online_recommendation.dart';
import 'package:prismwave_demo/src/services/netease_home_service.dart';

void main() {
  group('NeteaseHomeService remote daily home mirrors', () {
    late Directory cacheDirectory;

    setUp(() async {
      cacheDirectory = await Directory.systemTemp.createTemp(
        'prismwave-home-cache-',
      );
    });

    tearDown(() async {
      if (cacheDirectory.existsSync()) {
        await cacheDirectory.delete(recursive: true);
      }
    });

    test('uses a later mirror when the first source is unavailable', () async {
      final today = _beijingDate();
      final server = await _startHomeServer((request) async {
        if (request.uri.path == '/primary') {
          request.response.statusCode = HttpStatus.serviceUnavailable;
          await request.response.close();
          return;
        }
        if (request.uri.path == '/mirror') {
          await _writeJson(request, _homePayload(today));
          return;
        }
        request.response.statusCode = HttpStatus.notFound;
        await request.response.close();
      });
      addTearDown(() => server.close(force: true));

      final service = NeteaseHomeService(
        remoteHomeUris: <Uri>[
          _serverUri(server, '/primary'),
          _serverUri(server, '/mirror'),
        ],
        cacheDirectory: cacheDirectory,
      );
      addTearDown(service.dispose);

      final bundle = await service.loadRemoteDailyBundle();

      expect(bundle.data.editionDate, today);
      expect(bundle.data.topPlaylist?.tracks, hasLength(100));
      expect(bundle.recommendationsPendingGeneration, isFalse);
    });

    test(
      'waits for a fresh source instead of selecting stale data first',
      () async {
        final today = _beijingDate();
        final yesterday = _beijingDate(offsetDays: -1);
        final server = await _startHomeServer((request) async {
          if (request.uri.path == '/stale') {
            await _writeJson(request, _homePayload(yesterday));
            return;
          }
          if (request.uri.path == '/fresh') {
            await Future<void>.delayed(const Duration(milliseconds: 120));
            await _writeJson(request, _homePayload(today));
            return;
          }
          request.response.statusCode = HttpStatus.notFound;
          await request.response.close();
        });
        addTearDown(() => server.close(force: true));

        final service = NeteaseHomeService(
          remoteHomeUris: <Uri>[
            _serverUri(server, '/stale'),
            _serverUri(server, '/fresh'),
          ],
          cacheDirectory: cacheDirectory,
        );
        addTearDown(service.dispose);

        final bundle = await service.loadRemoteDailyBundle(
          allowLatestAvailable: true,
        );

        expect(bundle.data.editionDate, today);
        expect(bundle.recommendationsPendingGeneration, isFalse);
      },
    );

    test(
      'upgrades a fresh schema 7 chart with bundled style sections',
      () async {
        final today = _beijingDate();
        final server = await _startHomeServer((request) async {
          if (request.uri.path == '/schema7') {
            await _writeJson(
              request,
              _homePayload(
                today,
                schemaVersion: 7,
                useStyleSections: false,
                trackPrefix: 'Remote',
              ),
            );
            return;
          }
          request.response.statusCode = HttpStatus.notFound;
          await request.response.close();
        });
        addTearDown(() => server.close(force: true));

        final service = NeteaseHomeService(
          remoteHomeUris: <Uri>[_serverUri(server, '/schema7')],
          cacheDirectory: cacheDirectory,
          bundledHomeOverride: OnlineHomeData.fromJson(_homePayload(today)),
        );
        addTearDown(service.dispose);

        final bundle = await service.loadRemoteDailyBundle();
        final sectionIds = bundle.data.sections.map((section) => section.id);

        expect(bundle.data.schemaVersion, 8);
        expect(bundle.data.editionDate, today);
        expect(
          bundle.data.topPlaylist?.tracks.first.title,
          startsWith('Remote'),
        );
        expect(
          sectionIds,
          containsAll(<String>[
            'style-pop',
            'style-rock',
            'style-electronic',
            'style-hiphop',
            'style-rnb',
          ]),
        );
        expect(bundle.recommendationsPendingGeneration, isFalse);
      },
    );

    test(
      'synthesizes style sections when bundled sections are unavailable',
      () async {
        final today = _beijingDate();
        final server = await _startHomeServer((request) async {
          if (request.uri.path == '/schema7') {
            await _writeJson(
              request,
              _homePayload(
                today,
                schemaVersion: 7,
                useStyleSections: false,
                trackPrefix: 'Legacy',
              ),
            );
            return;
          }
          request.response.statusCode = HttpStatus.notFound;
          await request.response.close();
        });
        addTearDown(() => server.close(force: true));

        final service = NeteaseHomeService(
          remoteHomeUris: <Uri>[_serverUri(server, '/schema7')],
          cacheDirectory: cacheDirectory,
          bundledHomeOverride: OnlineHomeData.fromJson(
            _homePayload(today, useStyleSections: false),
          ),
        );
        addTearDown(service.dispose);

        final bundle = await service.loadRemoteDailyBundle();
        final sections = bundle.data.sections;

        expect(bundle.data.schemaVersion, 8);
        expect(sections, hasLength(5));
        expect(sections.every((section) => section.tracks.length >= 4), isTrue);
        expect(sections.map((section) => section.id), contains('style-pop'));
        expect(sections.first.tracks.first.title, startsWith('Legacy'));
      },
    );
  });
}

Future<HttpServer> _startHomeServer(
  FutureOr<void> Function(HttpRequest request) handler,
) async {
  final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
  unawaited(
    server.listen((request) async {
      try {
        await handler(request);
      } catch (_) {
        try {
          request.response.statusCode = HttpStatus.internalServerError;
          await request.response.close();
        } catch (_) {
          // The handler may already have closed the response.
        }
      }
    }).asFuture<void>(),
  );
  return server;
}

Future<void> _writeJson(HttpRequest request, Map<String, dynamic> json) async {
  request.response.headers.contentType = ContentType.json;
  request.response.write(jsonEncode(json));
  await request.response.close();
}

Uri _serverUri(HttpServer server, String path) {
  return Uri(
    scheme: 'http',
    host: InternetAddress.loopbackIPv4.address,
    port: server.port,
    path: path,
  );
}

Map<String, dynamic> _homePayload(
  String editionDate, {
  int schemaVersion = 8,
  bool useStyleSections = true,
  String trackPrefix = 'Track',
}) {
  return <String, dynamic>{
    'schemaVersion': schemaVersion,
    'generatedAt': DateTime.now().toUtc().toIso8601String(),
    'editionDate': editionDate,
    'tags': const <Map<String, dynamic>>[],
    'sections': useStyleSections
        ? <Map<String, dynamic>>[
            _section('style-pop', 4, trackPrefix: trackPrefix),
            _section('style-rock', 4, trackPrefix: trackPrefix),
            _section('style-electronic', 4, trackPrefix: trackPrefix),
            _section('style-hiphop', 4, trackPrefix: trackPrefix),
            _section('style-rnb', 4, trackPrefix: trackPrefix),
          ]
        : <Map<String, dynamic>>[
            _section('global-hot', 20, trackPrefix: trackPrefix),
            _section('streamable-now', 20, trackPrefix: trackPrefix),
            _section('world-charts', 20, trackPrefix: trackPrefix),
            _section('listener-trends', 20, trackPrefix: trackPrefix),
            _section('audius-trending', 20, trackPrefix: trackPrefix),
          ],
    'topPlaylist': _section('daily-top-100', 100, trackPrefix: trackPrefix),
    'albumRecommendations': const <Map<String, dynamic>>[],
  };
}

Map<String, dynamic> _section(
  String id,
  int count, {
  String trackPrefix = 'Track',
}) {
  return <String, dynamic>{
    'id': id,
    'title': <String, String>{'en-US': id, 'zh-Hans': id},
    'tracks': List<Map<String, dynamic>>.generate(
      count,
      (index) => _track('$id-$index', prefix: trackPrefix),
    ),
  };
}

Map<String, dynamic> _track(String id, {String prefix = 'Track'}) {
  return <String, dynamic>{
    'title': '$prefix $id',
    'artist': 'Artist $id',
    'album': 'Album $id',
    'durationMs': 180000,
    'coverUrl': 'https://p1.music.126.net/$id.jpg',
    'audioUrl': null,
    'audioProvider': 'netease',
    'providerTrackId': id,
    'sourceTags': const <String>['test'],
  };
}

String _beijingDate({int offsetDays = 0}) {
  final now = DateTime.now()
      .toUtc()
      .add(const Duration(hours: 8))
      .add(Duration(days: offsetDays));
  final y = now.year.toString().padLeft(4, '0');
  final m = now.month.toString().padLeft(2, '0');
  final d = now.day.toString().padLeft(2, '0');
  return '$y-$m-$d';
}
