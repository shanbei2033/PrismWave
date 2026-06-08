import '../models/app_language.dart';
import '../models/audio_output_mode.dart';
import '../models/top_bar_idle_mode.dart';

class AppStrings {
  const AppStrings(this.appLanguage);

  final AppLanguage appLanguage;

  String get settingsBasicTab => _tr('\u57fa\u7840', '\u57fa\u790e', 'Basic');
  String get settingsPlaybackTab =>
      _tr('\u64ad\u653e', '\u64ad\u653e', 'Playback');
  String get settingsOnlineTab =>
      _tr('\u5728\u7ebf', '\u7dda\u4e0a', 'Online');
  String get hits => 'HITS';
  String get hitsNoNetwork => _tr(
    '\u5f53\u524d\u65e0\u7f51\u7edc',
    '\u76ee\u524d\u7121\u7db2\u8def',
    'No network connection',
  );
  String get hitsCloudTimeout => _tr(
    '\u8fde\u63a5\u4e91\u7aef\u8d85\u65f6',
    '\u9023\u63a5\u96f2\u7aef\u8d85\u6642',
    'Cloud connection timed out',
  );
  String get hitsInDevelopment => _tr(
    '\u6b63\u5728\u5f00\u53d1\u4e2d',
    '\u6b63\u5728\u958b\u767c\u4e2d',
    'In development',
  );
  String get hitsUnavailable => _tr(
    'HITS\u4e0d\u53ef\u7528',
    'HITS\u4e0d\u53ef\u7528',
    'HITS unavailable',
  );
  String get hitsLoadingSchedule => _tr(
    '\u6b63\u5728\u52a0\u8f7d HITS \u8282\u76ee\u5355',
    '\u6b63\u5728\u8f09\u5165 HITS \u7bc0\u76ee\u55ae',
    'Loading HITS schedule',
  );
  String get hitsOffAir =>
      _tr('HITS \u4f11\u53f0\u4e2d', 'HITS \u4f11\u53f0\u4e2d', 'HITS off air');
  String get hitsScheduleStandby => _tr(
    'HITS \u8282\u76ee\u51c6\u5907\u4e2d',
    'HITS \u7bc0\u76ee\u6e96\u5099\u4e2d',
    'HITS standby',
  );
  String get hitsAudioComingSoon => _tr(
    '\u97f3\u9891\u63a5\u5165\u5f00\u53d1\u4e2d',
    '\u97f3\u8a0a\u63a5\u5165\u958b\u767c\u4e2d',
    'Audio integration in development',
  );
  String get hitsUsingCachedSchedule => _tr(
    '\u5df2\u5207\u6362\u5230\u79bb\u7ebf\u7f13\u5b58\u8282\u76ee\u5355',
    '\u5df2\u5207\u63db\u5230\u96e2\u7dda\u5feb\u53d6\u7bc0\u76ee\u55ae',
    'Using cached HITS schedule',
  );
  String get hitsLivePlaybackSynced => _tr(
    '\u5df2\u6309 HITS \u65f6\u95f4\u7ebf\u540c\u6b65\u64ad\u653e',
    '\u5df2\u6309 HITS \u6642\u9593\u7dda\u540c\u6b65\u64ad\u653e',
    'Playing in sync with the HITS timeline',
  );
  String get hitsResumeToLiveHint => _tr(
    '\u5df2\u6682\u505c\uff0c\u6062\u590d\u540e\u5c06\u8df3\u8f6c\u5230\u5f53\u524d\u76f4\u64ad\u8fdb\u5ea6',
    '\u5df2\u66ab\u505c\uff0c\u6062\u5fa9\u5f8c\u5c07\u8df3\u8f49\u5230\u76ee\u524d\u76f4\u64ad\u9032\u5ea6',
    'Paused. Resume will jump to the live position',
  );
  String get hitsTrackNotInLibrary => _tr(
    '\u5f53\u524d\u8282\u76ee\u672a\u5728\u672c\u5730\u5e93\u4e2d\u5339\u914d\u5230\u6b4c\u66f2',
    '\u76ee\u524d\u7bc0\u76ee\u672a\u5728\u672c\u5730\u5eab\u4e2d\u5339\u914d\u5230\u6b4c\u66f2',
    'This HITS track is not matched in your local library',
  );
  String get hitsNoPlayableSource => _tr(
    '\u5f53\u524d\u8282\u76ee\u5355\u672a\u63d0\u4f9b\u53ef\u64ad\u653e\u97f3\u6e90',
    '\u76ee\u524d\u7bc0\u76ee\u55ae\u672a\u63d0\u4f9b\u53ef\u64ad\u653e\u97f3\u6e90',
    'This HITS schedule item does not provide a playable audio source',
  );
  String get hitsResolvingOnlineSource => _tr(
    '\u6b63\u5728\u641c\u7d22\u5728\u7ebf\u97f3\u6e90',
    '\u6b63\u5728\u641c\u5c0b\u7dda\u4e0a\u97f3\u6e90',
    'Searching for an online audio source',
  );
  String get hitsOnlineLyricsReady => _tr(
    '\u5df2\u8054\u7f51\u5339\u914d\u6b4c\u8bcd\uff0c\u97f3\u9891\u6e90\u63a5\u5165\u4e2d',
    '\u5df2\u9023\u7db2\u5339\u914d\u6b4c\u8a5e\uff0c\u97f3\u8a0a\u6e90\u63a5\u5165\u4e2d',
    'Online lyrics are ready, audio source integration is in progress',
  );
  String get hitsSyncingPlayback => _tr(
    '\u6b63\u5728\u540c\u6b65 HITS \u64ad\u653e',
    '\u6b63\u5728\u540c\u6b65 HITS \u64ad\u653e',
    'Syncing HITS playback',
  );

