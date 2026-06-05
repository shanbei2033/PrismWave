import 'dart:convert';
import 'dart:io';

import 'package:crypto/crypto.dart';

// Centralized NetEase Cloud Music public API endpoints used by the online
// home page. These are the same endpoints music.163.com itself serves; we
// avoid the third-party NeteaseCloudMusicApi reverse proxy to keep the app
// self-contained.

const String kNeteaseHost = 'music.163.com';

const Map<String, String> kNeteaseHeaders = <String, String>{
  HttpHeaders.userAgentHeader: 'Mozilla/5.0 PrismWave/1.0.0',
  HttpHeaders.refererHeader: 'https://music.163.com/',
  HttpHeaders.acceptHeader: 'application/json',
};

const String kNeteaseDefaultPlaybackReferer = 'https://music.163.com/';

/// `/api/toplist` — full list of charts. Each entry includes id + coverImgUrl.
Uri neteaseToplistUri() => Uri.https(kNeteaseHost, '/api/toplist');

/// `/api/v6/playlist/detail` — playlist metadata + first [n] tracks inline.
/// `n` controls how many tracks are inlined (server caps at a few hundred).
Uri neteasePlaylistDetailUri({required int playlistId, required int n}) {
  return Uri.https(kNeteaseHost, '/api/v6/playlist/detail', <String, String>{
    'id': '$playlistId',
    'n': '$n',
  });
}

/// `/api/playlist/list` — search playlists by category. We use it to find a
/// hot playlist for each music style, then call playlistDetail to get tracks.
Uri neteasePlaylistByCategoryUri({
  required String category,
  int limit = 1,
  String order = 'hot',
}) {
  return Uri.https(kNeteaseHost, '/api/playlist/list', <String, String>{
    'cat': category,
    'order': order,
    'limit': '$limit',
  });
}

/// `/api/personalized/newsong` — global personalized new-song list. Each
/// entry has id, name, picUrl, and an embedded `song` with full metadata.
Uri neteasePersonalizedNewSongUri({int limit = 24}) {
  return Uri.https(kNeteaseHost, '/api/personalized/newsong', <String, String>{
    'limit': '$limit',
  });
}

/// `/api/search/get` — public web search endpoint. `type=1` searches songs.
Uri neteaseSongSearchUri({required String query, int limit = 5}) {
  return Uri.https(kNeteaseHost, '/api/search/get', <String, String>{
    's': query,
    'type': '1',
    'limit': '$limit',
  });
}

/// `/api/album/new` — new album releases. `area` is one of ALL/ZH/EA/KR/JP.
Uri neteaseNewAlbumsUri({String area = 'ALL', int limit = 12, int offset = 0}) {
  return Uri.https(kNeteaseHost, '/api/album/new', <String, String>{
    'area': area,
    'limit': '$limit',
    'offset': '$offset',
  });
}

/// `/api/v1/album/{id}` — album detail with full song list.
Uri neteaseAlbumDetailUri({required int albumId}) {
  return Uri.https(kNeteaseHost, '/api/v1/album/$albumId');
}

/// NetEase serves cover images on `p1.music.126.net` / `p2.music.126.net`,
/// often as `http://`. Force HTTPS so the cover loader doesn't trip platform
/// security defaults.
String? upgradeCoverUrl(String? url) {
  if (url == null) return null;
  final trimmed = url.trim();
  if (trimmed.isEmpty) return null;
  final httpsUrl = trimmed.startsWith('http://')
      ? 'https://${trimmed.substring(7)}'
      : trimmed;
  final uri = Uri.tryParse(httpsUrl);
  final host = uri?.host.toLowerCase() ?? '';
  if (uri != null &&
      (host.endsWith('music.126.net') || host.endsWith('music.163.com')) &&
      !uri.queryParameters.containsKey('param')) {
    return uri
        .replace(
          queryParameters: <String, String>{
            ...uri.queryParameters,
            'param': '512y512',
          },
        )
        .toString();
  }
  return httpsUrl;
}

String? neteaseCoverUrlFromPicId(Object? value) {
  final raw = value?.toString().trim() ?? '';
  if (raw.isEmpty || raw == '0') return null;
  final encrypted = _neteaseEncryptedPicId(raw);
  return 'https://p1.music.126.net/$encrypted/$raw.jpg?param=512y512';
}

String _neteaseEncryptedPicId(String picId) {
  const key = '3go8&\$8*3*3h0k(2)2';
  final encrypted = <int>[];
  for (var i = 0; i < picId.length; i++) {
    encrypted.add(picId.codeUnitAt(i) ^ key.codeUnitAt(i % key.length));
  }
  return _base64ForNetease(md5.convert(encrypted).bytes);
}

String _base64ForNetease(List<int> bytes) {
  return base64Encode(bytes).replaceAll('/', '_').replaceAll('+', '-');
}
