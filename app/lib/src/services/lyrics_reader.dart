import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:dart_tags/dart_tags.dart';
import 'package:path/path.dart' as p;

import '../models/lyric_line.dart';
import '../models/lyrics_document.dart';

final RegExp _timeTagPattern = RegExp(
  r'\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]',
);

Future<LyricsDocument?> readLocalLyricsDocumentForTrack(
  String audioPath, {
  Duration? durationHint,
  String? title,
  String? artist,
}) async {
  final embedded = await _readEmbeddedLyrics(audioPath);
  if (embedded != null && embedded.trim().isNotEmpty) {
    final parsed = parseLyricsDocument(embedded, durationHint: durationHint);
    if (parsed != null && !parsed.isEmpty) {
      return LyricsDocument(
        lines: parsed.lines,
        isSynced: parsed.isSynced,
        rawText: embedded,
      );
    }
  }

  final sidecar = await _readSidecarLrc(
    audioPath,
    title: title,
    artist: artist,
  );
  if (sidecar != null && sidecar.trim().isNotEmpty) {
    final parsed = parseLyricsDocument(sidecar, durationHint: durationHint);
    if (parsed != null && !parsed.isEmpty) {
      return LyricsDocument(
        lines: parsed.lines,
        isSynced: parsed.isSynced,
        rawText: sidecar,
      );
    }
  }

  return null;
}

Future<List<LyricLine>> readLyricsForTrack(
  String audioPath, {
  Duration? durationHint,
  String? title,
  String? artist,
}) async {
  return (await readLocalLyricsDocumentForTrack(
        audioPath,
        durationHint: durationHint,
        title: title,
        artist: artist,
      ))
          ?.lines ??
      const <LyricLine>[];
}

Future<String?> _readEmbeddedLyrics(String audioPath) async {
  final extension = p.extension(audioPath).toLowerCase();
  if (extension == '.mp3') {
    return _readMp3EmbeddedLyrics(audioPath);
  }
  if (extension == '.m4a' || extension == '.mp4') {
    return _readMp4EmbeddedLyrics(audioPath);
  }
  if (extension == '.flac') {
    return _readFlacEmbeddedLyrics(audioPath);
  }
  if (extension == '.wav' || extension == '.wave') {
    return _readWavEmbeddedLyrics(audioPath);
  }
  if (extension == '.ogg' || extension == '.opus' || extension == '.oga') {
    return _readVorbisLikeEmbeddedLyrics(audioPath);
  }
  return null;
}

Future<String?> _readFlacEmbeddedLyrics(String audioPath) async {
  try {
    final bytes = await File(audioPath).readAsBytes();
    if (bytes.length < 8) return null;
    if (!_bytesEq(bytes.sublist(0, 4), [0x66, 0x4C, 0x61, 0x43])) {
      return null;
    }

    var offset = 4;
    var isLast = false;
    while (!isLast && offset + 4 <= bytes.length) {
      final header = bytes[offset];
      isLast = (header & 0x80) != 0;
      final blockType = header & 0x7F;
      final blockLength =
          (bytes[offset + 1] << 16) |
          (bytes[offset + 2] << 8) |
          bytes[offset + 3];
      final dataStart = offset + 4;
      final dataEnd = dataStart + blockLength;
      if (dataEnd > bytes.length) break;

      if (blockType == 4) {
        final lyrics = _readFlacVorbisCommentLyrics(
          Uint8List.fromList(bytes.sublist(dataStart, dataEnd)),
        );
        if (lyrics != null && lyrics.trim().isNotEmpty) {
          return lyrics.trim();
        }
      }

      offset = dataEnd;
    }
  } catch (_) {
    // Ignore malformed FLAC metadata.
  }

  return _readVorbisLikeEmbeddedLyrics(audioPath);
}

