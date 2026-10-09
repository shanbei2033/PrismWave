# PrismWave 跨平台 UI 重写方案（Avalonia）

> 状态：已确认执行（2026-10-10：框架定 Avalonia 11 + Skia；动效从简；开发分支 Next）
> 日期：2026-10-09
> 前置文档：`ai_handoff.md`（当前 WinUI 主线状态、约束与红线）
> 适用范围：仅重写 UI 层；服务层（在线 provider、本地库扫描、播放调度、缓存、歌词解析）**全部保留复用**

---

## 0. 为什么重写，重写什么

### 0.1 重写动机（用户原话归纳）

1. **可移植性受限**：WinUI 3 仅能运行在 Windows，无法覆盖 macOS / Linux。
2. **显示效果一般**：WinUI 3 控件体系（XAML + 系统主题）在自定义视觉、动效流畅度、玻璃材质等方面天花板低，Win2D 需要额外互操作层。

### 0.2 重写边界（重要）

| 保留（不动） | 重写（新 UI 层） |
|---|---|
| 全部 `Services/Contracts`（17 个接口） | 全部 `Views/`（22 个 XAML 页面） |
| 全部 `Services/Implementations` | 全部 `Controls/`（11 个自定义控件） |
| `Infrastructure/Online`（7 个 provider + 竞速解析 + 缓存） | `Themes/`（令牌迁移 + 视觉升级） |
| `Infrastructure/Library`（扫描 + TagLibSharp） | 导航动画宿主（Composition → Avalonia Compositor） |
| `Infrastructure/Persistence`（设置持久化） | 歌词舞台渲染（Win2D → Skia 原生绘制） |
| `Infrastructure/Audio` 的 mpv P/Invoke 层 | mpv 画面呈现（句柄嵌入 → render-API/GL 集成） |
| `Models/` 全部 | `Shell` 结构（NavigationView → Avalonia SplitView/自制侧栏） |

**红线继承**：`ai_handoff.md` 第 2 节所有约束继续生效（两套 libmpv 不混用、schema 8 不降级、Deezer/iTunes 预览不作为播放源、HITS 强制 WASAPI Shared 等）。本方案不触碰任何红线。

---

## 1. 框架选型

### 1.1 候选对比

| 方案 | 语言 | 跨平台 | C# 服务层复用 | 视觉/动效上限 | mpv 集成 | 结论 |
|---|---|---|---|---|---|---|
| **Avalonia 11 + Skia** | C# | Win/macOS/Linux/iOS(实验) | **100%** | 高（自绘 + Compositor 动画） | render-API / NativeControlHost | **推荐** |
| Flutter（回归） | Dart | 全平台 | 0%（需 Dart 重写全部业务） | 高 | 差：需二进制修补 libmpv（R503 时代已踩坑） | 排除 |
| Uno Platform | C# | 全平台 | ~90%（XAML 方言） | 中 | 中 | 备选 |
| .NET MAUI | C# | 弱桌面 | ~80% | 低（Win11 上观感一般） | 差 | 排除 |
| Tauri / Electron | Rust/TS | 全平台 | 0%（前后端全重写） | 极高（Web） | 好（mpv.js 类方案） | 排除：成本不可接受 |
| Qt/QML | C++ | 全平台 | 0% | 高 | 好 | 排除：语言栈不匹配 |

### 1.2 选择 Avalonia 的决定性理由

1. **服务层零重写**：PrismWave 最有价值的资产是 7 个在线 provider、竞速解析、schema 8 缓存链、本地库扫描、HITS 会话——全部是纯 C#，直接复用。
2. **团队语言栈不变**：C# / MVVM / CommunityToolkit.Mvvm 原生支持（Avalonia 官方模板即含 MVVM 工具包）。
3. **Skia 渲染**：所有平台像素级一致；歌词舞台直接用 `DrawingContext` 自绘，**不再需要 Win2D 互操作**。
4. **FluentAvalonia 社区库**：提供 WinUI 风格控件 + Win11 Mica/Acrylic 支持，Windows 端观感不降级。
5. **本项目已有教训**：Flutter 时代为了 WASAPI 独奏修补过 libmpv 二进制（`ai_handoff.md` 第 7 节），换框架务必选能直接 P/Invoke libmpv 的方案。

