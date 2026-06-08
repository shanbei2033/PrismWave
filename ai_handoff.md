# PrismWave AI 接手文档

更新时间：2026-06-09

本文档用于让其他 AI 在尽量少读上下文的情况下，快速接手当前仓库 `F:\Project\PrismWave` 的开发工作。

---

## 1. 项目概况

PrismWave 是一个基于 Flutter 的 Windows 本地音乐播放器，当前发布版本 **R502**，`pubspec.yaml` 版本号 `502.0.0+504`。

**GitHub 仓库**：
- 主仓库：`https://github.com/shanbei2033/PrismWave`
- HITS 节目单仓库：`https://github.com/shanbei2033/prismwave-hits`

已打通的主链路：
- 本地音乐库扫描（文件夹管理、全库重扫、搜索）
- 音乐库 / 专辑 / 艺术家 / 我最爱的 四种视图 + 拖拽排序
- 底部播放栏 + 全屏播放页
- 播放队列（拖拽排序、移除、独立于库列表）
- 本地歌词 / 在线歌词 / 逐字歌词 / QRC 解码
- 在线模式：首页推荐、在线搜索、在线专辑详情、在线播放队列
- HITS 模式（节目单拉取、**10 个音源 provider**、封面歌词缓存、独立播放页、预加载）
- Windows 专用 DSD 后端（BASS/BASSDSD/BASSASIO）
- 开发者模式（实时日志窗口 + 本地日志文件）

**R502 发布主线（2026-06-09）**：
- 在线首页今日榜单未生成与网络/JSON 不可用拆成两个状态。
- 今日榜单尚未生成时默认显示昨日榜单，并只在进入榜单详情页后，在"今日趋势"标题右侧显示普通叹号 SVG，Tooltip 为"榜单于UTC+10更新"。
- 只有网络条件不好、远程 JSON 拉取失败或 JSON 不可用时，榜单详情页才显示黄色叹号 SVG，Tooltip 为"推荐不可用，请检查网络环境。"。
- 首页榜单卡片不再显示任何状态叹号，保持首页视觉干净。
- 版本号同步到 `R502`，安装包输出为 `PrismWave-Setup-R502.exe`。

**R501_fix2 发布主线（2026-06-08）**：
- 在线首页改为读取 `prismwave-hits/home/latest_home.json` 的 schema 7 每日 Top100 推荐。
- `prismwave-hits/scripts/build_home.py` 每天北京时间 10:00 由 GitHub Actions 自动生成 `home/latest_home.json` 和 `home/home_recommendations-YYYY-MM-DD.json`。
- 首页打开在线模式时先检查本地当天缓存；没有当天缓存才拉取远程；远程失败时只回退昨天缓存，并在榜单详情页"今日趋势"右侧显示黄色感叹号。
- 设置页新增"在线"分类，包含在线模式开关和"拉取今日榜单"刷新 SVG 按钮。
- 修复冷启动无缓存时的首页失败：app 内置 `assets/home/latest_home.json` 作为最后兜底；远程/昨天缓存都不可用时仍能进入首页，并显示黄色告警。
- Windows 无边框窗口支持从边缘和四角自由拉伸。
- 版本号同步到 `R501_fix2`，安装包输出为 `PrismWave-Setup-R501_fix2.exe`。

**R501_fix 发布主线（2026-06-07）**：
- 玻璃拟态 UI 继续细化，整体更简约高级。
- 全局字体改为 Resource Han Rounded CN/TW，支持简体与繁体中文。
- 底部主播放键与播放进度条移除红色调，统一为白色玻璃质感。
- fullplay 自动歌词匹配性能优化：避免重建重复触发、LRCLIB 优先快速返回、QQ 兜底限时与并发、旁置 `.lrc/.qrc` 优先于嵌入歌词。
- 版本号同步到 `R501_fix`，安装包输出为 `PrismWave-Setup-R501_fix.exe`。

**R501 发布主线（2026-06-06）**：
- 首页今日趋势徽标改为 `TOP100`。
- 首页刷新按钮可在应用内刷新推荐歌曲与专辑，趋势榜单仍独立更新。
- 在线搜索 / 在线播放接入更多非视频音乐源，并修复网易云搜索结果封面、歌手、专辑和播放兜底问题。
- fullplay 播放在线歌曲时会自动匹配在线歌词，并在拿到播放时长后重试一次自动匹配。
- 首页推荐模块标题下方的小字已移除，布局更清爽。
- 版本号同步到 `R501`，安装包输出为 `PrismWave-Setup-R501.exe`。

---

## 2. 仓库结构

```
PrismWave/
├── app/                          Flutter 主工程
│   ├── lib/main.dart             入口
│   ├── lib/src/
│   │   ├── controllers/          状态控制器（Riverpod StateNotifier）
│   │   ├── services/             服务层（音频解析、元数据、缓存等）
│   │   ├── state/                状态类定义
│   │   ├── ui/                   UI 页面与组件
│   │   ├── models/               数据模型
│   │   └── i18n/                 多语言字符串
│   ├── third_party/just_audio_media_kit/  自定义 patch 的 just_audio_media_kit
│   └── pubspec.yaml              依赖声明
├── native/windows_dsd/           BASS/BASSDSD/BASSASIO 原生运行库
│   └── vendor/                   bass24/, bassasio14/, bassdsd24/
├── prismwave-hits/               HITS 节目单生成仓库（本地副本）
│   ├── scripts/build_hits.py     节目单生成脚本
│   ├── scripts/build_home.py     在线首页 Top100 生成脚本
│   ├── home/                     每日首页推荐 JSON
│   ├── config/station.json       源权重与配置
│   ├── schedules/                每日生成的 JSON 节目单
│   └── data/                     辅助数据
├── installer/                    Inno Setup 安装包脚本
├── dist/                         构建产物（安装包、Release Notes）
├── release/                      历史 Release 安装包
├── assets/                       仓库级资源（logo 等）
├── tools/flutter/                内置 Flutter SDK
└── backups/                      备份文件
```

### 关键代码文件索引

