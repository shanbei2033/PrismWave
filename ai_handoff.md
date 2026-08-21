# PrismWave AI 接手文档

更新时间：2026-08-21 (v1.1.0)

**语言要求：接手 AI 必须全程使用中文与用户沟通。**

本文档帮助其他 AI 快速接手 `E:\Project\PrismWave` 开发。项目主线为 **原生 WinUI 3 / C# 音乐播放器**，完全移除了 Flutter 代码。

---

## 0. 当前状态

| 项目 | 状态 |
|------|------|
| 工作目录 | `E:\Project\PrismWave` |
| Git 分支 | `WinUI` (默认分支) |
| WinUI 工程 | `src/PrismWave.WinUI/`，技术栈 WinUI 3 / .NET 10 / CommunityToolkit.Mvvm / TagLibSharp |
| 测试工程 | `tests/PrismWave.WinUI.Tests/`，458 项测试通过 |
| 播放后端 | `native/libmpv-winui/libmpv-2.dll`（完整解码版，支持 MPV 自动/WASAPI 共享/WASAPI 独占） |
| 推荐生成器 | `e:\Project\prismwave-hits` (独立 Python 脚本仓库，schema 8 每日首页) |
| 安装包构建 | `tools/build_installer.ps1` + `tools/setup.iss`（Inno Setup 6），产物 `artifacts/PrismWave-Setup-x.x.x.exe` |
| 最高优先级 | 本地播放真机矩阵验证、在线链路验证、发布前打磨 |

WinUI 工程是**唯一活跃开发分支**，具备完整的本地曲库扫描、元数据读取、在线搜索和播放、歌词系统、HITS 电台模式、FullPlay 逐字歌词舞台、收藏管理、设置功能、启动动画和版本检测。当前版本 **v1.1.0**。

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

MVVM 模式：View 负责 XAML，ViewModel 基于 CommunityToolkit.Mvvm，服务通过接口注入。`AppServices.cs` 是手工组合根——不要把 mpv/文件系统/网络请求塞进页面 code-behind。

### 已完成能力概要

- **导航**：覆盖式动画（280ms，新页面从右滑入），`CoverNavigationCoordinator` 状态机驱动。
- **播放**：`PlaybackService` 统一调度，三档输出（MPV 自动/WASAPI 共享/WASAPI 独占）+ 自动回退。`PlaybackViewModel` 是全局播放状态入口。
- **本地库**：`LocalMusicScanner` 递归扫描 + TagLibSharp 元数据，`LibraryService` 可取消扫描 revision 管理。
- **在线**：schema 8 首页缓存/回退，7 个普通 provider，`OnlinePlaybackResolver` 多源竞速解析。
- **歌词/封面**：`LyricsStageControl` 单画布 Win2D 歌词舞台；`CoverService` 多源封面搜索，缓存文件名含内容哈希。
- **HITS**：`HitsService` manifest/schedule 拉取，10 个 provider（含 bilibili/YouTube 兜底），强制 WASAPI Shared。
- **启动动画**：`SplashPage` 显示 PrismWave Logo 入场动画（Prism 从左侧滑入 + Wave 从上方落下），停留后整体向右飞出渐隐，过渡到首页。总时长约 1.75 秒。
- **版本检测**：`UpdateService` 调用 GitHub Release API 检测新版本，设置页版本卡片支持手动检测，开启自动检测后启动时静默查询并在右上角弹出通知弹窗。
- **缺口**：`IWindowService`/`IDialogService` 仍在 `PlaceholderContracts.cs` 未正式实现。`IUpdateService` 已正式实现。

---

## 2. 关键约束（不要做的事）