### 1.3 版本基线

- Avalonia：**11.3.x**（当前稳定）
- FluentAvalonia.UI.Controls：**2.x**
- CommunityToolkit.Mvvm：**8.4.2**（沿用）
- SkiaSharp：仅 Avalonia 自带 Skia（不额外引 SkiaSharp，直接用 `DrawingContext`）
- .NET：10（与服务层一致）

---

## 2. 目标平台能力矩阵

| 能力 | Windows 10/11 | macOS 12+ | Linux (X11/Wayland) |
|---|---|---|---|
| mpv 解码播放（自动输出） | ✅ WASAPI Shared/Exclusive | ✅ CoreAudio | ✅ PipeWire/PulseAudio/ALSA |
| WASAPI 独占 | ✅ | —（用 CoreAudio 独占，后续阶段） | —（用 ALSA direct，后续阶段） |
| DSD（BASS/bassdsd） | ✅（Windows 专属 DLL） | ⛔ 首版不支持；备选 mpv 原生 DSD-over-PCM | ⛔ 同左 |
| Mica 材质 | ✅ FluentAvalonia (Win11) | ✅ 原生 vibrancy | ⛔ 用纯色/透明模拟 |
| Acrylic | ✅ | ✅ | ⚠️ 需合成器支持，失败回退纯色 |
| 全局快捷键 / SMTC（系统媒体控制） | ✅ SMTC | ✅ MPRemoteCommand | ⚠️ MPRIS |
| 首版交付优先级 | P0 | P1 | P1 |

> 首个里程碑只要求 Windows 打平现有 WinUI 版；macOS/Linux 在 Phase 7 验证（服务层本身无 Windows 锁定，唯一风险点是 `Infrastructure/Audio` 的 WASAPI 代码需条件编译）。

---

## 3. 解决方案结构

```
PrismWave/
├── src/
│   ├── PrismWave.Core/            # ★新：从 WinUI 工程抽出的无 UI 依赖层
│   │   ├── Models/                  # 原 Models/ 原样迁入
│   │   ├── Services/Contracts/      # 17 个接口原样迁入
│   │   ├── Services/Implementations/# 原样迁入（去掉 WinUI 类型引用）
│   │   ├── Infrastructure/
│   │   │   ├── Audio/               # mpv P/Invoke、输出模式枚举、回退逻辑
│   │   │   ├── Library/ Online/ Persistence/ Lyrics/   # 原样迁入
│   │   │   └── Abstractions/        # ★新：IUiDispatcher、IAudioOutputCapability、IPlatformAppearance
│   │   └── PrismWave.Core.csproj    # net10.0 类库，无任何 UI 包引用
│   ├── PrismWave.Avalonia/        # ★新：跨平台 UI 工程
│   │   ├── App.axaml(.cs)           # 组合根（原 AppServices.Create 的迁移）
│   │   ├── Program.cs               # Win32/Linux/macOS 启动
│   │   ├── Infrastructure/
│   │   │   ├── Mpv/                 # MpvRenderControl（GL 集成）
│   │   │   ├── Navigation/          # PageTransitionHost + CoverNavigationCoordinator 适配
│   │   │   └── Platform/            # Windows/Mac/Linux 差异实现
│   │   ├── Themes/
│   │   │   ├── PrismTokens.axaml    # 令牌 1:1 迁移
│   │   │   ├── PrismControls.axaml  # 控件样式
│   │   │   └── Motion.axaml         # ★新：统一动效曲线/时长令牌
│   │   ├── Views/                   # 与 WinUI Views 一一对应
│   │   ├── Controls/                # 与 WinUI Controls 一一对应
│   │   └── ViewModels/              # 直接从 WinUI 工程移入（纯 MVVM，本来就不依赖 View 类型）
│   ├── PrismWave.WinUI/            # 保留不动，直至 Avalonia 版验收通过后归档
│   └── PrismWave.WinUI.Tests/      # 保留：458 项测试改为引用 Core 工程
└── tests/PrismWave.Avalonia.Tests/ # ★新：UI 层 Headless 测试（Avalonia Headless 平台）
```

### 3.1 服务层抽离的机械步骤（约 0.5 天）