| 文件 | 路径 | 说明 |
|------|------|------|
| 入口 | `app/lib/main.dart` | 应用入口 |
| 主页面 | `app/lib/src/ui/main_page.dart` | 应用主框架、侧栏、路由 |
| 顶栏 | `app/lib/src/ui/window_top_bar.dart` | 自定义无边框顶栏 |
| 播放控制 | `app/lib/src/controllers/playback_controller.dart` | 播放核心（播放/暂停/队列/输出模式/日志） |
| 音乐库控制 | `app/lib/src/controllers/library_controller.dart` | 音乐库管理 |
| HITS 控制 | `app/lib/src/controllers/hits_controller.dart` | HITS 主控 |
| 设置控制 | `app/lib/src/controllers/app_settings_controller.dart` | 设置管理 |
| HITS 解析 | `app/lib/src/services/hits_audio_resolver_service.dart` | 9 个在线音源解析（~1900 行） |
| HITS 节目单 | `app/lib/src/services/hits_manifest_service.dart` | 节目单拉取 |
| HITS 缓存 | `app/lib/src/services/hits_media_cache_service.dart` | 封面/歌词/音频缓存 |
| HITS 调度 | `app/lib/src/services/hits_scheduler.dart` | 节目时间调度 |
| HITS 过渡页 | `app/lib/src/ui/hits_transition_page.dart` | HITS 过渡动画（含预渲染位图优化） |
| HITS 播放页 | `app/lib/src/ui/hits_fullplay_page.dart` | HITS 独立播放页 |
| HITS 可用性 | `app/lib/src/ui/hits_availability.dart` | 可用性检测 UI |
| HITS 不可用页 | `app/lib/src/ui/hits_unavailable_page.dart` | 不可用页 |
| HITS 共享 UI | `app/lib/src/ui/hits_ui_shared.dart` | 共享 UI 组件 |
| 在线主控 | `app/lib/src/controllers/online_controller.dart` | 在线首页、搜索、专辑、在线播放与队列主控 |
| 在线首页服务 | `app/lib/src/services/netease_home_service.dart` | 远程 daily home、首页按日缓存、昨日兜底、封面兜底 |
| 在线搜索服务 | `app/lib/src/services/online_search_service.dart` | 在线搜索聚合（本地库 + provider 结果） |
| 在线缓存 | `app/lib/src/services/online_media_cache_service.dart` | 在线封面缓存、Host 级请求头、URL retry |
| 在线端点 | `app/lib/src/services/netease_endpoints.dart` | 网易云端点、封面 URL 升级、picId 加密 URL |
| 在线首页 UI | `app/lib/src/ui/online_home_panel.dart` | 首页推荐 UI、专辑推荐、封面展示 |
| 在线搜索 UI | `app/lib/src/ui/online_search_panel.dart` | 在线搜索页 |
| 在线榜单 UI | `app/lib/src/ui/online_top_playlist_panel.dart` | 今日趋势/Top playlist 详情页 |
| 在线专辑 UI | `app/lib/src/ui/online_album_detail_panel.dart` | 专辑详情与专辑播放 |
| 歌词读取 | `app/lib/src/services/lyrics_reader.dart` | 本地歌词解析 |
| 在线歌词 | `app/lib/src/services/online_lyrics_service.dart` | 在线歌词搜索 |
| QQ QRC | `app/lib/src/services/qqmusic_qrc_decoder.dart` | QQ 逐字歌词解码 |
| 元数据 | `app/lib/src/services/local_audio_metadata_service.dart` | 元数据读取 |
| 封面详情 | `app/lib/src/services/audio_file_details_service.dart` | 文件信息 |
| 封面搜索 | `app/lib/src/services/online_cover_service.dart` | 在线封面搜索 |
| DSD 后端 | `app/lib/src/services/windows_dsd_backend_service.dart` | DSD 播放后端 |
| DSD FFI | `app/lib/src/services/bass_ffi_bindings.dart` | BASS FFI 绑定 |
| 版本更新 | `app/lib/src/services/release_update_service.dart` | GitHub Releases 检查 |
| Quote 服务 | `app/lib/src/services/quote_service.dart` | 顶栏在线 quote 拉取 |
| 曲目时长 | `app/lib/src/services/track_duration_resolver.dart` | 曲目时长解析 |
| 无级滚动 | `app/lib/src/ui/middle_click_autoscroll.dart` | 中键无级滚动 |
| 节目单生成 | `prismwave-hits/scripts/build_hits.py` | Python 脚本生成每日节目单 |
| 首页推荐生成 | `prismwave-hits/scripts/build_home.py` | Python 脚本生成在线首页 Top100 JSON |
| 源配置 | `prismwave-hits/config/station.json` | 音源权重与拉取参数 |

---

## 3. 技术栈关键信息

| 项目 | 详情 |
|------|------|
| Flutter | `tools/flutter/bin/flutter`（3.29.3-stable） |
| SDK | `^3.11.1` |
| 音频后端 | just_audio (0.10.5) → just_audio_media_kit (third_party patch) → media_kit → libmpv |
| DSD 后端 | BASS/BASSDSD/BASSASIO 通过 FFI |
| 状态管理 | flutter_riverpod (2.6.1) StateNotifier 模式 |
| 元数据 | metadata_god (1.1.0) + dart_tags fallback |
| 窗口管理 | window_manager (0.5.1) |
| 毛玻璃 | flutter_acrylic (1.1.4) |
| Win32 API | win32 (5.15.0) — ShellExecuteW 等 |
| YouTube | youtube_explode_dart (3.0.5) |
| flutter_rust_bridge | 锁定 2.11.1（匹配 metadata_god） |
| 依赖覆盖 | just_audio_media_kit → third_party 本地路径 |

---

## 4. 已完成功能清单

### 4.1 应用壳层与窗口
- Windows 无边框窗口 + 毛玻璃/acrylic 背景
- 自定义顶栏（WindowTopBar）
- 启动白屏/窗口隐藏修复（flutter_window.cpp 首帧回调 + ForceRedraw）
- 关键文件: `main.dart`, `flutter_window.cpp`, `window_top_bar.dart`

### 4.2 本地音乐库
- 添加/删除音乐文件夹、全库重扫、搜索
- 音乐库/专辑/艺术家/我最爱的 四个主视图
- 我最爱的列表持久化
- 库和我最爱的支持拖拽排序（独立持久化，不影响播放队列）
- 低特效模式
- 关键文件: `library_controller.dart`, `library_scanner.dart`, `library_state.dart`

### 4.3 元数据与封面
- metadata_god 主入口，dart_tags MP3 fallback，RIFF INFO WAV fallback
- 外部封面搜索增强（同名图片/常见目录封面名自动匹配）
- 封面组件支持 embedded bytes 失败后回退到外部文件
- 关键文件: `local_audio_metadata_service.dart`, `audio_file_details_service.dart`

### 4.4 播放控制
- 播放/暂停/上一首/下一首、列表循环/单曲循环/随机播放
- 音量控制、进度拖动、淡入淡出
- 音频输出模式切换、播放设备切换
- 播放日志/开发者模式
- 关键文件: `playback_controller.dart`, `playback_state.dart`

#### 4.4.1 mpv 缓冲配置

```dart
// playback_controller.dart
void _initializePlayer() {
  JustAudioMediaKit.nativeMpvProperties = const {
    'cache-secs': '12',       // 网络流 12 秒缓存
    'cache-on-disk': 'no',    // 内存缓存
    'audio-buffer': '0.5',    // 音频输出缓冲 500ms
  };
  _player = AudioPlayer();
  ...
}
```

#### 4.4.2 音频输出模式

三种模式，底层都走 WASAPI：

| 模式 | `preferWasapi` | `preferWasapiExclusive` | `fallbackToWasapiShared` |
|------|---------------|------------------------|------------------------|
| Compatibility (MPV) | true | false | true |
| WASAPI Shared | true | false | true |
| WASAPI Exclusive | true | true | true |

**注意**: Compatibility 模式也使用 WASAPI shared，不再依赖 mpv 的音频后端自动检测。

#### 4.4.3 解码错误恢复

三层解码错误处理：skip track → treat as completion → soft/reload recovery
检测关键词: `decode`, `decoding`, `format`

#### 4.4.4 开发者控制台（R401_fix 修复）

使用 `ShellExecuteW`（win32 包）打开日志查看器窗口，不再使用 `Process.start` 的 `DETACHED_PROCESS` 模式。

```dart
// playback_controller.dart — ShellExecuteW 替代 Process.start
import 'package:win32/win32.dart';
ShellExecute(0, 'open', 'cmd.exe', '/c start "" "path/to/logfile"', ...);
```

开发者模式现在**始终**在启用时打开日志窗口（不再受 `kReleaseMode` 限制）。

### 4.5 播放队列
- 队列按钮、左侧栏切换成播放列表
- 队列拖拽排序、hover 显示移除按钮
- 队列顺序与库/我最爱的列表解耦
- 关键文件: `playback_controller.dart`

关键行为：
- 点播时过滤不可播放条目 → 旋转列表使选中歌曲为第一首 → currentIndex=0
- 拖拽库/我最爱的不影响当前播放队列
- 移除的不是当前曲 → 只更新队列不中断播放
- 移除的是当前曲 → 切到合适的下一首
- `playStandaloneTrack()` 用于 HITS 模式，不建立常规队列上下文
- 播放模式 `loop/single/shuffle` 循环

### 4.6 歌词系统
- 本地歌词加载、在线歌词搜索与缓存
- 本地/在线歌词源切换、歌词偏移调整
- 逐字歌词显示、QQ QRC 解码
- 关键文件: `lyrics_reader.dart`, `online_lyrics_service.dart`, `qqmusic_qrc_decoder.dart`

### 4.7 详情页与删除
- 右击歌曲 → 详情页（文件信息）
- 红色删除按钮 → 确认弹窗 → 可选"同时删除源文件"
- 删除源文件时尝试一并删除同名 .lrc

### 4.8 无级滚动
- 中键触发/取消无级滚动
- 关键文件: `middle_click_autoscroll.dart`

### 4.9 设置
- 设置页分"基础/在线/播放"三个分类
- 基础：语言切换、顶栏空闲模式、在线 quote 拉取、版本更新检查
- 在线：在线模式开关、"拉取今日榜单"刷新 SVG 按钮
- 播放：输出模式、设备、WASAPI/DSD 相关设置
- 关键文件: `app_settings_controller.dart`, `release_update_service.dart`

### 4.10 版本更新检查
- `release_update_service.dart`: 调用 GitHub Releases API
- 当前版本常量: `kCurrentReleaseVersion = 'R502'`
- `installer/PrismWaveSetup.iss`: `#define MyAppVersion "R502"`