- **两套 libmpv 不可混用**：`native/libmpv/` 是 Flutter WASAPI 修补版（缺 E-AC-3 解码），`native/libmpv-winui/` 是 WinUI 完整解码版。两者均必须保留。
- **不要**让 WinUI 加载 `native/libmpv/libmpv-2.dll`——E-AC-3 M4A 会立即 mpv error。
- **不要**升级 media_kit 而不重新核对 libmpv 兼容性。
- **不要**把 bilibili/bilivideo/YouTube 加回普通在线搜索——只保留给 HITS 兜底。
- **不要**把 `_kSchemaVersion` 降回 7，不要把远端 `topPlaylist.subtitle` 作为生成时间展示。
- **不要**把首页今日趋势卡片的 TOP100 标签、副标题、查看按钮加回来。
- **不要**把 HITS 改回非 WASAPI Shared。
- **不要**运行 `git clean`/`git reset --hard`/大范围 checkout。
- **不要**因为文件 untracked 就删除——WinUI 工程依赖这些文件。
- `bin/`、`obj/`、`AppPackages/`、`artifacts/` 不得进入分支。
- 不要把 Flutter 改动与 WinUI 基线混成一个大提交。
- 不要提交 API key、CLAUDE.md 等隐私文件。
- Deezer/iTunes 音频 URL 是 30 秒预览，绝不能作为播放源。
- 每轮 UI 修改后验证 1280/1440/1600/1920 宽度 + 侧栏展开/折叠 + 快速连续点击导航。
- **不要使用 WinAppSDK self-contained 部署**：捆绑的原生 DLL（如 CoreMessagingXP.dll）在编译期拷贝到应用目录时会触发 FailFast(0xC0000602)。必须使用 framework-dependent 部署模式，运行时依赖系统级 Windows App Runtime 2.2 MSIX 包。
- **不要禁用 .NET 依赖检测**：自 v1.1.0 起，PrismWave 需要 .NET 10 Desktop Runtime。setup.iss 会在安装向导内自动检测并引导下载安装，移除该检测会导致安装失败。

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
| `IWindowService`/`IDialogService` 未正式实现 | 仍在 `PlaceholderContracts.cs` |
| 6 个中国 provider 长期可用性 | 需真机/长期验证 |

### 下一步

1. 本地播放真机矩阵（MP3/FLAC/WAV/OGG、中文长路径、seek、队列、设备切换）
2. 本地库写操作（拖拽排序、移出库、删除源文件、旁置联动）
3. 补齐 `IWindowService`/`IDialogService`
4. 在线链路验证（7 provider 搜索/解析、schema 8 缓存/回退）
5. 歌词/FullPlay 验证、HITS 验证
6. 安装包优化（体积、用户体验、错误处理）

---

## 5. 安装包构建规范（v1.1.0+）

自 v1.1.0 起改用 Inno Setup 单 exe 安装包，替代便携版 zip。构建流程如下：

### 前置条件

- **Inno Setup 6.7.3**：通过 `winget install JRSoftware.InnoSetup` 安装。注意用户级安装路径为 `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`，而非 ProgramFiles 目录。
- **ChineseSimplified.isl**：语言包文件需单独下载到 `tools/ChineseSimplified.isl`（jrsoftware 官方发行不带中文）。下载地址：`https://raw.githubusercontent.com/kira-96/Inno-Setup-Chinese-Simplified-Translation/main/ChineseSimplifed.isl`。
- **PowerShell 5.1**：`build_installer.ps1` 使用 UTF-8 with BOM 编码，避免 GBK 终端乱码。

### 构建命令

```powershell
cd e:\Project\PrismWave
.\tools\build_installer.ps1 -Version 1.1.0
```

产物：`artifacts\PrismWave-Setup-1.1.0.exe`（约 42 MB）。

### setup.iss 核心逻辑

1. **依赖检测**：
   - .NET 10 Desktop Runtime：枚举 `Microsoft.dotnetDesktopRuntimeRelease64` 和 `Microsoft.dotnetDesktopRuntimePreview64` 注册表路径，缺失时弹窗提示下载链接。
   - Windows App Runtime 2.2：枚举 `C:\WindowsApps\Microsoft.WindowsAppRuntime.*`，缺失时在 DownloadPage 内展示下载按钮。
   
2. **下载 Runtime**：利用 Inno 6 的 `DownloadPage` API，在向导内完成 Windows App Runtime 2.2 静默下载（~40 MB）。

3. **静默安装 Runtime**：在 `CurStepChanged(ssInstall)` 阶段执行 `/q` 参数静默安装 .NET runtime（如果检测到缺失）。