String? _readFlacVorbisCommentLyrics(Uint8List block) {
  if (block.length < 8) return null;
  var offset = 0;
  final vendorLen = _leInt(block, offset);
  offset += 4;
  if (vendorLen < 0 || offset + vendorLen > block.length) return null;
  offset += vendorLen;
  if (offset + 4 > block.length) return null;

  final commentsCount = _leInt(block, offset);
  offset += 4;
  if (commentsCount < 0) return null;

  for (var i = 0; i < commentsCount; i++) {
    if (offset + 4 > block.length) break;
    final commentLen = _leInt(block, offset);
    offset += 4;
    if (commentLen <= 0 || offset + commentLen > block.length) break;

    final raw = utf8.decode(
      block.sublist(offset, offset + commentLen),
      allowMalformed: true,
    );
    offset += commentLen;

    final sep = raw.indexOf('=');
    if (sep <= 0 || sep >= raw.length - 1) continue;
    final key = raw.substring(0, sep).trim().toLowerCase();
    final value = raw.substring(sep + 1).trim();
    if (value.isEmpty) continue;
    if (key.contains('lyric') || key == 'unsyncedlyrics') {
      return value;
    }
  }

  return null;
}

Future<String?> _readWavEmbeddedLyrics(String audioPath) async {
  try {
    final bytes = await File(audioPath).readAsBytes();
    if (bytes.length < 12) return null;
    final isRiff = _bytesEq(bytes.sublist(0, 4), [0x52, 0x49, 0x46, 0x46]);
    final isWave = _bytesEq(bytes.sublist(8, 12), [0x57, 0x41, 0x56, 0x45]);
    if (!isRiff || !isWave) return null;

    // 1) Prefer explicit ID3 chunks inside RIFF.
    var offset = 12;
    while (offset + 8 <= bytes.length) {
      final chunkId = bytes.sublist(offset, offset + 4);
      final chunkSize = _leInt(bytes, offset + 4);
      if (chunkSize < 0) break;
      final dataStart = offset + 8;
      final dataEnd = (dataStart + chunkSize).clamp(dataStart, bytes.length);

      final isId3Chunk =
          _bytesEq(chunkId, [0x49, 0x44, 0x33, 0x20]) || // 'ID3 '
          _bytesEq(chunkId, [0x69, 0x64, 0x33, 0x20]); // 'id3 '
      if (isId3Chunk && dataEnd > dataStart) {
        final chunk = Uint8List.fromList(bytes.sublist(dataStart, dataEnd));
        final lyric = _readUsltFromId3Bytes(chunk);
        if (lyric != null && lyric.trim().isNotEmpty) {
          return lyric.trim();
        }
      }

      // RIFF chunks are word aligned.
      final aligned = chunkSize + (chunkSize % 2);
      offset = dataStart + aligned;
    }

    // 2) Fallback: scan for embedded ID3 header in whole file.
    for (var i = 0; i + 10 <= bytes.length; i++) {
      if (bytes[i] == 0x49 && bytes[i + 1] == 0x44 && bytes[i + 2] == 0x33) {
        final lyric = _readUsltFromId3Bytes(
          Uint8List.fromList(bytes.sublist(i)),
        );
        if (lyric != null && lyric.trim().isNotEmpty) {
          return lyric.trim();
        }
      }
    }
  } catch (_) {
    // Ignore malformed WAV chunks.
  }

  return null;
}

Future<String?> _readMp3EmbeddedLyrics(String audioPath) async {
  try {
    final processor = TagProcessor();
    final tags = await processor.getTagsFromByteArray(
      File(audioPath).readAsBytes(),
      [TagType.id3v2],
    );

    for (final tag in tags) {
      final raw = tag.tags['lyrics'];
      if (raw is String && raw.trim().isNotEmpty) {
        return raw.trim();
      }
      if (raw is Map) {
        for (final value in raw.values) {
          if (value is UnSyncLyric && value.lyrics.trim().isNotEmpty) {
            return value.lyrics.trim();
          }
          if (value is String && value.trim().isNotEmpty) {
            return value.trim();
          }
        }
      }
      if (raw is UnSyncLyric && raw.lyrics.trim().isNotEmpty) {
        return raw.lyrics.trim();
      }

      final fromAny = _findLyricsInDynamic(tag.tags);
      if (fromAny != null && fromAny.trim().isNotEmpty) {
        return fromAny.trim();
      }
    }
  } catch (_) {
    // Ignore malformed tags and try manual parser below.
  }

  try {
    final bytes = await File(audioPath).readAsBytes();
    final raw = _readUsltFromId3Bytes(bytes);
    if (raw != null && raw.trim().isNotEmpty) {
      return raw.trim();
    }
  } catch (_) {
    // Ignore malformed ID3 frames.
  }

  return null;
}

