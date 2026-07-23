<div align="center">
  <h1>PrismWave</h1>
  <img src="assets/logo.png" alt="logo" width="128">
  <br>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPLv3-blue" alt="License"></a>
  <a href="https://github.com/shanbei2033/PrismWave/releases"><img src="https://img.shields.io/badge/release-v1.0.3-blue" alt="Release"></a>
  <a href="https://flutter.dev"><img src="https://img.shields.io/badge/flutter-3.41.4-blue" alt="Flutter"></a>
</div>

[English README](./README.md)

PrismWave WinUI 是 PrismWave 的原生 Windows 11 音乐播放器版本，结合真实本地曲库、在线推荐、搜索、播放队列、FullPlay 歌词和可配置的 Windows 材质。

## WinUI 版本

默认开发分支是 [`WinUI`](https://github.com/shanbei2033/PrismWave/tree/WinUI)。
该分支是当前主要的原生实现，包含：

- WinUI 3 桌面外壳与沉浸式 FullPlay
- 支持元数据和封面回退的真实递归本地曲库扫描
- MPV、WASAPI 共享和 WASAPI 独占播放路径
- 在线音源故障转移与短期磁盘音频缓存
- 自动歌词，以及可用时的 QQ QRC / 网易云 YRC 逐字时间轴
- 播放队列、右键操作、封面替换和返回状态保留
- 经典纯色、浅色 Windows 11 云母和亚克力外观切换
- 在线歌曲可从搜索页和首页添加到库与收藏

Flutter 应用仍保留在 `app/` 目录，作为旧版/对照客户端。

## 最新版本：v1.0.3

本次发布带来多项体验改进和新功能：

- 修复单曲循环模式下歌曲播放完成后无法重新播放的问题
- 修复切换歌曲时进度条滑块未归位的问题
- 移除库页面的"更多选项"按钮，改为右键上下文菜单
- 收藏按钮统一无边框样式，更简洁
- 修复播放在线歌曲时收藏按钮灰色不可用的问题
- 搜索结果和首页趋势歌曲支持"添加到库"
- 在线歌曲加入库后持久保存，rescan 后不丢失
- 增强 migu、酷狗、太合的音源解析（添加 gdstudio 兜底）

下载请前往 [v1.0.3 Release 页面](https://github.com/shanbei2033/PrismWave/releases/tag/v1.0.3)。

## 功能

- 本地音乐库扫描与文件夹管理
- 在线优先启动：应用启动后直接进入首页，首次打开默认启用在线模式
- 在线首页：TOP100 今日趋势、应用内刷新推荐、新专辑、热门歌曲，以及本地/在线统一搜索
- 音乐库 / 专辑 / 艺术家 / 我最爱 视图，支持拖拽排序与右键上下文菜单
- 底部播放栏 + 全屏播放页
- 播放队列，支持拖拽排序与在线音源按需恢复
- 播放模式：列表循环、单曲循环、随机播放
- 音频输出模式：兼容模式、WASAPI 共享、WASAPI 独占
- 歌词：本地歌词、在线自动匹配、在线搜索与缓存、逐字歌词、QQ QRC 解码
- HITS 广播模式：基于节目单的在线播放，10 个音源 provider，封面与歌词缓存，预加载
  - HITS 节目单由 [prismwave-hits](https://github.com/shanbei2033/prismwave-hits) 仓库生成
- 在线歌曲可从搜索页和首页添加到库与收藏
- 开发者模式：实时播放日志窗口与本地日志文件

## 技术栈

- WinUI 3 / Windows App SDK
- C# / .NET 10
- Win2D 与 Windows Composition
- libmpv，支持 WASAPI 共享 / 独占路由
- TagLib# 本地元数据读取
- Flutter (3.41.4) 旧版/对照客户端
- Riverpod
- just_audio + just_audio_media_kit (media_kit / MPV)
- Windows Desktop

## 项目结构

```text
PrismWave/
  app/                   Flutter 应用
  src/PrismWave.WinUI/   原生 WinUI 应用
  tests/                 WinUI 回归测试
  installer/             Inno Setup 安装包脚本
  tools/flutter/         内置 Flutter SDK
```

## 运行（WinUI）

```powershell
git switch WinUI
dotnet run --project src/PrismWave.WinUI/PrismWave.WinUI.csproj -p:Platform=x64
```

## 构建（WinUI）

```powershell
dotnet build src/PrismWave.WinUI/PrismWave.WinUI.csproj -p:Platform=x64 --no-restore
dotnet test tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj --no-restore
```

原生 WinUI 输出位于 `src/PrismWave.WinUI/bin/x64/`。

## 运行（Flutter 旧版客户端）

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

## 构建（Flutter 旧版客户端）

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
dist/PrismWave-Setup-R503.exe
```

## 音频说明

WinUI 播放后端直接使用 libmpv，支持 WASAPI 路由。

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

在搜索结果或首页趋势歌曲中遇到的在线歌曲，可以直接添加到库或收藏。已添加的在线歌曲在 rescan 后仍保留，并出现在音乐库、专辑、艺术家和我最爱视图中。

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