### 4.11 在线模式（普通在线音乐）

> 这是 R401_fix2/R501 期间重点增强的能力，和 HITS 电台模式共享部分音源解析服务，但入口、状态和播放队列是独立的。

#### 4.11.1 入口与默认行为

- `app/lib/src/ui/main_page.dart`
  - `MainSection _section = MainSection.home;`
  - 应用打开后默认进入首页，而不是音乐库。
  - 如果在线模式被关闭，监听 `appSettingsProvider.onlineModeEnabled`，当当前页面是 Home/Search 时自动切回 Library。
- `app/lib/src/state/app_settings_state.dart`
  - `onlineModeEnabled` 默认值为 `true`。
- `app/lib/src/controllers/app_settings_controller.dart`
  - 读取偏好 `online.modeEnabled`，没有历史设置时默认 `true`。

#### 4.11.2 在线首页推荐

核心文件：
- `app/lib/src/controllers/online_controller.dart`
- `app/lib/src/services/netease_home_service.dart`
- `app/lib/src/models/online_recommendation.dart`
- `app/lib/src/ui/online_home_panel.dart`
- `app/lib/src/ui/online_top_playlist_panel.dart`

首页推荐数据来源：
- 歌曲推荐 / 今日趋势 / 标签 section：使用 `prismwave-hits` 仓库生成的 schema 7 daily home JSON。
- JSON 由 `prismwave-hits/scripts/build_home.py` 生成，聚合 Last.fm（有 key 时）、Audius、Deezer、iTunes 等来源，整理为全球热门 Top100。
- GitHub Actions 文件：`prismwave-hits/.github/workflows/build_home.yml`。
- 定时：`0 2 * * *` UTC，即北京时间每天 10:00。
- 默认快速路径不批量解析音频 URL；需要时可设 `PRISMWAVE_HOME_RESOLVE_AUDIO=1`。普通在线播放点击后仍通过 resolver 解析实际播放源。
- 专辑推荐仍可从网易云新专辑接口补充，但今日趋势与推荐歌曲不再依赖网易云榜单兜底。

remote daily home 地址：
```text
https://raw.githubusercontent.com/shanbei2033/prismwave-hits/main/home/latest_home.json
```

生成文件：
```text
prismwave-hits/home/latest_home.json
prismwave-hits/home/home_recommendations-YYYY-MM-DD.json
```

缓存位置：
```text
%LOCALAPPDATA%\PrismWave\online_home_cache\home-YYYY-MM-DD.json
%LOCALAPPDATA%\PrismWave\online_home_cache\home-YYYY-MM-DD.stamp
%LOCALAPPDATA%\PrismWave\online_home_cache\home.json
%LOCALAPPDATA%\PrismWave\online_home_cache\home.stamp
```

缓存规则：
- `NeteaseHomeService._kSchemaVersion = 7`
- `editionDate` 使用北京时间日期，客户端用北京时间判断今天/昨天。
- 启动在线首页时先查当天缓存 `home-YYYY-MM-DD.json`，命中则不拉远程。
- 当天缓存不存在时拉取 remote `latest_home.json`，并按 remote 自身 `editionDate` 写入对应日期缓存。
- 远程拉取失败时才回退昨天缓存/内置兜底，并设置 `recommendationsUnavailable=true`。
- GitHub Actions 每天北京时间 10:00 生成当天 JSON；北京时间 00:00-10:00 期间 remote 通常仍是昨日 `editionDate`，这是正常窗口期，不等同于用户网络失败。
- 当 remote JSON 可用但不是当天时，默认显示昨日榜单，并设置 `recommendationsPendingGeneration=true`；仅榜单详情页使用普通叹号 SVG，Tooltip 为"榜单于UTC+10更新"。
- 如果没有当天缓存、没有昨天缓存，且远程不可用，会读取 app 内置 `assets/home/latest_home.json` 作为冷启动兜底，并设置 `recommendationsUnavailable=true`。
- 只有网络/JSON 真不可用并使用缓存/内置兜底时，榜单详情页的"今日趋势"标题右侧才显示黄色叹号 SVG，Tooltip 为"推荐不可用，请检查网络环境。"。
- 手动刷新入口：主页顶部刷新按钮、设置 > 在线 > "拉取今日榜单"刷新 SVG 按钮；只有网络/JSON 真不可用时提示"拉取失败"。
- remote payload 必须满足 schema >= 7、`topPlaylist.tracks.length >= 100`、至少 80 首有 `coverUrl`，否则视为不可用。

#### 4.11.3 在线首页启动性能修复（2026-06-06 历史记录）

> 注意：本节记录 R501 时的旧策略背景。2026-06-08 起，在线首页推荐改为"当天缓存优先、失败只回退昨天并显示告警"，不再把任意旧缓存作为正常首屏结果。

用户现象：
- 应用打开后要等很久才进入首页。
- 用户看到的体验像“什么也没更新，但首页卡住”。

根因有三条：
1. `main.dart` 在显示窗口前先跑 `MetadataGod.initialize()`。该初始化可能慢，导致窗口迟迟不显示。
2. 默认进入在线 Home 后，`OnlineHomePanel.initState()` 首帧后调用 `ensureHomeLoaded()`；旧实现如果缓存过期，会等待 fresh remote home、网易云专辑和大陆封面兜底都完成后才显示首页。
3. `OnlineController` 构造时同时 warm up resolver 和 home service；home warm-up 是真实网易云网络请求，会和首页实际加载抢网络资源。

已实施修复：
- `app/lib/main.dart`
  - `runApp()` 后使用 `unawaited(_completePlatformBootstrap())`。
  - `_completePlatformBootstrap()` 先 `_configureWindow()`，窗口显示/聚焦后才后台初始化元数据。
  - Windows acrylic 效果改成 `unawaited(_setWindowsAcrylicEffect())`，不阻塞窗口显示。
- `app/lib/src/services/local_audio_metadata_service.dart`
  - 新增共享 Future：`initializeLocalAudioMetadataBackend()`。
  - `readBestEffortAudioMetadata()` 读元数据前 await 这个共享 Future。
  - 这样 `main.dart` 可以后台预热，真正读取时仍有初始化保护。
- `app/lib/src/controllers/online_controller.dart`
  - 删除 home service 启动 warm-up。
  - resolver warm-up 延后 3 秒，避免和首页首屏抢网络。
  - `ensureHomeLoaded()` 增加 `Stopwatch` 和开发者日志。
  - 2026-06-08 后只接受当天缓存；没有当天缓存则拉 remote daily，失败再用昨天缓存并显示告警。
  - 使用 `_homeSeq` 防止旧异步结果覆盖新状态。
  - 使用 `_homeBackgroundRefreshRunning` 防止重复后台刷新。
- `app/lib/src/services/netease_home_service.dart`
  - `loadCachedBundle()` 只读取当天 `home-YYYY-MM-DD.json`。
  - `loadYesterdayCachedBundle()` 只读取昨天缓存，并返回 `recommendationsUnavailable=true`。
  - `loadBundledFallbackBundle()` 读取 app 内置 `assets/home/latest_home.json`，用于无缓存冷启动兜底。
  - `loadRemoteDailyBundle()`：只拉 remote daily home，不等待网易云专辑。
  - `enrichMainlandCoverFallbacks()`：后台做封面大陆兜底并写回缓存。
  - `_fetchFresh()` 不再在返回前等待 `_withMainlandCoverFallbacks()`。

新增开发者模式日志前缀：
```text
online.home.load.start
online.home.load.ready
online.home.load.failed
online.home.load.no-cache
online.home.load.remote-daily-fast.failed
online.home.load.stale-after-manual-refresh
online.home.refresh-background.start
online.home.refresh-background.ready
online.home.refresh-background.failed
online.home.cover-enrich.start
online.home.cover-enrich.ready
online.home.cover-enrich.failed
```

验证结果（2026-06-06）：
```powershell
cd F:\Project\PrismWave\app
..\tools\flutter\bin\dart.bat analyze lib\src lib\main.dart
..\tools\flutter\bin\flutter.bat test test\playback_strategy_test.dart
..\tools\flutter\bin\flutter.bat build windows --release
```