  String get navHome => _tr('\u9996\u9875', '\u9996\u9801', 'Home');
  String get navSearch => _tr('\u641c\u7d22', '\u641c\u5c0b', 'Search');
  String get onlineHomeLoading => _tr(
    '\u6b63\u5728\u52a0\u8f7d\u63a8\u8350',
    '\u6b63\u5728\u8f09\u5165\u63a8\u85a6',
    'Loading recommendations',
  );
  String get onlineHomeFailed => _tr(
    '\u63a8\u8350\u52a0\u8f7d\u5931\u8d25',
    '\u63a8\u85a6\u8f09\u5165\u5931\u6557',
    'Failed to load recommendations',
  );
  String get onlineHomeRetry => _tr('\u91cd\u8bd5', '\u91cd\u8a66', 'Retry');
  String get onlineRecommendationsUnavailableTooltip => _tr(
    '\u63a8\u8350\u4e0d\u53ef\u7528\uff0c\u8bf7\u68c0\u67e5\u7f51\u7edc\u73af\u5883\u3002',
    '\u63a8\u85a6\u4e0d\u53ef\u7528\uff0c\u8acb\u6aa2\u67e5\u7db2\u8def\u74b0\u5883\u3002',
    'Recommendations unavailable. Please check your network.',
  );
  String get onlineFetchTodayChart => _tr(
    '\u62c9\u53d6\u4eca\u65e5\u699c\u5355',
    '\u62c9\u53d6\u4eca\u65e5\u699c\u55ae',
    "Fetch today's chart",
  );
  String get onlineFetchTodayChartDescription => _tr(
    '\u4ece GitHub \u62c9\u53d6\u4eca\u5929\u751f\u6210\u7684 Top100 \u63a8\u8350 JSON\u3002',
    '\u5f9e GitHub \u62c9\u53d6\u4eca\u5929\u751f\u6210\u7684 Top100 \u63a8\u85a6 JSON\u3002',
    "Fetch today's generated Top 100 recommendation JSON from GitHub.",
  );
  String get onlineFetchTodayChartFailed =>
      _tr('\u62c9\u53d6\u5931\u8d25', '\u62c9\u53d6\u5931\u6557', 'Fetch failed');
  String get onlineFetchTodayChartSucceeded => _tr(
    '\u5df2\u62c9\u53d6\u4eca\u65e5\u699c\u5355',
    '\u5df2\u62c9\u53d6\u4eca\u65e5\u699c\u55ae',
    "Today's chart fetched",
  );
  String get onlineSearchPlaceholder => _tr(
    '\u641c\u7d22\u5728\u7ebf\u548c\u672c\u5730\u97f3\u4e50',
    '\u641c\u5c0b\u7dda\u4e0a\u8207\u672c\u6a5f\u97f3\u6a02',
    'Search online and local music',
  );
  String get onlinePopularTags => _tr(
    '\u70ed\u95e8\u6807\u7b7e',
    '\u71b1\u9580\u6a19\u7c64',
    'Popular tags',
  );
  String get onlineSourceLocal => _tr('\u672c\u5730', '\u672c\u6a5f', 'Local');
  String get onlineSourceOnline =>
      _tr('\u5728\u7ebf', '\u7dda\u4e0a', 'Online');
  String get onlineSearching =>
      _tr('\u6b63\u5728\u641c\u7d22', '\u6b63\u5728\u641c\u5c0b', 'Searching');
  String get onlineSearchEmpty =>
      _tr('\u6ca1\u6709\u7ed3\u679c', '\u6c92\u6709\u7d50\u679c', 'No results');
  String get onlineSearchFailed => _tr(
    '\u641c\u7d22\u5931\u8d25',
    '\u641c\u5c0b\u5931\u6557',
    'Search failed',
  );
  String get onlineResolveFailed => _tr(
    '\u65e0\u6cd5\u89e3\u6790\u5728\u7ebf\u97f3\u6e90',
    '\u7121\u6cd5\u89e3\u6790\u7dda\u4e0a\u97f3\u6e90',
    'Could not resolve an online audio source',
  );
  String get onlineModeSettingTitle => _tr(
    '\u5728\u7ebf\u6a21\u5f0f',
    '\u7dda\u4e0a\u6a21\u5f0f',
    'Online mode',
  );
  String get onlineModeSettingDescription => _tr(
    '\u5f00\u542f\u540e\u4fa7\u680f\u663e\u793a\u9996\u9875\u4e0e\u641c\u7d22\uff0c\u63d0\u4f9b\u5728\u7ebf\u63a8\u8350\u4e0e\u8de8\u672c\u5730/\u5728\u7ebf\u641c\u7d22',
    '\u958b\u555f\u5f8c\u5074\u6b04\u986f\u793a\u9996\u9801\u8207\u641c\u5c0b\uff0c\u63d0\u4f9b\u7dda\u4e0a\u63a8\u85a6\u8207\u8de8\u672c\u6a5f/\u7dda\u4e0a\u641c\u5c0b',
    'Show Home and Search in the sidebar with online recommendations and unified search',
  );
  String get onlineTopPlaylistTitle => _tr(
    '\u4eca\u65e5\u8d8b\u52bf',
    '\u4eca\u65e5\u8da8\u52e2',
    "Today's Trending",
  );
  String get onlineTopPlaylistSubtitle => _tr(
    '\u6574\u5408\u591a\u5e73\u53f0\u70ed\u95e8\u4fe1\u53f7\u7684 Top100 \u63a8\u8350',
    '\u6574\u5408\u591a\u5e73\u53f0\u71b1\u9580\u4fe1\u865f\u7684 Top100 \u63a8\u85a6',
    'Top 100 from global multi-platform trend signals',
  );
  String get onlineTopPlaylistOpen =>
      _tr('\u67e5\u770b\u699c\u5355', '\u67e5\u770b\u699c\u55ae', 'Open chart');
  String get onlineTopPlaylistBadge => 'TOP100';
  String get onlineNewAlbumsTitle =>
      _tr('\u4e13\u8f91\u63a8\u8350', '\u5c08\u8f2f\u63a8\u85a6', 'New Albums');
  String get onlineNewAlbumsSubtitle => _tr(
    '\u672c\u5468\u65b0\u53d1\u4e13\u8f91',
    '\u672c\u9031\u65b0\u767c\u5c08\u8f2f',
    'Released this week',
  );
  String get onlineHotSongsTitle => _tr(
    '\u6b4c\u66f2\u63a8\u8350',
    '\u6b4c\u66f2\u63a8\u85a6',
    'Songs For You',
  );
  String get onlineHotSongsSubtitle => _tr(
    '\u6df7\u5408\u591a\u79cd\u98ce\u683c\u7684\u65b0\u6b4c',
    '\u6df7\u5408\u591a\u7a2e\u98a8\u683c\u7684\u65b0\u6b4c',
    'Mixed-genre new releases',
  );
  String get onlineStylesTitle =>
      _tr('\u98ce\u683c\u63a8\u8350', '\u98a8\u683c\u63a8\u85a6', 'By Style');
  String get onlinePlayAlbum =>
      _tr('\u64ad\u653e\u4e13\u8f91', '\u64ad\u653e\u5c08\u8f2f', 'Play album');
  String get onlinePlayAlbumAll =>
      _tr('\u64ad\u653e\u5168\u90e8', '\u64ad\u653e\u5168\u90e8', 'Play all');
  String get onlineAlbumTrackCount => _tr('\u9996', '\u9996', 'tracks');

