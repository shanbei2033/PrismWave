import '../models/app_language.dart';
import '../models/audio_output_mode.dart';

class AppStrings {
  const AppStrings(this.appLanguage);

  final AppLanguage appLanguage;

  bool get _zh => appLanguage == AppLanguage.zhCn;

  String get appTitle => _zh ? 'PrismWave 演示版' : 'PrismWave Demo';
  String get localMusicPlayer => _zh ? '本地音乐播放器' : 'Local Music Player';

  String get settings => _zh ? '设置' : 'Settings';
  String get library => _zh ? '库' : 'Library';
  String get musicLibrary => _zh ? '音乐库' : 'Music Library';
  String get albums => _zh ? '专辑' : 'Albums';
  String get artists => _zh ? '艺术家' : 'Artists';
  String get favorites => _zh ? '我最爱的' : 'Favorites';
  String get trackUnit => _zh ? '首' : 'tracks';

  String get searchTrackArtistAlbum =>
      _zh ? '搜索歌曲 / 歌手 / 专辑' : 'Search track / artist / album';
  String get searchAlbumArtistTrack =>
      _zh ? '搜索专辑 / 歌手 / 歌名' : 'Search album / artist / track';
  String get searchArtist => _zh ? '搜索艺术家' : 'Search artist';

  String get noAlbumMatch => _zh ? '没有找到匹配的专辑' : 'No matching albums';
  String get noArtistMatch =>
      _zh ? '没有找到匹配的艺术家' : 'No matching artists';
  String get noTrackMatch =>
      _zh ? '当前筛选条件下没有匹配的歌曲' : 'No matching tracks';
  String get addFolderFirst =>
      _zh ? '请先通过设置添加歌曲文件夹' : 'Please add a music folder first';
  String get noFavoriteTracks => _zh
      ? '还没有收藏歌曲，点击歌曲右侧爱心即可加入我最爱的'
      : 'No favorites yet. Click the heart icon to add favorites.';

  String get cover => _zh ? '封面' : 'Cover';
  String get trackName => _zh ? '歌名' : 'Title';
  String get singer => _zh ? '歌手' : 'Artist';
  String get duration => _zh ? '时长' : 'Duration';
  String get collect => _zh ? '收藏' : 'Favorite';
  String get uncollect => _zh ? '取消收藏' : 'Unfavorite';
  String get playAll => _zh ? '播放全部' : 'Play All';
  String get noTracks => _zh ? '暂无歌曲' : 'No tracks';
  String get noTrackSelected => _zh ? '未选择歌曲' : 'No track selected';

  String get folders => _zh ? '文件夹' : 'Folders';
  String get tracks => _zh ? '歌曲' : 'Tracks';
  String get favoriteCountLabel => _zh ? '收藏' : 'Favorites';
  String get folderSection => _zh ? '歌曲文件夹' : 'Music Folders';
  String get addMusicFolder => _zh ? '添加歌曲文件夹' : 'Add Music Folder';
  String get rescanAll => _zh ? '重新刷新' : 'Rescan';
  String get noFolderConfigured => _zh
      ? '还没有添加文件夹，请先添加歌曲文件夹'
      : 'No folder added yet. Please add a music folder first.';
  String get remove => _zh ? '移除' : 'Remove';

  String get languageTitle => _zh ? '语言' : 'Language';
  String languageLabel(AppLanguage target) => switch (target) {
    AppLanguage.zhCn => '简体中文',
    AppLanguage.enUs => 'English',
  };

  String get audioOutputMode => _zh ? '音频输出模式' : 'Audio Output Mode';
  String outputModeLabel(AudioOutputMode mode) => switch (mode) {
    AudioOutputMode.compatibility =>
      _zh ? '兼容模式 (MPV)' : 'Compatibility (MPV)',
    AudioOutputMode.wasapiShared =>
      _zh ? 'WASAPI 共享模式' : 'WASAPI Shared',
    AudioOutputMode.wasapiExclusive =>
      _zh ? 'WASAPI 独占模式' : 'WASAPI Exclusive',
  };
  String outputModeDescription(AudioOutputMode mode) => switch (mode) {
    AudioOutputMode.compatibility => _zh
      ? '兼容模式，由 MPV 自动选择输出后端。'
      : 'Compatibility mode with MPV default output selection.',
    AudioOutputMode.wasapiShared => _zh
      ? '使用 WASAPI 共享模式，可与其他应用同时播放。'
      : 'Use WASAPI shared mode for maximum compatibility with other apps.',
    AudioOutputMode.wasapiExclusive => _zh
      ? '优先 WASAPI 独占模式，失败时自动回落共享模式。'
      : 'Prefer WASAPI exclusive mode; fallback to shared on failure.',
  };

  String get developerMode => _zh ? '开发者模式' : 'Developer Mode';
  String get developerModeHint => _zh
      ? '开启后弹出独立终端实时看日志，并同步写入本地日志文件'
      : 'Open a dedicated terminal and mirror logs to a local file in real time.';
  String get playbackLogs => _zh ? '播放日志' : 'Playback Logs';
  String get copy => _zh ? '复制' : 'Copy';
  String get clear => _zh ? '清空' : 'Clear';
  String get logsCopied => _zh ? '已复制播放日志' : 'Playback logs copied';
  String get noLogsHint => _zh
      ? '暂无日志。请先复现问题。'
      : 'No logs yet. Reproduce the failed case first.';

  String get listLoop => _zh ? '列表循环' : 'List Loop';
  String get singleLoop => _zh ? '单曲循环' : 'Single Loop';
  String get shuffle => _zh ? '随机播放' : 'Shuffle';
  String get back => _zh ? '返回' : 'Back';
  String get noTrackPlaying =>
      _zh ? '当前没有正在播放的歌曲' : 'No track is currently playing';
  String get noLyricsFound =>
      _zh ? '当前歌曲未找到可用歌词' : 'No lyrics found for this track';

  String trackCountText(int count) => '$count $trackUnit';
  String albumTrackCountText(int count) =>
      _zh ? '$count 首歌曲' : '$count tracks';
  String albumSubtitle(int count) =>
      _zh ? '专辑 · $count 首' : 'Album · $count tracks';
  String artistSubtitle(int count) =>
      _zh ? '艺术家 · $count 首' : 'Artist · $count tracks';
}
