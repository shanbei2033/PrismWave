import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../prismwave_theme.dart';

class SectionHeader extends StatelessWidget {
  const SectionHeader({
    super.key,
    required this.title,
    this.subtitle,
    this.actionLabel,
    this.onAction,
  });

  final String title;
  final String? subtitle;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.end,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: PrismWaveTheme.sectionTitleStyle()),
              if ((subtitle ?? '').trim().isNotEmpty) ...[
                const SizedBox(height: 4),
                Text(
                  subtitle!,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: PrismWaveTheme.captionStyle(),
                ),
              ],
            ],
          ),
        ),
        if (actionLabel != null && onAction != null)
          TextButton.icon(
            onPressed: onAction,
            style: PrismWaveTheme.rectangularButtonStyle(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            ),
            label: Text(actionLabel!),
            icon: const Icon(Icons.chevron_right_rounded, size: 18),
          ),
      ],
    );
  }
}

class IconGlassButton extends StatelessWidget {
  const IconGlassButton({
    super.key,
    required this.icon,
    required this.tooltip,
    this.onPressed,
    this.selected = false,
    this.size = 40,
    this.iconSize = 20,
  });

  final IconData icon;
  final String tooltip;
  final VoidCallback? onPressed;
  final bool selected;
  final double size;
  final double iconSize;

  @override
  Widget build(BuildContext context) {
    return Tooltip(
      message: tooltip,
      child: SizedBox(
        width: size,
        height: size,
        child: TextButton(
          onPressed: onPressed,
          style: PrismWaveTheme.iconButtonStyle(selected: selected).copyWith(
            fixedSize: WidgetStatePropertyAll(Size(size, size)),
            minimumSize: WidgetStatePropertyAll(Size(size, size)),
          ),
          child: Icon(icon, size: iconSize),
        ),
      ),
    );
  }
}

class PrismSearchBox extends StatelessWidget {
  const PrismSearchBox({
    super.key,
    required this.controller,
    required this.hintText,
    this.focusNode,
    this.onChanged,
    this.onSubmitted,
    this.onClear,
  });

  final TextEditingController controller;
  final FocusNode? focusNode;
  final String hintText;
  final ValueChanged<String>? onChanged;
  final ValueChanged<String>? onSubmitted;
  final VoidCallback? onClear;

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<TextEditingValue>(
      valueListenable: controller,
      builder: (context, value, _) {
        final showClear = value.text.isNotEmpty && onClear != null;
        return TextField(
          controller: controller,
          focusNode: focusNode,
          onChanged: onChanged,
          onSubmitted: onSubmitted,
          textInputAction: TextInputAction.search,
          decoration: PrismWaveTheme.searchInputDecoration(
            hintText: hintText,
            prefixIcon: const Icon(Icons.search_rounded, size: 20),
            suffixIcon: showClear
                ? IconButton(
                    tooltip: 'Clear',
                    onPressed: onClear,
                    icon: const Icon(Icons.close_rounded, size: 18),
                  )
                : null,
          ),
        );
      },
    );
  }
}

class HoverGlassCard extends StatefulWidget {
  const HoverGlassCard({
    super.key,
    required this.child,
    this.onTap,
    this.radius = PrismWaveTheme.cardRadius,
    this.padding = EdgeInsets.zero,
    this.selected = false,
  });

  final Widget child;
  final VoidCallback? onTap;
  final double radius;
  final EdgeInsetsGeometry padding;
  final bool selected;

  @override
  State<HoverGlassCard> createState() => _HoverGlassCardState();
}

class PrismMetricPill extends StatelessWidget {
  const PrismMetricPill({
    super.key,
    required this.icon,
    required this.label,
    required this.value,
    this.compact = false,
  });