4. **AI 组件剔除**：publish 后删除 `onnxruntime.dll`、`DirectML.dll`、`Microsoft.ML.OnnxRuntime.dll` 及所有 `Microsoft.Windows.AI.*.dll`，减少不必要的 AI 运行时库。

5. **Framework-Dependent 部署**：dotnet publish 传 `-r win-x64 --self-contained false`，**严禁**加 `-p:WindowsAppSdkSelfContained=true`，否则会因 CoreMessagingXP.dll 导致 FailFail(0xC0000602)。

### build_installer.ps1 注意事项

- **杀进程**：编译前先 `Stop-Process -Name PrismWave.WinUI -ErrorAction SilentlyContinue`，避免文件锁定。
- **输出目录清理**：`Remove-Item $payload -Recurse -Force` 确保无残留。
- **BOM 编码**：`.ps1` 和 `.iss` 文件必须用 UTF-8 with BOM，否则 PowerShell 5.1 解析中文注释会乱码报错。
- **SHA-256 计算**：生成后记录到 release note，校验格式纯大写无空格。

### 常见问题

- **ISCC.exe 未找到**：用户级 winget 安装不在 PATH，需手动指定路径 `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`。
- **中文乱码**：检查 `.iss` 和 `.ps1` 文件编码（Visual Studio Code 右下角→Save with Encoding→UTF-8 with BOM）。
- **CoreMessagingXP.dll FailFast**：一定是误用了 self-contained。改为 FDD 并在 setup.iss 内做 Runtime 检测 + 静默安装。
- **中文语言包缺失**：出现英文安装向导。确认 `tools/ChineseSimplified.isl` 存在，且 setup.iss 第 2 行 `[languages]` 段正确引用。

---

## 6. 推荐生成器 (prismwave-hits)

每日首页推荐由独立仓库 `E:\Project\prismwave-hits` 生成，GitHub Actions 每天北京时间 10:00 自动构建。

### 技术栈
- Python 脚本：`scripts/build_home.py`
- Schema: v8（包含 Top100、风格分区和 Trending Hot 板块）
- 数据源：Last.fm、Deezer、iTunes、Audius 等聚合
- 增强功能：歌手去重（Top100 最多 3 首/歌手）、新鲜度奖励、Hot Rising 独立音乐人板块

### 运行命令
```powershell
cd e:\Project\prismwave-hits
python scripts\build_home.py
```

输出文件：
- `home/home_recommendations-{YYYY-MM-DD}.json`
- `latest_home.json` (复制到 GitHub raw.githubusercontent.com)

客户端通过 `latest_home.json` 拉取，缓存策略：当天优先 → 昨天回退 → 内置兜底。

---

## 7. 最近修复（2026-08-21 v1.1.0）

### v1.1.0 变更

1. **Inno Setup 安装向导上线**：替代便携版 zip 解压方式，安装至 Program Files 并自动创建开始菜单快捷方式，控制面板卸载。
2. **.NET 依赖自动检测**：安装程序启动时自动检测 .NET 10 Desktop Runtime，缺失时弹窗引导下载安装。
3. **Windows App Runtime 自动安装**：安装程序自动检测 Windows App Runtime 2.2，缺失时在向导内静默下载安装（DownloadPage + CurStepChanged 静默安装）。
4. **AI 组件剔除**：移除未使用的 onnxruntime / DirectML / ML.OnnxRuntime / Windows AI Platform，安装包体积从 120MB 降至约 42MB。
5. **Framework-Dependent 部署**：dotnet publish 改用 `--self-contained false`，不再捆绑运行时 DLL，依赖系统级 .NET 10 + Windows App Runtime 2.2。
6. **一键打包脚本**：新增 `tools/build_installer.ps1` 和 `tools/setup.iss`，完整流程自动化。
7. **README 双语更新**：中英文 README 均更新下载链接至 v1.1.0，并在 Latest release 区块注明 .NET 依赖要求。

### v1.1.0 遇到的坑

