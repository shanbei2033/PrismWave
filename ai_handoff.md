# PrismWave AI 接手文档

更新时间：2026-10-10

**语言要求：接手 AI 必须全程使用中文与用户沟通。**

本文档帮助其他 AI 快速接手 `c:\Project\PrismWave` 开发。项目主线为 **Avalonia 11 + Skia 跨平台音乐播放器**（由 WinUI 3 版本重写而来），服务层复用 `PrismWave.Core`。

---

## 0. 当前状态

| 项目 | 状态 |
|------|------|
| 工作目录 | `c:\Project\PrismWave` |
| 主分支 | `main`（Avalonia 版本，唯一活跃开发分支） |
| 共享服务层 | `src/PrismWave.Core/`（Infrastructure / Models / Services / ViewModels，UI 无关） |
| Avalonia UI | `src/PrismWave.Avalonia/`（Avalonia 11.3.12 + FluentAvalonia 2.5.1 + CommunityToolkit.Mvvm） |
| WinUI 对照版 | `src/PrismWave.WinUI/`（旧版实现，保留作行为对照，不再新增功能） |
| 测试工程 | `tests/PrismWave.WinUI.Tests/`（仅引用 Core，458 项测试基线） |
| 播放后端 | `native/libmpv-winui/libmpv-2.dll`（完整解码版，支持 MPV 自动/WASAPI 共享/WASAPI 独占） |
| DSD 播放 | `native/windows_dsd/bass.dll` + `bassdsd.dll` + `bassasio.dll` |
| 推荐生成器 | 独立仓库 `shanbei2033/prismwave-hits`（schema 8 每日首页） |
| 重写方案文档 | `docs/superpowers/specs/2026-10-09-avalonia-crossplatform-ui-rewrite.md` |
| QA 记录 | `docs/qa/2026-10-10-avalonia-ui-test-findings.md` |

Avalonia 重写 Phase 0-7 已全部完成（最终代码提交 `7d62c82` 及其后的实测修复），具备完整本地曲库、在线搜索/播放、歌词舞台、HITS 电台、收藏管理和设置功能。458 项测试全部通过。

### 环境准备（本机特殊配置，必读）

```powershell
# 本机无全局 dotnet 环境变量，启动/测试前必须设置，否则运行时找不到
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"

# git 为便携版（本机原先无 git）
$git = "C:\Users\shanbei\PortableGit\cmd\git.exe"

# github.com 直连被阻断（api.github.com、codeload 可直连）
# git push/pull 需加代理参数（本机 Clash 系代理，勿写入全局 git config）：
& $git -c http.proxy=http://127.0.0.1:7897 push origin main
```

### 构建、测试和启动

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"

# 测试（基线 458 项）
& $dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --nologo -v q

# 构建 + 启动 Avalonia 版
& $dotnet build src\PrismWave.Avalonia\PrismWave.Avalonia.csproj
Start-Process "src\PrismWave.Avalonia\bin\Debug\net10.0\PrismWave.Avalonia.exe"
```

**强制规则：每次代码改动后都必须打开 demo 验证效果。若 demo 正在运行，先 `Get-Process PrismWave.Avalonia | Stop-Process -Force` 再重新构建启动**（旧 exe 被锁定会导致构建失败或看到旧界面）。

**运行时日志**：`%LOCALAPPDATA%\PrismWave\WinUI\startup.log`

---

## 1. 架构

```
src/PrismWave.Core/            UI 无关服务层（WinUI 与 Avalonia 共用）
├── Infrastructure/            Audio(mpv P/Invoke)/Lyrics(LyricsSceneController)/Persistence/...
├── Models/ / Services/ / ViewModels/   PlaybackViewModel 等共享 VM

src/PrismWave.Avalonia/        Avalonia UI（主线）
├── Views/                     Shell/Home/Search/Library/Player(Hits/Settings 同层)
├── Controls/                  Lyrics(LyricsStage 自绘)/Media(StableCoverImage)/...
├── Infrastructure/            AppServices 组合根