  final IconData icon;
  final String label;
  final String value;
  final bool compact;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.symmetric(
        horizontal: compact ? 10 : 12,
        vertical: compact ? 8 : 10,
      ),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(PrismWaveTheme.tileRadius),
        color: Colors.white.withValues(alpha: 0.045),
        border: Border.all(color: Colors.white.withValues(alpha: 0.07)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: compact ? 15 : 17, color: PrismWaveTheme.accentSoft),
          SizedBox(width: compact ? 7 : 9),
          Flexible(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  value,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    color: PrismWaveTheme.textPrimary,
                    fontSize: compact ? 12 : 13.5,
                    fontWeight: FontWeight.w800,
                    height: 1,
                  ),
                ),
                const SizedBox(height: 3),
                Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: PrismWaveTheme.captionStyle(
                    fontSize: compact ? 10.5 : 11.5,
                    alpha: 0.68,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class PrismMiniCover extends StatelessWidget {
  const PrismMiniCover({
    super.key,
    this.coverBytes,
    this.coverPath,
    this.size = 48,
    this.radius = 12,
    this.icon = Icons.music_note_rounded,
  });

  final Uint8List? coverBytes;
  final String? coverPath;
  final double size;
  final double radius;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final bytes = coverBytes;
    final path = coverPath;
    Widget child;
    if (bytes != null && bytes.isNotEmpty) {
      child = Image.memory(bytes, fit: BoxFit.cover);
    } else if (path != null && path.isNotEmpty && File(path).existsSync()) {
      child = Image.file(File(path), fit: BoxFit.cover);
    } else {
      child = DecoratedBox(
        decoration: BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: [
              PrismWaveTheme.accent.withValues(alpha: 0.28),
              PrismWaveTheme.surfaceStrong.withValues(alpha: 0.92),
            ],
          ),
        ),
        child: Icon(
          icon,
          size: size * 0.44,
          color: PrismWaveTheme.textPrimary.withValues(alpha: 0.74),
        ),
      );
    }

    return SizedBox(
      width: size,
      height: size,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(radius),
        child: child,
      ),
    );
  }
}

class PrismMediaListTile extends StatefulWidget {
  const PrismMediaListTile({
    super.key,
    required this.cover,
    required this.title,
    required this.subtitle,
    this.meta,
    this.leadingMeta,
    this.trailing,
    this.onTap,
    this.onSecondaryTapDown,
    this.selected = false,
    this.height = 64,
    this.radius = 14,
  });

  final Widget cover;
  final String title;
  final String subtitle;
  final String? meta;
  final String? leadingMeta;
  final Widget? trailing;
  final VoidCallback? onTap;
  final GestureTapDownCallback? onSecondaryTapDown;
  final bool selected;
  final double height;
  final double radius;

  @override
  State<PrismMediaListTile> createState() => _PrismMediaListTileState();
}