  bool get _zhHans => appLanguage == AppLanguage.zhCn;
  bool get _zhHant => appLanguage == AppLanguage.zhTw;

  String _tr(String zhHans, String zhHant, String enUs) {
    if (_zhHans) return zhHans;
    if (_zhHant) return zhHant;
    return enUs;
  }

  String get appTitle =>
      _tr('PrismWave 演示版', 'PrismWave 演示版', 'PrismWave Demo');
  String get localMusicPlayer =>
      _tr('本地音乐播放器', '本機音樂播放器', 'Local Music Player');

  String get settings => _tr('设置', '設定', 'Settings');
  String get library => _tr('库', '庫', 'Library');
  String get musicLibrary => _tr('音乐库', '音樂庫', 'Music Library');
  String get albums => _tr('专辑', '專輯', 'Albums');
  String get artists => _tr('艺术家', '藝術家', 'Artists');
  String get favorites => _tr('我最爱的', '我最愛的', 'Favorites');
  String get trackUnit => _tr('首', '首', 'tracks');

  String get searchTrackArtistAlbum =>
      _tr('搜索歌曲 / 歌手 / 专辑', '搜尋歌曲 / 歌手 / 專輯', 'Search track / artist / album');
  String get searchAlbumArtistTrack =>
      _tr('搜索专辑 / 歌手 / 歌名', '搜尋專輯 / 歌手 / 歌名', 'Search album / artist / track');
  String get searchArtist => _tr('搜索艺术家', '搜尋藝術家', 'Search artist');