1. 新建 `PrismWave.Core` 类库。
2. 在 WinUI 工程中逐个**剪切**目录（Models、Services、Infrastructure/{Online,Library,Persistence,Lyrics}、ViewModels）→ 粘贴到 Core，保持命名空间不变（`PrismWave_WinUI.*` 暂不改名，减少 diff）。
3. 编译报错的 WinUI 类型引用按第 3.2 节替换。
4. WinUI 工程改为 `ProjectReference` Core（在 Avalonia 版验收前，WinUI 版必须保持可编译可运行——**每个 Phase 结束都跑一遍 WinUI 测试与启动**）。
5. 测试工程改引用 Core，458 项测试必须全绿。

### 3.2 已知 WinUI 耦合点与替换表

| 现状（在 Core 层禁止出现） | 替换 |
|---|---|
| `Microsoft.UI.Dispatching.DispatcherQueue` | 已有 `IUiDispatcher` 接口，Avalonia 实现用 `Dispatcher.UIThread` |
| `Windows.Storage.ApplicationData`（本地设置） | `ISettingsStore` 抽象；默认实现为 JSON 文件（各平台用户目录），行为与现持久化对齐 |
| `Windows.Storage.Pickers`（文件夹选择） | 已有 `IMusicFolderPicker`，Avalonia 用 `StorageProvider` 实现 |
| `Microsoft.Graphics.Win2D` | 全部留在 UI 层（歌词舞台重写为 Skia 自绘） |
| `ISystemMediaTransportControls`（SMTC） | `ISystemMediaControls` 抽象；Windows 实现走 WinRT 互操作，mac/linux 后续补 |
| WASAPI 独占/DSD BASS 路径 | `IAudioOutputCapability` 按平台上报可用档位，设置页据此禁用不可用项 |

---

## 4. 视觉规范（总原则：与旧版观感对齐）

> 2026-10-10 用户补充硬性要求：新 UI 与旧版的布局、交互、观感**基本一致**（结构 parity），不重新设计。Skia 渲染带来的清晰度/文字渲染提升自然保留；以下升级要点仅作质量修正参考，**不得改变布局结构与交互行为**。

### 4.1 设计令牌迁移与扩充

现有 `PrismTokens.xaml` 的令牌**全部平移**到 `PrismTokens.axaml`（Avalonia 资源语法基本一致），并扩充以下三组：

```xml
<!-- 动效令牌（新增 Motion.axaml；原则：动效从简，仅保留功能性过渡，不做装饰性动画） -->
<x:Double x:Key="MotionFast">120</x:Double>        <!-- 悬停、按压 -->
<x:Double x:Key="MotionNormal">280</x:Double>      <!-- 页面转场（对齐现 CoverNavigation） -->
<x:Double x:Key="MotionSlow">450</x:Double>        <!-- FullPlay 进出、覆盖层 -->
<CubicBezierEasing x:Key="MotionEmphasized">0.2,0,0,1</CubicBezierEasing>

<!-- 海拔与阴影（新增） -->
<BoxShadows x:Key="ShadowCard">0 2 8 0 #22000000</BoxShadows>
<BoxShadows x:Key="ShadowOverlay">0 8 32 0 #33000000</BoxShadows>

<!-- 字号阶梯（新增，替代散落的 FontSize 字面量） -->
<x:Double x:Key="TypeDisplay">32</x:Double>
<x:Double x:Key="TypeTitle">20</x:Double>
<x:Double x:Key="TypeBody">14</x:Double>
<x:Double x:Key="TypeCaption">12</x:Double>
```

### 4.2 各页面视觉升级要点（开发时对照）