三项均通过。最终代码产物：
```text
F:\Project\PrismWave\app\build\windows\x64\runner\Release\data\app.so
LastWriteTime: 2026-06-06 00:18:24
```

注意：Flutter Windows 的 `prismwave_demo.exe` 外壳时间可能不变，Dart 代码实际在 `data/app.so`。

#### 4.11.4 在线播放队列与首播延迟修复

用户现象：
- 点击首页/榜单/专辑中的在线歌曲时，播放前卡 10 秒以上。
- 原因是旧的 `OnlineController.playOnlineTrack()` 会先把整个 contextTracks 全部解析成播放 URL，再调用播放。

已实施修复：
- `OnlineController.playOnlineTrack()` 先解析用户点击的 picked 曲目。
- picked 曲目解析成功后马上播放。
- 根据 section/专辑上下文构造完整 metadata queue，立即发布到 `PlaybackState.currentPlaylist`。
- 其他曲目在后台并发解析，解析成功后调用 `PlaybackController.replaceQueuePreservingCurrent()` 更新当前队列。
- `PlaybackController.playFromPlaylist()` / `replaceQueuePreservingCurrent()` 支持 `includeUnplayableInQueue`，允许未解析的在线曲目先显示在队列里。
- 队列里的未解析曲目被用户直接点击时，会通过 `setQueueTrackResolver()` 触发 on-demand resolve；失败时通过 `setQueueTrackFailureHandler()` 记录失败并跳过或提示。

相关日志前缀：
```text
online.play.start
online.play.picked-resolved
online.play.picked-failed
online.queue.placeholder-published
online.queue.background-start
online.queue.background-patched
online.queue.resolve-on-demand
```

#### 4.11.5 在线队列中不可播放歌曲修复

用户现象：
- 自动播放到某些在线歌曲时会跳过。
- 直接点击该歌曲时显示“播放已暂停”，再点播放按钮也无法播放。

修复要点：
- 在线队列保留 metadata-only track，但播放前必须 resolve 成带 `playbackUrl` 的 Track。
- resolver cache key 不能让 provider/id 为空的 metadata 行互相碰撞；现在使用 `canonicalKey`。
- 失败的队列项会通过失败处理器处理，不再把无法播放的空 URL 直接送进播放器。

#### 4.11.6 在线搜索与音源

公共在线搜索现在不再返回 bilibili 源。

相关文件：
- `app/lib/src/services/hits_audio_resolver_service.dart`
- `app/lib/src/services/online_search_service.dart`

Provider 现状：
- 在线搜索 UI：Audius、NetEase、Kuwo、Migu、QQ、Kugou、Taihe。
- HITS 内部解析：仍保留 bilibili / bilivideo / YouTube 等视频源，因为 HITS 电台需要更强兜底。

Taihe（千千音乐/百度音乐）已添加：
- 搜索 endpoint：`https://music.taihe.com/v1/search`
- 播放链接 endpoint：`/song/tracklink`
- 使用 `appid=16073360` 和静态签名后缀。

注意：
- 用户明确要求“搜索内容出来的歌曲资源，不要有 bilibili 源”。这只针对普通在线搜索 UI。
- 不要把 HITS 的 bilibili fallback 一并删掉，除非用户明确要求。

#### 4.11.7 在线专辑封面修复

用户现象：
- 专辑推荐中部分封面不显示，例如“情绪失格”。

根因：
- 网易云返回的原始封面可能很大（曾测到 6 MB 以上甚至约 11 MB）。
- `OnlineMediaCacheService` 为保护内存/磁盘会拒绝过大的图片。

修复：
- `netease_endpoints.dart` 的 `upgradeCoverUrl()` 会给网易云图片加 `?param=512y512`。
- `NeteaseHomeService._loadNewAlbums()` 如果 `picUrl` / `blurPicUrl` 不可用，会通过 album detail 兜底。
- `OnlineMediaCacheService` 对网易云等 host 使用合适 Referer/User-Agent。
- `OnlineCoverImage` 支持 URL retry，增强海外图片或特殊 CDN 的容错。

#### 4.11.8 首页歌曲封面大陆兜底

remote daily home 中很多 Last.fm / Deezer / Audius section 没有大陆可访问封面，甚至没有 `coverUrl`。

修复：
- `NeteaseHomeService._withMainlandCoverFallbacks()` 会对需要兜底的 track 搜网易云歌曲封面。
- 搜索 query 包括：
  - `artist title`
  - `title album`
  - `title`
- 匹配评分看标题、歌手、时长。
- 网易云 `picId` 不能直接拼 CDN，需要用加密算法生成 URL；实现位于 `netease_endpoints.dart` 的 `neteaseCoverUrlFromPicId()`。
- 该兜底现在不阻塞首屏，后台完成后写回 home cache 并更新 UI。

#### 4.11.9 首页每日更新分析

用户怀疑：首页推荐歌曲没有每天更新，可能是 `prismwave-hits` 的定时任务问题。

已确认：
- `prismwave-hits` remote workflow 在 2026-06-01 至 2026-06-04 多次 schedule 成功。
- remote `home/latest_home.json` 有新的 `editionDate`。
- 真正问题是旧 app 没有读取 `prismwave-hits/home/latest_home.json`，而是直接走网易云 endpoints，并按 12 小时/本地缓存逻辑刷新。

当前修复（2026-06-08）：
- `NeteaseHomeService` 读取 remote schema 7 daily home JSON。
- cache freshness 由 `editionDate` 对齐当前北京时间日期。
- 本地当天缓存存在时不拉远程；当天缓存不存在才拉 `latest_home.json`。
- remote 不可用或不是当天时不再回退网易云榜单，只回退昨天缓存，并在 UI 显示黄色告警。
- `prismwave-hits/scripts/build_home.py` 已生成 `home/home_recommendations-2026-06-08.json` 和新的 `home/latest_home.json`，本地验证 Top100 全部有 `coverUrl`。
- 2026-06-08 后续修复：线上 `raw.githubusercontent.com` 一度仍是 schema 1 / `daily-top-10` / 2026-06-07，导致无缓存冷启动报 `All online home requests failed`；已将 `prismwave-hits` 推送到 commit `a38efc6`，raw 已验证为 schema 7 / Top100 / coverUrl 100。

2026-06-09 榜单未生成/网络失败状态拆分：
- 用户在北京时间 00:00-10:00 本机测试时，remote `latest_home.json` 仍可能是昨日 `editionDate`，但 schema 7 / Top100 / coverUrl 均可用。
- 旧逻辑把 `editionDate != today` 直接当作 `OnlineHomeException(unavailable)`，主页和设置页都会显示"拉取失败"，容易误判为网络问题。
- `NeteaseHomeService.refreshLiveHome({allowLatestAvailable: true})` 和 `loadRemoteDailyBundle({allowLatestAvailable: true})` 现在可返回可用但非当天的 latest payload，并设置 `recommendationsPendingGeneration=true`。
- `recommendationsPendingGeneration=true`：默认显示昨日榜单，仅在榜单详情页标题旁使用 `app/assets/icons/chart_notice.svg` 普通叹号，Tooltip 为"榜单于UTC+10更新"。
- `recommendationsUnavailable=true`：仅用于网络/JSON 真不可用时的缓存/内置兜底，仅在榜单详情页标题旁使用黄色 `chart_notice.svg`，Tooltip 为"推荐不可用，请检查网络环境。"。
- `OnlineController.refreshHomeRecommendations()` 返回 `OnlineHomeRefreshResult.fresh/latestAvailable/failed`，UI 根据结果显示成功、"今日榜单尚未生成，已显示昨日榜单"或失败。
- 验证：`dart analyze lib\main.dart lib\src` 无问题。

### 4.12 HITS 模式（广播电台）

#### 4.12.1 HITS 概况

HITS（广播电台模式）是 PrismWave 的核心特色功能：
- 从 `prismwave-hits` 仓库拉取节目单（`latest.json` + 每日 schedule JSON）
- 根据 `service_windows` / `off_air_windows` 判断 ready/standby/offAir 状态
- 当前节目按 UTC 时间中途进入
- 在线音源解析 → 封面/歌词/音频缓存 → 独立播放页
- 播放当前曲时后台预取下一首/下两首资源

节目单 manifest 默认地址：
```
https://raw.githubusercontent.com/shanbei2033/prismwave-hits/main/latest.json
```

#### 4.12.2 HITS 入口