  String get noAlbumMatch =>
      _tr('没有找到匹配的专辑', '沒有找到符合的專輯', 'No matching albums');
  String get noArtistMatch =>
      _tr('没有找到匹配的艺术家', '沒有找到符合的藝術家', 'No matching artists');
  String get noTrackMatch =>
      _tr('当前筛选条件下没有匹配的歌曲', '目前篩選條件下沒有符合的歌曲', 'No matching tracks');
  String get addFolderFirst =>
      _tr('请先通过设置添加歌曲文件夹', '請先透過設定新增歌曲資料夾', 'Please add a music folder first');
  String get noFavoriteTracks => _tr(
    '还没有收藏歌曲，点击歌曲右侧爱心即可加入我最爱的',
    '還沒有收藏歌曲，點擊歌曲右側愛心即可加入我最愛的',
    'No favorites yet. Click the heart icon to add favorites.',
  );

  String get cover => _tr('封面', '封面', 'Cover');
  String get replaceCover => _tr('替换封面', '替換封面', 'Replace Cover');
  String get coverSearchHint =>
      _tr('按歌曲名搜索相关封面', '按歌曲名搜尋相關封面', 'Search related covers by song name');
  String get noOnlineCoverResults =>
      _tr('没有找到可用封面', '沒有找到可用封面', 'No covers found');
  String get chooseCoverHint => _tr(
    '点击列表中的封面即可替换',
    '點擊清單中的封面即可替換',
    'Click a cover below to replace the current artwork',
  );
  String get doubleClickToReplaceCover => _tr(
    '双击封面可替换当前封面',
    '雙擊封面可替換目前封面',
    'Double-click the cover to replace artwork',
  );
  String get trackName => _tr('歌名', '歌名', 'Title');
  String get singer => _tr('歌手', '歌手', 'Artist');
  String get duration => _tr('时长', '時長', 'Duration');
  String get collect => _tr('收藏', '收藏', 'Favorite');
  String get uncollect => _tr('取消收藏', '取消收藏', 'Unfavorite');
  String get details => _tr('详细信息', '詳細資訊', 'Details');
  String get deleteTrack => _tr('删除歌曲', '刪除歌曲', 'Delete Track');
  String get removeFromQueue => _tr('移出播放列表', '移出播放清單', 'Remove from Queue');
  String get removeFromListPrompt =>
      _tr('是否将其歌曲移出列表', '是否將其歌曲移出列表', 'Remove this track from the list?');
  String get deleteSourceFileToo =>
      _tr('同时删除源文件', '同時刪除來源檔案', 'Also delete source file');
  String get confirmYes => _tr('是', '是', 'Yes');
  String get confirmNo => _tr('否', '否', 'No');
  String get trackRemoved =>
      _tr('歌曲已移出列表', '歌曲已移出列表', 'Track removed from list');
  String get trackRemovedAndDeleted =>
      _tr('歌曲及源文件已删除', '歌曲及來源檔案已刪除', 'Track and source file deleted');
  String get revealInExplorer =>
      _tr('定位到文件资源管理器', '定位到檔案總管', 'Reveal in Explorer');
  String get detailsTitle => _tr('歌曲详细信息', '歌曲詳細資訊', 'Track Details');
  String get audioTrack => _tr('音轨', '音軌', 'Track');
  String get bitrate => _tr('码率', '碼率', 'Bitrate');
  String get sampleRate => _tr('采样率', '取樣率', 'Sample Rate');
  String get pathLabel => _tr('位置', '位置', 'Location');
  String get playAll => _tr('播放全部', '全部播放', 'Play All');
  String get noTracks => _tr('暂无歌曲', '暫無歌曲', 'No tracks');
  String get noTrackSelected => _tr('未选择歌曲', '未選擇歌曲', 'No track selected');

