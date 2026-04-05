import 'dart:convert';
import 'dart:io';

const String kCurrentReleaseVersion = 'R011';

class ReleaseUpdateInfo {
  const ReleaseUpdateInfo({
    required this.version,
    required this.releasePageUrl,
    required this.installerUrl,
  });

  final String version;
  final String releasePageUrl;
  final String installerUrl;
}

class ReleaseUpdateService {
  static const String repositoryOwner = 'shanbei2033';
  static const String repositoryName = 'PrismWave';
  static const String releasesPageUrl =
      'https://github.com/$repositoryOwner/$repositoryName/releases';
  static const String latestReleaseApiUrl =
      'https://api.github.com/repos/$repositoryOwner/$repositoryName/releases/latest';

  Future<ReleaseUpdateInfo> fetchLatestRelease() async {
    final client = HttpClient();
    client.connectionTimeout = const Duration(seconds: 10);
    try {
      final request = await client.getUrl(Uri.parse(latestReleaseApiUrl));
      request.headers.set(HttpHeaders.acceptHeader, 'application/vnd.github+json');
      request.headers.set(HttpHeaders.userAgentHeader, 'PrismWave/$kCurrentReleaseVersion');
      request.headers.set('X-GitHub-Api-Version', '2022-11-28');

      final response = await request.close();
      final responseBody = await utf8.decoder.bind(response).join();

      if (response.statusCode != 200) {
        throw HttpException(
          'GitHub release request failed (${response.statusCode})',
          uri: Uri.parse(latestReleaseApiUrl),
        );
      }

      final json = jsonDecode(responseBody);
      if (json is! Map<String, dynamic>) {
        throw const FormatException('Unexpected release payload');
      }

      final tagName = (json['tag_name'] as String? ?? '').trim();
      final htmlUrl = (json['html_url'] as String? ?? '').trim();
      final assets = json['assets'];

      if (tagName.isEmpty || htmlUrl.isEmpty) {
        throw const FormatException('Release metadata is incomplete');
      }

      return ReleaseUpdateInfo(
        version: tagName,
        releasePageUrl: htmlUrl,
        installerUrl: _selectInstallerUrl(assets) ?? htmlUrl,
      );
    } finally {
      client.close(force: true);
    }
  }

  bool isRemoteNewer(String remoteVersion, String currentVersion) {
    final normalizedRemote = remoteVersion.trim();
    final normalizedCurrent = currentVersion.trim();
    if (normalizedRemote.isEmpty || normalizedCurrent.isEmpty) return false;

    final remoteParsed = _parseReleaseVersion(normalizedRemote);
    final currentParsed = _parseReleaseVersion(normalizedCurrent);

    if (remoteParsed != null && currentParsed != null) {
      if (remoteParsed.$1 != currentParsed.$1) {
        return remoteParsed.$1 > currentParsed.$1;
      }
      return remoteParsed.$2 > currentParsed.$2;
    }

    return normalizedRemote.toLowerCase() != normalizedCurrent.toLowerCase();
  }

  (int, int)? _parseReleaseVersion(String version) {
    final match = RegExp(
      r'^R(\d+)(?:_fix(\d+))?$',
      caseSensitive: false,
    ).firstMatch(version);
    if (match == null) return null;

    final major = int.tryParse(match.group(1) ?? '');
    final fix = int.tryParse(match.group(2) ?? '0') ?? 0;
    if (major == null) return null;
    return (major, fix);
  }

  String? _selectInstallerUrl(Object? assetsValue) {
    if (assetsValue is! List) return null;

    String? fallbackExeUrl;
    for (final asset in assetsValue) {
      if (asset is! Map<String, dynamic>) continue;
      final name = (asset['name'] as String? ?? '').toLowerCase();
      final url = (asset['browser_download_url'] as String? ?? '').trim();
      if (url.isEmpty) continue;
      if (!url.toLowerCase().endsWith('.exe')) continue;
      if (name.contains('setup')) return url;
      fallbackExeUrl ??= url;
    }

    return fallbackExeUrl;
  }
}
