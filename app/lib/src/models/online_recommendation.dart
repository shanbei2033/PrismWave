import 'package:flutter/foundation.dart';

import '../models/app_language.dart';
import '../models/track.dart';
import '../utils/online_text_utils.dart';

@immutable
class OnlineHomeData {
  const OnlineHomeData({
    required this.schemaVersion,
    required this.generatedAt,
    required this.editionDate,
    required this.tags,
    required this.sections,
    required this.topPlaylist,
    required this.albumRecommendations,
  });

  final int schemaVersion;
  final DateTime generatedAt;
  final String editionDate;
  final List<OnlineTag> tags;
  final List<OnlineSection> sections;

  /// Optional curated playlist surfaced as the home page banner. Same shape
  /// as a section (id/title/subtitle/tracks) so the existing widget renders
  /// it identically. May be null on older payloads or if the upstream chart
  /// fetch failed; the banner UI hides itself in that case.
  final OnlineSection? topPlaylist;

  /// Album-card recommendations (new releases). Tracks aren't inlined; the
  /// controller fetches album detail on demand when the user plays one.
  final List<OnlineAlbumCard> albumRecommendations;

  factory OnlineHomeData.fromJson(Map<String, dynamic> json) {
    final rawTags = json['tags'];
    final tags = <OnlineTag>[];
    if (rawTags is List) {
      for (final item in rawTags) {
        if (item is Map) {
          final tag = OnlineTag.fromJson(item.cast<String, dynamic>());
          if (tag != null) tags.add(tag);
        }
      }
    }

    final rawSections = json['sections'];
    final sections = <OnlineSection>[];
    if (rawSections is List) {
      for (final item in rawSections) {
        if (item is Map) {
          final section = OnlineSection.fromJson(item.cast<String, dynamic>());
          if (section != null && section.tracks.isNotEmpty) {
            sections.add(section);
          }
        }
      }
    }

    OnlineSection? topPlaylist;
    final rawTop = json['topPlaylist'];
    if (rawTop is Map) {
      final parsed = OnlineSection.fromJson(rawTop.cast<String, dynamic>());
      if (parsed != null && parsed.tracks.isNotEmpty) {
        topPlaylist = parsed;
      }
    }

    final rawAlbums = json['albumRecommendations'];
    final albums = <OnlineAlbumCard>[];
    if (rawAlbums is List) {
      for (final item in rawAlbums) {
        if (item is Map) {
          final card = OnlineAlbumCard.fromJson(item.cast<String, dynamic>());
          if (card != null) albums.add(card);
        }
      }
    }

    return OnlineHomeData(
      schemaVersion: (json['schemaVersion'] as num?)?.toInt() ?? 1,
      generatedAt: _parseUtc(json['generatedAt']),
      editionDate: (json['editionDate'] as String?) ?? '',
      tags: List.unmodifiable(tags),
      sections: List.unmodifiable(sections),
      topPlaylist: topPlaylist,
      albumRecommendations: List.unmodifiable(albums),
    );
  }

  Map<String, dynamic> toJson() => {
    'schemaVersion': schemaVersion,
    'generatedAt': generatedAt.toUtc().toIso8601String(),
    'editionDate': editionDate,
    'tags': tags.map((t) => t.toJson()).toList(),
    'sections': sections.map((s) => s.toJson()).toList(),
    if (topPlaylist != null) 'topPlaylist': topPlaylist!.toJson(),
    'albumRecommendations': albumRecommendations
        .map((a) => a.toJson())
        .toList(),
  };
}

@immutable
class OnlineAlbumCard {
  const OnlineAlbumCard({
    required this.albumId,
    required this.name,
    required this.artist,
    required this.coverUrl,
  });

  final int albumId;
  final String name;
  final String artist;
  final String? coverUrl;

  String get canonicalKey => 'netease-album:$albumId';

  static OnlineAlbumCard? fromJson(Map<String, dynamic> json) {
    final id = (json['albumId'] as num?)?.toInt();
    final name = cleanOnlineText(json['name']);
    if (id == null || id <= 0 || name.isEmpty) return null;
    final artist = cleanOnlineText(json['artist']);
    final cover = (json['coverUrl'] as String?)?.trim();
    return OnlineAlbumCard(
      albumId: id,
      name: name,
      artist: artist,
      coverUrl: (cover == null || cover.isEmpty) ? null : cover,
    );
  }

  Map<String, dynamic> toJson() => {
    'albumId': albumId,
    'name': name,
    'artist': artist,
    if (coverUrl != null) 'coverUrl': coverUrl,
  };
}

@immutable
class OnlineTag {
  const OnlineTag({
    required this.name,
    required this.weight,
    required this.count,
  });

  final String name;
  final double weight;
  final int count;

  static OnlineTag? fromJson(Map<String, dynamic> json) {
    final name = (json['name'] as String?)?.trim();
    if (name == null || name.isEmpty) return null;
    return OnlineTag(
      name: name,
      weight: (json['weight'] as num?)?.toDouble() ?? 1.0,
      count: (json['count'] as num?)?.toInt() ?? 0,
    );
  }

  Map<String, dynamic> toJson() => {
    'name': name,
    'weight': weight,
    'count': count,
  };
}

@immutable
class OnlineSection {
  const OnlineSection({
    required this.id,
    required this.titleByLang,
    required this.subtitle,
    required this.tracks,
  });

  final String id;
  final Map<String, String> titleByLang;
  final String? subtitle;
  final List<OnlineTrackCandidate> tracks;

  String localizedTitle(AppLanguage language) {
    final key = switch (language) {
      AppLanguage.zhCn => 'zh-Hans',
      AppLanguage.zhTw => 'zh-Hant',
      AppLanguage.enUs => 'en-US',
    };
    return titleByLang[key] ??
        titleByLang['en-US'] ??
        titleByLang.values.firstOrNull ??
        id;
  }