Future<String?> _readMp4EmbeddedLyrics(String audioPath) async {
  try {
    final bytes = await File(audioPath).readAsBytes();
    final payload = _readMp4LyricAtom(bytes);
    if (payload == null || payload.isEmpty) return null;
    final text = _decodeTextBytes(payload);
    if (text != null && text.trim().isNotEmpty) {
      return text.trim();
    }
  } catch (_) {
    // Ignore malformed MP4 atoms.
  }
  return null;
}

Future<String?> _readVorbisLikeEmbeddedLyrics(String audioPath) async {
  try {
    final bytes = await File(audioPath).readAsBytes();
    final text = latin1.decode(bytes, allowInvalid: true);
    final match = RegExp(
      r'(?:LYRICS|UNSYNCEDLYRICS|LYRIC)=(.+?)(?:\u0000|\r\n|\n)',
      caseSensitive: false,
      dotAll: true,
    ).firstMatch(text);
    final body = match?.group(1)?.trim();
    if (body != null && body.isNotEmpty) {
      return body;
    }
  } catch (_) {
    // Ignore parsing issues for unsupported files.
  }
  return null;
}

Future<String?> _readSidecarLrc(
  String audioPath, {
  String? title,
  String? artist,
}) async {
  final exactPath = p.setExtension(audioPath, '.lrc');
  final exact = File(exactPath);
  if (exact.existsSync()) {
    final text = await _readTextFileSmart(exact);
    if (text != null && text.trim().isNotEmpty) return text;
  }

  final baseDir = Directory(p.dirname(audioPath));
  if (!baseDir.existsSync()) return null;
  final parentDir = baseDir.parent;
  final candidateDirs = <Directory>[
    baseDir,
    Directory(p.join(baseDir.path, 'lyrics')),
    if (parentDir.path != baseDir.path) parentDir,
    if (parentDir.path != baseDir.path)
      Directory(p.join(parentDir.path, 'lyrics')),
  ].where((d) => d.existsSync()).toList(growable: false);

  final keys = <String>{
    _normalizeName(p.basenameWithoutExtension(audioPath)),
    if (title != null && title.trim().isNotEmpty) _normalizeName(title),
    if (artist != null &&
        title != null &&
        artist.trim().isNotEmpty &&
        title.trim().isNotEmpty)
      _normalizeName('$artist - $title'),
    if (artist != null &&
        title != null &&
        artist.trim().isNotEmpty &&
        title.trim().isNotEmpty)
      _normalizeName('$title - $artist'),
  }..removeWhere((e) => e.isEmpty);

  for (final dir in candidateDirs) {
    try {
      await for (final entity in dir.list(followLinks: false)) {
        if (entity is! File) continue;
        if (p.extension(entity.path).toLowerCase() != '.lrc') continue;
        final stem = _normalizeName(p.basenameWithoutExtension(entity.path));
        final matched = keys.any(
          (k) => stem == k || stem.contains(k) || k.contains(stem),
        );
        if (!matched) continue;
        final text = await _readTextFileSmart(entity);
        if (text != null && text.trim().isNotEmpty) {
          return text;
        }
      }
    } catch (_) {
      // Ignore I/O errors and continue next directory.
    }
  }

  return null;
}

String _normalizeName(String input) {
  return input
      .toLowerCase()
      .replaceAll(RegExp(r'\[[^\]]*\]'), '')
      .replaceAll(RegExp(r'\([^)]*\)'), '')
      .replaceAll(RegExp(r'[^a-z0-9\u4e00-\u9fa5]+'), '');
}

