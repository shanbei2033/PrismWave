import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:dart_tags/dart_tags.dart';
import 'package:metadata_god/metadata_god.dart';
import 'package:path/path.dart' as p;

class LocalAudioMetadataSnapshot {
  const LocalAudioMetadataSnapshot({
    this.title,
    this.artist,
    this.album,
    this.duration,
    this.trackNumber,
    this.trackTotal,
    this.pictureData,
  });

  final String? title;
  final String? artist;
  final String? album;
  final Duration? duration;
  final int? trackNumber;
  final int? trackTotal;
  final Uint8List? pictureData;

  bool get hasAnyValue =>
      (title ?? '').trim().isNotEmpty ||
      (artist ?? '').trim().isNotEmpty ||
      (album ?? '').trim().isNotEmpty ||
      duration != null ||
      trackNumber != null ||
      trackTotal != null ||
      (pictureData?.isNotEmpty ?? false);

  LocalAudioMetadataSnapshot merge(LocalAudioMetadataSnapshot? other) {
    if (other == null) {
      return this;
    }
    return LocalAudioMetadataSnapshot(
      title: _firstNonEmpty(title, other.title),
      artist: _firstNonEmpty(artist, other.artist),
      album: _firstNonEmpty(album, other.album),
      duration: duration ?? other.duration,
      trackNumber: trackNumber ?? other.trackNumber,
      trackTotal: trackTotal ?? other.trackTotal,
      pictureData: (pictureData?.isNotEmpty ?? false)
          ? pictureData
          : other.pictureData,
    );
  }

  static String? _firstNonEmpty(String? primary, String? fallback) {
    if ((primary ?? '').trim().isNotEmpty) {
      return primary!.trim();
    }
    if ((fallback ?? '').trim().isNotEmpty) {
      return fallback!.trim();
    }
    return null;
  }
}

Future<LocalAudioMetadataSnapshot?> readBestEffortAudioMetadata(
  String path,
) async {
  LocalAudioMetadataSnapshot? snapshot;

  try {
    final metadata = await MetadataGod.readMetadata(file: path);
    snapshot = LocalAudioMetadataSnapshot(
      title: metadata.title?.trim(),
      artist: metadata.artist?.trim(),
      album: metadata.album?.trim(),
      duration: metadata.duration,
      trackNumber: metadata.trackNumber,
      trackTotal: metadata.trackTotal,
      pictureData: metadata.picture?.data,
    );
  } catch (_) {
    // Fall back to pure-Dart readers below.
  }

  final extension = p.extension(path).toLowerCase();
  final fallback = switch (extension) {
    '.mp3' => await _readMp3Id3Metadata(path),
    '.wav' => await _readWavMetadata(path),
    _ => null,
  };
  final merged = (snapshot ?? const LocalAudioMetadataSnapshot()).merge(fallback);
  return merged.hasAnyValue ? merged : null;
}

Future<LocalAudioMetadataSnapshot?> _readMp3Id3Metadata(String path) async {
  try {
    return _readId3MetadataFromBytes(
      await File(path).readAsBytes(),
      allowId3v1: true,
    );
  } catch (_) {
    return null;
  }
}

Future<LocalAudioMetadataSnapshot?> _readWavMetadata(String path) async {
  try {
    final bytes = Uint8List.fromList(await File(path).readAsBytes());
    if (bytes.length < 12) {
      return null;
    }
    if (!_bytesEq(bytes, 0, 'RIFF') || !_bytesEq(bytes, 8, 'WAVE')) {
      return null;
    }

    String? title;
    String? artist;
    String? album;
    int? trackNumber;
    int? trackTotal;
    Uint8List? pictureData;

    var offset = 12;
    while (offset + 8 <= bytes.length) {
      final chunkSize = _readLeInt32(bytes, offset + 4);
      if (chunkSize < 0) {
        break;
      }
      final dataStart = offset + 8;
      final dataEnd = (dataStart + chunkSize).clamp(dataStart, bytes.length);
      final chunkId = _asciiAt(bytes, offset, 4);

      if (chunkId == 'LIST' &&
          dataEnd - dataStart >= 4 &&
          _asciiAt(bytes, dataStart, 4) == 'INFO') {
        final infoSnapshot = _readWavInfoList(bytes, dataStart + 4, dataEnd);
        title ??= infoSnapshot.title;
        artist ??= infoSnapshot.artist;
        album ??= infoSnapshot.album;
        trackNumber ??= infoSnapshot.trackNumber;
        trackTotal ??= infoSnapshot.trackTotal;
      } else if ((chunkId == 'ID3 ' || chunkId == 'id3 ') &&
          dataEnd > dataStart) {
        final id3Snapshot = await _readId3MetadataFromBytes(
          Uint8List.fromList(bytes.sublist(dataStart, dataEnd)),
          allowId3v1: false,
        );
        if (id3Snapshot != null) {
          title ??= id3Snapshot.title;
          artist ??= id3Snapshot.artist;
          album ??= id3Snapshot.album;
          trackNumber ??= id3Snapshot.trackNumber;
          trackTotal ??= id3Snapshot.trackTotal;
          pictureData ??= id3Snapshot.pictureData;
        }
      }

      offset = dataEnd + (chunkSize.isOdd ? 1 : 0);
    }

    if (pictureData == null) {
      final headerOffset = _findEmbeddedId3Header(bytes);
      if (headerOffset != null) {
        final id3Snapshot = await _readId3MetadataFromBytes(
          Uint8List.fromList(bytes.sublist(headerOffset)),
          allowId3v1: false,
        );
        if (id3Snapshot != null) {
          title ??= id3Snapshot.title;
          artist ??= id3Snapshot.artist;
          album ??= id3Snapshot.album;
          trackNumber ??= id3Snapshot.trackNumber;
          trackTotal ??= id3Snapshot.trackTotal;
          pictureData ??= id3Snapshot.pictureData;
        }
      }
    }

    final snapshot = LocalAudioMetadataSnapshot(
      title: title,
      artist: artist,
      album: album,
      trackNumber: trackNumber,
      trackTotal: trackTotal,
      pictureData: pictureData,
    );
    return snapshot.hasAnyValue ? snapshot : null;
  } catch (_) {
    return null;
  }
}

