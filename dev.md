# PrismWave 开发文档

本文档基于当前仓库代码、现有交接文档与项目目录结构整理，目标是为后续开发、维护、排障和发布提供一份相对完整的内部说明。

## 1. 项目定位

- 项目名称：PrismWave
- 当前正式版本：`R011`
- GitHub 仓库：[https://github.com/shanbei2033/PrismWave](https://github.com/shanbei2033/PrismWave)
- 主要平台：Windows 桌面
- 当前核心定位：本地音乐播放器 + Windows 桌面体验 + 玻璃拟态 / Acrylic UI + 歌词与音频输出控制

当前代码已经不应再按“仅可用 demo”理解。虽然仓库内部仍保留 `prismwave_demo` 这一历史命名，但从产品形态、安装器、更新检查与功能面看，项目已经处于正式版本迭代阶段。

## 2. 文档使用说明

当前仓库中与项目相关的 Markdown 主要有：

- `memory.md`
- `README.md`
- `README_zh.md`
- `dev_in_progress.md`
- `feature_ideas.md`

建议理解优先级如下：

1. 代码实现
2. 本文档 `dev.md`
3. `memory.md`
4. `dev_in_progress.md` / `feature_ideas.md`
5. `README.md` / `README_zh.md`

原因：

- `memory.md` 中部分内容仍停留在 `R010`
- `README.md` 仍将项目描述为“first usable demo version”
- 实际代码中的版本、安装器和更新逻辑已经前进到 `R011`

## 3. 当前版本现状

### 3.1 版本号体系

项目目前同时存在两套版本表达：

- 对外发布版本：`R011`
- Flutter 包版本：`11.0.0+11`

这两者并不冲突，当前用法是：

- GitHub Release / 安装器 / 更新检查：使用 `R011`
- Flutter `pubspec.yaml` / 平台构建版本：使用 `11.0.0+11`

### 3.2 当前代码中的权威版本来源

- `app/lib/src/services/release_update_service.dart`
  - `kCurrentReleaseVersion = 'R011'`
- `app/pubspec.yaml`
  - `version: 11.0.0+11`
- `installer/PrismWaveSetup.iss`
  - `#define MyAppVersion "R011"`

### 3.3 安装器现状

当前 Inno Setup 脚本已按 `R011` 配置，输出文件名为：

```text
dist/PrismWave-Setup-R011.exe
```

安装器默认路径规则：

- 若存在 `D:\`，默认安装到 `D:\PrismWave`
- 否则安装到 `C:\PrismWave`

注意：这和旧的 `memory.md` 中“回退到 `C:\Program Files\PrismWave`”的记录已经不一致，代码以安装器脚本为准。

### 3.4 当前代码已实现的功能概览

按现有代码状态，当前已经实际落地的功能可以概括为：

- 音乐库基础能力
  - 添加多个音乐目录
  - 重新扫描全部目录
  - 本地曲库扫描、基础元数据推断、异步元数据补全
  - Library / Albums / Artists / Favorites 四种浏览入口
  - 收藏管理与搜索筛选
- 播放与控制能力
  - 从不同上下文播放曲目
  - 上一首 / 下一首 / 播放暂停 / 拖动进度 / 音量调节
  - 列表循环 / 单曲循环 / 随机播放
  - 渐入渐出
  - 曲目结束自动推进
- Windows 音频能力
  - Compatibility / WASAPI Shared / WASAPI Exclusive
  - 音频设备枚举与切换
  - 音频设备初始化失败后的回退恢复链
  - 开发者模式音频日志
- 歌词能力
  - 读取本地内嵌歌词
  - 读取 sidecar `.lrc` / `.qrc`
  - 在线歌词搜索
  - 本地 / 在线歌词来源切换
  - 歌词偏移调整
  - FullPlay 中的逐字 / 分段高亮与 fallback 高亮
- 封面能力
  - 目录封面识别
  - 元数据内嵌封面读取
  - 在线封面搜索
  - 手动替换并缓存自定义封面
- 桌面应用能力
  - 无边框窗口
  - 自定义窗口控制按钮
  - 顶栏歌词 / quote / 自定义文本显示
  - 曲目详情页
  - 在资源管理器中定位当前文件
  - 检查 GitHub 新版本并打开更新地址
- 国际化
  - 简体中文
  - 繁体中文
  - 英文

### 3.5 命名与文案遗留现状

虽然当前版本已经按 `R011` 正式版维护，但代码中仍保留一些历史命名：

- Flutter 包名仍为 `prismwave_demo`
- Windows runner 构建出的源可执行文件仍叫 `prismwave_demo.exe`
- `AppStrings.appTitle` 当前仍返回：
  - `PrismWave 演示版`
  - `PrismWave Demo`

这类遗留不会直接影响功能，但会影响：

- 窗口标题
- 某些系统层显示文案
- 后续品牌统一

如果下一步要继续清理正式版痕迹，建议优先统一：

- `pubspec.yaml` 中的工程命名
- i18n 里的 app title
- Windows runner 的最终产品命名链路

## 4. 仓库结构

当前项目主要结构如下：

```text
PrismWave/
  app/                           Flutter 主应用
    lib/
      main.dart                  应用入口
      src/
        app.dart                 MaterialApp 与主题
        providers.dart           Riverpod provider 装配
        controllers/             状态控制器
        services/                业务服务层
        models/                  数据模型
        state/                   StateNotifier 状态对象
        domain/                  纯策略逻辑
        ui/                      主界面 / FullPlay / 顶栏 / 玻璃组件
    third_party/
      just_audio_media_kit/      本地 fork 的音频桥接层
    windows/                     Flutter Windows runner
    test/                        少量单元测试
  installer/
    PrismWaveSetup.iss           Windows 安装器脚本
  native/
    rust_core/                   预留的 Rust 核心工作区
  dist/                          构建产物输出目录
  memory.md                      交接型文档
  dev_in_progress.md             暂停中或在研功能记录
  feature_ideas.md               后续功能路线图
```

## 5. 技术栈与依赖角色

### 5.1 应用层

- Flutter
- flutter_riverpod
- shared_preferences
- file_picker
- path

### 5.2 音频播放层

- just_audio
- media_kit
- media_kit_libs_windows_audio
- 本地 fork 的 `just_audio_media_kit`

### 5.3 Windows 桌面层

- window_manager
- flutter_acrylic
- win32

### 5.4 媒体元数据与歌词

- metadata_god
- dart_tags
- fast_gbk

### 5.5 资源展示

- flutter_svg

## 6. 入口与启动流程

应用入口在 `app/lib/main.dart`。

启动顺序如下：

1. `WidgetsFlutterBinding.ensureInitialized()`
2. 按用户设置初始化音频后端参数
3. 初始化 `JustAudioMediaKit`
4. 初始化 `MetadataGod`
5. 配置窗口参数与 Acrylic 效果
6. `runApp(ProviderScope(child: PrismWaveApp()))`

### 6.1 音频后端初始化

在启动前会根据 `SharedPreferences` 中保存的输出模式设置：

- `Compatibility`
- `WASAPI Shared`
- `WASAPI Exclusive`

同时会设置：

```text
sub-auto = no
```

这是一个很关键的项目级决策。原因是 mpv 自动加载同名 `.lrc` 时，可能把外部歌词文件加载失败误判为致命播放错误。PrismWave 当前歌词逻辑完全由应用层自行控制，因此这里显式禁止 mpv 自动加载 sidecar 字幕/歌词。

### 6.2 窗口初始化

窗口初始化通过：

- `Window.initialize()`
- `windowManager.ensureInitialized()`
- `Window.setEffect(...)`

当前窗口配置特征：

- 无边框
- 自定义标题栏按钮
- 背景透明
- Windows 上优先使用 Acrylic
- Acrylic 失败时回退到 Aero

## 7. 应用架构概览

项目整体是比较典型的 Flutter + Riverpod 分层结构：

- `models`
  - 纯数据模型
- `state`
  - 页面或控制器持有的状态快照
- `controllers`
  - `StateNotifier`，负责状态变更与业务流程
- `services`
  - I/O、网络、解析器、缓存
- `domain`
  - 纯策略逻辑
- `ui`
  - 页面与组件

核心 provider 在 `app/lib/src/providers.dart` 中定义：

- `appSettingsProvider`
- `libraryProvider`
- `playbackProvider`

其中 `libraryProvider` 会拿到 `playbackProvider.notifier` 的日志写入接口，用于把歌词搜索/渲染等调试日志统一写入播放日志流。

## 8. 主要状态对象

### 8.1 `AppSettingsState`

负责：

- 当前语言
- 顶栏空闲显示模式
- 顶栏自定义文本 / quote 文本
- 当前版本号
- 更新检查状态
- 最新 release 版本 / URL / 安装器 URL

### 8.2 `LibraryState`

负责：

- 音乐库目录列表
- 曲目列表
- 时长缓存
- 封面缓存
- 自定义封面映射
- 歌词偏移
- 本地歌词缓存
- 在线歌词缓存
- 歌词来源偏好
- 收藏列表
- 搜索关键字
- 扫描状态
- 低特效标志

### 8.3 `PlaybackState`

负责：

- 当前曲目
- 当前播放列表
- 当前索引
- 播放模式
- 播放 / 加载状态
- 当前播放时间 / 总时长
- 音量
- 渐入渐出开关与时长
- 开发者模式
- 当前输出模式
- 当前输出设备
- 可用输出设备列表
- 调试日志

## 9. 音乐库模块

### 9.1 入口

核心实现位于：

- `app/lib/src/controllers/library_controller.dart`
- `app/lib/src/services/library_scanner.dart`

### 9.2 目录扫描策略

`LibraryController` 启动后会从 `SharedPreferences` 恢复：

- 历史音乐目录
- 收藏
- 低特效
- 歌词来源偏好
- 自定义封面映射
- 歌词偏移

若存在已保存目录，会自动触发扫描。

底层扫描通过 `scanTracksFromRoots` 实现，特点：

- 每个根目录使用 `Isolate.run` 扫描，避免阻塞 UI
- 递归遍历子目录
- 按完整路径去重
- 按标题排序
- 首轮先依据文件名与目录结构构造基础 `Track`

### 9.3 扫描支持格式

扫描器支持识别的扩展名：

- `.mp3`
- `.aac`
- `.m4a`
- `.mp4`
- `.wav`
- `.flac`
- `.ogg`
- `.ape`
- `.dsf`
- `.dff`

注意：这里的“可扫描”不等于“当前后端一定可播放”。

### 9.4 当前 demo 后端的实际可播放格式

`PlaybackController` 中当前显式允许播放的格式为：

- `.mp3`
- `.wav`
- `.flac`
- `.ogg`
- `.aac`
- `.m4a`
- `.mp4`

这意味着：

- `.ape`
- `.dsf`
- `.dff`

可能会被扫描进音乐库，但当前不会在播放控制器中被视为“demo backend 可播放格式”。

这类格式差异在后续如果要做 HiFi 深化，需要优先统一。

### 9.5 目录内封面查找

扫描器会为每个目录尝试寻找常见封面文件：

- `cover.jpg/jpeg/png`
- `folder.jpg/jpeg/png`
- `front.jpg/png`

找到后会把封面路径写入初始 `Track.coverPath`。

### 9.6 基础标题/艺术家/专辑推断

在没有元数据前，扫描器会做一轮文件名推断：

- 若文件名符合 `Artist - Title`
  - 取前半部分作为艺术家
  - 后半部分作为标题
- 否则尝试从目录结构推断
- 专辑默认取当前目录名
- 空值统一回退为 `Unknown ...`

### 9.7 元数据补全

首轮扫描后，`LibraryController` 会异步调用 `_enrichMetadata(...)`：

- 使用 `MetadataGod.readMetadata`
- 批量覆盖标题、艺术家、专辑
- 尝试读取时长
- 尝试读取内嵌封面

为了避免扫描中途重入导致旧任务回写新状态，控制器使用 `_metadataJobSeed` 作为任务代号。如果扫描任务刷新，旧补全任务会自动失效。

### 9.8 时长补全

如果 `MetadataGod` 没拿到时长，会再通过 `track_duration_resolver.dart` 用 `just_audio` 逐个加载文件补时长。

特点：

- 每个文件默认超时 4 秒
- 每批 8 条提交一次
- 失败会静默跳过，不阻塞整体扫描

### 9.9 收藏与搜索

收藏通过 `favoritePaths` 维护，本质是按完整文件路径保存。

搜索对以下字段做大小写不敏感匹配：

- 标题
- 艺术家
- 专辑
- 路径

## 10. 播放模块

### 10.1 入口

核心实现位于：

- `app/lib/src/controllers/playback_controller.dart`
- `app/lib/src/domain/playback_strategy.dart`

### 10.2 当前播放模型

PrismWave 当前不是“全局队列模型”，而是“上下文播放列表模型”：

- 从 Library 点歌：当前上下文通常是整个库
- 从 Favorites 点歌：当前上下文是收藏列表
- 从某张专辑 / 某位艺术家点歌：当前上下文是局部列表

`PlaybackState` 中保存：

- `currentPlaylist`
- `currentIndex`
- `currentTrack`

这决定了下一首 / 上一首如何解析。

### 10.3 播放模式的真实语义

当前播放模式由 Dart 层控制，而不是交给 native loop mode。

`PlaybackStrategy` 的规则：

- `loop`
  - 线性播放，末尾回绕
- `single`
  - 自动播完时重复当前曲目
  - 手动上一首 / 下一首行为仍按 loop
- `shuffle`
  - 从当前上下文中随机选择一首
  - 避免重复当前索引

一个非常重要的维护约束：

- native loop mode 被强制保持为 `off`
- 看到日志中 `loopMode=off` 不能直接判定播放模式逻辑异常

真正的模式行为在 Dart 层，主要是：

- `PlaybackController.next(...)`
- `PlaybackController.previous()`
- `PlaybackStrategy.resolveNextIndex(...)`
- `PlaybackStrategy.resolvePreviousIndex(...)`

### 10.4 播放请求主流程

点播时典型流程是：

1. 校验格式是否可播放
2. 过滤当前上下文中的不可播放文件
3. 计算所选曲目在可播放列表中的索引
4. 写入 `currentPlaylist / currentTrack / currentIndex`
5. 调用 `_loadPlaylistAndPlay(...)`

`_loadPlaylistAndPlay(...)` 是最核心的内部播放流程，负责：

- 按需淡出当前曲目
- 在独占模式下执行播放器重建 handoff
- `setFilePath(...)`
- 恢复播放
- 写入日志
- 出错恢复

### 10.5 重新播放与曲目切换

切换到同一索引时不会简单忽略，而是执行“重启当前曲目”逻辑。

`_restartCurrentTrack()` 会：

- 先尝试淡出
- 优先 `seek(0)+play`
- 若失败则回退到完整 reload
- 在 WASAPI Exclusive + completed 场景下，会优先走 fresh player reload

### 10.6 渐入渐出

播放控制器内置淡入淡出：

- 可开关
- 时长可配置
- 默认 220ms
- 允许范围 100ms 到 1200ms

核心实现：

- `_pauseWithFade()`
- `_resumeWithFade()`
- `_fadeOutCurrentTrack()`
- `_fadePlayerVolume()`

这个系统不是 UI 动画，而是真实在控制播放器音量。

## 11. 音频输出与设备管理

### 11.1 当前实现方式

项目当前的输出模式 / 输出设备不是 Flutter 原生能力，而是建立在本地 fork 的 `just_audio_media_kit` 之上。

关键文件：

- `app/third_party/just_audio_media_kit/lib/just_audio_media_kit.dart`
- `app/third_party/just_audio_media_kit/lib/mediakit_player.dart`

### 11.2 本地 fork 做了什么

本地 fork 新增或强化了以下能力：

- 暴露原生音频设备列表
- 暴露当前选中设备
- 提供原生音频路由日志回调
- 支持 `preferredAudioDevice`
- 支持 `preferWasapi`
- 支持 `preferWasapiExclusive`
- 支持 `fallbackToWasapiShared`
- 支持附加 mpv 属性 `nativeMpvProperties`
- 忽略 benign 的外部 `.lrc` 打开错误

也就是说，PrismWave 当前的 Windows 音频输出能力并不是纯上层逻辑，而是“上层控制器 + 本地 fork 播放桥接层”共同完成的。

### 11.3 输出模式

当前支持：

- `Compatibility (MPV)`
- `WASAPI Shared`
- `WASAPI Exclusive`

其中：

- `Compatibility` 会关闭 WASAPI 偏好
- `WASAPI Shared` 会打开 WASAPI，但关闭独占
- `WASAPI Exclusive` 会请求独占，同时允许回退到 shared

### 11.4 设备探测

`PlaybackController` 内部除了实际播放用的 `AudioPlayer`，还维护了一个额外的 `media_kit.Player _audioDeviceProbe` 用于探测设备。

这个 probe 的作用：

- 刷新设备列表
- 得知当前可见设备
- 辅助构建下拉框可选项

### 11.5 设备列表来源

设备列表有两路来源：

1. 本地 fork 的 native 回调
2. 额外 probe player 的 `audioDevices` 流

控制器最终会合并两路信息并去重。

### 11.6 输出设备选择

用户选择设备后会：

1. 持久化设备 ID
2. 刷新 probe
3. 重建播放器

设备默认值是：

```text
auto
```

即让底层自行选择系统默认设备。

### 11.7 独占模式的额外降级逻辑

控制器中有一个额外规则：

- 如果用户选择 `WASAPI Exclusive`
- 且设备标签看起来像耳机 / 耳麦 / headset / earbuds 等

则 `_resolveEffectiveAudioOutputMode(...)` 可能自动把实际后端输出改成 `WASAPI Shared`。

也就是说：

- UI 选择的模式是“请求模式”
- 实际应用到后端的可能是“降级后的有效模式”

这个设计是为了降低独占模式在某些耳机设备上的问题率。

### 11.8 设备恢复链

如果播放时出现音频设备初始化失败，当前实现会按以下链路尝试恢复：

1. 指定设备 -> `auto`
2. `WASAPI Exclusive` -> `WASAPI Shared`
3. `WASAPI Shared` -> `Compatibility`

恢复入口主要在：

- `_shouldRecoverFromAudioDeviceError(...)`
- `_recoverToAutoAudioDevice(...)`

这是当前 Windows 音频稳定性的重要保障。

## 12. 错误恢复与稳定性策略

### 12.1 FLAC / 结尾 decode error 处理

当前播放器对 decode error 做了多级策略：

- 视作可跳过故障时，自动跳到下一首
- 视作可恢复故障时，尝试 recovery
- 如果错误发生在曲目接近结尾的位置，按“伪完成”处理

“伪完成”逻辑非常关键：

- 会把 near-end decode error 当成曲目自然播完
- 然后调用 `next(fromAutoEnded: true)`
- 从而让单曲循环 / 列表循环 / 随机模式继续遵守 Dart 层规则

### 12.2 decoder recovery

对部分 FLAC 场景，控制器会尝试：

- 轻微向后 seek
- 重新播放

如果不适用，再回退到 stop + reload。

### 12.3 外部歌词错误忽略

本地 fork 的桥接层里会忽略类似：

```text
can not open external file ... .lrc
```

的 benign 错误，避免把它上报为播放失败。

## 13. 歌词系统

### 13.1 总体设计

歌词系统分三层：

1. 本地歌词读取
2. 在线歌词搜索与缓存
3. UI 层渲染

### 13.2 本地歌词读取

入口：

- `app/lib/src/services/lyrics_reader.dart`

读取顺序：

1. 尝试读取音频文件内嵌歌词
2. 若失败，再尝试 sidecar 文件

### 13.3 内嵌歌词支持

当前已实现对以下格式的歌词读取逻辑：

- MP3
  - ID3 / USLT / `dart_tags`
- M4A / MP4
  - MP4 atom 扫描
- FLAC
  - Vorbis Comment
- WAV
  - RIFF 内嵌 ID3 chunk 或全文件扫描
- OGG / Opus / OGA
  - Vorbis-like 文本键值

### 13.4 Sidecar 歌词查找策略

会依次尝试：

- 同名 `.lrc`
- 同名 `.qrc`
- 当前目录
- 当前目录下 `lyrics/`
- 父目录
- 父目录下 `lyrics/`

匹配时会对文件名做归一化，支持：

- 曲名
- `artist - title`
- `title - artist`

### 13.5 文本解码策略

本地歌词文本会尝试智能解码：

- UTF-8 BOM
- UTF-16 LE / BE
- UTF-8
- Latin1

这对中文老歌词文件尤其重要。

### 13.6 解析能力

当前解析器支持：

- 普通 LRC
- Enhanced LRC
- QRC
- 纯文本歌词 fallback

解析结果统一映射为：

- `LyricsDocument`
- `LyricLine`
- `LyricSegment`

### 13.7 同步与 fallback 机制

若歌词包含逐字时间信息：

- UI 使用真正的逐字 / 分段高亮

若没有逐字时间信息但有行时间：

- 使用整行进度推进

若完全没有时间轴：

- 按总时长或默认步长平均分配时间，生成非严格同步歌词

### 13.8 歌词来源偏好

对每首歌，状态里都维护“偏好来源”：

- `local`
- `online`

但真正生效的来源会根据数据是否存在动态回退：

- 本地优先但本地为空，则退在线
- 在线优先但在线为空，则退本地

### 13.9 歌词偏移

每首歌支持单独的歌词偏移，精度为 0.1 秒。

偏移是作用在渲染前的数据层，而不是 UI 层单纯显示偏移值。

### 13.10 在线歌词服务

实现位于：

- `app/lib/src/services/online_lyrics_service.dart`

当前接入来源：

- `lrclib.net`
- `api.ygking.top` 的 QQ 歌词接口

搜索流程：

1. 同时请求 LRCLIB 与 QQ
2. 合并结果
3. 依据标题、艺术家、专辑、时长、是否同步、是否逐字进行评分
4. 去重后返回

### 13.11 在线歌词缓存

缓存目录默认位于：

```text
%LOCALAPPDATA%\PrismWave\lyrics_cache\
```

缓存键基于曲目路径哈希生成。

缓存内容是 `LyricsDocument.toCacheJson()` 的 JSON。

### 13.12 FullPlay 歌词渲染

FullPlay 页面中歌词面板实现了两种高亮方式：

- 有 `segments`
  - 使用逐段高亮
- 无 `segments`
  - 使用字符级渐进高亮 fallback

滚动策略：

- 当前行始终尽量居中
- 不允许手动滚动，跟随播放自动滚动

调试日志：

- `lyrics.loaded`
- `lyrics.search`
- `lyrics.render`

这些日志会进入开发者日志系统。

## 14. 封面系统

### 14.1 本地封面来源

封面来源优先级大致为：

1. 用户手动替换后的自定义封面
2. 音频内嵌封面
3. 目录中的 `cover/folder/front` 文件

### 14.2 在线封面搜索

实现位于：

- `app/lib/src/services/online_cover_service.dart`

当前来源：

- Apple iTunes Search
- MusicBrainz + Cover Art Archive

流程：

1. 优先查 Apple
2. 若 Apple 结果不足，再补 MusicBrainz
3. 评分排序
4. 缓存搜索结果

### 14.3 在线封面缓存

封面缓存默认位于：

```text
%LOCALAPPDATA%\PrismWave\cover_cache\
```

搜索结果缓存位于：

```text
%LOCALAPPDATA%\PrismWave\cover_cache\search_cache\
```

搜索缓存 TTL 为 7 天。

### 14.4 自定义封面持久化

用户选择在线封面后：

- 会下载图片
- 保存到本地缓存目录
- 在 `LibraryState.customCoverPathByTrackPath` 中建立映射
- 持久化到 `SharedPreferences`

## 15. 设置、语言、顶栏与更新检查

### 15.1 应用设置控制器

实现位于：

- `app/lib/src/controllers/app_settings_controller.dart`

负责：

- 语言
- 顶栏空闲内容模式
- 顶栏自定义文本
- 顶栏 quote 获取与缓存
- GitHub Release 更新检查

### 15.2 多语言

当前支持：

- 简体中文
- 繁体中文
- 英文

### 15.3 顶栏空闲显示模式

支持：

- `empty`
- `custom`
- `quote`

顶栏组件位于：

- `app/lib/src/ui/window_top_bar.dart`

行为：

- 播放中优先显示当前歌词行
- 非播放中根据设置显示自定义文本或 quote

### 15.4 Quote 系统

服务位于：

- `app/lib/src/services/quote_service.dart`

当前来源：

- 中文：`v1.hitokoto.cn`
- 英文：`zenquotes.io`

缓存按语言分开保存。

当前代码中，当顶栏处于 quote 模式且未播放时，会启动定时器，每 10 秒触发一次 `forceRefresh`。因此在空闲状态下 quote 会动态轮换，而不是严格一天只显示一句。

### 15.5 更新检查

服务位于：

- `app/lib/src/services/release_update_service.dart`

逻辑：

1. 请求 GitHub `releases/latest`
2. 读取 `tag_name`
3. 解析 installer 资产 URL
4. 与本地 `kCurrentReleaseVersion` 比较

当前版本比较规则支持：

- `R011`
- `R011_fix1`

## 16. UI 结构

### 16.1 主界面

主界面文件：

- `app/lib/src/ui/main_page.dart`

页面结构：

- 左侧导航栏
- 右侧主内容区
- 底部播放栏
- 顶部自定义标题栏

主分区：

- Library
- Albums
- Artists
- Favorites
- Settings

### 16.2 列表交互

当前列表行为：

- 单击曲目：播放
- 右键曲目：打开曲目详情页
- 可收藏/取消收藏

补充说明：

- Favorites 页支持 `Play All`
- 专辑详情页和艺术家详情页也支持 `Play All`
- 从不同入口点歌时，会带上对应上下文作为当前播放列表，而不是始终使用全库

### 16.3 专辑与艺术家页

当前专辑 / 艺术家页不是独立路由，而是在主页面内部切换：

- 先显示聚合网格
- 再切换到该专辑 / 艺术家的明细列表

### 16.4 设置页

设置页当前已接出的主要项：

- 音乐库目录管理
- 语言切换
- 顶栏显示模式
- 顶栏自定义文本
- 音频输出模式
- 音频输出设备
- 渐入渐出开关
- 渐入渐出时长
- 开发者模式
- 更新检查

### 16.5 当前代码里存在但未明显接出的项

`LibraryState` 和 `GlassPanel` 支持 `lowEffects`，控制器也支持持久化，但当前设置页没有看到对应用户开关。

这说明：

- 低特效模式逻辑还在
- 但当前 UI 没有明确入口

如果要恢复这个设置，需要补一个设置项，而不是重做底层能力。

### 16.6 FullPlay 页面

文件：

- `app/lib/src/ui/fullplay_page.dart`

主要组成：

- 大封面
- 大号曲目信息
- 进度条与播放控制
- 歌词主区域
- 右下角歌词快捷工具
- 双击封面打开在线封面搜索

歌词快捷工具提供：

- 切换本地 / 在线歌词来源
- 在线搜索歌词
- 调整歌词偏移

### 16.7 曲目详情页

主页面中还包含 `_TrackDetailsPage`，用于展示：

- 文件路径
- 时长
- 比特率
- 采样率
- 曲号

底层信息读取来自：

- `audio_file_details_service.dart`

注意：采样率读取目前只对部分格式实现了手工解析：

- WAV
- FLAC
- MP3

其他格式可能显示 `--`。

补充说明：

- 详情页使用 `FutureBuilder<AudioFileDetails>` 异步加载技术信息
- 当元数据尚未返回时，会先显示 fallback 值
- 路径字段支持选择复制
- 详情页提供“在资源管理器中定位文件”按钮

### 16.8 与系统和外部资源的交互

当前 UI 已经接出的系统交互包括：

- 打开 GitHub Release / 安装器更新地址
- 在 Windows 资源管理器中选中文件
- 回退到打开文件所在目录
- 复制开发者日志到剪贴板

实现方式上有一些值得注意的技术细节：

- 外部 URL 打开使用 `cmd.exe /c start`
- 文件定位优先尝试 Win32 `ShellExecute`
- 失败后回退到 `explorer.exe`
- 这些交互都已经在 `main_page.dart` 中有完整落地

## 17. 开发者模式与日志

### 17.1 开发者模式

`PlaybackController` 中维护开发者模式开关：

- 开启后把调试日志写入状态
- 同时落盘
- 同时在 Windows 上拉起一个 PowerShell 日志尾随窗口

### 17.2 日志目录

默认目录：

```text
%LOCALAPPDATA%\PrismWave\logs\
```

若拿不到 `LOCALAPPDATA`，会回退到：

- `%USERPROFILE%\Documents\PrismWave\logs\`
- 或当前目录下的 `logs/`

### 17.3 当前重点日志类型

- `player.state`
- `player.error`
- `native.output`
- `audio.route`
- `audio.deviceProbe`
- `decode completion`
- `lyrics.loaded`
- `lyrics.search`
- `lyrics.render`

如果后续排查播放问题，优先打开开发者模式并看这些日志。

## 18. 本地持久化与缓存

### 18.1 SharedPreferences 关键键名

音频与播放相关：

- `audio.outputMode`
- `audio.outputDevice`
- `audio.fadeEnabled`
- `audio.fadeDurationMs`
- `debug.playbackDeveloperMode`

应用设置相关：

- `ui.language`
- `ui.topBarIdleMode`
- `ui.topBarIdleText`
- `ui.topBarQuoteText.<lang>`
- `ui.topBarQuoteDate.<lang>`

音乐库相关：

- `library.rootPath`
- `library.folders`
- `library.favorites`
- `ui.lowEffects`
- `lyrics.preferredSources`
- `library.customCoverPaths`
- `lyrics.offsets`

### 18.2 路径特征

当前很多缓存与日志键是“按音频完整路径”做哈希或直接映射，因此：

- 移动文件路径
- 更换盘符
- 迁移音乐库

都会影响缓存命中与部分状态恢复。

这也是 `feature_ideas.md` 中“缺失文件修复”“库备份恢复”等功能有价值的原因。

## 19. 本地 fork 音频桥的维护说明

### 19.1 为什么必须关注 fork

很多当前产品能力并不只在 Flutter 上层：

- WASAPI 独占 / 共享控制
- 指定输出设备
- 设备列表同步
- 原生路由日志
- 忽略 `.lrc` 假错误

这些都依赖 `app/third_party/just_audio_media_kit` 中的修改。

如果将来升级上游 `just_audio_media_kit`，必须优先比对以下能力是否仍保留：

- `preferredAudioDevice`
- `nativeAudioRouteLogger`
- `nativeAudioDevicesListener`
- `nativeSelectedAudioDeviceListener`
- `nativeMpvProperties`
- 独占失败回退逻辑
- 外部 `.lrc` 错误忽略逻辑

### 19.2 当前项目的播放模式约束

由于 PrismWave 把播放模式逻辑留在 Dart 层，上层会强制 native loop mode 为 `off`。

这意味着：

- 不要尝试把原生 loop mode 重新打开来“修复”单曲循环
- 任何循环/随机问题先查 `PlaybackStrategy` 和 `PlaybackController`

## 20. 构建、运行与发布

### 20.1 使用仓库内置 Flutter

推荐命令：

```powershell
cd app
..\tools\flutter\bin\flutter.bat pub get
..\tools\flutter\bin\flutter.bat run -d windows
```

### 20.2 Windows Release 构建

```powershell
cd app
..\tools\flutter\bin\flutter.bat build windows --release
```

### 20.3 安装器构建

```powershell
"C:\Users\shanbei2033\AppData\Local\Programs\Inno Setup 6\ISCC.exe" "F:\Project\PrismWave\installer\PrismWaveSetup.iss"
```

### 20.4 产物命名说明

当前构建产物有一个历史命名差异：

- Flutter 生成的 runner 可执行文件名仍是 `prismwave_demo.exe`
- 安装器会把它重命名为 `PrismWave.exe`

这一点来自 Inno Setup 脚本：

- 源文件：`prismwave_demo.exe`
- 安装目标名：`PrismWave.exe`

因此，仓库内部仍能看到 `demo` 字样，不代表当前对外产品仍叫 demo。

## 21. 测试现状

当前测试数量很少，主要包括：

- `app/test/playback_strategy_test.dart`
  - 覆盖 next/previous 的模式策略
- `app/test/widget_test.dart`
  - 仅校验播放模式 label

这意味着高风险区域目前几乎没有自动化保障：

- 扫描与元数据补全
- 音频路由切换
- 独占模式恢复
- 歌词解析
- 在线歌词 / 封面接口
- FullPlay 渲染

后续若要增强稳定性，建议优先补：

1. `PlaybackController` 关键分支测试
2. `lyrics_reader.dart` 解析器测试
3. `online_lyrics_service.dart` 的 mock 数据测试
4. `online_cover_service.dart` 的排序 / 去重测试

## 22. 当前已知差异与注意事项

### 22.1 旧文档与代码不一致的点

当前已确认的不一致项包括：

- 正式版本不是 `R010`，而是 `R011`
- 安装器默认回退路径不是 `C:\Program Files\PrismWave`，而是 `C:\PrismWave`
- README 里“demo 版本”的表述已经落后
- README 中提到的 `step.md` 当前仓库并不存在
- 当前应用标题文案仍是 `PrismWave Demo / 演示版`

### 22.2 扫描格式与播放格式不完全一致

扫描支持更多格式，但播放控制器当前只允许一部分格式进入播放链路。

### 22.3 `lowEffects` 逻辑还在，但 UI 没有明显入口

这是一个值得后续决定是恢复还是清理的点。

### 22.4 Rust 核心仍是占位

`native/rust_core` 当前只有最基础的占位 FFI：

- `prismwave_core_api_version`
- `prismwave_ping`
- `prismwave_load_track`
- `prismwave_play`
- `prismwave_pause`
- `prismwave_seek`

目前真正的播放仍完全由 Flutter + just_audio/media_kit 完成。

## 23. 后续开发建议

结合当前代码与现有路线图，下一阶段更适合继续做的方向包括：

1. 播放队列系统
2. 本地播放列表系统
3. 最近播放 / 播放历史
4. 文件夹变更自动刷新
5. 歌词编辑器
6. 输出设备热切换增强
7. Gapless / Crossfade 深化

不建议当前优先转向：

- 在线曲库
- 云同步账号体系
- 社交功能
- 复杂推荐算法

项目当前最强的优势仍然是：

- 本地库管理
- Windows 音频输出控制
- FullPlay 歌词体验
- 质感桌面 UI

## 24. 建议的维护方式

如果后续继续迭代，建议遵守以下顺序：

1. 先判断问题发生在 Dart 上层，还是本地 fork 音频桥
2. 播放模式问题优先查 `PlaybackStrategy`
3. 设备/无声问题优先查开发者模式日志
4. 歌词问题先区分本地解析、在线搜索、UI 渲染三个层面
5. 文档冲突时以代码和本文件为准

---

最后更新说明：

- 本文档基于当前仓库代码状态整理
- 当前正式版本按 `R011` 维护
- 仓库地址按 `https://github.com/shanbei2033/PrismWave` 维护
