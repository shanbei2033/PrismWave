import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../i18n/app_strings.dart';
import '../providers.dart';
import '../services/online_search_service.dart';
import '../state/library_state.dart';
import '../state/online_state.dart';
import 'components/prism_components.dart';
import 'online_home_panel.dart';
import 'prismwave_theme.dart';

class OnlineSearchPanel extends ConsumerStatefulWidget {
  const OnlineSearchPanel({super.key, required this.t});

  final AppStrings t;

  @override
  ConsumerState<OnlineSearchPanel> createState() => _OnlineSearchPanelState();
}

class _OnlineSearchPanelState extends ConsumerState<OnlineSearchPanel> {
  late final TextEditingController _controller;
  late final FocusNode _focusNode;

  @override
  void initState() {
    super.initState();
    final initialQuery = ref.read(onlineProvider).search.query;
    _controller = TextEditingController(text: initialQuery);
    _focusNode = FocusNode();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      _focusNode.requestFocus();
    });
  }

  @override
  void dispose() {
    _controller.dispose();
    _focusNode.dispose();
    super.dispose();
  }

  void _applyQuery(String value) {
    if (_controller.text != value) {
      _controller.value = TextEditingValue(
        text: value,
        selection: TextSelection.collapsed(offset: value.length),
      );
    }
    ref.read(onlineProvider.notifier).setSearchQuery(value);
  }

  void _submitSearch(String value) {
    final controller = ref.read(onlineProvider.notifier);
    controller.setSearchQuery(value);
    unawaited(controller.commitSearchHistory(value));
  }

  @override
  Widget build(BuildContext context) {
    final t = widget.t;
    final search = ref.watch(onlineProvider.select((s) => s.search));
    final library = ref.watch(libraryProvider);

    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SectionHeader(title: t.navSearch),
          const SizedBox(height: 14),
          _SearchTextField(
            controller: _controller,
            focusNode: _focusNode,
            placeholder: t.onlineSearchPlaceholder,
            onChanged: (value) =>
                ref.read(onlineProvider.notifier).setSearchQuery(value),
            onSubmitted: _submitSearch,
            onClear: () => _applyQuery(''),
          ),
          const SizedBox(height: 18),
          Expanded(
            child: search.query.trim().isEmpty
                ? _SearchHistoryList(
                    t: t,
                    history: search.history,
                    onSelect: _applyQuery,
                    onRemove: (value) => ref
                        .read(onlineProvider.notifier)
                        .removeSearchHistory(value),
                  )
                : _SearchResults(
                    t: t,
                    state: search,
                    library: library,
                    onPlayResult: _playResult,
                  ),
          ),
        ],
      ),
    );
  }

  Future<void> _playResult(OnlineSearchResult result) async {
    final controller = ref.read(onlineProvider.notifier);
    await controller.commitSearchHistory(ref.read(onlineProvider).search.query);
    if (result.source == OnlineSearchResultSource.local) {
      // Build a local-only context: every local hit in the current results.
      final results = ref.read(onlineProvider).search.results;
      final localContext = results
          .where((r) => r.source == OnlineSearchResultSource.local)
          .map((r) => r.localTrack!)
          .toList(growable: false);
      await controller.playLocalTrack(
        track: result.localTrack!,
        queue: localContext,
      );
    } else {
      final results = ref.read(onlineProvider).search.results;
      final onlineContext = results
          .where((r) => r.source == OnlineSearchResultSource.online)
          .map((r) => r.onlineHit!)
          .toList(growable: false);
      await controller.playSearchHit(
        hit: result.onlineHit!,
        contextHits: onlineContext,
      );
    }
  }
}

class _SearchTextField extends StatelessWidget {
  const _SearchTextField({
    required this.controller,
    required this.focusNode,
    required this.placeholder,
    required this.onChanged,
    required this.onSubmitted,
    required this.onClear,
  });

  final TextEditingController controller;
  final FocusNode focusNode;
  final String placeholder;
  final ValueChanged<String> onChanged;
  final ValueChanged<String> onSubmitted;
  final VoidCallback onClear;

