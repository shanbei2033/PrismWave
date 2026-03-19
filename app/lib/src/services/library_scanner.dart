import 'dart:io';
import 'dart:isolate';

import 'package:path/path.dart' as p;

import '../models/track.dart';

const _supportedExtensions = {
  '.mp3',
  '.aac',
  '.m4a',
  '.mp4',
  '.wav',
  '.flac',
  '.ogg',
  '.ape',
  '.dsf',
  '.dff',
};

Future<List<Track>> scanTracks(String rootPath) async {
  return scanTracksFromRoots([rootPath]);
}

Future<List<Track>> scanTracksFromRoots(List<String> rootPaths) async {
  if (rootPaths.isEmpty) return const [];

  final allPayload = <Map<String, String>>[];
  for (final rootPath in rootPaths) {
    final payload = await Isolate.run(() => _scanTracksRaw(rootPath));
    allPayload.addAll(payload);
  }

  final deduped = <String, Map<String, String>>{};
  for (final row in allPayload) {
    final path = row['path'];
    if (path == null || path.isEmpty) continue;
    deduped[path] = row;
  }

  final payload = deduped.values.toList(growable: false);
  payload.sort((a, b) => (a['title'] ?? '').compareTo(b['title'] ?? ''));
  return payload.map(Track.fromMap).toList(growable: false);
}

List<Map<String, String>> _scanTracksRaw(String rootPath) {
  final root = Directory(rootPath);
  if (!root.existsSync()) return const [];

  final tracks = <Map<String, String>>[];
  final coverByDirectory = <String, String?>{};
  final pendingDirs = <Directory>[root];

  while (pendingDirs.isNotEmpty) {
    final dir = pendingDirs.removeLast();

    List<FileSystemEntity> entries;
    try {
      entries = dir.listSync(recursive: false, followLinks: false);
    } catch (_) {
      // Ignore unreadable directories and continue scanning others.
      continue;
    }

    for (final entity in entries) {
      if (entity is Directory) {
        pendingDirs.add(entity);
        continue;
      }
      if (entity is! File) continue;
      if (!_isSupportedAudioFilePath(entity.path)) continue;

      final directory = p.dirname(entity.path);
      final fileName = p.basenameWithoutExtension(entity.path).trim();
      final parsed = _parseTitleArtistAlbum(
        fileName: fileName,
        directory: directory,
        rootPath: rootPath,
      );
      final coverPath = coverByDirectory.putIfAbsent(
        directory,
        () => _findCoverInDirectory(directory),
      );
      final coverEntry = coverPath == null
          ? null
          : <String, String>{'coverPath': coverPath};

      tracks.add({
        'path': entity.path,
        'title': parsed['title']!,
        'artist': parsed['artist']!,
        'album': parsed['album']!,
        ...?coverEntry,
      });
    }
  }

  return tracks;
}

bool _isSupportedAudioFilePath(String path) {
  final normalized = path.toLowerCase().trimRight().replaceAll(
    RegExp(r'[.\s]+$'),
    '',
  );
  return _supportedExtensions.any(normalized.endsWith);
}

Map<String, String> _parseTitleArtistAlbum({
  required String fileName,
  required String directory,
  required String rootPath,
}) {
  final parts = fileName.split(' - ');
  final hasArtistPattern = parts.length >= 2;

  String title = hasArtistPattern
      ? parts.sublist(1).join(' - ').trim()
      : fileName;
  String artist = hasArtistPattern ? parts.first.trim() : '';
  String album = p.basename(directory).trim();

  final relativeDir = p.relative(directory, from: rootPath);
  final segments = p
      .split(relativeDir)
      .where((segment) => segment != '.' && segment.trim().isNotEmpty)
      .toList(growable: false);

  if (!hasArtistPattern && segments.length >= 2) {
    artist = segments[segments.length - 2].trim();
    album = segments.last.trim();
  }

  if (title.trim().isEmpty) title = 'Unknown Title';
  if (artist.trim().isEmpty) artist = 'Unknown Artist';
  if (album.trim().isEmpty) album = 'Unknown Album';

  return {'title': title, 'artist': artist, 'album': album};
}

String? _findCoverInDirectory(String directoryPath) {
  const candidateNames = [
    'cover.jpg',
    'cover.jpeg',
    'cover.png',
    'folder.jpg',
    'folder.jpeg',
    'folder.png',
    'front.jpg',
    'front.png',
  ];

  for (final name in candidateNames) {
    final candidate = p.join(directoryPath, name);
    if (File(candidate).existsSync()) {
      return candidate;
    }
  }
  return null;
}