HITS 入口位于左侧菜单栏"我最爱的"下方：

```
📚 音乐库
💽 专辑
🎤 艺术家
❤️ 我最爱的
H   HITS          ← 点击后从右侧滑入过渡页
```

点击 HITS 按钮后：
1. 先切音频输出为 WASAPI Shared（避免独占模式破音）
2. 加载页从右边滑入（480ms）+ 动态模糊
3. 显示 HITS 过渡动画 → 检查可用性 → 进入播放页

#### 4.12.3 HITS 在线音源（10 个 provider）

| Provider | 类型 | 状态 |
|----------|------|------|
| bilibili | 视频平台 | 已有 |
| bilivideo | 视频平台 | 已有 |
| youtube | 视频平台 | 已有 |
| audius | 音乐平台 | 已有 |
| netease (网易云) | 音乐平台 | 已落地 |
| kuwo (酷我) | 音乐平台 | 已落地 |
| migu (咪咕) | 音乐平台 | 已落地 |
| qq (QQ音乐) | 音乐平台 | 已落地 |
| kugou (酷狗) | 音乐平台 | 已落地 |
| taihe (千千音乐/百度音乐) | 音乐平台 | 已落地 |

所有 resolver 实现在: `hits_audio_resolver_service.dart`（约 4000 行）

解析链路特性：
- 并发波次搜索（`_firstSuccessful`）
- 地区路由（`_HitsRoutingProfile.orderProviders`）
- 成功率记忆（`_recordProviderResult`）
- 失败缓存（`_failedResolveCacheTtl`）
- 8 个搜索变体 query
- 匹配评分（标题/歌手/时长/variant penalty）

**不可播放 URL 过滤（R401_fix 新增）**：
```dart
// hits_controller.dart
static final RegExp _nonPlayableUrlPattern = RegExp(
  r'(?:cdnt?-preview\.dzcdn\.net|audio-ssl\.itunes\.apple\.com|'
  r'preview\.music\.apple\.com)',
);
```

#### 4.12.4 HITS 节目单生成（prismwave-hits 仓库）

节目单由 Python 脚本 `build_hits.py` 通过 GitHub Actions 每日自动生成。

**音源配置** (`config/station.json`)：

| 源 | 权重 | 说明 |
|----|------|------|
| lastfm_global | 0.28 | Last.fm 全球榜（元数据） |
| audius_trending | 0.15 | Audius 热门（可播放） |
| deezer_chart | 0.15 | Deezer 排行榜（元数据，audio_url=null） |
| lastfm_tag | 0.10 | Last.fm 标签榜（元数据） |
| audius_trending_monthly | 0.08 | Audius 月度热门 |
| audius_genre | 0.08 | Audius 分类热门 |
| itunes_rss | 0.08 | iTunes RSS（元数据，audio_url=null） |
| bootstrap_seed | 0.08 | 引导种子 |

**关键设计**：
- Deezer 和 iTunes **仅贡献元数据**（标题、艺术家、封面），`audio_url` 设为 `null`，播放时通过 Audius 等渠道匹配
- `candidate_resolution_limit`: 400（确保充足候选）
- 加权随机选择（按 edition date 种子，保证每天不同但可重现）
- 每日生成约 120+ 首不重复曲目

#### 4.12.5 HITS 关键文件

| 文件 | 用途 |
|------|------|
| `hits_controller.dart` | HITS 主控（初始化、ticker、播放同步、封面/歌词加载、URL 过滤） |
| `hits_state.dart` | HITS 状态 |
| `hits_manifest_service.dart` | 节目单拉取 |
| `hits_scheduler.dart` | 节目时间调度 |
| `hits_audio_resolver_service.dart` | 在线音源解析（10 provider；普通在线搜索会排除 bilibili/bilivideo/youtube） |
| `hits_media_cache_service.dart` | 封面/歌词/音频缓存 |
| `hits_transition_page.dart` | 过渡动画页（含预渲染位图优化） |
| `hits_fullplay_page.dart` | HITS 独立播放页 |
| `hits_unavailable_page.dart` | 不可用页（UI） |
| `hits_availability.dart` | 可用性检测（UI） |
| `hits_ui_shared.dart` | 共享 UI 组件 |

#### 4.12.6 HITS 过渡动画优化（R401_fix）

呼吸动画阶段使用预渲染位图（`RenderRepaintBoundary.toImage`），在入场动画完成后捕获。呼吸和退出阶段使用单个 `RawImage` widget 仅做 GPU 纹理变换（scale + opacity），消除每帧文字光栅化和大半径模糊阴影计算。

```dart
// hits_transition_page.dart
import 'dart:ui' as ui;
// 注意：dart:ui 导入后 lerpDouble、ImageFilter 等需加 ui. 前缀
// 同时需要 import 'package:flutter/rendering.dart'; 以获取 RenderRepaintBoundary
```

#### 4.12.7 HITS 与普通播放的关系

- 进入 HITS 前捕获 `PlaybackSessionSnapshot`（队列、当前曲、index、时间、模式、播放状态）
- HITS 内使用 `playStandaloneTrack()`（无队列上下文）
- 退出 HITS 时 `restoreSession()` 恢复原播放状态
- HITS dispose 时自动恢复

### 4.13 Windows 专用 DSD 后端

- BASS/BASSDSD/BASSASIO FFI 绑定
- `.dsf/.dff` 分流到专用 backend
- ASIO 设备枚举 + 选择 UI
- DSD 回退原因写入开发者日志
- **已删除**旧的"DSD 自动强制独占"逻辑；回退后沿用用户当前输出模式
- 关键文件: `bass_ffi_bindings.dart`, `windows_dsd_backend_service.dart`
- 原始库位置: `native/windows_dsd/vendor/bass24/`, `bassasio14/`, `bassdsd24/`

未完成：
- 切换 DSD 设备后当前 .dsf/.dff 不会自动重载到新设备
- 需真机 ASIO 环境验证

### 4.14 国际化
- 单文件 `app_strings.dart` 支持多语言字符串
- 关键文件: `app/lib/src/i18n/app_strings.dart`

---

## 5. 当前未完成项与已知风险

### 5.1 音源成功率验证
6 个中国 provider 已实现（NetEase、Kuwo、Migu、QQ、Kugou、Taihe），但未经充分真机/长期验证，不确定各 API 是否仍可用。

普通在线搜索已经排除 bilibili / bilivideo / YouTube；HITS 仍保留这些视频源作为兜底。

### 5.2 独占模式破音（R401_fix1 已修复）

WASAPI Exclusive 模式在所有播放场景（本地文件 + HITS）的破音/卡顿问题已修复。

**根因**：mpv 的 WASAPI exclusive 实现把端点缓冲硬编码为设备 period（~3-10ms），在某些设备上无法稳定喂数据导致 underrun → 破音。`audio-buffer` 是 player-side 解码缓冲，无法影响 ao 层的 WASAPI 端点缓冲；`mpv_set_property_string` 在 `mpv_initialize()` 之后调用也来不及。

**修复方案**（commit `94495cd`）：
1. **二进制修补 libmpv**，把 WASAPI exclusive 端点缓冲改成 ~50ms，dll 放在 `native/libmpv/libmpv-2.dll`
2. `app/windows/CMakeLists.txt` 在打包时把 `native/libmpv/libmpv-2.dll` 复制到 `INSTALL_BUNDLE_LIB_DIR` 和 `${CMAKE_BINARY_DIR}/libmpv/`，覆盖 `media_kit_libs_windows_audio` 提供的默认 dll
3. `mediakit_player.dart` 在 `preferWasapiExclusive` 时也通过 `setProperty(_player, 'wasapi-exclusive-buffer', '50000')` 设置（前置准备，配合修补后的 libmpv 生效）

**注意事项**：
- 自定义 libmpv 是 mpv v0.41.0 二进制修补版本（注释里标 v0.36.0 是旧描述，实际打包的是 0.41.0），切换 media_kit 大版本时要重新核对 dll 兼容性
- `nativeMpvOptions` 字段（`platform_player.dart` / `real.dart`）作为备用通道保留，当前修复方案没有走它

### 5.3 DSD 设备切换
切换 DSD 设备后当前 .dsf/.dff 不会自动重载到新设备。