  String get folders => _tr('文件夹', '資料夾', 'Folders');
  String get tracks => _tr('歌曲', '歌曲', 'Tracks');
  String get favoriteCountLabel => _tr('收藏', '收藏', 'Favorites');
  String get folderSection => _tr('歌曲文件夹', '歌曲資料夾', 'Music Folders');
  String get addMusicFolder => _tr('添加歌曲文件夹', '新增歌曲資料夾', 'Add Music Folder');
  String get rescanAll => _tr('重新刷新', '重新整理', 'Rescan');
  String get noFolderConfigured => _tr(
    '还没有添加文件夹，请先添加歌曲文件夹',
    '還沒有新增資料夾，請先新增歌曲資料夾',
    'No folder added yet. Please add a music folder first.',
  );
  String get remove => _tr('移除', '移除', 'Remove');
  String get folderSize => _tr('占用空间', '佔用空間', 'Disk usage');

  String get languageTitle => _tr('语言', '語言', 'Language');
  String get checkUpdates => _tr('检查更新', '檢查更新', 'Check for Updates');
  String get checkingUpdates => _tr('检查中...', '檢查中...', 'Checking...');
  String get currentVersionLabel => _tr('当前版本', '目前版本', 'Current Version');
  String get latestVersionLabel => _tr('最新版本', '最新版本', 'Latest Version');
  String get getUpdate => _tr('获取更新', '取得更新', 'Get Update');
  String get updateCheckTitle => _tr('版本更新', '版本更新', 'Version Update');
  String get updateUpToDate =>
      _tr('当前已经是最新版本。', '目前已是最新版本。', 'You are already on the latest version.');
  String updateAvailable(String version) => _tr(
    '检测到新版本：$version',
    '檢測到新版本：$version',
    'New version available: $version',
  );
  String get updateCheckFailed => _tr(
    '检查更新失败，请稍后重试。',
    '檢查更新失敗，請稍後再試。',
    'Update check failed. Please try again later.',
  );
  String languageLabel(AppLanguage target) => switch (target) {
    AppLanguage.zhCn => '简体中文',
    AppLanguage.zhTw => '繁體中文',
    AppLanguage.enUs => 'English',
  };

