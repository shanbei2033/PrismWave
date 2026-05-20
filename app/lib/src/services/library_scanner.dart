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

const _supportedImageExtensions = {
  '.jpg',
  '.jpeg',
  '.png',
  '.webp',
  '.bmp',
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

    final genericCoverPath = _findGenericCoverInEntries(entries);
    final trackSpecificCoverByStem = _buildTrackSpecificCoverMap(entries);

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
      final coverPath = trackSpecificCoverByStem[_normalizeStem(fileName)] ??
          coverByDirectory.putIfAbsent(directory, () => genericCoverPath);
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

Map<String, String> _buildTrackSpecificCoverMap(List<FileSystemEntity> entries) {
  final coverByStem = <String, String>{};

  for (final entity in entries) {
    if (entity is! File) continue;
    if (!_isSupportedImageFilePath(entity.path)) continue;

    final stem = _normalizeStem(p.basenameWithoutExtension(entity.path));
    if (stem.isEmpty) continue;
    coverByStem.putIfAbsent(stem, () => entity.path);
  }

  return coverByStem;
}

bool _isSupportedAudioFilePath(String path) {
  final normalized = path.toLowerCase().trimRight().replaceAll(
    RegExp(r'[.\s]+$'),
    '',
  );
  return _supportedExtensions.any(normalized.endsWith);
}

bool _isSupportedImageFilePath(String path) {
  final normalized = path.toLowerCase().trimRight().replaceAll(
    RegExp(r'[.\s]+$'),
    '',
  );
  return _supportedImageExtensions.any(normalized.endsWith);
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

String? _findGenericCoverInEntries(List<FileSystemEntity> entries) {
  const candidateNames = [
    'cover.jpg',
    'cover.jpeg',
    'cover.png',
    'cover.webp',
    'folder.jpg',
    'folder.jpeg',
    'folder.png',
    'folder.webp',
    'front.jpg',
    'front.png',
    'front.webp',
    'artwork.jpg',
    'artwork.png',
    'artwork.webp',
    'album.jpg',
    'album.png',
    'album.webp',
    'albumart.jpg',
    'albumart.png',
    'albumart.webp',
  ];

  final imageFiles = entries
      .whereType<File>()
      .where((file) => _isSupportedImageFilePath(file.path))
      .map((file) => file.path)
      .toList(growable: false);

  if (imageFiles.isEmpty) {
    return null;
  }

  final lowerByPath = <String, String>{
    for (final path in imageFiles) path: p.basename(path).toLowerCase(),
  };

  for (final candidate in candidateNames) {
    for (final path in imageFiles) {
      if (lowerByPath[path] == candidate) {
        return path;
      }
    }
  }

  if (imageFiles.length == 1) {
    return imageFiles.first;
  }

  final scored = imageFiles
      .map((path) => (path: path, score: _genericCoverScore(lowerByPath[path]!)))
      .toList(growable: false)
    ..sort((left, right) {
      final scoreCompare = right.score.compareTo(left.score);
      if (scoreCompare != 0) return scoreCompare;
      return left.path.compareTo(right.path);
    });

  return scored.first.score > 0 ? scored.first.path : null;
}

int _genericCoverScore(String lowerName) {
  if (lowerName.contains('cover')) return 100;
  if (lowerName.contains('folder')) return 96;
  if (lowerName.contains('front')) return 92;
  if (lowerName.contains('artwork')) return 88;
  if (lowerName.contains('albumart')) return 84;
  if (lowerName.contains('album')) return 76;
  if (lowerName.contains('art')) return 60;
  return 0;
}

String _normalizeStem(String value) {
  return value.trim().toLowerCase();
}
