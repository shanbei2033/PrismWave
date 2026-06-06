import 'dart:convert';

import 'package:fast_gbk/fast_gbk.dart';

/// Repairs common mojibake seen from legacy Chinese music endpoints and keeps
/// online metadata suitable for display and provider fallback search.
String cleanOnlineText(Object? value) {
  final raw = value?.toString().trim() ?? '';
  if (raw.isEmpty) return '';

  final candidates = <String>{raw};
  if (_looksMojibake(raw)) {
    candidates.add(_tryDecode(raw, latin1.encode, utf8.decode));
    candidates.add(_tryDecode(raw, gbk.encode, utf8.decode));
  }

  var best = raw;
  var bestScore = _textQualityScore(raw);
  for (final candidate in candidates) {
    final cleaned = candidate.replaceAll(RegExp(r'\s+'), ' ').trim();
    if (cleaned.isEmpty) continue;
    final score = _textQualityScore(cleaned);
    if (score > bestScore) {
      best = cleaned;
      bestScore = score;
    }
  }
  return best;
}

String _tryDecode(
  String value,
  List<int> Function(String value) encode,
  String Function(List<int> bytes, {bool allowMalformed}) decode,
) {
  try {
    return decode(encode(value), allowMalformed: true);
  } catch (_) {
    return value;
  }
}

bool _looksMojibake(String value) {
  return value.contains('\uFFFD') ||
      RegExp(r'[ÃÂÄÅÆÇÈÉËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞßà-ÿ]').hasMatch(value) ||
      RegExp(r'(鏈€|浣虫|崯鍙|棣栭|涓|闈|鏃)').hasMatch(value);
}

int _textQualityScore(String value) {
  var score = 0;
  for (final rune in value.runes) {
    if (rune == 0xFFFD) {
      score -= 80;
    } else if (rune >= 0x4E00 && rune <= 0x9FFF) {
      score += 6;
    } else if ((rune >= 0x30 && rune <= 0x39) ||
        (rune >= 0x41 && rune <= 0x5A) ||
        (rune >= 0x61 && rune <= 0x7A)) {
      score += 2;
    } else if (rune == 0x20) {
      score += 1;
    }
  }
  score -=
      RegExp(r'[ÃÂÄÅÆÇÈÉËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞßà-ÿ]').allMatches(value).length * 8;
  return score;
}
