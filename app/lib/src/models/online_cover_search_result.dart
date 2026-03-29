class OnlineCoverSearchResult {
  const OnlineCoverSearchResult({
    required this.id,
    required this.title,
    required this.artist,
    required this.thumbnailUrl,
    required this.fullImageUrl,
    this.album = '',
    this.source = '',
    this.score = 0,
  });

  final String id;
  final String title;
  final String artist;
  final String album;
  final String thumbnailUrl;
  final String fullImageUrl;
  final String source;
  final int score;

  OnlineCoverSearchResult copyWith({int? score, String? source}) {
    return OnlineCoverSearchResult(
      id: id,
      title: title,
      artist: artist,
      album: album,
      thumbnailUrl: thumbnailUrl,
      fullImageUrl: fullImageUrl,
      source: source ?? this.source,
      score: score ?? this.score,
    );
  }

  Map<String, dynamic> toJson() {
    return <String, dynamic>{
      'id': id,
      'title': title,
      'artist': artist,
      'album': album,
      'thumbnailUrl': thumbnailUrl,
      'fullImageUrl': fullImageUrl,
      'source': source,
      'score': score,
    };
  }

  factory OnlineCoverSearchResult.fromJson(Map<String, dynamic> json) {
    return OnlineCoverSearchResult(
      id: json['id']?.toString() ?? '',
      title: json['title']?.toString() ?? '',
      artist: json['artist']?.toString() ?? '',
      album: json['album']?.toString() ?? '',
      thumbnailUrl: json['thumbnailUrl']?.toString() ?? '',
      fullImageUrl: json['fullImageUrl']?.toString() ?? '',
      source: json['source']?.toString() ?? '',
      score: (json['score'] as num?)?.toInt() ?? 0,
    );
  }
}