Future<String?> _readTextFileSmart(File file) async {
  try {
    final bytes = await file.readAsBytes();
    return _decodeTextFileBytes(Uint8List.fromList(bytes));
  } catch (_) {
    return null;
  }
}

String? _decodeTextFileBytes(Uint8List bytes) {
  if (bytes.isEmpty) return null;

  if (bytes.length >= 3 &&
      bytes[0] == 0xEF &&
      bytes[1] == 0xBB &&
      bytes[2] == 0xBF) {
    return utf8.decode(bytes.sublist(3), allowMalformed: true);
  }
  if (bytes.length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) {
    return _decodeUtf16(bytes.sublist(2), littleEndian: false);
  }
  if (bytes.length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) {
    return _decodeUtf16(bytes.sublist(2), littleEndian: true);
  }

  final utf8Text = utf8.decode(bytes, allowMalformed: true);
  if (_hasLikelyLyricsShape(utf8Text)) return utf8Text;

  if (_looksLikeUtf16(bytes)) {
    final le = _decodeUtf16(bytes, littleEndian: true);
    if (le != null && _hasLikelyLyricsShape(le)) return le;
    final be = _decodeUtf16(bytes, littleEndian: false);
    if (be != null && _hasLikelyLyricsShape(be)) return be;
  }

  final latin = latin1.decode(bytes, allowInvalid: true);
  if (_hasLikelyLyricsShape(latin)) return latin;

  return utf8Text;
}

bool _hasLikelyLyricsShape(String text) {
  if (_timeTagPattern.hasMatch(text)) return true;
  final lines = text
      .split(RegExp(r'[\r\n]+'))
      .where((line) => line.trim().isNotEmpty);
  return lines.length >= 4;
}

String? _findLyricsInDynamic(dynamic input) {
  if (input == null) return null;
  if (input is UnSyncLyric && input.lyrics.trim().isNotEmpty) {
    return input.lyrics.trim();
  }
  if (input is String) {
    final value = input.trim();
    if (value.isEmpty) return null;
    final hasLineBreak = value.contains('\n') || value.contains('\r');
    if (_timeTagPattern.hasMatch(value) || hasLineBreak) {
      return value;
    }
    return null;
  }
  if (input is Map) {
    for (final entry in input.entries) {
      final key = entry.key.toString().toLowerCase();
      final value = entry.value;
      if (key.contains('lyric') || key == 'unsyncedlyrics' || key == 'uslt') {
        final hit = _findLyricsInDynamic(value);
        if (hit != null) return hit;
      }
      final nested = _findLyricsInDynamic(value);
      if (nested != null) return nested;
    }
  }
  if (input is Iterable) {
    for (final item in input) {
      final nested = _findLyricsInDynamic(item);
      if (nested != null) return nested;
    }
  }
  return null;
}

LyricsDocument? parseLyricsDocument(String raw, {Duration? durationHint}) {
  final normalized = raw.replaceAll('\r\n', '\n').replaceAll('\r', '\n');
  final lines = normalized.split('\n');
  final parsed = <LyricLine>[];
  final plain = <String>[];

  for (final line in lines) {
    final text = line.replaceAll(_timeTagPattern, '').trim();
    final matches = _timeTagPattern.allMatches(line).toList(growable: false);
    if (matches.isNotEmpty) {
      if (text.isEmpty) continue;
      for (final match in matches) {
        parsed.add(LyricLine(time: _parseTimestamp(match), text: text));
      }
      continue;
    }

    if (_isMetadataLine(line)) continue;
    if (text.isNotEmpty) plain.add(text);
  }

  if (parsed.isNotEmpty) {
    parsed.sort((a, b) => a.time.compareTo(b.time));
    return LyricsDocument(
      lines: parsed,
      isSynced: true,
      rawText: raw,
    );
  }

  if (plain.isEmpty) return null;

  final totalMs = durationHint != null && durationHint > Duration.zero
      ? durationHint.inMilliseconds
      : plain.length * 3200;
  final stepMs = (totalMs / plain.length).round().clamp(1200, 8000);

  return LyricsDocument(
    lines: List<LyricLine>.generate(
      plain.length,
      (index) => LyricLine(
        time: Duration(milliseconds: stepMs * index),
        text: plain[index],
      ),
      growable: false,
    ),
    isSynced: false,
    rawText: raw,
  );
}