Future<LocalAudioMetadataSnapshot?> _readId3MetadataFromBytes(
  List<int> bytes, {
  required bool allowId3v1,
}) async {
  try {
    final processor = TagProcessor();
    final tags = await processor.getTagsFromByteArray(
      Future<List<int>>.value(bytes),
      allowId3v1 ? [TagType.id3v2, TagType.id3v1] : [TagType.id3v2],
    );

    String? title;
    String? artist;
    String? album;
    int? trackNumber;
    int? trackTotal;
    Uint8List? pictureData;

    for (final tag in tags) {
      title ??= _asTrimmedString(tag.tags['title']);
      artist ??= _asTrimmedString(tag.tags['artist']);
      album ??= _asTrimmedString(tag.tags['album']);

      final track = tag.tags['track'];
      final parsedTrack = _parseTrackInfo(track);
      trackNumber ??= parsedTrack.$1;
      trackTotal ??= parsedTrack.$2;

      pictureData ??= _extractPictureBytes(tag.tags['picture']);
    }

    final snapshot = LocalAudioMetadataSnapshot(
      title: title,
      artist: artist,
      album: album,
      trackNumber: trackNumber,
      trackTotal: trackTotal,
      pictureData: pictureData,
    );
    return snapshot.hasAnyValue ? snapshot : null;
  } catch (_) {
    return null;
  }
}

LocalAudioMetadataSnapshot _readWavInfoList(
  Uint8List bytes,
  int start,
  int end,
) {
  String? title;
  String? artist;
  String? album;
  int? trackNumber;
  int? trackTotal;

  var offset = start;
  while (offset + 8 <= end) {
    final chunkSize = _readLeInt32(bytes, offset + 4);
    if (chunkSize < 0) {
      break;
    }
    final dataStart = offset + 8;
    final dataEnd = (dataStart + chunkSize).clamp(dataStart, end);
    final chunkId = _asciiAt(bytes, offset, 4);
    final rawValue = latin1
        .decode(bytes.sublist(dataStart, dataEnd), allowInvalid: true)
        .replaceAll('\u0000', '')
        .trim();

    if (rawValue.isNotEmpty) {
      switch (chunkId) {
        case 'INAM':
          title ??= rawValue;
          break;
        case 'IART':
          artist ??= rawValue;
          break;
        case 'IPRD':
        case 'IALB':
          album ??= rawValue;
          break;
        case 'ITRK':
        case 'TRCK':
          final parsedTrack = _parseTrackInfo(rawValue);
          trackNumber ??= parsedTrack.$1;
          trackTotal ??= parsedTrack.$2;
          break;
      }
    }

    offset = dataEnd + (chunkSize.isOdd ? 1 : 0);
  }

  return LocalAudioMetadataSnapshot(
    title: title,
    artist: artist,
    album: album,
    trackNumber: trackNumber,
    trackTotal: trackTotal,
  );
}

String? _asTrimmedString(Object? value) {
  final text = value?.toString().trim() ?? '';
  return text.isEmpty ? null : text;
}

(int?, int?) _parseTrackInfo(Object? value) {
  if (value is int) {
    return (value > 0 ? value : null, null);
  }

  final raw = value?.toString().trim() ?? '';
  if (raw.isEmpty) {
    return (null, null);
  }

  final parts = raw.split('/');
  final track = int.tryParse(parts.first.trim());
  final total = parts.length > 1 ? int.tryParse(parts[1].trim()) : null;
  return (
    track != null && track > 0 ? track : null,
    total != null && total > 0 ? total : null,
  );
}

Uint8List? _extractPictureBytes(Object? value) {
  if (value is AttachedPicture && value.imageData.isNotEmpty) {
    return Uint8List.fromList(value.imageData);
  }

  if (value is Map) {
    for (final item in value.values) {
      if (item is AttachedPicture && item.imageData.isNotEmpty) {
        return Uint8List.fromList(item.imageData);
      }
    }
  }

  return null;
}

bool _bytesEq(Uint8List bytes, int offset, String ascii) {
  if (offset < 0 || offset + ascii.length > bytes.length) {
    return false;
  }
  return _asciiAt(bytes, offset, ascii.length) == ascii;
}

String _asciiAt(Uint8List bytes, int offset, int length) {
  if (offset < 0 || offset + length > bytes.length) {
    return '';
  }
  return ascii.decode(bytes.sublist(offset, offset + length), allowInvalid: true);
}

int _readLeInt32(Uint8List bytes, int offset) {
  if (offset < 0 || offset + 4 > bytes.length) {
    return -1;
  }
  return bytes[offset] |
      (bytes[offset + 1] << 8) |
      (bytes[offset + 2] << 16) |
      (bytes[offset + 3] << 24);
}

int? _findEmbeddedId3Header(Uint8List bytes) {
  for (var i = 0; i + 10 <= bytes.length; i += 1) {
    if (bytes[i] == 0x49 && bytes[i + 1] == 0x44 && bytes[i + 2] == 0x33) {
      return i;
    }
  }
  return null;
}
