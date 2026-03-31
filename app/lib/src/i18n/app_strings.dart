import '../models/app_language.dart';
import '../models/audio_output_mode.dart';
import '../models/top_bar_idle_mode.dart';

class AppStrings {
  const AppStrings(this.appLanguage);

  final AppLanguage appLanguage;

  bool get _zhHans => appLanguage == AppLanguage.zhCn;
  bool get _zhHant => appLanguage == AppLanguage.zhTw;

  String _tr(String zhHans, String zhHant, String enUs) {
    if (_zhHans) return zhHans;
    if (_zhHant) return zhHant;
    return enUs;
  }

  String get appTitle => _tr('PrismWave 演示版', 'PrismWave 演示版', 'PrismWave Demo');
  String get localMusicPlayer => _tr('本地音乐播放器', '本機音樂播放器', 'Local Music Player');

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

  String get noAlbumMatch => _tr('没有找到匹配的专辑', '沒有找到符合的專輯', 'No matching albums');
  String get noArtistMatch => _tr('没有找到匹配的艺术家', '沒有找到符合的藝術家', 'No matching artists');
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
  String get revealInExplorer => _tr('定位到文件资源管理器', '定位到檔案總管', 'Reveal in Explorer');
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

  String get languageTitle => _tr('语言', '語言', 'Language');
  String languageLabel(AppLanguage target) => switch (target) {
    AppLanguage.zhCn => '简体中文',
    AppLanguage.zhTw => '繁體中文',
    AppLanguage.enUs => 'English',
  };

  String get audioOutputMode => _tr('音频输出模式', '音訊輸出模式', 'Audio Output Mode');
  String get audioOutputDevice => _tr('播放设备', '播放裝置', 'Playback Device');
  String get defaultAudioDevice => _tr('默认设备', '預設裝置', 'Default Device');
  String get audioOutputDeviceHint => _tr(
        '选择当前使用的播放设备，切换后会重建播放器以应用到当前后端。',
        '選擇目前使用的播放裝置，切換後會重建播放器以套用到目前後端。',
        'Choose the playback device. The player will be recreated to apply it.',
      );
  String outputModeLabel(AudioOutputMode mode) => switch (mode) {
    AudioOutputMode.compatibility => _tr('兼容模式 (MPV)', '相容模式 (MPV)', 'Compatibility (MPV)'),
    AudioOutputMode.wasapiShared => _tr('WASAPI 共享模式', 'WASAPI 共享模式', 'WASAPI Shared'),
    AudioOutputMode.wasapiExclusive => _tr('WASAPI 独占模式', 'WASAPI 獨占模式', 'WASAPI Exclusive'),
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
  String get noLogsHint => _tr('暂无日志。请先复现问题。', '暫無日誌。請先重現問題。', 'No logs yet. Reproduce the failed case first.');

  String get listLoop => _tr('列表循环', '列表循環', 'List Loop');
  String get singleLoop => _tr('单曲循环', '單曲循環', 'Single Loop');
  String get shuffle => _tr('随机播放', '隨機播放', 'Shuffle');
  String get back => _tr('返回', '返回', 'Back');
  String get noTrackPlaying => _tr('当前没有正在播放的歌曲', '目前沒有正在播放的歌曲', 'No track is currently playing');
  String get noLyricsFound => _tr('当前歌曲未找到可用歌词', '目前歌曲未找到可用歌詞', 'No lyrics found for this track');

  String get loadingLyrics => _tr('正在加载歌词...', '正在載入歌詞...', 'Loading lyrics...');
  String get loading => _tr('加载中...', '載入中...', 'Loading...');
  String get lyricsSource => _tr('歌词来源', '歌詞來源', 'Lyrics Source');
  String get localLyricsSource => _tr('本地', '本機', 'Local');
  String get onlineLyricsSource => _tr('在线', '線上', 'Online');
  String get currentLyricsInfo => _tr('当前歌词', '目前歌詞', 'Current Lyrics');
  String get currentLyricsUnavailable => _tr('当前未加载歌词', '目前未載入歌詞', 'No lyrics loaded');
  String get syncedLyricsLabel => _tr('同步歌词', '同步歌詞', 'Synced');
  String get unsyncedLyricsLabel => _tr('非同步歌词', '非同步歌詞', 'Unsynced');
  String get karaokeSupported => _tr('支持逐字高亮', '支援逐字高亮', 'Word-by-word supported');
  String get karaokeUnsupported => _tr('不支持逐字高亮', '不支援逐字高亮', 'Word-by-word unsupported');
  String get onlineLyricsSearch => _tr('在线搜索', '線上搜尋', 'Online Search');
  String get onlineLyricsSearchHint => _tr('按歌曲名搜索歌词', '按歌曲名搜尋歌詞', 'Search lyrics by song name');
  String get lyricsTools => _tr('歌词工具', '歌詞工具', 'Lyrics Tools');
  String get toggleLyricsSource => _tr('切换歌词源', '切換歌詞來源', 'Toggle Lyrics Source');
  String get lyricsOffset => _tr('歌词偏移', '歌詞偏移', 'Lyrics Offset');
  String get lyricsOffsetHint => _tr('输入秒数，支持一位小数', '輸入秒數，支援一位小數', 'Enter seconds with up to one decimal place');
  String get offsetInvalid => _tr('请输入正确的秒数', '請輸入正確的秒數', 'Enter a valid offset value');
  String get addSign => _tr('加号', '加號', 'Plus');
  String get minusSign => _tr('减号', '減號', 'Minus');
  String get searchAction => _tr('搜索', '搜尋', 'Search');
  String get noOnlineLyricsResults => _tr('没有找到在线歌词结果', '沒有找到線上歌詞結果', 'No online lyrics found');
  String get useThisCover => _tr('使用这张封面', '使用這張封面', 'Use This Cover');
  String get volume => _tr('音量', '音量', 'Volume');

  String get topBarDisplayTitle => _tr('顶部栏显示', '頂部欄顯示', 'Top Bar Display');
  String get topBarIdleModeTitle => _tr('空闲时显示内容', '空閒時顯示內容', 'Idle Content');
  String topBarIdleModeLabel(TopBarIdleMode mode) => switch (mode) {
    TopBarIdleMode.empty => _tr('关闭', '關閉', 'Off'),
    TopBarIdleMode.custom => _tr('自定义文字', '自訂文字', 'Custom Text'),
    TopBarIdleMode.quote => _tr('一言', '一言', 'Quote'),
  };
  String audioDeviceLabel(String label, {required bool isAuto}) =>
      isAuto ? defaultAudioDevice : label;
  String get topBarCustomTextTitle => _tr('自定义显示文字', '自訂顯示文字', 'Custom Display Text');
  String get topBarCustomTextHint => _tr(
        '没有播放时显示在顶部栏里的文字',
        '沒有播放時顯示在頂部欄裡的文字',
        'Text shown in the top bar when nothing is playing',
      );

  String trackCountText(int count) => '$count $trackUnit';
  String albumTrackCountText(int count) => _tr('$count 首歌曲', '$count 首歌曲', '$count tracks');
  String albumSubtitle(int count) => _tr('专辑 · $count 首', '專輯 · $count 首', 'Album · $count tracks');
  String artistSubtitle(int count) => _tr('艺术家 · $count 首', '藝術家 · $count 首', 'Artist · $count tracks');
}