src/PrismWave.WinUI/           旧版 WinUI 3（对照基线，不新增功能）
```

- MVVM：ViewModel 在 Core（CommunityToolkit.Mvvm），Avalonia View 仅 XAML + 少量 code-behind。
- `AppServices` 是手工组合根——不要把 mpv/文件系统/网络请求塞进页面 code-behind。
- 歌词舞台 `LyricsStage`（Avalonia 自绘，DispatcherTimer 16ms）复用 Core 的 `LyricsSceneController` 场景状态机（滚动动画 0.32s EaseInOutCubic、卡拉OK进度、手动浏览）。
- 封面统一走 `StableCoverImage`（自带 CornerRadius 圆角裁剪、DecodeMaxEdge=1600 降采样、位图生命周期管理）。
- 动效原则：**从简，仅保留功能性过渡**（页面转场、覆盖层、悬停），不做装饰性动画。

---

## 2. 关键约束（硬性禁令）

- **libmpv 不可混用**：项目仅可用 `native/libmpv-winui/` 下的版本，不可引入其他 libmpv 实现。
- **Deezer、iTunes 的 30 秒预览绝不能作为正式播放源**。
- **bilibili、YouTube 仅可作为 HITS 电台的兜底播放源**，不得加回普通搜索。
- **schema 版本不得降级**（当前 schema 8）。
- `bin/`、`obj/`、`artifacts/` 不得提交。
- 不要提交 API key 等隐私文件。
- **不要**运行 `git clean`/`git reset --hard`/大范围 checkout。
- 每次修改 UI 后必须在 **1280/1440/1600/1920 四种宽度 × 侧栏展开/折叠**状态下验证显示效果。

---

## 3. 重要经验教训（避免踩坑）

| 坑 | 结论 |
|----|------|
| Avalonia Matrix 行向量约定 | `p' = p × M1 × M2`，M1 先作用。"关于锚点 c 缩放"必须写 `T(-c) × S × T(+c)`，顺序写反会把内容拉向窗口原点（曾导致歌词全部挤向顶部） |
| PrintWindow 截图 DPI 陷阱 | DPI-unaware PowerShell 的 `GetWindowRect` 返回虚拟化坐标（如 1368x872），但 `PrintWindow(PW_RENDERFULLCONTENT)` 输出 PMv2 进程的物理表面（2736x1744）；**位图必须按 w×2, h×2 分配**，否则只截到左上 1/4 |
| RenderTargetBitmap 自截图 | 对含自定义 DrawingContext 绘制的页面输出全黑，不可作为 UI 验证手段；用正确尺寸的 PrintWindow |
| Skia 网络流 | 无法直接对网络流 seek，直接解码抛异常；先缓冲到 MemoryStream 再解码 |
| FluentAvalonia 2.5.1 | 缺 ContextMenu ControlTheme，右键菜单打开但尺寸为零；改用 FAMenuFlyout + ContextFlyout |
| 命名空间遮蔽 | `PrismWave.Avalonia` 项目命名空间使 `Avalonia.Media.XXX` 解析失败，需 `global::` 前缀 |
| 后台线程读 StyledProperty | Avalonia 中会抛 "Call from invalid thread"；UI 线程先捕获值再传参 |
| Avalonia Border.CornerRadius | **不裁剪子内容**（与 WinUI 不同）；图片圆角需在控件内做 Clip（StableCoverImage 已内置） |
| WinAppSDK（WinUI 对照版） | 未打包模式构建输出 `PrismWave.WinUI.pri`，MRT Core 只认 `resources.pri`，构建流程需自动复制 |
| WinUI 窗口宽度钳制 | WM_GETMINMAXINFO 钳制 MaxTrack，程序化 SetWindowPos 也被钳；四宽度验证需临时方案（见 QA 文档），验证完必须还原 |

---

## 4. 在线首页与 prismwave-hits

每日推荐由独立仓库 `https://github.com/shanbei2033/prismwave-hits` 的 GitHub Actions 每天北京时间 10:00 生成 schema 8 JSON。客户端不生成推荐，只拉取 `home/latest_home.json`。

缓存策略：当天缓存优先 → 无当天缓存拉远程 → 远程失败回退昨日缓存 → 无昨日缓存用内置 `Assets/HomeFallback/latest_home.json`。北京时间 00:00-10:00 期间 remote 可能仍是昨日 edition，属正常窗口期。

普通在线 provider：Audius、NetEase、Kuwo、Migu、QQ、Kugou、Taihe（多源竞速解析，netease/kuwo/migu/kugou/taihe 有 gdstudio 兜底）。
HITS 在此基础上增加 bilibili、bilivideo、YouTube（仅兜底，强制 WASAPI Shared）。

---

## 5. 已知待优化项（非阻塞）

| 问题 | 说明 |
|------|------|
| HITS 封面加载慢 | 大图未走降采样路径 |
| 非活动歌词行无模糊 | 当前仅低透明度近似，未做高斯模糊 |
| 卡拉OK整行 clip | 进度揭示是整行裁剪，非逐字 |
| `IWindowService`/`IDialogService`/`IUpdateService` | 仍为占位契约（WinUI 时代遗留，按需在 Core 补齐） |

## 6. 验证与测试工作流

1. 改代码 → 构建 → 杀旧进程重启 demo。
2. 用 `trae-remote-official:computer-use` 插件操作 UI 复现用户路径（用户偏好的测试方式）。
3. UI 问题先定位、记录到 `docs/qa/2026-10-10-avalonia-ui-test-findings.md`，再按文档逐一修复，修完更新文档。
4. 跑 458 项测试确认无回归（注意：`OnlineProviderArchitectureTests` 中有一个 80ms 优雅期时序测试，机器高负载时会偶发失败，单独重跑或停掉应用进程后重跑即可）。
5. UI 布局类修改按四宽度 × 侧栏两态矩阵验证（截图方案见 QA 文档 P4-2：物理尺寸 PrintWindow）。
6. 提交信息用中文一行说明"为什么"；提交身份用 `shanbei2033 <shanbei2033@users.noreply.github.com>`（可用 `-c user.name=... -c user.email=...` 一次性传入）。