bool _isMetadataLine(String raw) {
  final lower = raw.trim().toLowerCase();
  return lower.startsWith('[ti:') ||
      lower.startsWith('[ar:') ||
      lower.startsWith('[al:') ||
      lower.startsWith('[by:') ||
      lower.startsWith('[offset:');
}

Duration _parseTimestamp(RegExpMatch match) {
  final minutes = int.tryParse(match.group(1) ?? '') ?? 0;
  final seconds = int.tryParse(match.group(2) ?? '') ?? 0;
  final fraction = match.group(3) ?? '0';
  final milliseconds = switch (fraction.length) {
    0 => 0,
    1 => (int.tryParse(fraction) ?? 0) * 100,
    2 => (int.tryParse(fraction) ?? 0) * 10,
    _ => int.tryParse(fraction.substring(0, 3)) ?? 0,
  };
  return Duration(
    minutes: minutes,
    seconds: seconds,
    milliseconds: milliseconds,
  );
}

String? _readUsltFromId3Bytes(List<int> bytes) {
  if (bytes.length < 10) return null;
  if (latin1.decode(bytes.sublist(0, 3), allowInvalid: true) != 'ID3') {
    return null;
  }

  final tagSize = _syncSafeInt(bytes, 6);
  var offset = 10;
  final end = (10 + tagSize).clamp(10, bytes.length);

  while (offset + 10 <= end) {
    final frameId = latin1.decode(bytes.sublist(offset, offset + 4));
    final frameSize = _beInt(bytes, offset + 4);
    if (frameSize <= 0) break;
    final dataStart = offset + 10;
    final dataEnd = (dataStart + frameSize).clamp(dataStart, end);
    if (dataEnd <= dataStart) break;

    if (frameId == 'USLT') {
      final body = bytes.sublist(dataStart, dataEnd);
      if (body.length > 4) {
        final text = _decodeUsltFrameBody(body);
        if (text != null && text.trim().isNotEmpty) {
          return text.trim();
        }
      }
    }
    offset = dataEnd;
  }

  return null;
}

String? _decodeUsltFrameBody(List<int> body) {
  final encoding = body[0];
  final lyricSection = body.sublist(4); // skip encoding + lang(3)
  if (lyricSection.isEmpty) return null;

  if (encoding == 0x00 || encoding == 0x03) {
    final split = lyricSection.indexOf(0x00);
    final lyricBytes = split >= 0 && split + 1 < lyricSection.length
        ? lyricSection.sublist(split + 1)
        : lyricSection;
    if (encoding == 0x00) {
      return latin1.decode(lyricBytes, allowInvalid: true);
    }
    return utf8.decode(lyricBytes, allowMalformed: true);
  }

  final split = _indexOfDoubleZero(lyricSection);
  final lyricBytes = split >= 0 && split + 2 < lyricSection.length
      ? lyricSection.sublist(split + 2)
      : lyricSection;
  return _decodeTextBytes(lyricBytes);
}

int _indexOfDoubleZero(List<int> data) {
  for (var i = 0; i < data.length - 1; i++) {
    if (data[i] == 0x00 && data[i + 1] == 0x00) return i;
  }
  return -1;
}

Uint8List? _readMp4LyricAtom(Uint8List bytes) {
  return _scanAtomsForLyric(bytes, 0, bytes.length, insideMeta: false);
}