  String get audioOutputMode => _tr('音频输出模式', '音訊輸出模式', 'Audio Output Mode');
  String get audioOutputDevice => _tr('播放设备', '播放裝置', 'Playback Device');
  String get audioFade => _tr('淡入淡出', '淡入淡出', 'Fade In / Out');
  String get audioFadeEnabled =>
      _tr('启用淡入淡出', '啟用淡入淡出', 'Enable Fade In / Out');
  String get audioFadeDuration => _tr('淡入淡出时长', '淡入淡出時長', 'Fade Duration');
  String get audioFadeHint => _tr(
    '切歌、暂停和继续播放时应用音量渐变。',
    '切歌、暫停和繼續播放時套用音量漸變。',
    'Apply a volume ramp when switching tracks, pausing, and resuming.',
  );
  String get audioFadeDisabledHint => _tr(
    '关闭后，播放与切换将立即生效，不做音量渐变。',
    '關閉後，播放與切換將立即生效，不做音量漸變。',
    'When off, playback changes apply immediately with no volume ramp.',
  );
  String get defaultAudioDevice => _tr('默认设备', '預設裝置', 'Default Device');
  String get audioOutputDeviceHint => _tr(
    '选择当前使用的播放设备，切换后会重建播放器以应用到当前后端。',
    '選擇目前使用的播放裝置，切換後會重建播放器以套用到目前後端。',
    'Choose the playback device. The player will be recreated to apply it.',
  );
  String get windowsDsdDevice =>
      _tr('DSD 输出设备', 'DSD 輸出裝置', 'DSD Output Device');
  String get windowsDsdDeviceHint => _tr(
    'DSF/DFF 播放使用所选 ASIO 设备，优先 raw DSD，不支持时自动回退 DoP。',
    'DSF/DFF 播放使用所選 ASIO 裝置，優先 raw DSD，不支援時自動回退 DoP。',
    'DSF/DFF playback uses the selected ASIO device, preferring raw DSD and falling back to DoP.',
  );
  String get windowsDsdUnavailableHint => _tr(
    '当前未检测到可用的 Windows DSD 后端或 ASIO 设备。',
    '目前未檢測到可用的 Windows DSD 後端或 ASIO 裝置。',
    'No Windows DSD backend or ASIO device is currently available.',
  );
  String get windowsDsdStatus => _tr('DSD 状态', 'DSD 狀態', 'DSD Status');
  String get windowsDsdRuntimeStatus => _tr('运行库', '執行庫', 'Runtime');
  String get windowsDsdRuntimeReady => _tr('已加载', '已載入', 'Loaded');
  String get windowsDsdRuntimeMissing => _tr('未加载', '未載入', 'Unavailable');
  String get windowsDsdDeviceCountLabel =>
      _tr('ASIO 设备', 'ASIO 裝置', 'ASIO Devices');
  String windowsDsdDeviceCountValue(int count) =>
      _tr('$count 个可用设备', '$count 個可用裝置', '$count available');
  String get windowsDsdNoDevice =>
      _tr('未检测到可用设备', '未偵測到可用裝置', 'No device detected');
  String get windowsDsdCurrentBackend => _tr('当前后端', '目前後端', 'Current Backend');
  String get windowsDsdBackendActive =>
      _tr('Windows DSD 专用后端', 'Windows DSD 專用後端', 'Windows DSD Backend');
  String get windowsDsdBackendFallback =>
      _tr('已回退到常规后端', '已回退到一般後端', 'Fallback to media backend');
  String get windowsDsdBackendIdle =>
      _tr('等待 DSD 曲目', '等待 DSD 曲目', 'Waiting for DSD track');
  String get windowsDsdOutputModeStatus => _tr('输出模式', '輸出模式', 'Output Mode');
  String get windowsDsdActiveDevice => _tr('活动设备', '作用中裝置', 'Active Device');
  String get windowsDsdFallbackReason => _tr('回退原因', '回退原因', 'Fallback Reason');
  String outputModeLabel(AudioOutputMode mode) => switch (mode) {
    AudioOutputMode.compatibility => _tr(
      '兼容模式 (MPV)',
      '相容模式 (MPV)',
      'Compatibility (MPV)',
    ),
    AudioOutputMode.wasapiShared => _tr(
      'WASAPI 共享模式',
      'WASAPI 共享模式',
      'WASAPI Shared',
    ),
    AudioOutputMode.wasapiExclusive => _tr(
      'WASAPI 独占模式',
      'WASAPI 獨占模式',
      'WASAPI Exclusive',
    ),
  };
  String outputModeDescription(AudioOutputMode mode) => switch (mode) {
    AudioOutputMode.compatibility => _tr(
      '兼容模式，由 MPV 自动选择输出后端。',
      '相容模式，由 MPV 自動選擇輸出後端。',
      'Compatibility mode with MPV default output selection.',
    ),
    AudioOutputMode.wasapiShared => _tr(
      '使用 WASAPI 共享模式，可与其他应用同时播放。',
      '使用 WASAPI 共享模式，可與其他應用同時播放。',
      'Use WASAPI shared mode for maximum compatibility with other apps.',
    ),
    AudioOutputMode.wasapiExclusive => _tr(
      '优先 WASAPI 独占模式，失败时自动回落共享模式。',
      '優先 WASAPI 獨占模式，失敗時自動回落共享模式。',
      'Prefer WASAPI exclusive mode; fallback to shared on failure.',
    ),
  };

