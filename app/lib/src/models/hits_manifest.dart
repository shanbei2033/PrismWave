class HitsTimeWindow {
  const HitsTimeWindow({
    required this.label,
    required this.startAt,
    required this.endAt,
  });

  final String label;
  final DateTime startAt;
  final DateTime endAt;

  bool contains(DateTime value) =>
      !value.isBefore(startAt) && value.isBefore(endAt);

  factory HitsTimeWindow.fromJson(Map<String, dynamic> json) {
    return HitsTimeWindow(
      label: (json['label'] as String? ?? '').trim(),
      startAt: _parseUtcDateTime(json['start_at']),
      endAt: _parseUtcDateTime(json['end_at']),
    );
  }
}

class HitsSourceSnapshot {
  const HitsSourceSnapshot({
    required this.source,
    required this.status,
    this.scope,
    this.candidateCount,
  });

  final String source;
  final String status;
  final String? scope;
  final int? candidateCount;

  factory HitsSourceSnapshot.fromJson(Map<String, dynamic> json) {
    return HitsSourceSnapshot(
      source: (json['source'] as String? ?? '').trim(),
      status: (json['status'] as String? ?? '').trim(),
      scope: (json['scope'] as String?)?.trim(),
      candidateCount: (json['candidate_count'] as num?)?.toInt(),
    );
  }
}

class HitsLatestManifest {
  const HitsLatestManifest({
    required this.schemaVersion,
    required this.stationId,
    required this.timezone,
    required this.generatedAt,
    required this.generatorVersion,
    required this.activeEditionDate,
    required this.schedulePath,
    required this.scheduleUrl,
    required this.serviceWindows,
    required this.offAirWindows,
    required this.sourceSnapshots,
  });

  final int schemaVersion;
  final String stationId;
  final String timezone;
  final DateTime generatedAt;
  final String generatorVersion;
  final String activeEditionDate;
  final String schedulePath;
  final Uri scheduleUrl;
  final List<HitsTimeWindow> serviceWindows;
  final List<HitsTimeWindow> offAirWindows;
  final List<HitsSourceSnapshot> sourceSnapshots;

  factory HitsLatestManifest.fromJson(Map<String, dynamic> json) {
    final scheduleUrlValue = (json['schedule_url'] as String? ?? '').trim();
    if (scheduleUrlValue.isEmpty) {
      throw const FormatException(
        'HITS latest manifest is missing schedule_url',
      );
    }

    return HitsLatestManifest(
      schemaVersion: (json['schema_version'] as num?)?.toInt() ?? 0,
      stationId: (json['station_id'] as String? ?? '').trim(),
      timezone: (json['timezone'] as String? ?? '').trim(),
      generatedAt: _parseUtcDateTime(json['generated_at']),
      generatorVersion: (json['generator_version'] as String? ?? '').trim(),
      activeEditionDate: (json['active_edition_date'] as String? ?? '').trim(),
      schedulePath: (json['schedule_path'] as String? ?? '').trim(),
      scheduleUrl: Uri.parse(scheduleUrlValue),
      serviceWindows: _parseWindowList(json['service_windows']),
      offAirWindows: _parseWindowList(json['off_air_windows']),
      sourceSnapshots: _parseSourceSnapshotList(json['source_snapshot']),
    );
  }

  Uri scheduleUrlForEditionDate(String editionDate) {
    final segments = [...scheduleUrl.pathSegments];
    if (segments.isEmpty) return scheduleUrl;
    segments[segments.length - 1] = '$editionDate.json';
    return scheduleUrl.replace(pathSegments: segments);
  }
}

class HitsScheduleTrack {
  const HitsScheduleTrack({
    required this.slot,
    required this.stationTrackId,
    required this.window,
    required this.startAt,
    required this.endAt,
    required this.duration,
    required this.title,
    required this.artist,
    required this.album,
    required this.audioUrl,
    required this.audioProvider,
    required this.providerTrackId,
    required this.coverUrl,
    required this.score,
    required this.sourceTags,
    required this.titleVariants,
    required this.artistVariants,
    required this.searchQuery,
  });

  final int slot;
  final String stationTrackId;
  final String window;
  final DateTime startAt;
  final DateTime endAt;
  final Duration duration;
  final String title;
  final String artist;
  final String album;
  final Uri? audioUrl;
  final String audioProvider;
  final String providerTrackId;
  final Uri? coverUrl;
  final double score;
  final List<String> sourceTags;
  final List<String> titleVariants;
  final List<String> artistVariants;
  final String searchQuery;

  bool contains(DateTime value) =>
      !value.isBefore(startAt) && value.isBefore(endAt);