Uint8List? _scanAtomsForLyric(
  Uint8List bytes,
  int start,
  int end, {
  required bool insideMeta,
}) {
  var offset = start;
  while (offset + 8 <= end) {
    final header = _readAtomHeader(bytes, offset, end);
    if (header == null) break;
    final atomEnd = header.end;
    final type = header.type;
    var contentStart = header.contentStart;

    final isMeta = _bytesEq(type, [0x6D, 0x65, 0x74, 0x61]); // meta
    if (isMeta && contentStart + 4 <= atomEnd) {
      // meta atom has 4-byte version/flags before its children
      contentStart += 4;
    }

    final isLyricAtom = _bytesEq(type, [0xA9, 0x6C, 0x79, 0x72]); // ©lyr
    if (isLyricAtom) {
      final payload = _extractDataAtomPayload(bytes, contentStart, atomEnd);
      if (payload != null && payload.isNotEmpty) {
        return payload;
      }
    }

    final isFreeFormAtom = _bytesEq(type, [0x2D, 0x2D, 0x2D, 0x2D]);
    if (isFreeFormAtom) {
      final payload = _extractFreeFormLyricPayload(
        bytes,
        contentStart,
        atomEnd,
      );
      if (payload != null && payload.isNotEmpty) {
        return payload;
      }
    }

    final isContainer =
        isMeta ||
        _bytesEq(type, [0x6D, 0x6F, 0x6F, 0x76]) || // moov
        _bytesEq(type, [0x75, 0x64, 0x74, 0x61]) || // udta
        _bytesEq(type, [0x69, 0x6C, 0x73, 0x74]) || // ilst
        _bytesEq(type, [0x74, 0x72, 0x61, 0x6B]) || // trak
        _bytesEq(type, [0x6D, 0x64, 0x69, 0x61]) || // mdia
        _bytesEq(type, [0x6D, 0x69, 0x6E, 0x66]) || // minf
        _bytesEq(type, [0x73, 0x74, 0x62, 0x6C]) || // stbl
        _bytesEq(type, [0x6D, 0x6F, 0x6F, 0x66]) || // moof
        _bytesEq(type, [0x74, 0x72, 0x61, 0x66]); // traf

    if (isContainer && contentStart < atomEnd) {
      final nested = _scanAtomsForLyric(
        bytes,
        contentStart,
        atomEnd,
        insideMeta: insideMeta || isMeta,
      );
      if (nested != null && nested.isNotEmpty) {
        return nested;
      }
    }

    offset = atomEnd;
  }
  return null;
}

_AtomHeader? _readAtomHeader(Uint8List bytes, int offset, int limit) {
  if (offset + 8 > limit) return null;
  final size32 = _beInt(bytes, offset);
  final type = bytes.sublist(offset + 4, offset + 8);
  var headerSize = 8;
  var atomSize = size32;

  if (size32 == 1) {
    if (offset + 16 > limit) return null;
    atomSize = _beInt64(bytes, offset + 8);
    headerSize = 16;
  } else if (size32 == 0) {
    atomSize = limit - offset;
  }

  if (atomSize < headerSize || offset + atomSize > limit) {
    return null;
  }

  return _AtomHeader(
    type: type,
    contentStart: offset + headerSize,
    end: offset + atomSize,
  );
}

Uint8List? _extractDataAtomPayload(Uint8List bytes, int start, int end) {
  var offset = start;
  while (offset + 8 <= end) {
    final header = _readAtomHeader(bytes, offset, end);
    if (header == null) break;
    final type = header.type;
    if (_bytesEq(type, [0x64, 0x61, 0x74, 0x61])) {
      final payloadStart = header.contentStart + 8; // flags/type + locale
      if (payloadStart < header.end) {
        return bytes.sublist(payloadStart, header.end);
      }
    }
    offset = header.end;
  }
  return null;
}