| 区域 | 现状问题 | 升级做法 |
|---|---|---|
| 首页 Hero（TrendingBanner） | 平铺卡片，层级弱 | 大图 + 底部渐变遮罩 + 标题强调排版；封面加载淡入（180ms） |
| 歌曲行/列表 | 行高紧、悬停反馈生硬 | 44px 行高、悬停 4% 白色叠加圆角 8；播放中行显示静态均衡器图标 |
| 卡片（SongCard/EditorialFeature） | 阴影与圆角不统一 | 全部走 `ShadowCard` + `CardRadius` 令牌；封面 8px 圆角 |
| 底部播放栏 | 与页面同质 | 独立海拔层 + 上边框 1px 分隔 + Acrylic 底（可关） |
| FullPlay | 背景模糊效果一般 | 封面高斯模糊 + 主色提取渐变双层背景（Skia `BlurEffect`），歌词舞台保留单画布架构 |
| 滚动条/滚动 | 系统默认 | 细滚动条样式，悬停展开 |
| 空态/加载 | 缺失或简陋 | 统一 Skeleton 骨架屏控件（所有列表卡片复用） |

### 4.3 材质策略

- Windows 11：FluentAvalonia 的 `Mica`/`AcrylicBlur`（跟随系统主题与现有设置项：纯色/云母/亚克力）。
- macOS：`ExperimentalAcrylicBorder` + 原生 vibrancy（Phase 7）。
- Linux：回退纯色主题，不报错。

---

## 5. 关键技术方案（三大难点）

### 5.1 mpv 画面集成（播放内核上屏）

**结论：使用 libmpv render API（`mpv_render_context`）+ Avalonia `OpenGlControlBase`。**

不采用 `NativeControlHost`（窗口句柄嵌入）的原因： airspace 问题——歌词舞台、队列覆盖层、FullPlay 模糊背景都要叠在视频/可视化之上，句柄嵌入无法实现；且句柄嵌入在 macOS/Linux 上行为不一致。

实现要点（新文件 `Infrastructure/Mpv/MpvRenderHost.cs`）：

```csharp
// 伪代码骨架
public sealed class MpvRenderHost : Control {   // 替代 OpenGlControlBase 亦可
    // 1. 复用 Core 层现有 MpvContext（播放命令/事件不动）
    // 2. mpv_render_param_create → mpv_render_context_create(MPV_RENDER_API_TYPE_OPENGL)
    //    用 Avalonia IGlContext 的 GLProcAddress 获取 get_proc_address
    // 3. Render(DrawingContext) 内：mpv_render_context_render(fbo, w, h)
    //    参数 flip-y = true（GL 与 Avalonia 坐标系差异）
    // 4. mpv_render_context_set_update_callback → Dispatcher.UIThread.Post(InvalidateVisual)
    // 5. 尺寸变化走 mpv_render_context_report_swap
}
```

- **视频可视化模式**：现 WinUI 版没有视频需求（音频播放器），首版可**仅用 mpv 做音频 + 自绘频谱**（`af` loudnorm/`fftbars`? 不引入）——因此 render API 首版甚至只用于将来扩展；音频播放路径与 UI 无画面耦合，集成复杂度大幅下降。
- 回退方案：若 GL 上下文互操作在某平台异常，该平台降级 `NativeControlHost` 仅呈现纯黑画面（音频不受影响）。
- **验收标准**：播放期间 UI 帧率稳定 60fps；切歌无渲染卡顿；窗口缩放无闪烁。

### 5.2 歌词舞台重写（原 Win2D LyricsStageControl）

原实现：单 `CanvasControl`，订阅 `CompositionTarget_Rendering` 采样播放进度，逐行绘制歌词，支持逐字（QRC/YRC）时间轴、手动浏览、自动回弹。

Avalonia 对应实现（`Controls/Lyrics/LyricsStage.axaml.cs`，纯 code-only 控件）：

| WinUI 机制 | Avalonia 机制 |
|---|---|
| `CanvasControl.Draw` 事件 | 重写 `Render(DrawingContext)`，`InvalidateVisual()` 触发下一帧 |
| `CompositionTarget.Rendering` 帧循环 | `TopLevel.RequestAnimationFrame` 循环（11.1+），不可用则 `DispatcherTimer` 16ms 兜底 |
| `CanvasDrawingSession` 文本 | `FormattedText` + `drawingContext.DrawText`；逐字渐变用 `LinearGradientBrush` 按字宽插值 |
| Win2D 高斯模糊背景 | `VisualBrush` 拷贝封面 + `BlurEffect`（Avalonia 11 支持 `BlurEffect` 于控件层） |
| 惯性滚动/手动浏览 | `ScrollViewer` 嵌套模式替换为自绘 offset（保持"单画布"架构，现有滚动状态机逻辑直接移植） |

