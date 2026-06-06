<div align="center">
  <h1>PrismWave</h1>
  <img src="assets/logo.png" alt="logo" width="128">
  <br>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPLv3-blue" alt="License"></a>
  <a href="https://github.com/shanbei2033/PrismWave/releases"><img src="https://img.shields.io/badge/release-R501-blue" alt="Release"></a>
  <a href="https://flutter.dev"><img src="https://img.shields.io/badge/flutter-3.29.3-blue" alt="Flutter"></a>
</div>

[English README](./README.md)

PrismWave 是一个基于 Flutter 开发的 Windows 音乐播放器，结合了本地音乐库和在线优先的首页推荐、搜索与播放队列。

## 功能

- 本地音乐库扫描与文件夹管理
- 在线优先启动：应用启动后直接进入首页，首次打开默认启用在线模式
- 在线首页：TOP100 今日趋势、应用内刷新推荐、新专辑、热门歌曲，以及本地/在线统一搜索
- 音乐库 / 专辑 / 艺术家 / 我最爱 视图，支持拖拽排序
- 底部播放栏 + 全屏播放页
- 播放队列，支持拖拽排序与在线音源按需恢复
- 播放模式：列表循环、单曲循环、随机播放
- 音频输出模式：兼容模式、WASAPI 共享、WASAPI 独占
- 歌词：本地歌词、在线自动匹配、在线搜索与缓存、逐字歌词、QQ QRC 解码
- HITS 广播模式：基于节目单的在线播放，10 个音源 provider，封面与歌词缓存，预加载
  - HITS 节目单由 [prismwave-hits](https://github.com/shanbei2033/prismwave-hits) 仓库生成
- Windows DSD 后端（BASS/BASSDSD/BASSASIO FFI）
- 开发者模式：实时播放日志窗口与本地日志文件

## 技术栈

- Flutter (3.29.3)
- Riverpod
- just_audio + just_audio_media_kit (media_kit / MPV)
- BASS / BASSDSD / BASSASIO（DSD 播放）
- Windows Desktop

## 项目结构

```text
PrismWave/
  app/                   Flutter 应用
  native/windows_dsd/    BASS/BASSDSD/BASSASIO 原生运行库
  installer/             Inno Setup 安装包脚本
  tools/flutter/         内置 Flutter SDK
```

## 运行

如果你的环境中已经安装了 Flutter：

```powershell
cd app
flutter pub get
flutter run -d windows
```

如果你希望使用仓库内置的本地 Flutter 工具链：

```powershell
cd app
..\tools\flutter\bin\flutter.bat pub get
..\tools\flutter\bin\flutter.bat run -d windows
```

## 构建

```powershell
cd app
..\tools\flutter\bin\flutter.bat build windows --release
```

Release 输出路径：

```text
app/build/windows/x64/runner/Release/prismwave_demo.exe
```

安装包构建：

```powershell
& "C:\Users\Admin\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer\PrismWaveSetup.iss
```

安装包输出路径：

```text
dist/PrismWave-Setup-R501.exe
```

## 音频说明

播放后端为 `just_audio + media_kit + MPV`。

Windows 下可用的输出模式：

- 兼容模式
- WASAPI 共享
- WASAPI 独占

## HITS 模式

HITS 是一个广播电台模式，按节目单播放在线内容：

- 从 `prismwave-hits` 仓库拉取节目单
- 10 个音源 provider 解析音频（B 站、YouTube、Audius、网易云、酷我、咪咕、QQ 音乐、酷狗、千千/太合）
- 封面、歌词、音频本地缓存
- 后台预加载即将播放的曲目

## 在线模式

首次启动时在线模式默认启用，PrismWave 会直接打开首页，加载推荐分区，并支持搜索结果立即组成播放队列。队列中尚未解析完成的在线歌曲会在后台继续解析；如果某个在线音源在实际播放时失败，PrismWave 会让该源失效并重新从可用 provider 中寻找可播放地址。

R501 中，首页右上角刷新按钮会直接从应用内刷新推荐歌曲与专辑，同时保持趋势榜单独立更新。在线搜索和在线播放接入了更多非视频音乐源；fullplay 页面会在播放在线歌曲时结合当前播放时长自动匹配在线歌词。

## 开发者模式

启用开发者模式后，PrismWave 会打开一个实时日志窗口，并将播放日志写入：

```text
C:\Users\<你的用户名>\AppData\Local\PrismWave\logs\
```

## 鸣谢

- [QQMusicDecoder](https://github.com/WXRIW/QQMusicDecoder)：帮助确认了 QQ `QRC` 逐字歌词的处理链路，尤其是歌词内容在解析前所需的解密与解压步骤。
- [LDDC](https://github.com/chenmozhijin/LDDC)：为逐字歌词 / 同步歌词的格式细节、解析容错和边界情况处理提供了很有价值的参考。

## 许可证

GPL-3.0