### 5.4 缓存清理
升级后建议清理旧缓存：
- HITS manifest: `%LOCALAPPDATA%\PrismWave\hits_manifest_cache`
- HITS 媒体: `F:/PrismWave_HITS_Cache`
- 在线首页缓存: `%LOCALAPPDATA%\PrismWave\online_home_cache`

### 5.5 在线首页后台刷新
2026-06-08 后在线首页改为按北京时间日期缓存：
- 启动在线模式时先查当天 `home-YYYY-MM-DD.json`。
- 当天缓存命中则直接显示，不主动拉远程。
- 当天缓存不存在才拉取 `home/latest_home.json`。
- remote 不是当天但 JSON 可用时显示昨日榜单，并设置 `recommendationsPendingGeneration=true`。
- 拉取失败时优先使用昨天缓存；没有昨天缓存时使用 app 内置 `assets/home/latest_home.json` 冷启动兜底，并设置 `recommendationsUnavailable=true`。
- 北京时间 00:00-10:00 期间，remote 可能尚未生成当天 JSON；手动刷新会使用昨日榜单并提示榜单尚未生成，不应提示网络拉取失败。
- `recommendationsPendingGeneration=true` 时，仅榜单详情页的"今日趋势"标题旁显示普通叹号 SVG，Tooltip 为"榜单于UTC+10更新"。
- `recommendationsUnavailable=true` 时，仅榜单详情页的"今日趋势"标题旁显示黄色叹号 SVG，Tooltip 为"推荐不可用，请检查网络环境。"。

注意事项：
- 如果用户反馈“首页显示的是昨天内容”，先看叹号颜色/Tooltip：普通叹号表示当天榜单尚未生成；黄色叹号才表示远程 JSON 拉取失败或不可用。
- 对比 `%LOCALAPPDATA%\PrismWave\online_home_cache\home-YYYY-MM-DD.json` 的 `editionDate`、`schemaVersion`、`topPlaylist.tracks` 和 `coverUrl` 数量。
- 如果 `home/latest_home.json` remote 当天没有更新，要检查 `prismwave-hits` GitHub Actions，而不是先改 app。
- 无缓存冷启动失败时还要确认 `app/assets/home/latest_home.json` 是否被 `pubspec.yaml` 的 assets 打包，并检查 `build/windows/x64/runner/Release/data/flutter_assets/assets/home/latest_home.json` 是否存在。
- 手动刷新入口有两个：在线首页顶部刷新按钮、设置 > 在线 > "拉取今日榜单"。

---

## 6. 构建与产物

### 构建命令
```powershell
cd F:\Project\PrismWave\app
..\tools\flutter\bin\flutter.bat build windows --release
```

或使用环境中的 Flutter：
```powershell
cd F:\Project\PrismWave\app
flutter pub get
flutter build windows --release
```

### 构建产物
- EXE: `app/build/windows/x64/runner/Release/prismwave_demo.exe`
- DSD 运行库 (`bass.dll`, `bassdsd.dll`, `bassasio.dll`) 由 CMake 复制
- 安装包: 通过 Inno Setup 打包，输出到 `dist/`
  - ISCC 路径: `C:\Users\Admin\AppData\Local\Programs\Inno Setup 6\ISCC.exe`
  - 脚本: `installer/PrismWaveSetup.iss`

### 分析命令
```powershell
F:\Project\PrismWave\tools\flutter\bin\dart analyze <file>
```

### 节目单生成
```powershell
cd F:\Project\PrismWave\prismwave-hits
python scripts/build_hits.py
```
也可通过 GitHub Actions 每日自动触发。

### 在线首页 Top100 生成
```powershell
cd F:\Project\PrismWave\prismwave-hits
python scripts/build_home.py
```
GitHub Actions: `.github/workflows/build_home.yml`，定时 `0 2 * * *` UTC（北京时间 10:00）。

---

## 7. 最近改动摘要

### R502 (2026-06-09, tag `R502`)

#### 版本号
- `app/pubspec.yaml`: `502.0.0+504`
- `release_update_service.dart`: `kCurrentReleaseVersion = 'R502'`
- `installer/PrismWaveSetup.iss`: `#define MyAppVersion "R502"`

#### 在线首页榜单状态
- `NeteaseHomeService` 新增 `recommendationsPendingGeneration` 状态，用于区分"今日榜单尚未生成"与"远程 JSON 不可用"。
- remote `latest_home.json` 可用但 `editionDate` 不是当天时，默认显示昨日榜单，并设置 `recommendationsPendingGeneration=true`。
- 网络条件不好、远程 JSON 拉取失败或 payload 不满足 schema 7 Top100 要求时，才设置 `recommendationsUnavailable=true`。
- 首页榜单卡片不显示状态叹号；只有进入榜单详情页后，"今日趋势"标题右侧才显示 `app/assets/icons/chart_notice.svg`。
- 普通叹号 Tooltip 为"榜单于UTC+10更新"；黄色叹号 Tooltip 为"推荐不可用，请检查网络环境。"。
- `OnlineController.refreshHomeRecommendations()` 返回 `OnlineHomeRefreshResult.fresh/latestAvailable/failed`，刷新提示可区分"已拉取今日榜单"、"今日榜单尚未生成，已显示昨日榜单"和"拉取失败"。

#### Release
- 安装包目标：`dist/PrismWave-Setup-R502.exe`
- Release notes：`dist/R502_RELEASE_NOTES.md`
- Release URL：`https://github.com/shanbei2033/PrismWave/releases/tag/R502`

### R501_fix2 (2026-06-08, tag `R501_fix2`)

#### 版本号
- `app/pubspec.yaml`: `501.0.2+503`
- `release_update_service.dart`: `kCurrentReleaseVersion = 'R501_fix2'`
- `installer/PrismWaveSetup.iss`: `#define MyAppVersion "R501_fix2"`

#### 推荐 JSON
- `prismwave-hits/scripts/build_home.py` 生成 schema 7 首页 JSON。
- 输出 `home/latest_home.json` 和 `home/home_recommendations-YYYY-MM-DD.json`。
- `editionDate` 使用北京时间日期，GitHub Actions 每天北京时间 10:00 自动生成。
- `topPlaylist.id = daily-top-100`，标题为"今日趋势"，包含 Top100。
- 本地验证：`schemaVersion=7`，`editionDate=2026-06-08`，`topPlaylist.tracks=100`，`coverUrl=100`。

#### App 拉取与缓存
- `NeteaseHomeService._kSchemaVersion = 7`。
- 客户端优先读取当天 `home-YYYY-MM-DD.json`；当天缓存命中则不拉远程。
- 无当天缓存时拉 remote `home/latest_home.json`。
- 拉取失败或 remote 不是当天时，只回退昨天缓存，并显示黄色告警。
- 无昨天缓存时会读取 `assets/home/latest_home.json` 内置 Top100，避免首页直接进入 failed 状态。
- UI 告警：`online_home_panel.dart` 和 `online_top_playlist_panel.dart` 在"今日趋势"旁显示 `Icons.warning_amber_rounded`，Tooltip 为"推荐不可用，请检查网络环境。"。
- 设置页新增"在线"分类，"拉取今日榜单"使用 `app/assets/icons/refresh.svg`。
- Windows 无边框窗口新增边缘 hit-test，可从边缘和四角自由拉伸。

#### 验证
```powershell
cd F:\Project\PrismWave\app
..\tools\flutter\bin\dart.bat analyze lib\main.dart lib\src
..\tools\flutter\bin\flutter.bat build windows --release

cd F:\Project\PrismWave\prismwave-hits
python -m py_compile scripts\build_home.py scripts\build_hits.py
python scripts\build_home.py
```

#### Release
- 安装包目标：`dist/PrismWave-Setup-R501_fix2.exe`
- Release notes：`dist/R501_fix2_RELEASE_NOTES.md`
- Release URL：`https://github.com/shanbei2033/PrismWave/releases/tag/R501_fix2`

### R501_fix (2026-06-07, tag `R501_fix`)

#### 版本号
- `app/pubspec.yaml`: `501.0.1+502`
- `release_update_service.dart`: `kCurrentReleaseVersion = 'R501_fix'`
- `installer/PrismWaveSetup.iss`: `#define MyAppVersion "R501_fix"`
- `release_update_service.dart` 的版本比较已支持 `R501_fix`（无数字后缀）并将其视为 R501 的第一个 fix 版本。