- 现有歌词解析、时间轴、偏移设置、搜索缓存全部在 Core 层，**零改动**。
- 验收标准：逐字染色与音频偏移误差 ≤ 50ms；拖动进度后歌词即时跟随；手动浏览 3 秒后自动回弹（对齐现有交互）。

### 5.3 覆盖式导航转场（280ms 从右滑入）

现机制：`CoverNavigationCoordinator` 状态机驱动传入 Frame 从右滑入、覆盖旧页。Avalonia 版：

- `PageTransitionHost`：Grid 内两层 `ContentPresenter`（outgoing/incoming），转场期间双层并存，结束后卸载 outgoing（对齐"覆盖式"而非"替换式"）。
- 动画：`Transitions` + `TranslateTransform.X`（0→宽，`MotionNormal` 280ms，`MotionEmphasized` 曲线）；透明度 0→1。
- `CoverNavigationCoordinator` 状态机代码**直接复用**（把 WinUI Frame 调用替换为 ContentPresenter 切换，其余状态判断不动）。
- 验收标准：快速连续点击导航不闪烁不叠层（对齐 `ai_handoff` 验证项）；返回时状态保留（滚动位置、搜索词）。

---

## 6. 页面/控件逐一映射表（开发任务分解基础）

### 6.1 Views（22 页）

| # | WinUI 源 | Avalonia 目标 | 复杂度 | 备注 |
|---|---|---|---|---|
| 1 | Shell/ShellPage | Shell/ShellWindow | 高 | NavigationView→自制侧栏+内容宿主+底部播放栏+队列覆盖层 |
| 2 | Home/HomePage | Home/HomePage | 高 | 4 个 Home 控件组合，虚拟化列表 |
| 3 | Home/TopPlaylistPage | Home/TopPlaylistPage | 中 | |
| 4 | Home/AlbumDetailPage | Home/AlbumDetailPage | 中 | 在线专辑 |
| 5 | Library/LibraryPage | Library/LibraryPage | 高 | 表格+右键菜单+拖拽排序 |
| 6 | Library/AlbumsPage | Library/AlbumsPage | 中 | 网格封面 |
| 7 | Library/LocalAlbumDetailPage | 同名 | 中 | |
| 8 | Library/ArtistsPage | 同名 | 中 | |
| 9 | Library/ArtistDetailPage | 同名 | 中 | |
| 10 | Library/FavoritesPage | 同名 | 中 | |
| 11 | Search/SearchPage | Search/SearchPage | 高 | 防抖输入+多类型结果 |
| 12 | Player/FullPlayPage | Player/FullPlayPage | 极高 | 歌词舞台+模糊背景+进出场动画 |
| 13 | Hits/HitsStatusPage | Hits/HitsStatusPage | 中 | 电台模式 |
| 14 | Settings/SettingsPage | Settings/SettingsPage | 高 | 分组设置+输出模式+材质切换 |
| 15-21 | Dialogs×7 | Window 轻对话框（复用 DialogService） | 低-中 | TrackDetails/TrackDelete/RiskNotice/OnlineLogin(QR)/LyricsSearch/LibraryFolders/CoverSearch |
| 22 | （无） | AboutWindow | 低 | 新增：版本与许可 |

### 6.2 Controls（11 个）

| WinUI 源 | Avalonia 目标 | 实现方式 |
|---|---|---|
| LyricsStageControl | LyricsStage | 纯自绘（5.2 节） |
| BottomPlayerBar | PlayerBar | UserControl；进度条自绘支持拖拽 seek 与缓冲指示 |
| QueuePane | QueuePane | UserControl + ListBox 拖拽排序（行为对齐） |
| Sidebar | Sidebar | 自制（SplitView 变体），展开/折叠 64px↔256px |
| StableCoverImage | CoverImage | Control：磁盘缓存路径→内存位图 LRU→失败多源回退（逻辑在 CoverService，控件只管显示态） |
| TrendingBanner | TrendingBanner | UserControl，Hero 升级（4.2） |
| TrendingSongList | TrendingSongList | ItemsControl 虚拟化 |
| SongCard | SongCard | UserControl |
| GenreExplorer | GenreExplorer | UserControl |
| EditorialFeature | EditorialFeature | UserControl |
| MetricPill | MetricPill | TemplatedControl |

