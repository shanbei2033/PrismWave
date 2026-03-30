# PrismWave

[English README](./README.md)

PrismWave 是一个基于 Flutter 开发的 Windows 本地音乐播放器。

这个仓库目前包含第一个可用的 Demo 版本。当前构建重点打通了桌面端体验、音乐库结构、播放流程、输出模式以及歌词显示等核心链路。

## 当前功能

- 本地音乐库扫描
- 音乐库 / 专辑 / 艺术家 / 我最爱 视图
- 搜索与收藏管理
- 底部播放栏
- 带同步歌词的 Full Play 页面
- 播放模式：列表循环、单曲循环、随机播放
- 音频输出模式：兼容模式、WASAPI 共享、WASAPI 独占
- 开发者模式：实时播放日志窗口与本地日志文件

## 技术栈

- Flutter
- Riverpod
- just_audio
- media_kit / MPV
- Windows Desktop

## 项目结构

```text
PrismWave/
  app/               Flutter 应用
  native/rust_core/  预留的 Rust 音频核心工作区
  backups/           本地备份
  dev.md             产品与架构说明
  step.md            开发流程记录
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

当前 Demo 使用 `just_audio + media_kit + MPV` 作为播放后端。

Windows 下可用的输出模式：

- 兼容模式
- WASAPI 共享
- WASAPI 独占

曲目切换行为由应用层控制，依据当前播放列表上下文、当前索引以及播放模式决定下一首逻辑。

## 开发者模式

启用开发者模式后，PrismWave 会打开一个实时日志窗口，并将播放日志写入：

```text
C:\Users\<你的用户名>\AppData\Local\PrismWave\logs\
```

这些日志主要用于排查播放错误、输出模式诊断以及自动切换相关问题。

## 许可证

GPL-3.0