1. **Inno Setup 用户级安装路径**：winget install JRSoftware.InnoSetup 默认装到 `%LOCALAPPDATA%\Programs\Inno Setup 6\`，而非传统 ProgramFiles。build 脚本需支持多路径枚举查找 ISCC.exe。
2. **PowerShell UTF-8 BOM**：Windows PowerShell 5.1 对无 BOM 的 UTF-8 文件解析中文注释失败。`.ps1` 和 `.iss` 必须保存为 UTF-8 with BOM，否则中文乱码导致语法错误。
3. **ChineseSimplified.isl 不存在**：Inno Setup 用户级安装不带中文语言包。从 GitHub 第三方仓库下载 `ChineseSimplified.isl` 到 `tools/` 目录解决。
4. **WinAppSDK self-contained FailFast**：尝试 `--self-contained true` 或 csproj 属性 `-p:WindowsAppSdkSelfContained=true` 时，捆绑的原生 DLL（CoreMessagingXP.dll）触发 0xC0000602 FailFast。经多轮实验（加 csproj 属性、清理 obj/bin、还原 AI 组件）确认为 WinAppSDK self-contained 本身缺陷。最终方案：**放弃 self-contained，改用 framework-dependent**，在 setup.iss 内做 Runtime 检测 + 静默安装。
5. **package.xml.inn 权限不足**：setup.iss 写入 `C:\WindowsApps` 元数据读取失败。改为枚举注册表 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore` 和 `WindowsApps` 文件夹名称匹配，避免直接访问。
6. **GH CLI Release 上传**：首次尝试 `gh release create` 时未认证 GITHUB_TOKEN，直接打开浏览器手动创建。后续改用 gh CLI 自动化。

---

## 8. v1.0.7 变更（2026-07-28）

1. **mpv 进度条不更新**：原实现依赖 mpv property-change 事件汇报播放位置，部分输出模式下事件不可靠导致进度条完全静止。修复：改为定时器轮询播放位置，增加 0.05 秒防抖，所有输出模式下进度汇报稳定。
2. **WASAPI 共享/独占模式切换失败**：切换输出模式时设备 ID 格式未按 mpv WASAPI 后端要求处理。修复：显式 `ao=wasapi` 时自动去除设备 ID 的 `{0.0.0.00000000}.` 前缀。
3. **MPV 兼容模式"音频输出初始化失败"**：统一使用 mpv 要求的 `wasapi/{0.0.0.00000000}.{guid}` 规范设备 ID 格式，所有模式下均可正常初始化。

---

## 9. v1.0.6 变更（2026-07-26）

1. **主页刷新闪退修复**：`HomeViewModel` 的 `Notify()` 方法在 `App.DispatcherQueue` 为 null 时直接在后台线程触发事件，导致 `ObservableCollection` 跨线程访问崩溃。修复：添加 `SynchronizationContext` 确保事件处理在 UI 线程执行，`RefreshHomeAsync` 添加 try-catch。
2. **外观样式精简**：删除"亚克力"选项，"Windows 11 云母"改为"浅色 (Beta)"，"经典纯色"改为"深色"。`SettingsModels.cs` 中 `Acrylic` 常量删除，`Normalize` 自动降级为 `Mica`。
3. **浅色模式 UI 统一**：修复底部播放栏封面占位图、首页趋势 Banner、导航菜单 SVG 图标、专辑详情页渐变在浅色模式下仍为深色的问题。`TrendingBanner.xaml` 移除 `TrendingAcrylicBrush` 改用 `PrismGlassBrush`；`LocalAlbumDetailPage.xaml.cs` 添加 `UpdateGradientColors()` 方法在 `ActualThemeChanged` 时动态更新渐变色。
4. **HITS 导航图标**：从 FontIcon 改为自定义 SVG 收音机图标（`Assets/Icons/hits_radio.svg`），使用 `ImageIcon` + `SvgImageSource`。SVG 使用固定颜色 `#F2F2F2`。
5. **专辑封面显示优化**：`AlbumHero` 高度从 430 增加到 520，`StableCoverImage` 新增 `ImageVerticalAlignment` 依赖属性，专辑详情页设置 `Top` 对齐让封面中间显示在上方。
6. **开发者日志实时输出**：`DeveloperLogService.OpenLogFile()` 从直接打开日志文件改为启动 PowerShell 窗口，使用 `Get-Content -Wait -Tail 50` 实时监控日志输出。
7. **Portable 版本 Bootstrap 初始化**：csproj 添加 `WindowsAppSdkBootstrapInitialize=true` 和 `WindowsAppSdkDeploymentManagerInitialize=false`，使 portable 版本通过 Bootstrap API 自动加载 Windows App SDK 运行时，无需系统级 MSIX 包注册。

