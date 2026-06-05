import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../i18n/app_strings.dart';
import '../models/online_recommendation.dart';
import '../providers.dart';
import '../services/online_search_service.dart';
import '../state/online_state.dart';

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
      // Trigger an empty load so the home recommendations service warms up.
      ref.read(onlineProvider.notifier).ensureHomeLoaded();
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

  @override
  Widget build(BuildContext context) {
    final t = widget.t;
    final search = ref.watch(onlineProvider.select((s) => s.search));
    final home = ref.watch(onlineProvider.select((s) => s.home));
    final tags = home.data?.tags ?? const <OnlineTag>[];

    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            t.navSearch,
            style: const TextStyle(fontSize: 24, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 14),
          _SearchTextField(
            controller: _controller,
            focusNode: _focusNode,
            placeholder: t.onlineSearchPlaceholder,
            onChanged: (value) =>
                ref.read(onlineProvider.notifier).setSearchQuery(value),
            onClear: () => _applyQuery(''),
          ),
          const SizedBox(height: 18),
          Expanded(
            child: search.query.trim().isEmpty
                ? _TagCloud(t: t, tags: tags, onSelect: _applyQuery)
                : _SearchResults(
                    t: t,
                    state: search,
                    onPlayResult: _playResult,
                  ),
          ),
        ],
      ),
    );
  }

  Future<void> _playResult(OnlineSearchResult result) async {
    final controller = ref.read(onlineProvider.notifier);
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
    required this.onClear,
  });

  final TextEditingController controller;
  final FocusNode focusNode;
  final String placeholder;
  final ValueChanged<String> onChanged;
  final VoidCallback onClear;

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<TextEditingValue>(
      valueListenable: controller,
      builder: (context, value, _) {
        final showClear = value.text.isNotEmpty;
        return TextField(
          controller: controller,
          focusNode: focusNode,
          onChanged: onChanged,
          textInputAction: TextInputAction.search,
          decoration: InputDecoration(
            hintText: placeholder,
            prefixIcon: const Icon(Icons.search_rounded),
            suffixIcon: showClear
                ? IconButton(
                    icon: const Icon(Icons.close_rounded),
                    onPressed: onClear,
                    tooltip: 'Clear',
                  )
                : null,
            filled: true,
            fillColor: Colors.white.withValues(alpha: 0.06),
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(12),
              borderSide: BorderSide.none,
            ),
            contentPadding: const EdgeInsets.symmetric(vertical: 14),
          ),
        );
      },
    );
  }
}

class _TagCloud extends StatelessWidget {
  const _TagCloud({
    required this.t,
    required this.tags,
    required this.onSelect,
  });

  final AppStrings t;
  final List<OnlineTag> tags;
  final ValueChanged<String> onSelect;

  @override
  Widget build(BuildContext context) {
    if (tags.isEmpty) {
      return Center(
        child: Text(
          t.onlinePopularTags,
          style: TextStyle(color: Colors.white.withValues(alpha: 0.55)),
        ),
      );
    }
    return SingleChildScrollView(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            t.onlinePopularTags,
            style: TextStyle(
              color: Colors.white.withValues(alpha: 0.7),
              fontSize: 14,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 10,
            runSpacing: 10,
            children: [
              for (final tag in tags)
                _TagChip(tag: tag, onTap: () => onSelect(tag.name)),
            ],
          ),
        ],
      ),
    );
  }
}

class _TagChip extends StatelessWidget {
  const _TagChip({required this.tag, required this.onTap});

  final OnlineTag tag;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final intensity = (0.18 + tag.weight * 0.4).clamp(0.18, 0.7);
    final fontSize = (13 + tag.weight * 4).clamp(13, 19).toDouble();
    return Material(
      color: Colors.white.withValues(alpha: intensity * 0.18),
      borderRadius: BorderRadius.circular(20),
      child: InkWell(
        borderRadius: BorderRadius.circular(20),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
          child: Text(
            tag.name,
            style: TextStyle(
              color: Colors.white.withValues(alpha: 0.78 + intensity * 0.2),
              fontSize: fontSize,
              fontWeight: FontWeight.w500,
            ),
          ),
        ),
      ),
    );
  }
}

class _SearchResults extends StatelessWidget {
  const _SearchResults({
    required this.t,
    required this.state,
    required this.onPlayResult,
  });

  final AppStrings t;
  final OnlineSearchView state;
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
      itemCount: state.results.length,
      separatorBuilder: (_, _) => const Divider(height: 1, thickness: 0.4),
      itemBuilder: (context, index) {
        final row = state.results[index];
        return _SearchResultTile(
          t: t,
          result: row,
          onTap: () => onPlayResult(row),
        );
      },
    );
  }
}

class _SearchResultTile extends StatelessWidget {
  const _SearchResultTile({
    required this.t,
    required this.result,
    required this.onTap,
  });

  final AppStrings t;
  final OnlineSearchResult result;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final isLocal = result.source == OnlineSearchResultSource.local;
    return ListTile(
      leading: CircleAvatar(
        radius: 18,
        backgroundColor: Colors.white.withValues(alpha: 0.08),
        child: Icon(
          isLocal ? Icons.sd_storage_rounded : Icons.cloud_rounded,
          size: 18,
        ),
      ),
      title: Text(
        result.displayTitle,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
      ),
      subtitle: Text(
        result.displayArtist,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: TextStyle(color: Colors.white.withValues(alpha: 0.65)),
      ),
      onTap: onTap,
    );
  }
}
