<div align="center">
  <h1>PrismWave</h1>
  <img src="assets/logo.png" alt="logo" width="128">
  <br>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPLv3-blue" alt="License"></a>
  <a href="https://github.com/shanbei2033/PrismWave/releases"><img src="https://img.shields.io/badge/release-v1.0.4-blue" alt="Release"></a>
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-10-blue" alt=".NET"></a>
</div>

[English README](./README.md)

PrismWave 是一款基于 **WinUI 3 和 .NET 10** 的原生 Windows 11 音乐播放器。
支持真实本地曲库管理、在线推荐、FullPlay 歌词舞台、DSD HiFi 播放，以及可配置的 Windows 11 材质（纯色/云母/亚克力）。

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

## 最新版本：v1.0.4

本次发布新增启动动画、搜索历史管理和版本检测功能：

- 新增应用启动动画：PrismWave Logo 从左侧滑入并从上方落下组合，停留后向右飞出渐隐，过渡到首页
- 新增搜索历史右键删除功能：在搜索页历史记录上右键即可删除对应记录
- 新增版本检测功能：设置页基本选项底部新增版本卡片，支持手动检测 GitHub Release 最新版本
- 新增自动检测更新：开启后每次启动应用静默检测新版本，有更新时右上角弹出通知弹窗
- 更新通知弹窗支持关闭按钮，点击后弹窗缩回右侧
- 修复启动动画播放期间关闭窗口导致进程无法正常退出的问题

下载请前往 [v1.0.4 Release 页面](https://github.com/shanbei2033/PrismWave/releases/tag/v1.0.4)。

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
git checkout main
dotnet run --project src/PrismWave.WinUI/PrismWave.WinUI.csproj -p:Platform=x64
```

## 构建（WinUI）

```powershell
git checkout main
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

## 鸣谢

- [QQMusicDecoder](https://github.com/WXRIW/QQMusicDecoder)：帮助确认了 QQ `QRC` 逐字歌词的处理链路，尤其是歌词内容在解析前所需的解密与解压步骤。
- [LDDC](https://github.com/chenmozhijin/LDDC)：为逐字歌词 / 同步歌词的格式细节、解析容错和边界情况处理提供了很有价值的参考。

## 许可证

GPL-3.0