  static OnlineSection? fromJson(Map<String, dynamic> json) {
    final id = (json['id'] as String?)?.trim();
    if (id == null || id.isEmpty) return null;

    final rawTitle = json['title'];
    final titleByLang = <String, String>{};
    if (rawTitle is Map) {
      rawTitle.forEach((key, value) {
        if (key is String && value is String && value.trim().isNotEmpty) {
          titleByLang[key] = value;
        }
      });
    }
    if (titleByLang.isEmpty) {
      titleByLang['en-US'] = id;
    }

    final rawTracks = json['tracks'];
    final tracks = <OnlineTrackCandidate>[];
    if (rawTracks is List) {
      for (var index = 0; index < rawTracks.length; index++) {
        final raw = rawTracks[index];
        if (raw is Map) {
          final track = OnlineTrackCandidate.fromJson(
            raw.cast<String, dynamic>(),
            sectionId: id,
            index: index,
          );
          if (track != null) tracks.add(track);
        }
      }
    }

    return OnlineSection(
      id: id,
      titleByLang: Map.unmodifiable(titleByLang),
      subtitle: (json['subtitle'] as String?)?.trim().isEmpty == true
          ? null
          : json['subtitle'] as String?,
      tracks: List.unmodifiable(tracks),
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'title': titleByLang,
    if (subtitle != null) 'subtitle': subtitle,
    'tracks': tracks.map((t) => t.toJson()).toList(),
  };
}

@immutable
class OnlineTrackCandidate {
  const OnlineTrackCandidate({
    required this.title,
    required this.artist,
    required this.album,
    required this.durationMs,
    required this.coverUrl,
    required this.audioUrl,
    required this.audioProvider,
    required this.providerTrackId,
    required this.sourceTags,
    required this.canonicalKey,
  });

  final String title;
  final String artist;
  final String album;
  final int durationMs;
  final String? coverUrl;
  final String? audioUrl;
  final String? audioProvider;
  final String? providerTrackId;
  final List<String> sourceTags;
  final String canonicalKey;

  bool get hasDirectAudio => (audioUrl ?? '').trim().isNotEmpty;

  static OnlineTrackCandidate? fromJson(
    Map<String, dynamic> json, {
    required String sectionId,
    required int index,
  }) {
    final title = cleanOnlineText(json['title']);
    final artist = cleanOnlineText(json['artist']);
    if (title.isEmpty || artist.isEmpty) return null;

    final providerTrackId = (json['providerTrackId'] as String?)?.trim();
    final audioProvider = (json['audioProvider'] as String?)?.trim();
    final audioUrl = (json['audioUrl'] as String?)?.trim();
    final coverUrl = (json['coverUrl'] as String?)?.trim();
    final album = cleanOnlineText(json['album']);
    final duration = (json['durationMs'] as num?)?.toInt() ?? 0;

    final tagsList = <String>[];
    final rawTags = json['sourceTags'];
    if (rawTags is List) {
      for (final value in rawTags) {
        if (value is String && value.isNotEmpty) tagsList.add(value);
      }
    }

    final canonicalKey = _canonicalKey(
      sectionId: sectionId,
      index: index,
      provider: audioProvider,
      providerTrackId: providerTrackId,
      title: title,
      artist: artist,
    );

    return OnlineTrackCandidate(
      title: title,
      artist: artist,
      album: album,
      durationMs: duration,
      coverUrl: (coverUrl == null || coverUrl.isEmpty) ? null : coverUrl,
      audioUrl: (audioUrl == null || audioUrl.isEmpty) ? null : audioUrl,
      audioProvider: (audioProvider == null || audioProvider.isEmpty)
          ? null
          : audioProvider,
      providerTrackId: (providerTrackId == null || providerTrackId.isEmpty)
          ? null
          : providerTrackId,
      sourceTags: List.unmodifiable(tagsList),
      canonicalKey: canonicalKey,
    );
  }

  Map<String, dynamic> toJson() => {
    'title': title,
    'artist': artist,
    'album': album,
    'durationMs': durationMs,
    'coverUrl': coverUrl,
    'audioUrl': audioUrl,
    'audioProvider': audioProvider,
    'providerTrackId': providerTrackId,
    'sourceTags': sourceTags,
  };

  Track toTrack() {
    final pathPart = providerTrackId != null && providerTrackId!.isNotEmpty
        ? '${audioProvider ?? 'unknown'}/$providerTrackId'
        : 'meta/${Uri.encodeComponent(canonicalKey)}';
    return Track(
      path: 'online://$pathPart',
      title: title,
      artist: artist,
      album: album.isEmpty ? '' : album,
      coverPath: coverUrl,
      playbackUrl: audioUrl,
    );
  }

  static String _canonicalKey({
    required String sectionId,
    required int index,
    required String? provider,
    required String? providerTrackId,
    required String title,
    required String artist,
  }) {
    if (provider != null &&
        provider.isNotEmpty &&
        providerTrackId != null &&
        providerTrackId.isNotEmpty) {
      return '$provider:$providerTrackId';
    }
    final normalized = '${title.toLowerCase()}|${artist.toLowerCase()}';
    return 'q:$sectionId#$index:$normalized';
  }
}

DateTime _parseUtc(Object? value) {
  if (value is String && value.isNotEmpty) {
    final parsed = DateTime.tryParse(value);
    if (parsed != null) return parsed.toUtc();
  }
  return DateTime.now().toUtc();
}

extension _IterableHead<T> on Iterable<T> {
  T? get firstOrNull => isEmpty ? null : first;
}
