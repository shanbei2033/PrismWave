import 'dart:convert';
import 'dart:io';

class QuoteService {
  QuoteService();

  static const String _endpoint =
      'https://v1.hitokoto.cn/?encode=json&min_length=10&max_length=36&c=d&c=e&c=k';

  final HttpClient _httpClient = HttpClient()
    ..connectionTimeout = const Duration(seconds: 6);

  Future<String?> fetchQuote() async {
    final uri = Uri.parse(_endpoint);

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