  @override
  Widget build(BuildContext context) {
    return PrismSearchBox(
      controller: controller,
      focusNode: focusNode,
      hintText: placeholder,
      onChanged: onChanged,
      onSubmitted: onSubmitted,
      onClear: onClear,
    );
  }
}

class _SearchHistoryList extends StatelessWidget {
  const _SearchHistoryList({
    required this.t,
    required this.onSelect,
    required this.onRemove,
    required this.history,
  });

  final AppStrings t;
  final List<String> history;
  final ValueChanged<String> onSelect;
  final ValueChanged<String> onRemove;

  @override
  Widget build(BuildContext context) {
    if (history.isEmpty) {
      return Center(
        child: Text(
          t.onlineSearchHistoryEmpty,
          style: TextStyle(color: Colors.white.withValues(alpha: 0.55)),
        ),
      );
    }
    return SingleChildScrollView(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SectionHeader(title: t.onlineSearchHistory),
          const SizedBox(height: 14),
          Column(
            children: [
              for (final item in history) ...[
                _SearchHistoryTile(
                  value: item,
                  removeTooltip: t.onlineSearchHistoryRemove,
                  onTap: () => onSelect(item),
                  onRemove: () => onRemove(item),
                ),
                const SizedBox(height: 8),
              ],
            ],
          ),
        ],
      ),
    );
  }
}

class _SearchHistoryTile extends StatelessWidget {
  const _SearchHistoryTile({
    required this.value,
    required this.removeTooltip,
    required this.onTap,
    required this.onRemove,
  });

  final String value;
  final String removeTooltip;
  final VoidCallback onTap;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    return HoverGlassCard(
      onTap: onTap,
      radius: 14,
      padding: EdgeInsets.zero,
      child: Row(
        children: [
          Expanded(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(14, 11, 10, 11),
              child: Row(
                children: [
                  Icon(
                    Icons.history_rounded,
                    size: 17,
                    color: PrismWaveTheme.textMuted.withValues(alpha: 0.76),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      value,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        color: PrismWaveTheme.textSecondary,
                        fontSize: 13.5,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
          Tooltip(
            message: removeTooltip,
            child: IconButton(
              onPressed: onRemove,
              icon: const Icon(Icons.close_rounded, size: 17),
              color: PrismWaveTheme.textMuted.withValues(alpha: 0.74),
              splashRadius: 18,
            ),
          ),
        ],
      ),
    );
  }
}

class _SearchResults extends StatelessWidget {
  const _SearchResults({
    required this.t,
    required this.state,
    required this.library,
    required this.onPlayResult,
  });

  final AppStrings t;
  final OnlineSearchView state;
  final LibraryState library;
  final ValueChanged<OnlineSearchResult> onPlayResult;

  @override
  Widget build(BuildContext context) {
    if (state.status == OnlineSearchStatus.searching) {
      return Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        mainAxisSize: MainAxisSize.min,
        children: [
          const SizedBox(
            width: 18,
            height: 18,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
          const SizedBox(width: 12),
          Text(t.onlineSearching),
        ],
      );
    }
    if (state.status == OnlineSearchStatus.failed) {
      return Center(
        child: Text(
          '${t.onlineSearchFailed}\n${state.errorMessage}',
          textAlign: TextAlign.center,
          style: TextStyle(color: Colors.white.withValues(alpha: 0.7)),
        ),
      );
    }
    if (state.results.isEmpty) {
      return Center(
        child: Text(
          t.onlineSearchEmpty,
          style: TextStyle(color: Colors.white.withValues(alpha: 0.55)),
        ),
      );
    }
    return ListView.separated(
      padding: const EdgeInsets.only(right: 4, bottom: 18),
      itemCount: state.results.length,
      separatorBuilder: (_, _) => const SizedBox(height: 8),
      itemBuilder: (context, index) {
        final row = state.results[index];
        return _SearchResultTile(
          t: t,
          result: row,
          library: library,
          onTap: () => onPlayResult(row),
        );
      },
    );
  }
}

class _SearchResultTile extends ConsumerWidget {
  const _SearchResultTile({
    required this.t,
    required this.result,
    required this.library,
    required this.onTap,
  });

  final AppStrings t;
  final OnlineSearchResult result;
  final LibraryState library;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final isLocal = result.source == OnlineSearchResultSource.local;
    final track = result.localTrack;
    final duration = isLocal && track != null
        ? library.durationOf(track)
        : Duration(milliseconds: result.displayDurationMs);
    final coverBytes = isLocal && track != null
        ? library.coverBytesOf(track)
        : null;
    final album = result.displayAlbum.trim();
    final artist = result.displayArtist.trim();
    final artistAlbum = [
      if (artist.isNotEmpty) artist,
      if (album.isNotEmpty) album,
    ].join(' - ');
    final meta = [
      result.displayProvider,
      if ((duration?.inMilliseconds ?? 0) > 0) _formatDuration(duration!),
    ].join(' - ');

    return HoverGlassCard(
      onTap: onTap,
      radius: 14,
      padding: const EdgeInsets.fromLTRB(10, 9, 12, 9),
      child: Row(
        children: [
          _SearchResultCover(
            isLocal: isLocal,
            coverPathOrUrl: result.displayCoverUrl,
            coverBytes: coverBytes,
            fallbackIcon: isLocal ? Icons.sd_storage_rounded : Icons.cloud_rounded,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  result.displayTitle,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    color: PrismWaveTheme.textPrimary,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 3),
                Text(
                  artistAlbum.isEmpty ? 'Unknown Artist' : artistAlbum,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: PrismWaveTheme.captionStyle(fontSize: 13),
                ),
              ],
            ),
          ),
          const SizedBox(width: 12),
          Text(
            meta,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: PrismWaveTheme.captionStyle(fontSize: 12, alpha: 0.62),
          ),
        ],
      ),
    );
  }
}

