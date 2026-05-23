<div align="center">
  <h1>PrismWave</h1>
  <img src="assets/logo.png" alt="logo" width="128">
  <br>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPLv3-blue" alt="License"></a>
  <a href="https://github.com/shanbei2033/PrismWave/releases"><img src="https://img.shields.io/badge/release-R401_fix-blue" alt="Release"></a>
  <a href="https://flutter.dev"><img src="https://img.shields.io/badge/flutter-3.29.3-blue" alt="Flutter"></a>
</div>

<br>

[English README](./README.md)

PrismWave 是一个基于 Flutter 开发的 Windows 本地音乐播放器。

## 功能

- 本地音乐库扫描与文件夹管理
- 音乐库 / 专辑 / 艺术家 / 我最爱 视图，支持拖拽排序
- 底部播放栏 + 全屏播放页
- 播放队列，支持拖拽排序
- 播放模式：列表循环、单曲循环、随机播放
- 音频输出模式：兼容模式、WASAPI 共享、WASAPI 独占
- 歌词：本地歌词、在线搜索与缓存、逐字歌词、QQ QRC 解码
- HITS 广播模式：基于节目单的在线播放，9 个音源 provider，封面与歌词缓存，预加载
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

## 音频说明

播放后端为 `just_audio + media_kit + MPV`。

Windows 下可用的输出模式：

- 兼容模式
- WASAPI 共享
- WASAPI 独占

## HITS 模式

HITS 是一个广播电台模式，按节目单播放在线内容：

- 从 `prismwave-hits` 仓库拉取节目单
- 9 个音源 provider 解析音频（B 站、YouTube、Audius、网易云、酷我、咪咕、QQ 音乐、酷狗）
- 封面、歌词、音频本地缓存
- 后台预加载即将播放的曲目

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
