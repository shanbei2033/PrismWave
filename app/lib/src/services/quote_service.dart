import 'dart:convert';
import 'dart:io';

import '../models/app_language.dart';

class QuoteService {
  QuoteService();

  static const String _zhEndpoint =
      'https://v1.hitokoto.cn/?encode=json&min_length=10&max_length=36&c=d&c=e&c=k';
  static const String _enEndpoint = 'https://zenquotes.io/api/random';

  final HttpClient _httpClient = HttpClient()
    ..connectionTimeout = const Duration(seconds: 6);

  Future<String?> fetchQuote({required AppLanguage language}) async {
    return switch (language) {
      AppLanguage.enUs => _fetchEnglishQuote(),
      AppLanguage.zhCn || AppLanguage.zhTw => _fetchChineseQuote(),
    };
  }

  String fallbackQuote({required AppLanguage language}) {
    return switch (language) {
      AppLanguage.enUs =>
        'Stay patient with the process. Quiet progress still counts.',
      AppLanguage.zhCn => '慢一点也没关系，稳定向前也是前进。',
      AppLanguage.zhTw => '慢一點也沒關係，穩定向前也是前進。',
    };
  }

  Future<String?> _fetchChineseQuote() async {
    final uri = Uri.parse(_zhEndpoint);

    try {
      final request = await _httpClient.getUrl(uri);
      request.headers.set(HttpHeaders.acceptHeader, 'application/json');
      request.headers.set(
        HttpHeaders.userAgentHeader,
        'PrismWave/1.0.0 (+https://github.com/shanbei2033/PrismWave)',
      );
      final response = await request.close();
      if (response.statusCode < 200 || response.statusCode >= 300) {
        return null;
      }

      final bytes = <int>[];
      await for (final chunk in response) {
        bytes.addAll(chunk);
      }

      final raw = jsonDecode(utf8.decode(bytes, allowMalformed: true));
      if (raw is! Map<String, dynamic>) return null;

      final hitokoto = raw['hitokoto']?.toString().trim() ?? '';
      final fromWho = raw['from_who']?.toString().trim() ?? '';
      final from = raw['from']?.toString().trim() ?? '';
      final type = raw['type']?.toString().trim() ?? '';

      if (type == 'a' || type == 'b' || type == 'c') {
        return null;
      }

      final sourceText = [fromWho, from]
          .where((item) => item.isNotEmpty)
          .join(' | ');
      final candidate = sourceText.isEmpty
          ? hitokoto
          : '$hitokoto  -  $sourceText';

      if (!_isAllowed(candidate)) return null;
      return candidate;
    } catch (_) {
      return null;
    }
  }

  Future<String?> _fetchEnglishQuote() async {
    final uri = Uri.parse(_enEndpoint);

    try {
      final request = await _httpClient.getUrl(uri);
      request.headers.set(HttpHeaders.acceptHeader, 'application/json');
      request.headers.set(
        HttpHeaders.userAgentHeader,
        'PrismWave/1.0.0 (+https://github.com/shanbei2033/PrismWave)',
      );
      final response = await request.close();
      if (response.statusCode < 200 || response.statusCode >= 300) {
        return null;
      }

      final bytes = <int>[];
      await for (final chunk in response) {
        bytes.addAll(chunk);
      }

      final raw = jsonDecode(utf8.decode(bytes, allowMalformed: true));
      if (raw is! List || raw.isEmpty) return null;
      final first = raw.first;
      if (first is! Map) return null;

      final quote = first['q']?.toString().trim() ?? '';
      final author = first['a']?.toString().trim() ?? '';
      if (quote.isEmpty) return null;

      final candidate = author.isEmpty ? quote : '$quote  -  $author';
      if (!_isAllowed(candidate)) return null;
      return candidate;
    } catch (_) {
      return null;
    }
  }

  bool _isAllowed(String quote) {
    final normalized = quote.toLowerCase();
    if (normalized.isEmpty) return false;

    const blocked = <String>[
      'anime',
      'manga',
      'otaku',
      'comic',
      'animation',
      'cartoon',
    ];

    for (final keyword in blocked) {
      if (normalized.contains(keyword)) {
        return false;
      }
    }
    return true;
  }
}