class _SearchResultCover extends ConsumerWidget {
  const _SearchResultCover({
    required this.isLocal,
    required this.coverPathOrUrl,
    required this.coverBytes,
    required this.fallbackIcon,
  });

  final bool isLocal;
  final String? coverPathOrUrl;
  final Uint8List? coverBytes;
  final IconData fallbackIcon;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(8),
      child: SizedBox(width: 48, height: 48, child: _buildImage(ref)),
    );
  }

  Widget _buildImage(WidgetRef ref) {
    if (coverBytes != null && coverBytes!.isNotEmpty) {
      return Image.memory(
        coverBytes!,
        fit: BoxFit.cover,
        gaplessPlayback: true,
        errorBuilder: (_, _, _) => _fallback(),
      );
    }

    final value = coverPathOrUrl?.trim() ?? '';
    if (value.isEmpty) {
      return _fallback();
    }

    if (value.startsWith('http://') || value.startsWith('https://')) {
      return OnlineCoverImage(
        coverCache: ref.read(onlineCoverCacheProvider),
        cacheKey: value,
        coverUrl: value,
      );
    }

    if (isLocal && File(value).existsSync()) {
      return Image.file(
        File(value),
        fit: BoxFit.cover,
        errorBuilder: (_, _, _) => _fallback(),
      );
    }

    return _fallback();
  }

  Widget _fallback() {
    return Container(
      color: Colors.white.withValues(alpha: 0.08),
      alignment: Alignment.center,
      child: Icon(
        fallbackIcon,
        size: 20,
        color: Colors.white.withValues(alpha: 0.56),
      ),
    );
  }
}

String _formatDuration(Duration duration) {
  final totalSeconds = duration.inSeconds;
  if (totalSeconds <= 0) return '';
  final hours = totalSeconds ~/ 3600;
  final minutes = (totalSeconds % 3600) ~/ 60;
  final seconds = totalSeconds % 60;
  if (hours > 0) {
    return '$hours:${minutes.toString().padLeft(2, '0')}:'
        '${seconds.toString().padLeft(2, '0')}';
  }
  return '$minutes:${seconds.toString().padLeft(2, '0')}';
}