  String get developerMode => _tr('开发者模式', '開發者模式', 'Developer Mode');
  String get developerModeHint => _tr(
    '开启后弹出独立终端实时查看日志，并同步写入本地日志文件。',
    '開啟後彈出獨立終端即時查看日誌，並同步寫入本機日誌檔。',
    'Open a dedicated terminal and mirror logs to a local file in real time.',
  );
  String get playbackLogs => _tr('播放日志', '播放日誌', 'Playback Logs');
  String get copy => _tr('复制', '複製', 'Copy');
  String get clear => _tr('清空', '清空', 'Clear');
  String get logsCopied => _tr('已复制播放日志', '已複製播放日誌', 'Playback logs copied');
  String get noLogsHint => _tr(
    '暂无日志。请先复现问题。',
    '暫無日誌。請先重現問題。',
    'No logs yet. Reproduce the failed case first.',
  );

  String get listLoop => _tr('列表循环', '列表循環', 'List Loop');
  String get singleLoop => _tr('单曲循环', '單曲循環', 'Single Loop');
  String get shuffle => _tr('随机播放', '隨機播放', 'Shuffle');
  String get playbackQueue => _tr('播放队列', '播放佇列', 'Playback Queue');
  String get noActivePlaylist =>
      _tr('当前没有可显示的播放列表', '目前沒有可顯示的播放清單', 'No active playlist to display');
  String get back => _tr('返回', '返回', 'Back');
  String get noTrackPlaying =>
      _tr('当前没有正在播放的歌曲', '目前沒有正在播放的歌曲', 'No track is currently playing');
  String get noLyricsFound =>
      _tr('当前歌曲未找到可用歌词', '目前歌曲未找到可用歌詞', 'No lyrics found for this track');