### 6.3 服务接口处置（18 个契约）

- 17 个既有接口 → 原样进 Core，Avalonia `App.axaml.cs` 组合根逐个注册（对应现 `AppServices.cs` 内容平移）。
- `PlaceholderContracts.cs`（IWindowService/IDialogService/IUpdateService）→ **在 Avalonia 版正式实现**：
  - `IDialogService`：封装 7 个对话框的打开/关闭与结果回传；
  - `IWindowService`：主窗尺寸记忆（对应现 WindowLaunchSize）、全屏 FullPlay 切换、关闭最小化行为；
  - `IUpdateService`：首版仅"检查更新"跳转 Release 页。

---

## 7. 分阶段开发计划

> 原则：每个 Phase 结束必须满足验收标准并可演示；WinUI 版全程保持可运行，作为行为对照基线。预计每 Phase 2-5 天（单人），总 4-6 周。

### Phase 0：地基（0.5-1 天）

- [ ] 抽离 `PrismWave.Core`（3.1 机械步骤），458 项测试转绿
- [ ] 新建 `PrismWave.Avalonia` 工程跑通 Hello Window（Windows）
- [ ] git 分支：`avalonia-rewrite`（不混入 WinUI 提交，遵守 handoff 规约）

### Phase 1：Shell 与设计系统（2-3 天）

- [ ] PrismTokens/Motion 令牌迁移；明暗主题
- [ ] ShellWindow：侧栏（展开/折叠）、内容宿主、底部预留区
- [ ] PageTransitionHost + CoverNavigationCoordinator 接入
- [ ] 窗口尺寸记忆、关闭行为
- ✅ 验收：四个占位页互跳动画正确；1280/1440/1600/1920 + 侧栏两态无布局破绽；快速连点无叠层

### Phase 2：播放基座（2-3 天）

- [ ] Core 层 `IPlaybackService` 接入；PlaybackViewModel 平移
- [ ] PlayerBar（播放/暂停/上一首/下一首/进度/音量/模式）
- [ ] QueuePane 覆盖层（滑入动画对齐现 Composition 版）
- [ ] 音频输出三档 + 自动回退（复用 Core 现有逻辑）
- ✅ 验收：本地文件播放/seek/切歌/循环模式；输出模式切换；SMTC 显示正确

### Phase 3：本地库（3-4 天）

- [ ] LibraryPage 表格、Albums/Artists/Favorites 及详情页
- [ ] 扫描进度联动（LibraryService revision 机制）
- [ ] 右键上下文菜单、拖拽排序、收藏
- [ ] LibraryFoldersDialog、TrackDetailsDialog、TrackDeleteDialog
- ✅ 验收：与 WinUI 版并排对照，扫描/排序/删除/收藏行为一致；中文长路径正常

### Phase 4：在线首页与搜索（3-4 天）

- [ ] OnlineHomeService schema 8 缓存/回退链验证（当天→昨日→内置兜底）
- [ ] HomePage 全套（Banner/歌曲列表/新专辑/风格分区/编辑推荐）
- [ ] SearchPage（防抖、多类型结果、添加到库/收藏）
- [ ] TopPlaylistPage、AlbumDetailPage
- ✅ 验收：7 个 provider 搜索可用；断网回退内置 JSON；在线歌曲加库后 rescan 不丢（对齐 handoff 已知修复）

### Phase 5：FullPlay 歌词舞台（3-5 天，最高风险）

- [ ] LyricsStage 自绘控件（5.2 节全部机制）
- [ ] FullPlayPage：模糊背景、进出场动画、歌词工具条（搜索/偏移）
- [ ] LyricsSearchDialog、CoverSearchDialog
- ✅ 验收：QRC/YRC 逐字染色误差 ≤50ms；手动浏览/自动回弹/点击 seek；封面替换即时生效（内容哈希路径守卫）

### Phase 6：HITS 与设置（2-3 天）