---

## 10. v1.0.5 变更（2026-07-24）

1. **单曲循环无法重播**：`HandleMediaEnded` 中单曲循环用 `Seek(0) + Play()` 不创建 MPV load context，`_loaded` 恒为 false，`IsPlaying` 返回 false。修复：改为 `LoadCurrentTrack(autoplay: true)` 重新加载文件，正确重置所有 MPV 状态。
2. **进度条滑块未归位**：WinUI 3 Slider OneWay 绑定在用户交互（拖动 seek）后不更新 thumb 视觉。修复：在 `BottomPlayerBar` 和 `FullPlayPage` 的 code-behind 中显式 `Slider.Value = 0`，监听 `CurrentTrack` 变化和 `PositionSeconds == 0` 两种触发（后者覆盖单曲循环同一曲目不切换的场景）。
3. **库页面"更多选项"按钮移除**：移除库、专辑详情、艺术家详情、我最爱四个页面中歌曲行右侧的"..."按钮，改为右键上下文菜单，收藏按钮移至最右侧。收藏按钮统一无边框样式（`Background=Transparent`、`BorderThickness=0`）。
4. **在线歌曲收藏按钮灰色不可用**：`CanFavoriteCurrentTrack` 原条件排除 `IsRemote` 轨道。修复：放宽条件为 `CurrentTrack.IsRemote || !string.IsNullOrWhiteSpace(CurrentTrack.Path)`，`ToggleFavoriteAsync` 已自动处理在线歌曲加库 + 收藏逻辑。
5. **在线歌曲加入库功能**：新增 `OnlineLibraryTrackEntry` 持久化模型和 `AddOnlineTrackAsync` / `IsOnlineTrackInLibrary` 接口方法。搜索页和首页趋势歌曲列表新增"添加到库"菜单项。`RescanAsync` 合并在线歌曲条目，确保 rescan 后不丢失。
6. **在线音源解析增强**：`ResolveMiguAsync`、`ResolveKugouAsync`、`ResolveTaiheAsync` 添加 `ResolveGdStudioAsync` 兜底（与 netease/kuwo 对齐）。migu 主端点 `cms_audio_play` 返回无效 JSON 时走 gdstudio API 回退。部分歌曲仍可能解析失败（上游 API 限制）。

---

## 11. v1.0.4 变更（2026-07-22）

1. **封面替换 Bug**：`CoverService` 文件名仅基于曲目身份，同格式不同封面产生同路径，导致 `StableCoverImage` 和 `PlaybackViewModel` 路径守卫跳过更新。修复：文件名加入图片内容 SHA-256。
2. **导航动画闪烁**：`ShellPage.xaml.cs` 的 `PrepareIncoming` 用 `intent.HostWidth`（始终为 0）定位传入 Frame。修复：改用 `PageTransitionHost.ActualWidth`。
3. **布局间隙**：Shell 内容 Grid 的 `Margin="24,0,30,0"` 与页面 `PrismPagePadding` 重复叠加。修复：移除 Shell Margin，为 HomePage 补 `PrismPagePadding`。
4. **Release 便携版打不开**：`PrismWave.WinUI.csproj` Release 默认 `PublishTrimmed=True`，WinUI 3 不支持 trimming（裁掉 WinRT/XAML/Win2D 反射元数据），解压式 exe 启动即崩、无窗口。修复：Release `PublishTrimmed` 改 False；`win-x64.pubxml` 首次入库（原被 `.gitignore` 的 `*.pubxml` 忽略）并补 `WindowsPackageType=None` 确保无包身份主题资源完整。
5. **导航栏"专辑/HITS"图标消失**：两个 svg 以 `<Content>` 引入但缺 `CopyToOutputDirectory`，unpackaged 发布不复制，`ms-appx` 运行时解析不到。修复：`csproj` 两个 svg 补 `CopyToOutputDirectory="PreserveNewest"`。
6. **音量条非无级调节**：`Slider` 默认 `StepFrequency=1`，音量范围 0-1 只能停两端。修复：`BottomPlayerBar.xaml` 与 `FullPlayPage.xaml` 音量条加 `StepFrequency="0.01"`；BottomPlayerBar 音量绑定 `TwoWay`→`OneWay`（`Volume` 为 private set，实际变更走 `ValueChanged`）。
7. **FullPlay 最大化布局**：左栏封面所在 `*` 行吸收全部额外高度，封面居中而歌名/控件/进度条被压到底部、中间留大空隙。修复：`FullPlayPage.xaml` 改为顶部 + 底部两个 `*` 弹性 spacer 包裹，内容行全部 `Auto`，整块内容垂直居中、控件紧贴封面下方。
8. **FullPlay 切行颤动**：此前已修复。