#### UI 与字体
- 新增 `app/lib/src/ui/prismwave_theme.dart`，统一玻璃拟态主题色、按钮样式、字体 fallback。
- 全局字体改为 Resource Han Rounded CN/TW：
  - `app/assets/fonts/resource_han_rounded/cn/*`
  - `app/assets/fonts/resource_han_rounded/tw/*`
- 首页/主页面/全屏播放页/顶栏等界面改为更透明的玻璃拟态风格。
- 移除 PrismWave 标题下方的 `R501 Music Player` 小字。
- 主播放键从红色 selected 按钮改为白色玻璃按钮；底部播放进度条 active/thumb/overlay 均改为白色。
- 删除/错误提示等语义性红色仍保留，不属于本次“播放控件红色调”范围。

#### 自动歌词匹配性能
- `fullplay_page.dart`：不再在每次 build 重复调用 `ensureLyricsLoaded()`，只在切歌或播放时长补齐时调度一次。
- `library_controller.dart`：本地歌词检查和在线歌词预取并发，避免大音频嵌入标签读取拖慢在线匹配。
- `lyrics_reader.dart`：本地歌词优先检查旁置 `.lrc/.qrc`，再读取音频嵌入歌词。
- `online_lyrics_service.dart`：
  - LRCLIB exact/search 优先并行，拿到可解析结果立即返回。
  - QQ 音乐仅作为兜底；候选数从 8 收窄到 3。
  - QQ QRC 与普通歌词接口并发，任一有效结果先返回。
  - LRCLIB / QQ 请求加 3 秒超时，避免 fullplay 等待十几秒。

#### Release
- 安装包目标：`dist/PrismWave-Setup-R501_fix.exe`
- Release notes：`dist/R501_fix_RELEASE_NOTES.md`
- Release URL：`https://github.com/shanbei2033/PrismWave/releases/tag/R501_fix`

### R501 (2026-06-06, tag `R501`)

#### 版本号
- `app/pubspec.yaml`: `501.0.0+501`
- `release_update_service.dart`: `kCurrentReleaseVersion = 'R501'`
- `installer/PrismWaveSetup.iss`: `#define MyAppVersion "R501"`

#### 首页与推荐
- 今日趋势徽标：`TOP10` → `TOP100`。
- 首页刷新按钮调用 `refreshHomeRecommendations()`，R501 当时用于刷新推荐歌曲和专辑，趋势榜单仍由 `prismwave-hits` 定时任务更新。
- 首页推荐模块标题下方的小字已移除，包括普通歌曲推荐分区和专辑推荐行。
- R501 当时在线首页采用旧缓存先上屏、后台刷新 remote daily / 专辑 / 封面的策略；2026-06-08 已改为当天缓存优先、失败只回退昨天并显示告警。

#### 在线搜索与播放源
- 普通在线搜索保持不引入视频平台源。
- 增强非视频音乐源解析链，新增 / 改进 Taihe、Kuwo、Migu、QQ、Kugou、NetEase 等来源。
- 修复网易云搜索结果封面、歌手、专辑名、时长等元数据显示问题。
- 修复网易云直连返回 HTML 导致不可播放时的解析兜底。
- 新增 `app/lib/src/utils/online_text_utils.dart`，用于修复旧中文音乐接口常见 mojibake。

#### 在线歌词
- fullplay 播放在线歌曲时默认走在线歌词自动匹配。
- 自动匹配从“只尝试第一条结果”改为逐个尝试排序后的可解析结果。
- 自动 / 手动在线歌词搜索会带入当前播放时长；首次无时长失败后，拿到时长会自动重试一次。

#### Release
- 安装包目标：`dist/PrismWave-Setup-R501.exe`
- Release notes：`dist/R501_RELEASE_NOTES.md`
- Release URL：`https://github.com/shanbei2033/PrismWave/releases/tag/R501`

### 2026-06-06：在线首页启动性能修复

#### 启动窗口显示顺序
- `main.dart` 不再在窗口显示前等待 `MetadataGod.initialize()`。
- `MetadataGod.initialize()` 移入 `local_audio_metadata_service.dart` 的共享 Future：`initializeLocalAudioMetadataBackend()`。
- Windows acrylic 效果后台套用，不阻塞窗口 `show()` / `focus()`。

#### 在线首页首屏
- R501 当时 `OnlineController.ensureHomeLoaded()` 允许旧缓存立即上屏。
- 当时缓存过期会后台 `loadBundle(forceRefresh: true)`。
- 当时没有缓存会先 `loadRemoteDailyBundle()` 拉轻量 daily home，再后台补完整专辑和封面。
- 2026-06-08 后当前策略是当天缓存优先；没有当天缓存才拉远程，失败只回退昨天缓存并显示告警。
- `_homeSeq` 防止旧异步结果覆盖新状态。
- 删除 home service 启动 warm-up，resolver warm-up 延后 3 秒。
- `NeteaseHomeService._fetchFresh()` 不再等待大陆封面兜底；封面兜底改为后台 `enrichMainlandCoverFallbacks()`。

#### 验证
```powershell
cd F:\Project\PrismWave\app
..\tools\flutter\bin\dart.bat analyze lib\src lib\main.dart
..\tools\flutter\bin\flutter.bat test test\playback_strategy_test.dart
..\tools\flutter\bin\flutter.bat build windows --release
```
三项均通过；最终 `data/app.so` 修改时间为 `2026-06-06 00:18:24`。

### R401_fix2 (2026-06-05, commit `3bb4462`)

#### 默认在线首页
- 应用启动默认打开 Home。
- 首次打开在线模式默认开启。
- 版本号同步：
  - `app/pubspec.yaml`: `401.0.3+403`
  - `release_update_service.dart`: `kCurrentReleaseVersion = 'R401_fix2'`
  - `installer/PrismWaveSetup.iss`: `#define MyAppVersion "R401_fix2"`

#### 在线首页每日推荐
- app 开始使用 `prismwave-hits/home/latest_home.json` 作为首页歌曲推荐来源。
- 网易云新专辑仍作为专辑推荐来源。
- cache freshness 改为按 remote `editionDate` 判断。

#### 在线播放和队列
- 点击在线歌曲时先解析 picked track 并立即播放。
- 完整 metadata queue 立即显示，其余曲目后台解析并 patch 队列。
- 修复在线队列中 metadata-only / 无播放 URL 曲目直接点击后无法播放的问题。
- 增加在线播放解析和队列日志。

#### 在线搜索与封面
- 普通在线搜索移除 bilibili 源。
- 新增 Taihe provider。
- 专辑封面使用网易云 `512y512` 缩略图和 album detail 兜底。
- 首页歌曲封面使用网易云搜索兜底，解决 remote daily home 无封面或海外封面不可达问题。

#### Release
- GitHub pre-release tag: `R401_fix2`
- Release URL: `https://github.com/shanbei2033/PrismWave/releases/tag/R401_fix2`
- 上传安装包：`PrismWave-Setup-R401_fix2.exe`

### R401_fix1 (2026-05-27, commit `94495cd`)

#### WASAPI Exclusive 破音修复（核心）
- 二进制修补 libmpv，端点缓冲 ~3ms → ~50ms，消除 underrun
- `native/libmpv/libmpv-2.dll`（mpv v0.41.0 修补版）覆盖 media_kit 默认 dll
- `app/windows/CMakeLists.txt` 添加 install 规则，把自定义 dll 复制到运行时目录
- `mediakit_player.dart` 加 `wasapi-exclusive-buffer=50000` 属性设置

#### 设置页文件夹大小显示
- `_SettingsFoldersCard` 改为 `StatefulWidget`，异步递归计算每个音乐文件夹的磁盘占用
- 文件夹路径下方以 `subtitle` 显示格式化大小
- 新增 i18n 字符串 `folderSize`

#### 版本号
- `pubspec.yaml`: `401.0.1+401` → `401.0.2+402`
- `kCurrentReleaseVersion`: `R401_fix` → `R401_fix1`
- `installer/PrismWaveSetup.iss`: 同步

#### 移除/恢复
- `playback_controller.dart` 移除调试中加入的 `audio-buffer` 属性

### R401_fix (2026-05-21, commit `9f63c3b`)

