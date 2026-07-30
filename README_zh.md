<div align="center">
  <h1>PrismWave</h1>
  <img src="assets/logo.png" alt="logo" width="128">
  <br>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPLv3-blue" alt="License"></a>
  <a href="https://github.com/shanbei2033/PrismWave/releases"><img src="https://img.shields.io/badge/release-v1.0.6-blue" alt="Release"></a>
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-10-blue" alt=".NET"></a>
</div>

[English README](./README.md)

PrismWave 是一款基于 **WinUI 3 和 .NET 10** 的原生 Windows 11 音乐播放器。
支持真实本地曲库管理、在线推荐、FullPlay 歌词舞台，以及可配置的外观样式（深色/浅色 Beta）。

## WinUI 版本

默认开发分支是 [`WinUI`](https://github.com/shanbei2033/PrismWave/tree/WinUI)。
该分支是当前主要的原生实现，包含：

- WinUI 3 桌面外壳与沉浸式 FullPlay
- 支持元数据和封面回退的真实递归本地曲库扫描
- MPV、WASAPI 共享和 WASAPI 独占播放路径
- 在线音源故障转移与短期磁盘音频缓存
- 自动歌词，以及可用时的 QQ QRC / 网易云 YRC 逐字时间轴
- 播放队列、右键操作、封面替换和返回状态保留
- 经典深色和浅色 Windows 11 云母（Beta）外观切换
- 在线歌曲可从搜索页和首页添加到库与收藏

## 使用文档

详细的功能使用说明——更换封面、歌词搜索与偏移调整、播放队列、HITS 电台、音频输出设置等——请参阅[使用指南](./docs/usage-guide.md)。

## 最新版本：v1.0.6

本次发布修复关键播放可靠性问题：

- 修复进度条不更新：mpv 播放时进度条偶发完全静止，改为定时器轮询播放位置并增加 0.05 秒防抖，所有输出模式下进度汇报均稳定可靠
- 修复 WASAPI 共享/独占模式切换失败：切换输出模式时设备 ID 格式未按 mpv WASAPI 后端要求处理，现已自动修正
- 修复 MPV 兼容模式下"音频输出初始化失败"：统一使用 mpv 要求的规范设备 ID 格式，所有模式下均可正常初始化

下载请前往 [v1.0.6 Release 页面](https://github.com/shanbei2033/PrismWave/releases/tag/v1.0.6)。

## 功能

- 本地音乐库扫描与文件夹管理
- 在线功能首次启动默认关闭，可在设置 > BETA / 在线模式中手动开启
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
- 开发者模式：实时 PowerShell 日志输出窗口与本地日志文件
- 启动动画：PrismWave Logo 入场与退场动画
- 版本检测：GitHub Release API 自动检测更新与通知弹窗

## 技术栈

- WinUI 3 / Windows App SDK
- C# / .NET 10
- Win2D 与 Windows Composition
- libmpv，支持 WASAPI 共享 / 独占路由
- TagLib# 本地元数据读取
- Windows Desktop

## 项目结构

```text
PrismWave/
  src/PrismWave.WinUI/   原生 WinUI 应用
  tests/                 WinUI 回归测试
  native/                原生库（libmpv、BASS/DSD）
  tools/                 构建工具
```

## 运行（WinUI）

```powershell
git checkout WinUI
dotnet run --project src/PrismWave.WinUI/PrismWave.WinUI.csproj -p:Platform=x64
```

## 构建（WinUI）

```powershell
git checkout WinUI
dotnet build src/PrismWave.WinUI/PrismWave.WinUI.csproj -p:Platform=x64 --no-restore
dotnet test tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj --no-restore
```

原生输出位于 `src/PrismWave.WinUI/bin/x64/`。

## 音频说明

WinUI 播放后端直接使用 libmpv，支持 WASAPI 路由。

Windows 下可用的输出模式：

- 兼容模式
- WASAPI 共享
- WASAPI 独占

## 在线音源

PrismWave 使用 [GD音乐台在线音乐平台 API](https://music-api.gdstudio.xyz/api.php)
作为播放链接解析、跨源搜索和封面获取的回退代理。

## 鸣谢

- [GD音乐台](https://music.gdstudio.xyz)：提供在线音乐回退 API，用于播放链接解析、跨源搜索与封面获取。
- [QQMusicDecoder](https://github.com/WXRIW/QQMusicDecoder)：帮助确认了 QQ `QRC` 逐字歌词的处理链路，尤其是歌词内容在解析前所需的解密与解压步骤。
- [LDDC](https://github.com/chenmozhijin/LDDC)：为逐字歌词 / 同步歌词的格式细节、解析容错和边界情况处理提供了很有价值的参考。

## 许可证

GPL-3.0