---

## 12. v1.0.3 变更（2026-07-21）

1. **启动动画**：新增 `SplashPage` 启动页，"Prism" 从左侧滑入中心（0.4s），"Wave" 从上方落下（0.5s），停留 0.3s 后整体向右飞出渐隐（0.55s），过渡到首页。`App.OnLaunched` 中 `async void` 流程控制，添加 `_isWindowClosed` 标志防止关闭后访问已释放 UI。
2. **搜索历史右键删除**：`SearchPage.xaml` 历史记录项添加 `Grid.ContextFlyout` + `MenuFlyout`，`Tag` 绑定移到 Grid 上（MenuFlyoutItem 不在可视化树中无法绑定），code-behind 通过 `VisualTreeHelper` 遍历查找父级 Grid 的 Tag。
3. **版本检测功能**：`IUpdateService` 接口正式实现，`UpdateService` 调用 GitHub Release API（`https://api.github.com/repos/shanbei2033/PrismWave/releases/latest`），语义化版本比较，筛选 `win-x64` + `.zip` 下载直链。
4. **设置页版本卡片**：基本选项底部新增版本卡片，左侧版本号（28px Bold）+ 最新版本小字，右侧检测按钮（MinWidth=140）+ 自动检测开关（水平排列）。
5. **更新通知弹窗**：`MainWindow.xaml` 右上角新增 `UpdateNotification` Border，从右侧 320px 处滑入 + 淡入（0.3s），含关闭按钮点击后缩回右侧 + 渐隐（0.25s）。
6. **自动检测**：`App.OnLaunched` 中首页加载后检查 `AutoCheckUpdate` 设置，后台静默检测，有更新时 `DispatcherQueue.TryEnqueue` 在 UI 线程弹出通知。

---

## 13. 清理 Flutter 代码库（2026-07-24）

清理了 Flutter 代码库，释放 ~2.4 GB 空间：
- 删除 `app/` (Flutter 源代码 + 资源 + 构建产物)
- 删除 `tools/flutter/` (Flutter SDK, ~2.1 GB)
- 删除 `installer/PrismWaveSetup.iss` (Flutter Inno Setup 脚本)
- 删除 `native/libmpv/` (Flutter WASAPI 修补版 DLL)
- 删除 `dist/` 中所有 R401-R503 版本发布说明
- 迁移 `app/assets/home/latest_home.json` → `src/PrismWave.WinUI/Assets/HomeFallback/latest_home.json`

结果：
- 删除代码：**45,316 行**
- 新增/迁移代码：9,345 行
- 净减少：**35,971 行**
- WinUI 测试全部通过：458/458

---

## 14. Flutter 基线参考

R503（`pubspec.yaml` 503.0.0+505）是行为对照基线。技术栈：Flutter 3.41.4 / Riverpod / just_audio_media_kit → libmpv。构建：`tools\flutter\bin\flutter.bat build windows --release`，产物 `app/build/windows/x64/runner/Release/prismwave_demo.exe`。WASAPI Exclusive 破音已通过二进制修补 `native/libmpv/libmpv-2.dll`（端点缓冲 3ms→50ms）修复，切换 media_kit 版本需重新核对。