Uint8List? _extractFreeFormLyricPayload(Uint8List bytes, int start, int end) {
  String? name;
  Uint8List? data;
  var offset = start;
  while (offset + 8 <= end) {
    final header = _readAtomHeader(bytes, offset, end);
    if (header == null) break;
    final type = header.type;

    if (_bytesEq(type, [0x6E, 0x61, 0x6D, 0x65])) {
      final payloadStart = header.contentStart + 4;
      if (payloadStart < header.end) {
        name = _decodeTextBytes(bytes.sublist(payloadStart, header.end));
      }
    } else if (_bytesEq(type, [0x64, 0x61, 0x74, 0x61])) {
      final payloadStart = header.contentStart + 8;
      if (payloadStart < header.end) {
        data = bytes.sublist(payloadStart, header.end);
      }
    }

    offset = header.end;
  }

  final fieldName = name?.toLowerCase() ?? '';
  if (fieldName.contains('lyric') && data != null && data.isNotEmpty) {
    return data;
  }

  return null;
}

String? _decodeTextBytes(List<int> bytes) {
  if (bytes.isEmpty) return null;
  final trimmed = _trimTrailingNulls(Uint8List.fromList(bytes));
  if (trimmed.isEmpty) return null;

  if (trimmed.length >= 2 && trimmed[0] == 0xFE && trimmed[1] == 0xFF) {
    return _decodeUtf16(trimmed.sublist(2), littleEndian: false);
  }
  if (trimmed.length >= 2 && trimmed[0] == 0xFF && trimmed[1] == 0xFE) {
    return _decodeUtf16(trimmed.sublist(2), littleEndian: true);
  }

  if (_looksLikeUtf16(trimmed)) {
    return _decodeUtf16(trimmed, littleEndian: true) ??
        _decodeUtf16(trimmed, littleEndian: false);
  }

  return utf8.decode(trimmed, allowMalformed: true);
}

Uint8List _trimTrailingNulls(Uint8List input) {
  var end = input.length;
  while (end > 0 && input[end - 1] == 0x00) {
    end--;
  }
  return end == input.length ? input : input.sublist(0, end);
}

bool _looksLikeUtf16(Uint8List bytes) {
  if (bytes.length < 4) return false;
  var zeroCount = 0;
  for (final b in bytes) {
    if (b == 0) zeroCount++;
  }
  return zeroCount > bytes.length ~/ 4;
}

String? _decodeUtf16(Uint8List bytes, {required bool littleEndian}) {
  if (bytes.length < 2) return null;
  final evenLength = bytes.length.isEven ? bytes.length : bytes.length - 1;
  if (evenLength <= 0) return null;
  final data = ByteData.sublistView(bytes, 0, evenLength);
  final units = List<int>.generate(
    evenLength ~/ 2,
    (i) => data.getUint16(i * 2, littleEndian ? Endian.little : Endian.big),
    growable: false,
  );
  return String.fromCharCodes(units);
}

bool _bytesEq(List<int> a, List<int> b) {
  if (a.length != b.length) return false;
  for (var i = 0; i < a.length; i++) {
    if (a[i] != b[i]) return false;
  }
  return true;
}

int _beInt(List<int> bytes, int offset) {
  return (bytes[offset] << 24) |
      (bytes[offset + 1] << 16) |
      (bytes[offset + 2] << 8) |
      bytes[offset + 3];
}

int _beInt64(List<int> bytes, int offset) {
  final high = _beInt(bytes, offset);
  final low = _beInt(bytes, offset + 4);
  return (high * 0x100000000) + low;
}

int _leInt(List<int> bytes, int offset) {
  if (offset + 4 > bytes.length) return -1;
  return bytes[offset] |
      (bytes[offset + 1] << 8) |
      (bytes[offset + 2] << 16) |
      (bytes[offset + 3] << 24);
}

int _syncSafeInt(List<int> bytes, int offset) {
  return ((bytes[offset] & 0x7F) << 21) |
      ((bytes[offset + 1] & 0x7F) << 14) |
      ((bytes[offset + 2] & 0x7F) << 7) |
      (bytes[offset + 3] & 0x7F);
}

class _AtomHeader {
  const _AtomHeader({
    required this.type,
    required this.contentStart,
    required this.end,
  });

  final Uint8List type;
  final int contentStart;
  final int end;
}