- [ ] HitsStatusPage + HitsService 会话（强制 WASAPI Shared 逻辑保留在 Core）
- [ ] SettingsPage 全项（外观三档、输出三档、缓存、开发者模式日志窗口）
- [ ] OnlineLoginDialog（QRCoder PNG → Image）、RiskNoticeDialog
- [ ] IUpdateService 最小实现
- ✅ 验收：HITS 连播/预加载；设置项持久化重启生效

### Phase 7：打磨与多平台（3-5 天）

- [ ] macOS 构建验证（mpv dylib 打包、acrylic、MPRIS）
- [ ] Linux 构建验证（X11/Wayland、纯色回退）
- [ ] 无障碍：键盘导航、Tab 序、Tooltip、高对比主题
- [ ] 打包：Windows 自包含 zip（沿用手写 exe 惯例，**保持 PublishTrimmed=false**）、macOS .app、Linux tar.gz/AppImage
- [ ] WinUI 版归档决策（保留 `native/libmpv-winui` 供回滚）
- ✅ 验收：三平台冒烟（扫描→播放→歌词→HITS）；安装包解压即用

---

## 8. 测试与验证矩阵

| 类别 | 工具 | 覆盖 |
|---|---|---|
| 服务层回归 | 现有 458 项 xUnit（改引 Core） | 全部，每 Phase 必绿 |
| ViewModel 测试 | 现有 + 新增 | 播放状态机、队列、收藏 |
| UI Headless 测试 | Avalonia.Headless.XUnit | 导航转场、命令绑定、对话框 |
| 手工矩阵 | 沿用 handoff 规约 | 1280/1440/1600/1920；侧栏两态；快速连点导航 |
| 平台矩阵 | 三 OS 冒烟清单 | Phase 7 |
| 音频矩阵 | 真机 | MP3/FLAC/WAV/OGG、DSD（Win）、三档输出、设备热切换 |

---

## 9. 风险与缓解

| 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|
| mpv GL 集成平台差异 | 中 | Phase 2 阻塞 | 音频为主无视频依赖；备选 NativeControlHost 降级；Phase 0 先做 spike |
| 逐字歌词渲染性能 | 中 | Phase 5 | 单画布架构已验证；超过 16ms 帧预算时逐字段改预渲染位图缓存 |
| Core 抽离引入回归 | 低 | 全局 | 458 项测试先行；WinUI 版保持可运行作对照 |
| FluentAvalonia 与自绘样式冲突 | 中 | 视觉不统一 | 控件样式全部经 PrismControls.axaml 二次封装，页面不直接用三方样式 |
| 双版本并行维护成本 | 中 | 时间 | Phase 3 起只修 WinUI 致命 bug，新功能只进 Avalonia 版 |
| Linux 无合成器导致透明失效 | 高 | 视觉降级 | 令牌层预设"纯色模式"一档，检测失败自动切换 |

---

## 10. 开发规约（执行期强制）

1. 每次代码改动后构建并打开 Avalonia demo 验证；若在运行先杀进程（沿用手 off 命令，进程名 `PrismWave.Avalonia`）。
2. ViewModel 与 Services 禁止出现任何 `Avalonia.*` 命名空间引用（CI 用 grep 守护）。
3. 所有颜色/字号/动效时长必须走令牌，禁止页面内字面量。
4. 新控件一律 code-only 或 `.axaml` + `x:Class`，禁止 code-behind 里写业务逻辑（对齐现 MVVM 纪律）。
5. 提交按 Phase 切分，不与 WinUI 修改混提交（继承 handoff 规约）。
6. 每阶段更新本文档勾选进度，并在 `ai_handoff.md` 追加"最近变更"。

---

## 11. 待确认决策点

1. **框架确认**：✅ 已确认 Avalonia 11 + Skia（2026-10-10）。
2. **命名空间**：Core 层是否顺手把 `PrismWave_WinUI.*` 改名为 `PrismWave.Core.*`（建议改，一次性 sed + 编译验证）。
3. **WinUI 版归档时点**：Phase 7 结束归档，还是双版本维护一个版本周期。
4. **首版平台优先级**：macOS 与 Linux 是否进首个发布（影响 Phase 7 时长）。
5. **动效原则**：✅ 已确认从简（2026-10-10）——仅保留功能性过渡（页面转场、覆盖层、悬停反馈），不做装饰性动画。
