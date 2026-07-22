# PrismWave AI 接手文档

更新时间：2026-07-22

**语言要求：接手 AI 必须使用中文与用户沟通。**

本文档帮助其他 AI 快速接手 `E:\Project\PrismWave` 开发。主线为 WinUI 3 / C# 重构，Flutter 工程（`app/`）保留为行为回归基线。

---

## 0. 当前状态

| 项目 | 状态 |
|------|------|
| 工作目录 | `E:\Project\PrismWave` |
| Git 分支 | `WinUI`（`main` 是最终主分支，WinUI 重构完成后统一合并） |
| WinUI 工程 | `src/PrismWave.WinUI/`，技术栈 WinUI 3 / .NET 10 / CommunityToolkit.Mvvm / TagLibSharp |
| 测试工程 | `tests/PrismWave.WinUI.Tests/`，461 项测试通过 |
| 播放后端 | 普通播放用 `native/libmpv-winui/libmpv-2.dll`（完整解码版）；DSD 用 BASS 三件套 |
| Flutter 基线 | R503，位于 `app/`，不得删除 |
| 最高优先级 | 完成 MP3/FLAC/WAV/OGG、中文长路径、删除源文件、DSD/ASIO、在线 provider 真机矩阵 |

WinUI 工程已具备壳层、MVVM、mpv/DSD 播放、本地库、在线服务、歌词、封面、HITS、设置和多页面 UI，但仍处迁移开发期，不能宣称全功能等价。

### 构建、测试和启动

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore
dotnet run --project src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-build
```

Debug exe：`src\PrismWave.WinUI\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\PrismWave.WinUI.exe`

**强制规则：每次代码改动后都必须打开 demo 验证效果。若 demo 正在运行，先杀掉进程再重新打开**（否则运行中的旧 exe 被锁定，会导致重新构建/启动失败或看到旧界面）。

```powershell
# 1) 杀掉正在运行的实例
Get-Process PrismWave.WinUI -ErrorAction SilentlyContinue | Stop-Process -Force
# 2) 构建后启动最新 exe
Start-Process "src\PrismWave.WinUI\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\PrismWave.WinUI.exe"
```

---

## 1. WinUI 架构

```
src/PrismWave.WinUI/
├── App.xaml(.cs) / MainWindow.xaml(.cs)    启动、窗口、Shell 宿主
├── Infrastructure/                          AppServices（手工组合根）、Audio/、Navigation/、Persistence/
├── Models/ / Services/Contracts/ / Services/Implementations/
├── ViewModels/                              Shell/Player/Home/Search/Library/Hits/Settings
├── Views/                                   Shell/Home/Search/Library/Player/Hits/Settings/Dialogs
├── Controls/                                Navigation/Playback/Home/Lyrics/Media/Common
└── Themes/                                  PrismTokens.xaml（含 PrismPagePadding 24,18,24,18）、PrismControls.xaml
```

MVVM 模式：View 负责 XAML，ViewModel 基于 CommunityToolkit.Mvvm，服务通过接口注入。`AppServices.cs` 是手工组合根——不要把 mpv/BASS/文件系统/网络请求塞进页面 code-behind。

### 已完成能力概要

- **导航**：覆盖式动画（280ms，新页面从右滑入），`CoverNavigationCoordinator` 状态机驱动。
- **播放**：`PlaybackService` 统一调度，三档输出（MPV自动/WASAPI共享/WASAPI独占）+ 自动回退。`PlaybackViewModel` 是全局播放状态入口。
- **本地库**：`LocalMusicScanner` 递归扫描 + TagLibSharp 元数据，`LibraryService` 可取消扫描 revision 管理。
- **在线**：schema 8 首页缓存/回退，7 个普通 provider，`OnlinePlaybackResolver` 多源竞速解析。
- **歌词/封面**：`LyricsStageControl` 单画布 Win2D 歌词舞台；`CoverService` 多源封面搜索，缓存文件名含内容哈希。
- **HITS**：`HitsService` manifest/schedule 拉取，10 个 provider（含 bilibili/YouTube 兜底），强制 WASAPI Shared。
- **缺口**：`IWindowService`/`IDialogService`/`IUpdateService` 仍在 `PlaceholderContracts.cs` 未正式实现。

---

## 2. 关键约束（不要做的事）

- **两套 libmpv 不可混用**：`native/libmpv/` 是 Flutter WASAPI 修补版（缺 E-AC-3 解码），`native/libmpv-winui/` 是 WinUI 完整解码版。两者均必须保留。
- **不要**让 WinUI 加载 `native/libmpv/libmpv-2.dll`——E-AC-3 M4A 会立即 mpv error。
- **不要**升级 media_kit 而不重新核对 libmpv 兼容性。
- **不要**把 bilibili/bilivideo/YouTube 加回普通在线搜索——只保留给 HITS 兜底。
- **不要**把 `_kSchemaVersion` 降回 7，不要把远端 `topPlaylist.subtitle` 作为生成时间展示。
- **不要**把首页今日趋势卡片的 TOP100 标签、副标题、查看按钮加回来。
- **不要**重新引入"DSD 自动强制独占"或把 HITS 改回非 WASAPI Shared。
- **不要**运行 `git clean`/`git reset --hard`/大范围 checkout。
- **不要**因为文件 untracked 就删除——WinUI 工程依赖这些文件。
- `bin/`、`obj/`、`AppPackages/`、`artifacts/` 不得进入分支。
- 不要把 Flutter 改动与 WinUI 基线混成一个大提交。
- 不要提交 API key、CLAUDE.md 等隐私文件。
- Deezer/iTunes 音频 URL 是 30 秒预览，绝不能作为播放源。
- 每轮 UI 修改后验证 1280/1440/1600/1920 宽度 + 侧栏展开/折叠 + 快速连续点击导航。

---

## 3. 在线首页与 prismwave-hits

每日推荐由独立仓库 `https://github.com/shanbei2033/prismwave-hits`（本地 `D:\Project\prismwave-hits`）的 GitHub Actions 每天北京时间 10:00 生成 schema 8 JSON。客户端不生成推荐，只拉取 `home/latest_home.json`。