  factory HitsScheduleTrack.fromJson(Map<String, dynamic> json) {
    final searchHints =
        (json['search_hints'] as Map?)?.cast<String, dynamic>() ??
        const <String, dynamic>{};
    final audioUrlValue = (json['audio_url'] as String?)?.trim() ?? '';
    final audioProviderValue =
        (json['audio_provider'] as String?)?.trim() ?? '';
    final providerTrackIdValue =
        (json['provider_track_id'] as String?)?.trim() ?? '';
    final coverUrlValue = (json['cover_url'] as String?)?.trim() ?? '';

    return HitsScheduleTrack(
      slot: (json['slot'] as num?)?.toInt() ?? 0,
      stationTrackId: (json['station_track_id'] as String? ?? '').trim(),
      window: (json['window'] as String? ?? '').trim(),
      startAt: _parseUtcDateTime(json['start_at']),
      endAt: _parseUtcDateTime(json['end_at']),
      duration: Duration(
        milliseconds: (json['duration_ms'] as num?)?.toInt() ?? 0,
      ),
      title: (json['title'] as String? ?? '').trim(),
      artist: (json['artist'] as String? ?? '').trim(),
      album: (json['album'] as String? ?? '').trim(),
      audioUrl: audioUrlValue.isEmpty ? null : Uri.tryParse(audioUrlValue),
      audioProvider: audioProviderValue,
      providerTrackId: providerTrackIdValue,
      coverUrl: coverUrlValue.isEmpty ? null : Uri.tryParse(coverUrlValue),
      score: (json['score'] as num?)?.toDouble() ?? 0,
      sourceTags: _parseStringList(json['source_tags']),
      titleVariants: _parseStringList(searchHints['title_variants']),
      artistVariants: _parseStringList(searchHints['artist_variants']),
      searchQuery: (searchHints['query'] as String? ?? '').trim(),
    );
  }
}

class HitsSchedule {
  const HitsSchedule({
    required this.schemaVersion,
    required this.stationId,
    required this.editionDate,
    required this.timezone,
    required this.generatedAt,
    required this.generatorVersion,
    required this.generationMode,
    required this.serviceWindows,
    required this.offAirWindows,
    required this.sourceSnapshots,
    required this.tracks,
  });

  final int schemaVersion;
  final String stationId;
  final String editionDate;
  final String timezone;
  final DateTime generatedAt;
  final String generatorVersion;
  final String generationMode;
  final List<HitsTimeWindow> serviceWindows;
  final List<HitsTimeWindow> offAirWindows;
  final List<HitsSourceSnapshot> sourceSnapshots;
  final List<HitsScheduleTrack> tracks;

  factory HitsSchedule.fromJson(Map<String, dynamic> json) {
    final tracks =
        (json['tracks'] as List? ?? const [])
            .whereType<Map>()
            .map(
              (item) =>
                  HitsScheduleTrack.fromJson(item.cast<String, dynamic>()),
            )
            .toList(growable: false)
          ..sort((a, b) => a.startAt.compareTo(b.startAt));

    return HitsSchedule(
      schemaVersion: (json['schema_version'] as num?)?.toInt() ?? 0,
      stationId: (json['station_id'] as String? ?? '').trim(),
      editionDate: (json['edition_date'] as String? ?? '').trim(),
      timezone: (json['timezone'] as String? ?? '').trim(),
      generatedAt: _parseUtcDateTime(json['generated_at']),
      generatorVersion: (json['generator_version'] as String? ?? '').trim(),
      generationMode: (json['generation_mode'] as String? ?? '').trim(),
      serviceWindows: _parseWindowList(json['service_windows']),
      offAirWindows: _parseWindowList(json['off_air_windows']),
      sourceSnapshots: _parseSourceSnapshotList(json['source_snapshot']),
      tracks: tracks,
    );
  }
}

List<HitsTimeWindow> _parseWindowList(Object? value) {
  return (value as List? ?? const [])
      .whereType<Map>()
      .map((item) => HitsTimeWindow.fromJson(item.cast<String, dynamic>()))
      .toList(growable: false);
}

List<HitsSourceSnapshot> _parseSourceSnapshotList(Object? value) {
  return (value as List? ?? const [])
      .whereType<Map>()
      .map((item) => HitsSourceSnapshot.fromJson(item.cast<String, dynamic>()))
      .toList(growable: false);
}

List<String> _parseStringList(Object? value) {
  return (value as List? ?? const [])
      .map((item) => item?.toString().trim() ?? '')
      .where((item) => item.isNotEmpty)
      .toList(growable: false);
}

DateTime _parseUtcDateTime(Object? value) {
  final raw = value?.toString().trim() ?? '';
  if (raw.isEmpty) {
    throw const FormatException('Missing UTC datetime value');
  }
  return DateTime.parse(raw).toUtc();
}
