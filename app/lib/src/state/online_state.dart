import 'package:flutter/foundation.dart';

import '../models/online_recommendation.dart';
import '../services/online_search_service.dart';

enum OnlineHomeStatus { idle, loading, ready, failed }

enum OnlineHomeRefreshResult { fresh, latestAvailable, failed }

enum OnlineSearchStatus { idle, searching, ready, failed }

enum OnlineAlbumDetailStatus { idle, loading, ready, failed }

@immutable
class OnlineAlbumDetailView {
  const OnlineAlbumDetailView({
    required this.status,
    required this.album,
    required this.tracks,
    required this.errorMessage,
  });

  static const empty = OnlineAlbumDetailView(
    status: OnlineAlbumDetailStatus.idle,
    album: null,
    tracks: <OnlineTrackCandidate>[],
    errorMessage: '',
  );

  final OnlineAlbumDetailStatus status;
  final OnlineAlbumCard? album;
  final List<OnlineTrackCandidate> tracks;
  final String errorMessage;

  OnlineAlbumDetailView copyWith({
    OnlineAlbumDetailStatus? status,
    OnlineAlbumCard? album,
    List<OnlineTrackCandidate>? tracks,
    String? errorMessage,
    bool clearError = false,
  }) {
    return OnlineAlbumDetailView(
      status: status ?? this.status,
      album: album ?? this.album,
      tracks: tracks ?? this.tracks,
      errorMessage: clearError ? '' : (errorMessage ?? this.errorMessage),
    );
  }
}

@immutable
class OnlineHomeView {
  const OnlineHomeView({
    required this.status,
    required this.data,
    required this.usedCache,
    required this.errorMessage,
    required this.recommendationsUnavailable,
    this.recommendationsPendingGeneration = false,
  });

  static const empty = OnlineHomeView(
    status: OnlineHomeStatus.idle,
    data: null,
    usedCache: false,
    errorMessage: '',
    recommendationsUnavailable: false,
    recommendationsPendingGeneration: false,
  );

  final OnlineHomeStatus status;
  final OnlineHomeData? data;
  final bool usedCache;
  final String errorMessage;
  final bool recommendationsUnavailable;
  final bool recommendationsPendingGeneration;

  OnlineHomeView copyWith({
    OnlineHomeStatus? status,
    OnlineHomeData? data,
    bool? usedCache,
    String? errorMessage,
    bool clearError = false,
    bool? recommendationsUnavailable,
    bool? recommendationsPendingGeneration,
  }) {
    return OnlineHomeView(
      status: status ?? this.status,
      data: data ?? this.data,
      usedCache: usedCache ?? this.usedCache,
      errorMessage: clearError ? '' : (errorMessage ?? this.errorMessage),
      recommendationsUnavailable:
          recommendationsUnavailable ?? this.recommendationsUnavailable,
      recommendationsPendingGeneration:
          recommendationsPendingGeneration ??
          this.recommendationsPendingGeneration,
    );
  }
}

@immutable
class OnlineSearchView {
  const OnlineSearchView({
    required this.query,
    required this.status,
    required this.results,
    required this.errorMessage,
  });

  static const empty = OnlineSearchView(
    query: '',
    status: OnlineSearchStatus.idle,
    results: <OnlineSearchResult>[],
    errorMessage: '',
  );

  final String query;
  final OnlineSearchStatus status;
  final List<OnlineSearchResult> results;
  final String errorMessage;

  OnlineSearchView copyWith({
    String? query,
    OnlineSearchStatus? status,
    List<OnlineSearchResult>? results,
    String? errorMessage,
    bool clearError = false,
  }) {
    return OnlineSearchView(
      query: query ?? this.query,
      status: status ?? this.status,
      results: results ?? this.results,
      errorMessage: clearError ? '' : (errorMessage ?? this.errorMessage),
    );
  }
}

@immutable
class OnlinePlaybackResolveStatus {
  const OnlinePlaybackResolveStatus({
    required this.resolvingTrackKey,
    required this.errorMessage,
  });

  static const empty = OnlinePlaybackResolveStatus(
    resolvingTrackKey: '',
    errorMessage: '',
  );

  final String resolvingTrackKey;
  final String errorMessage;

  bool get isResolving => resolvingTrackKey.isNotEmpty;

  OnlinePlaybackResolveStatus copyWith({
    String? resolvingTrackKey,
    String? errorMessage,
    bool clearError = false,
    bool clearResolvingTrack = false,
  }) {
    return OnlinePlaybackResolveStatus(
      resolvingTrackKey: clearResolvingTrack
          ? ''
          : (resolvingTrackKey ?? this.resolvingTrackKey),
      errorMessage: clearError ? '' : (errorMessage ?? this.errorMessage),
    );
  }
}

@immutable
class OnlineState {
  const OnlineState({
    this.home = OnlineHomeView.empty,
    this.search = OnlineSearchView.empty,
    this.resolve = OnlinePlaybackResolveStatus.empty,
    this.albumDetail = OnlineAlbumDetailView.empty,
  });

  final OnlineHomeView home;
  final OnlineSearchView search;
  final OnlinePlaybackResolveStatus resolve;
  final OnlineAlbumDetailView albumDetail;

  OnlineState copyWith({
    OnlineHomeView? home,
    OnlineSearchView? search,
    OnlinePlaybackResolveStatus? resolve,
    OnlineAlbumDetailView? albumDetail,
  }) {
    return OnlineState(
      home: home ?? this.home,
      search: search ?? this.search,
      resolve: resolve ?? this.resolve,
      albumDetail: albumDetail ?? this.albumDetail,
    );
  }
}