  String get loadingLyrics =>
      _tr('正在加载歌词...', '正在載入歌詞...', 'Loading lyrics...');
  String get loading => _tr('加载中...', '載入中...', 'Loading...');
  String get lyricsSource => _tr('歌词来源', '歌詞來源', 'Lyrics Source');
  String get localLyricsSource => _tr('本地', '本機', 'Local');
  String get onlineLyricsSource => _tr('在线', '線上', 'Online');
  String get currentLyricsInfo => _tr('当前歌词', '目前歌詞', 'Current Lyrics');
  String get currentLyricsUnavailable =>
      _tr('当前未加载歌词', '目前未載入歌詞', 'No lyrics loaded');
  String get syncedLyricsLabel => _tr('同步歌词', '同步歌詞', 'Synced');
  String get unsyncedLyricsLabel => _tr('非同步歌词', '非同步歌詞', 'Unsynced');
  String get karaokeSupported =>
      _tr('支持逐字高亮', '支援逐字高亮', 'Word-by-word supported');
  String get karaokeUnsupported =>
      _tr('不支持逐字高亮', '不支援逐字高亮', 'Word-by-word unsupported');
  String get onlineLyricsSearch => _tr('在线搜索', '線上搜尋', 'Online Search');
  String get onlineLyricsSearchHint =>
      _tr('按歌曲名搜索歌词', '按歌曲名搜尋歌詞', 'Search lyrics by song name');
  String get lyricsTools => _tr('歌词工具', '歌詞工具', 'Lyrics Tools');
  String get toggleLyricsSource =>
      _tr('切换歌词源', '切換歌詞來源', 'Toggle Lyrics Source');
  String get lyricsOffset => _tr('歌词偏移', '歌詞偏移', 'Lyrics Offset');
  String get lyricsOffsetHint => _tr(
    '输入秒数，支持一位小数',
    '輸入秒數，支援一位小數',
    'Enter seconds with up to one decimal place',
  );
  String get offsetInvalid =>
      _tr('请输入正确的秒数', '請輸入正確的秒數', 'Enter a valid offset value');
  String get addSign => _tr('加号', '加號', 'Plus');
  String get minusSign => _tr('减号', '減號', 'Minus');
  String get searchAction => _tr('搜索', '搜尋', 'Search');
  String get noOnlineLyricsResults =>
      _tr('没有找到在线歌词结果', '沒有找到線上歌詞結果', 'No online lyrics found');
  String get useThisCover => _tr('使用这张封面', '使用這張封面', 'Use This Cover');
  String get volume => _tr('音量', '音量', 'Volume');
  String audioFadeDurationValue(Duration duration) {
    final seconds = duration.inMilliseconds / 1000;
    return _tr(
      '${seconds.toStringAsFixed(1)} 秒',
      '${seconds.toStringAsFixed(1)} 秒',
      '${seconds.toStringAsFixed(1)} s',
    );
  }

  String get topBarDisplayTitle => _tr('顶部栏显示', '頂部欄顯示', 'Top Bar Display');
  String get topBarIdleModeTitle => _tr('空闲时显示内容', '空閒時顯示內容', 'Idle Content');
  String topBarIdleModeLabel(TopBarIdleMode mode) => switch (mode) {
    TopBarIdleMode.empty => _tr('关闭', '關閉', 'Off'),
    TopBarIdleMode.custom => _tr('自定义文字', '自訂文字', 'Custom Text'),
    TopBarIdleMode.quote => _tr('一言', '一言', 'Quote'),
  };
  String audioDeviceLabel(String label, {required bool isAuto}) =>
      isAuto ? defaultAudioDevice : label;
  String windowsDsdDeviceLabel(
    String label, {
    required bool isAuto,
    required bool supportsNativeDsd,
  }) {
    if (isAuto) {
      return defaultAudioDevice;
    }
    return supportsNativeDsd ? '$label · Native DSD' : '$label · DoP';
  }

  String get topBarCustomTextTitle =>
      _tr('自定义显示文字', '自訂顯示文字', 'Custom Display Text');
  String get topBarCustomTextHint => _tr(
    '没有播放时显示在顶部栏里的文字',
    '沒有播放時顯示在頂部欄裡的文字',
    'Text shown in the top bar when nothing is playing',
  );

  String trackCountText(int count) => '$count $trackUnit';
  String albumTrackCountText(int count) =>
      _tr('$count 首歌曲', '$count 首歌曲', '$count tracks');
  String albumSubtitle(int count) =>
      _tr('专辑 · $count 首', '專輯 · $count 首', 'Album · $count tracks');
  String artistSubtitle(int count) =>
      _tr('艺术家 · $count 首', '藝術家 · $count 首', 'Artist · $count tracks');
}