缓存策略：当天缓存优先 → 无当天缓存拉远程 → 远程失败回退昨日缓存 → 无昨日缓存用内置 `Assets/HomeFallback/latest_home.json`。北京时间 00:00-10:00 期间 remote 可能仍是昨日 edition，属正常窗口期。

普通在线 provider：Audius、NetEase、Kuwo、Migu、QQ、Kugou、Taihe。
HITS 在此基础上增加 bilibili、bilivideo、YouTube。

---

## 4. 已知问题

| 问题 | 状态 |
|------|------|
| `IWindowService` 等三个服务未正式实现 | 仍在 `PlaceholderContracts.cs` |
| DSD 设备切换后当前曲目不自动重载 | 需修复 |
| 6 个中国 provider 长期可用性 | 需真机/长期验证 |

### 下一步

1. 本地播放真机矩阵（MP3/FLAC/WAV/OGG、中文长路径、seek、队列、设备切换）
2. 本地库写操作（拖拽排序、移出库、删除源文件、旁置联动）
3. DSD 真机（DSF/DFF、ASIO、raw DSD、DoP）
4. 补齐 `IWindowService`/`IDialogService`/`IUpdateService`
5. 在线链路验证（7 provider 搜索/解析、schema 8 缓存/回退）
6. 歌词/FullPlay 验证、HITS 验证
7. 发布前打磨（可访问性、多 DPI、MSIX/安装包）

---

## 5. 最近修复（2026-07-22）

1. **封面替换 Bug**：`CoverService` 文件名仅基于曲目身份，同格式不同封面产生同路径，导致 `StableCoverImage` 和 `PlaybackViewModel` 路径守卫跳过更新。修复：文件名加入图片内容 SHA-256。
2. **导航动画闪烁**：`ShellPage.xaml.cs` 的 `PrepareIncoming` 用 `intent.HostWidth`（始终为 0）定位传入 Frame。修复：改用 `PageTransitionHost.ActualWidth`。
3. **布局间隙**：Shell 内容 Grid 的 `Margin="24,0,30,0"` 与页面 `PrismPagePadding` 重复叠加。修复：移除 Shell Margin，为 HomePage 补 `PrismPagePadding`。
4. **Release 便携版打不开**：`PrismWave.WinUI.csproj` Release 默认 `PublishTrimmed=True`，WinUI 3 不支持 trimming（裁掉 WinRT/XAML/Win2D 反射元数据），解压式 exe 启动即崩、无窗口。修复：Release `PublishTrimmed` 改 False；`win-x64.pubxml` 首次入库（原被 `.gitignore` 的 `*.pubxml` 忽略）并补 `WindowsPackageType=None` 确保无包身份主题资源完整。（提交 `ddb30d8`）
5. **导航栏"专辑/HITS"图标消失**：两个 svg 以 `<Content>` 引入但缺 `CopyToOutputDirectory`，unpackaged 发布不复制，`ms-appx` 运行时解析不到。修复：`csproj` 两个 svg 补 `CopyToOutputDirectory="PreserveNewest"`。
6. **音量条非无级调节**：`Slider` 默认 `StepFrequency=1`，音量范围 0-1 只能停两端。修复：`BottomPlayerBar.xaml` 与 `FullPlayPage.xaml` 音量条加 `StepFrequency="0.01"`；BottomPlayerBar 音量绑定 `TwoWay`→`OneWay`（`Volume` 为 private set，实际变更走 `ValueChanged`）。
7. **FullPlay 最大化布局**：左栏封面所在 `*` 行吸收全部额外高度，封面居中而歌名/控件/进度条被压到底部、中间留大空隙。修复：`FullPlayPage.xaml` 改为顶部+底部两个 `*` 弹性 spacer 包裹，内容行全部 `Auto`，整块内容垂直居中、控件紧贴封面下方。
8. **FullPlay 切行颤动**：此前已修复。

---

## 6. Flutter 基线参考

R503（`pubspec.yaml` 503.0.0+505）是行为对照基线。技术栈：Flutter 3.41.4 / Riverpod / just_audio_media_kit → libmpv。构建：`tools\flutter\bin\flutter.bat build windows --release`，产物 `app/build/windows/x64/runner/Release/prismwave_demo.exe`。WASAPI Exclusive 破音已通过二进制修补 `native/libmpv/libmpv-2.dll`（端点缓冲 3ms→50ms）修复，切换 media_kit 版本需重新核对。
