import 'dart:io';
import 'dart:typed_data';

import 'package:metadata_god/metadata_god.dart';
import 'package:path/path.dart' as p;

import '../models/audio_file_details.dart';
import '../models/track.dart';

Future<AudioFileDetails> readAudioFileDetails(
  Track track, {
  Duration? fallbackDuration,
}) async {
  Metadata? metadata;
  try {
    metadata = await MetadataGod.readMetadata(file: track.path);
  } catch (_) {
    metadata = null;
  }

  final duration = metadata?.duration ?? fallbackDuration;
  final file = File(track.path);
  final fileSize = await file.exists() ? await file.length() : 0;
  final bitrateKbps = _resolveAverageBitrateKbps(
    fileSize: fileSize,
    duration: duration,
  );
  final sampleRateHz = await _readSampleRate(track.path);

  final trackNumberLabel = _buildTrackNumberLabel(
    metadata?.trackNumber,
    metadata?.trackTotal,
  );

  return AudioFileDetails(
    durationLabel: _formatDuration(duration),
    trackNumberLabel: trackNumberLabel,
    bitrateLabel: bitrateKbps == null ? '--' : '$bitrateKbps kbps',
    sampleRateLabel: sampleRateHz == null ? '--' : '${sampleRateHz ~/ 1000} kHz',
    path: track.path,
  );
}

String _buildTrackNumberLabel(int? trackNumber, int? trackTotal) {
  if (trackNumber == null || trackNumber <= 0) return '--';
  if (trackTotal != null && trackTotal > 0) {
    return '$trackNumber / $trackTotal';
  }
  return '$trackNumber';
}

int? _resolveAverageBitrateKbps({
  required int fileSize,
  required Duration? duration,
}) {
  if (fileSize <= 0 || duration == null || duration <= Duration.zero) return null;
  final seconds = duration.inMilliseconds / 1000;
  if (seconds <= 0) return null;
  return ((fileSize * 8) / seconds / 1000).round();
}

String _formatDuration(Duration? duration) {
  if (duration == null || duration <= Duration.zero) return '--';
  final minutes = duration.inMinutes.remainder(60).toString().padLeft(2, '0');
  final seconds = duration.inSeconds.remainder(60).toString().padLeft(2, '0');
  final hours = duration.inHours;
  if (hours > 0) {
    return '${hours.toString().padLeft(2, '0')}:$minutes:$seconds';
  }
  return '$minutes:$seconds';
}

Future<int?> _readSampleRate(String path) async {
  final extension = p.extension(path).toLowerCase();
  final file = File(path);
  if (!await file.exists()) return null;

  try {
    if (extension == '.wav') return _readWavSampleRate(file);
    if (extension == '.flac') return _readFlacSampleRate(file);
    if (extension == '.mp3') return _readMp3SampleRate(file);
  } catch (_) {
    return null;
  }

  return null;
}

Future<int?> _readWavSampleRate(File file) async {
  final bytes = await file.openRead(0, 28).fold<BytesBuilder>(
    BytesBuilder(),
    (builder, chunk) => builder..add(chunk),
  );
  final data = bytes.takeBytes();
  if (data.length < 28) return null;
  final view = ByteData.sublistView(data);
  return view.getUint32(24, Endian.little);
}

Future<int?> _readFlacSampleRate(File file) async {
  final bytes = await file.openRead(0, 42).fold<BytesBuilder>(
    BytesBuilder(),
    (builder, chunk) => builder..add(chunk),
  );
  final data = bytes.takeBytes();
  if (data.length < 42) return null;
  if (String.fromCharCodes(data.sublist(0, 4)) != 'fLaC') return null;
  final header = data.sublist(18, 26);
  final sampleRate = (header[0] << 12) | (header[1] << 4) | (header[2] >> 4);
  return sampleRate > 0 ? sampleRate : null;
}

Future<int?> _readMp3SampleRate(File file) async {
  final bytes = await file.openRead(0, 4096).fold<BytesBuilder>(
    BytesBuilder(),
    (builder, chunk) => builder..add(chunk),
  );
  final data = bytes.takeBytes();
  if (data.length < 4) return null;

  var offset = 0;
  if (data.length >= 10 &&
      String.fromCharCodes(data.sublist(0, 3)) == 'ID3') {
    final size = ((data[6] & 0x7F) << 21) |
        ((data[7] & 0x7F) << 14) |
        ((data[8] & 0x7F) << 7) |
        (data[9] & 0x7F);
    offset = 10 + size;
  }

  const sampleRates = <List<int>>[
    [11025, 12000, 8000, 0],
    [0, 0, 0, 0],
    [22050, 24000, 16000, 0],
    [44100, 48000, 32000, 0],
  ];

  for (var i = offset; i <= data.length - 4; i++) {
    final b1 = data[i];
    final b2 = data[i + 1];
    final b3 = data[i + 2];
    if (b1 != 0xFF || (b2 & 0xE0) != 0xE0) continue;

    final versionBits = (b2 >> 3) & 0x03;
    final sampleBits = (b3 >> 2) & 0x03;
    final value = sampleRates[versionBits][sampleBits];
    if (value > 0) return value;
  }

  return null;
}
