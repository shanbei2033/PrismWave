import 'package:path/path.dart' as p;

class Track {
  const Track({
    required this.path,
    required this.title,
    required this.artist,
    required this.album,
    this.coverPath,
  });

  final String path;
  final String title;
  final String artist;
  final String album;
  final String? coverPath;

  Track copyWith({
    String? path,
    String? title,
    String? artist,
    String? album,
    String? coverPath,
  }) {
    return Track(
      path: path ?? this.path,
      title: title ?? this.title,
      artist: artist ?? this.artist,
      album: album ?? this.album,
      coverPath: coverPath ?? this.coverPath,
    );
  }

  String get id => path;

  String get fileName => p.basename(path);

  factory Track.fromPath(String fullPath) {
    final name = p.basenameWithoutExtension(fullPath).trim();
    return Track(
      path: fullPath,
      title: name.isEmpty ? 'Unknown Title' : name,
      artist: 'Unknown Artist',
      album: p.basename(p.dirname(fullPath)),
      coverPath: null,
    );
  }

  factory Track.fromMap(Map<String, String> map) {
    return Track(
      path: map['path'] ?? '',
      title: map['title'] ?? 'Unknown Title',
      artist: map['artist'] ?? 'Unknown Artist',
      album: map['album'] ?? 'Unknown Album',
      coverPath: map['coverPath'],
    );
  }
}