class _PrismMediaListTileState extends State<PrismMediaListTile> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final active = widget.selected || _hovered;
    return MouseRegion(
      cursor: widget.onTap == null
          ? MouseCursor.defer
          : SystemMouseCursors.click,
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onSecondaryTapDown: widget.onSecondaryTapDown,
        child: AnimatedContainer(
          duration: PrismWaveTheme.fastMotion,
          curve: Curves.easeOutCubic,
          height: widget.height,
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(widget.radius),
            gradient: widget.selected
                ? LinearGradient(
                    begin: Alignment.centerLeft,
                    end: Alignment.centerRight,
                    colors: [
                      PrismWaveTheme.accent.withValues(alpha: 0.24),
                      PrismWaveTheme.accentDeep.withValues(alpha: 0.10),
                      Colors.white.withValues(alpha: 0.035),
                    ],
                  )
                : null,
            color: widget.selected
                ? null
                : Colors.white.withValues(alpha: active ? 0.062 : 0.024),
            border: Border.all(
              color: Colors.white.withValues(alpha: active ? 0.13 : 0.055),
            ),
          ),
          child: Material(
            color: Colors.transparent,
            borderRadius: BorderRadius.circular(widget.radius),
            child: InkWell(
              onTap: widget.onTap,
              borderRadius: BorderRadius.circular(widget.radius),
              child: Padding(
                padding: const EdgeInsets.fromLTRB(10, 8, 10, 8),
                child: Row(
                  children: [
                    if (widget.leadingMeta != null) ...[
                      SizedBox(
                        width: 34,
                        child: Text(
                          widget.leadingMeta!,
                          textAlign: TextAlign.center,
                          style: PrismWaveTheme.captionStyle(
                            fontSize: 12,
                            alpha: widget.selected ? 0.96 : 0.62,
                          ),
                        ),
                      ),
                      const SizedBox(width: 6),
                    ],
                    widget.cover,
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            widget.title,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: PrismWaveTheme.mediaTitleStyle(),
                          ),
                          const SizedBox(height: 4),
                          Text(
                            widget.subtitle,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: PrismWaveTheme.captionStyle(
                              fontSize: 12.5,
                              alpha: 0.70,
                            ),
                          ),
                        ],
                      ),
                    ),
                    if (widget.meta != null) ...[
                      const SizedBox(width: 12),
                      SizedBox(
                        width: 72,
                        child: Text(
                          widget.meta!,
                          textAlign: TextAlign.right,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: PrismWaveTheme.captionStyle(
                            fontSize: 12,
                            alpha: 0.76,
                          ),
                        ),
                      ),
                    ],
                    if (widget.trailing != null) ...[
                      const SizedBox(width: 8),
                      widget.trailing!,
                    ],
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class PrismFeatureTile extends StatelessWidget {
  const PrismFeatureTile({
    super.key,
    required this.icon,
    required this.title,
    required this.subtitle,
    this.onTap,
    this.enabled = true,
    this.selected = false,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final VoidCallback? onTap;
  final bool enabled;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    return Opacity(
      opacity: enabled ? 1 : 0.52,
      child: HoverGlassCard(
        selected: selected,
        onTap: enabled ? onTap : null,
        radius: PrismWaveTheme.tileRadius,
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 12),
        child: Row(
          children: [
            Container(
              width: 38,
              height: 38,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: selected
                    ? Colors.white.withValues(alpha: 0.16)
                    : PrismWaveTheme.accent.withValues(alpha: 0.16),
                border: Border.all(
                  color: Colors.white.withValues(alpha: selected ? 0.22 : 0.08),
                ),
              ),
              child: Icon(icon, size: 20, color: PrismWaveTheme.textPrimary),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: PrismWaveTheme.textPrimary,
                      fontWeight: FontWeight.w800,
                      fontSize: 13.5,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    subtitle,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: PrismWaveTheme.captionStyle(fontSize: 11.5),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class PrismEmptyState extends StatelessWidget {
  const PrismEmptyState({
    super.key,
    required this.icon,
    required this.title,
    this.subtitle,
  });

  final IconData icon;
  final String title;
  final String? subtitle;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Container(
        constraints: const BoxConstraints(maxWidth: 340),
        padding: const EdgeInsets.all(22),
        decoration: PrismWaveTheme.glassDecoration(
          radius: PrismWaveTheme.panelRadius,
          alpha: 0.52,
          borderAlpha: 0.08,
          withShadow: false,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 32, color: PrismWaveTheme.textMuted),
            const SizedBox(height: 12),
            Text(
              title,
              textAlign: TextAlign.center,
              style: PrismWaveTheme.sectionTitleStyle(fontSize: 17),
            ),
            if ((subtitle ?? '').trim().isNotEmpty) ...[
              const SizedBox(height: 8),
              Text(
                subtitle!,
                textAlign: TextAlign.center,
                style: PrismWaveTheme.captionStyle(fontSize: 13),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _HoverGlassCardState extends State<HoverGlassCard> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final active = widget.selected || _hovered;
    return MouseRegion(
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      cursor: widget.onTap == null
          ? MouseCursor.defer
          : SystemMouseCursors.click,
      child: AnimatedContainer(
        duration: PrismWaveTheme.fastMotion,
        curve: Curves.easeOutCubic,
        decoration: BoxDecoration(
          gradient: widget.selected
              ? PrismWaveTheme.accentGradient
              : PrismWaveTheme.cardGradient(alpha: active ? 1.05 : 0.72),
          borderRadius: BorderRadius.circular(widget.radius),
          border: Border.all(
            color: Colors.white.withValues(alpha: active ? 0.15 : 0.075),
          ),
          boxShadow: active
              ? PrismWaveTheme.cardShadow(alpha: widget.selected ? 0.24 : 0.16)
              : null,
        ),
        child: Material(
          color: Colors.transparent,
          borderRadius: BorderRadius.circular(widget.radius),
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            onTap: widget.onTap,
            borderRadius: BorderRadius.circular(widget.radius),
            child: Padding(padding: widget.padding, child: widget.child),
          ),
        ),
      ),
    );
  }
}