#### HITS 播放源过滤（`hits_controller.dart`）
- 新增 `_nonPlayableUrlPattern` 正则，拒绝 Deezer CDN 和 iTunes preview URL
- 修复 `_refreshPlaybackSource`：条件从 `if (directTrack != null)` 改为 `if (directTrack != null && immediateSource != null)`，当节目单 URL 不可播放导致 `immediateSource` 为 null 时，不再短路返回，而是正确回退到在线多源解析链（bilibili → YouTube → Audius → 网易云 → 酷我 → 咪咕 → QQ → 酷狗）

#### 节目单生成（`build_hits.py`）
- Deezer/iTunes 仅贡献元数据，`audio_url=null`，通过 Audius 匹配
- 新增 Deezer 和 iTunes RSS 数据源
- `candidate_resolution_limit` 96 → 400
- 加权随机选择减少曲目重复

#### 开发者控制台（`playback_controller.dart`）
- `Process.start`（`DETACHED_PROCESS`）替换为 `ShellExecuteW`（win32 包）
- 移除 `!kReleaseMode` 门控，开发者模式始终打开日志窗口

#### HITS 过渡动画（`hits_transition_page.dart`）
- 呼吸动画使用预渲染位图（`RenderRepaintBoundary.toImage`），消除每帧文字重绘

#### README 重新格式化
- 居中标题 + logo PNG + 徽章（License GPLv3 / Release R401_fix / Flutter 3.29.3）
- 添加 prismwave-hits 仓库链接

---

## 8. Git 历史（关键节点）

```
R501 feat: release R501 online source and lyrics refresh
3bb4462 feat: release R401_fix2 online home
94495cd fix: R401_fix1 — WASAPI exclusive mode buffer fix, folder size display
cbba939 docs: remove extra line breaks below title section
98b073f docs: reformat README with centered title, logo, and badges
d2ca0c8 docs: add prismwave-hits repo link to READMEs
8db8787 chore: remove CLAUDE.md from tracking
9f63c3b fix: R401_fix — HITS playback source filter, dev console, animation perf  ← R401_fix
47dcb1d fix: HITS critical bugs and animation performance
b2040c4 feat: R401_Pre — HITS mode, DSD backend, lyrics overhaul  ← R401 大版本
503b656 feat: release R020 queue and autoscroll polish
ca1bb92 feat: improve lyrics flow and playlist management
4f9e87e feat: improve lyrics search and karaoke groundwork
```

### R501 发布提交包含（2026-06-06）
- 启动与在线首页性能修复：
  - `app/lib/main.dart`
  - `app/lib/src/services/local_audio_metadata_service.dart`
  - `app/lib/src/controllers/online_controller.dart`
  - `app/lib/src/services/netease_home_service.dart`
- 在线搜索 / 播放源 / 文本修复：
  - `app/lib/src/services/hits_audio_resolver_service.dart`
  - `app/lib/src/services/online_search_service.dart`
  - `app/lib/src/services/netease_endpoints.dart`
  - `app/lib/src/utils/online_text_utils.dart`
- 在线歌词自动匹配：
  - `app/lib/src/controllers/library_controller.dart`
  - `app/lib/src/services/online_lyrics_service.dart`
  - `app/lib/src/ui/fullplay_page.dart`
  - `app/lib/src/ui/main_page.dart`
  - `app/lib/src/ui/window_top_bar.dart`
- 首页 UI 与版本文档：
  - `app/lib/src/i18n/app_strings.dart`
  - `app/lib/src/ui/online_home_panel.dart`
  - `README.md`, `README_zh.md`
  - `app/pubspec.yaml`, `release_update_service.dart`, `installer/PrismWaveSetup.iss`

---

## 9. 当前工作区状态

```
分支: main
当前发布: R502
提交后应只有本地未纳入发布的工具/环境残留:
  - tools/（本地 Flutter / potrace / userscripts，不要整体提交）
  - app/.dart_tool/, app/build/ 等构建缓存
注意：`app/assets/fonts/resource_han_rounded/` 已被 `pubspec.yaml` 引用；`app/assets/home/latest_home.json` 和 `app/assets/icons/refresh.svg` 是 R501_fix2 需要提交的资源；`app/assets/icons/chart_notice.svg` 是 R502 需要提交的资源。`app/assets/logo.png`、`app/assets/hits-loading.webp` 当前未被代码引用，除非后续 UI 明确使用，否则不要为了本次 release 强行提交。
```

---

## 10. 推荐的接手顺序

1. 先确认用户当前要推进的主线（在线首页 / 在线播放 / 在线搜索 / DSD / HITS / 元数据 / UI）。
2. 如果继续在线首页，优先：
   - 让用户开开发者模式，观察 `online.home.load.*`、`online.home.refresh-background.*`、`online.home.cover-enrich.*`。
   - 对比 `%LOCALAPPDATA%\PrismWave\online_home_cache\home.json` 的 `editionDate`。
   - 检查 `prismwave-hits/home/latest_home.json` remote 是否当天更新。
3. 如果继续在线播放，优先：
   - 看 `online.play.*`、`online.queue.*`、`queue.resolve-on-demand.*` 日志。
   - 确认失败曲目是 resolver 找不到源，还是播放器无法解码 URL。
4. 如果做普通在线搜索，优先：
   - 保持 bilibili / bilivideo / YouTube 不进入搜索 UI。
   - 真机验证 Taihe、Kuwo、Migu、QQ、Kugou、NetEase 的可用性。
5. 如果做 HITS，优先：
   - 真机验证 6 个中国 provider 解析成功率。
   - 封面/歌词缓存命中率优化。
   - 保持 HITS 内部视频源兜底，除非用户明确要求删除。
6. 如果做 DSD，优先：
   - 切换设备后即时重载
   - 真机 ASIO 验证
7. 如果做元数据，优先补 flac/m4a/ape fallback

---

## 11. 特别注意（不要做的事情）

- **不要**重新引入"DSD 自动强制独占"旧逻辑
- **不要**把 HITS "兼容模式"改回 `preferWasapi=false`
- **不要**随意回退窗口相关修复（flutter_window.cpp 的显示时机是专门调过的）
- **不要**把 `MetadataGod.initialize()` 放回窗口显示前等待；这会复发启动很久才进入首页的问题
- **不要**让在线首页首屏等待专辑接口或 `_withMainlandCoverFallbacks()`；当前策略是当天缓存优先、无当天缓存再拉 remote daily，失败只回退昨天并显示告警
- **不要**把 bilibili / bilivideo / YouTube 加回普通在线搜索 UI；这些只保留给 HITS 兜底
- HITS 入口已移到侧栏，不要在标题栏再加按钮
- **不要**上传 API key、CLAUDE.md 等隐私/无关文件到 GitHub
- 当前工具链: `F:\Project\PrismWave\tools\flutter\bin\flutter.bat`
- Windows 下 flutter build 可能静默几十秒，不要误判死锁
- `dart:ui` 导入需加 `as ui` 前缀，`lerpDouble`、`ImageFilter` 等需加 `ui.` 前缀
- Deezer/iTunes 音频 URL 都是 30 秒预览片段，**绝不能**作为播放源
- **不要**删除 `native/libmpv/libmpv-2.dll` —— 这是修补过的 WASAPI exclusive 缓冲版本，删了破音会复发
- **不要**升级 media_kit / media_kit_libs_windows_audio 而不重新核对自定义 libmpv 兼容性
- **不要**在 `nativeMpvProperties` 里设 `audio-buffer` —— 它是 player-side 解码缓冲，跟修复无关，反而可能干扰
- media_kit 的 `PlayerConfiguration` 已扩展 `nativeMpvOptions` 字段（`platform_player.dart`），作为备用通道保留

---

## 12. 一句话总结

PrismWave **R502** 已具备"玻璃拟态 Windows 播放器 UI + 可自由拉伸的无边框窗口 + Resource Han Rounded 中文字体 + 本地播放器 + schema 7 Top100 在线首页/搜索/专辑/队列 + 今日榜单未生成/网络不可用分离提示 + 应用内推荐刷新 + 冷启动内置 Top100 兜底 + 更多非视频在线音源 + 更快的在线歌词自动匹配 + HITS 10 音源电台（+ Deezer/iTunes 元数据源）+ DSD 后端 + 开发者日志"的完整形态；WASAPI Exclusive 破音通过自定义 libmpv 已修复，当前最大风险仍是多个在线 provider 的长期可用性和 DSD 设备切换不能即时重载。
