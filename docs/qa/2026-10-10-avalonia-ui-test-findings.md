# PrismWave.Avalonia UI 实测问题清单（2026-10-10）

来源：computer-use 实机走查（首页/搜索/曲库/HITS/设置/FullPlay/队列）+ 启动日志取证 + 代码定位。
对照基准：WinUI 版行为（`src/PrismWave.WinUI`）。本文档为修复依据，逐项带状态更新。

## P1-1 网络封面全部加载失败（用户报告）— 已修复

- 现象：首页"今日趋势"背景、"全球热门"缩略图、底部播放栏、全屏播放页大图、搜索结果封面全部显示占位符。
- 取证：`cover.load.failed: ... ArgumentException: Unable to load bitmap from provided data`；封面 URL（mzstatic/dzcdn）直连 200 OK，排除网络。
- 根因：[StableCoverImage.cs](../../src/PrismWave.Avalonia/Controls/Media/StableCoverImage.cs) 用 `new Bitmap(stream)` 直接解码 HttpClient 响应流；网络流不可 seek，Skia 解码抛 ArgumentException。
- 修复：先 `CopyToAsync` 缓冲到 `MemoryStream` 再解码；catch 内补 StartupLog 诊断。
- 验证：重启后无 failed 日志，PrintWindow 截图确认封面渲染。

## P1-2 HITS 电台播放无反馈且易误导 — 修复中

- 现象（computer-use）：HITS 页 LIVE 徽标亮起但底部播放栏仍显示旧曲目；期间 mpv 从未上报 started（日志 10:54:18 会话开始 → 10:55:21 会话结束，零事件）。
- 取证：
  - HITS 会话是临时会话（PlaybackService.HitsSession 快照/恢复主会话），主播放状态冻结 → 底部栏信息陈旧是架构使然；WinUI 中 HITS 为沉浸式全屏路由（ShellPage.xaml.cs `IsImmersiveRoute`），底部栏不可见，因此该问题在 WinUI 不出现。
  - Audius 音频流 URL 直连正常（api.audius.co 302 → creatornode，960ms），但 mpv 打开流长时间无事件：mpv 未设置 `network-timeout`（默认 0=无限等待），停滞时既不开始也不报错。
  - `HitsStatusPage` 在加载中/失败时无任何反馈，LIVE 徽标在缓冲中也照常显示。
- 修复方案：
  1. Avalonia Shell 将 `Hits` 路由按 WinUI 方式处理为沉浸式（复用 FullPlayOverlay 覆盖侧栏与底部栏），离开路由由页面 Unloaded 结束会话（现有逻辑保留）。
  2. mpv 引擎增加 `network-timeout=20`，使停滞流 20s 后转为失败事件。
  3. HITS 页增加缓冲指示（会话 IsLoading 时转圈）与会话错误文本显示。
- 状态：已修复并复测通过（HITS 沉浸式全屏、缓冲转圈与错误文本均生效；mpv network-timeout=20 已加入）。

## P2-1 搜索结果"…"按钮/右键菜单无响应 — 已修复

- 现象（computer-use）：点击搜索结果行尾"…"按钮及右键行均无菜单弹出；深挖后发现应用还会崩溃（事件日志 .NET Runtime 1026，`ArgumentException: Cannot show ContentMenu on a different control to the one it is attached to`）。
- 根因（决定性）：FluentAvalonia 2.5.1 主题库**没有 ContextMenu 的 ControlTheme**（WinUI 无此控件，FA 未移植）——模板不应用 → 无 ItemsPresenter → 菜单项不实现。表现为弹窗已打开（IsOpen=True、Opened 事件触发）但零尺寸不可见、UIA 树无内容、无新 HWND；且 Avalonia `ContextMenu.Open(control)` 要求传入附着控件否则抛 ArgumentException 导致崩溃。
- 修复：全局弃用 `ContextMenu`，改用 FA 自带的移植版 `fa:FAMenuFlyout` + `Control.ContextFlyout`（右键自动打开）/ `flyout.ShowAt(target)`（"…"按钮），与 WinUI 原版（ContextFlyout/MenuFlyout）完全对应。涉及 8 个文件：SearchPage、TrendingSongList（首页"…"两处）、QueuePane、AlbumsPage、LocalAlbumDetailPage、LibraryPage、FavoritesPage、ArtistDetailPage；绑定失效处（Flyout 不在可视树，`$parent` 不可用）改 Tag+Click 模式（WinUI 原版即如此）。
- 验证：右键菜单 7 项完整渲染；点击"立即播放"成功播放；"…"按钮菜单锚定按钮弹出；应用不再崩溃；458 项测试全部通过。

## P2-2 全屏播放页"返回按钮失效"（存疑）— 确认为误报，非代码缺陷

- 现象（computer-use）：报告称点击后按钮变 disabled 且未返回，需 Esc 退出。
- 代码复核：返回钮位于左上角（FullPlayPage.axaml L189，`BackButton_Click` → `Shell.GoBackCommand`），Esc 处理（ShellPage `TopLevel_KeyDown`）与按钮走同一路径，代码无差异。
- 结论：复测确认返回按钮正常；测试代理点击的是底部歌词工具钮（点击后工具区隐藏，观感像"按钮失效"）。

## P3-1 主题下拉当前项 UIA 不可选 — 不修（设计行为）

- FluentAvalonia ComboBox 选中项显示在头部区域而非列表项，UIA 树形态如此；键盘可用。记录为已知限制。

## P3-2 本地曲目播放短暂显示"正在缓冲" — 不修（设计行为）

- 本地文件加载也经过 Buffering 状态（mpv 打开文件极快，状态一闪而过），测试音频为 8 秒 WAV 单曲循环放大了观感。

## P3-3 曲库页无"曲目/专辑/艺术家/收藏"顶部 Tab — 不修（设计决定）

- Avalonia 重写将专辑/艺术家/收藏提升为侧栏独立导航项（与 WinUI 单页内 Tab 不同），功能等价，页面切换流畅。

## 已知非阻塞项（沿用重写收尾记录）

- ~~非活动歌词行无模糊~~（2026-10-10 第四轮已实现，见 P5-1）、卡拉OK 整行 clip 非逐字、封面双击搜封面（CoverSearchDialog）未做。

## P4-1 全屏播放页歌词错位/封面直角/动画卡顿/内存过高 — 已修复（2026-10-10 第二轮）

- 现象（用户实测反馈）：①歌词各行居中对齐导致左缘参差错落，非列表状；②全屏页大封面及各处封面无圆角；③动画卡顿不流畅；④内存占用过高。
- 根因：
  1. 歌词舞台 [LyricsStage.cs](../../src/PrismWave.Avalonia/Controls/Lyrics/LyricsStage.cs) 沿用 WinUI 的 `TextAlignment.Center`（居中对齐，长短行左缘参差）。
  2. Avalonia `Border.CornerRadius` **不裁剪子内容**（与 WinUI 行为不同），封面图溢出直角。
  3. [StableCoverImage.cs](../../src/PrismWave.Avalonia/Controls/Media/StableCoverImage.cs) 存在**位图泄漏**：旧 Bitmap 从不 Dispose（切歌每张泄漏约 36MB 全尺寸位图，单调增长不回落）；每次加载 new HttpClient；切换时 pending 被移除会瞬间露出第一张封面（闪烁）。
- 修复：
  1. LyricsStage 改 `TextAlignment.Left` 左对齐列表状；缩放锚点从水平中心改为左缘（缩放时行不漂移）；karaoke clip 起点同步调整。滚动动画本就存在（LyricsSceneController 0.32s EaseInOutCubic 插值）。
  2. StableCoverImage 新增 `CornerRadius` 属性（StreamGeometry 圆角矩形 Clip）；15 处使用面（FullPlay/TrendingSongList/TrendingBanner×5/Albums/Favorites/Library/Search/TopPlaylist/AlbumDetail×2/QueuePane/BottomPlayerBar/EditorialFeature/HitsStatus）按各自 Border 圆角值设置。
  3. StableCoverImage 重构生命周期：pending 淡入完成后提升为 current 并 Dispose 旧位图（任何时刻最多 2 张常驻）；共享 static HttpClient；解码按 `DecodeMaxEdge`（默认 1600）等比降采样后再释放原图。修复切歌闪烁。
- 修复中回归与纠正：`LoadBitmapAsync` 在后台线程读 StyledProperty（`DecodeMaxEdge`）触发 "Call from invalid thread" 导致全部封面加载失败——改为 UI 线程先捕获值再传参。
- 验证：全屏页真实网络封面（bôa/Twilight 等）圆角正常、歌词左对齐列表状、高亮行清晰、换歌无闪烁；5 次换歌内存 246→274→278→250→304MB 有波动但可回落（GC 正常回收，对比修复前单调泄漏）；458 项测试通过。

## P4-2 歌词仍错乱（真因：变换矩阵顺序颠倒）+ 验证截图被裁剪 — 已修复（2026-10-10 第三轮）

- 现象（用户二次反馈）：歌词仍挤向窗口顶部、中心下方的行出现在活动行上方，看不到完整歌词列表；且指出此前验证截图位置有问题（被裁剪），结论无效。
- 根因（第二轮修复只治标）：
  1. **真正根因**：[LyricsStage.cs](../../src/PrismWave.Avalonia/Controls/Lyrics/LyricsStage.cs) `DrawLine` 的 `PushTransform` 矩阵顺序颠倒。Avalonia `Matrix` 为**行向量约定**（`p' = p × M1 × M2`，M1 先作用），"关于锚点缩放"必须写 `T(-锚点) × S × T(+锚点)`；原代码顺序写反 → 实际缩放关于 `(-HorizontalPadding, -centerY)`，非活动行 `scale=0.7` 时 Y 被压为 `0.7y`，视口中心下方的行全部跑到活动行上方、挤向窗口原点，呈"错位/堆叠"状。
  2. **验证管道假象**：DPI-unaware 的 PowerShell（虚拟 `SM_CXSCREEN=1368`）经 `GetWindowRect` 拿到的是虚拟化坐标（1368x872），而 `PrintWindow(PW_RENDERFULLCONTENT)` 对 PMv2 感知进程输出的是**物理表面**（2736x1744）；位图按逻辑尺寸分配只截到左上角 1/4——既造成早期"整页 2x 渲染"误判，也造成"截图看不到完整歌词列表"。
- 修复：
  1. `DrawLine` 矩阵改为 `CreateTranslation(-HorizontalPadding, -centerY) * CreateScale(s, s) * CreateTranslation(HorizontalPadding, centerY)`（缩放锚定左缘中点）。
  2. 截图脚本位图按物理尺寸 `(w*2, h*2)` 分配，拿到完整 2736x1744 表面后再裁剪歌词区。
- 排除项：歌词数据本身有序（逐秒转储确认时间戳单调递增）；[LyricsSceneController.cs](../../src/PrismWave.Core/Infrastructure/Lyrics/LyricsSceneController.cs) 场景状态正确（bounds 单调、active/scroll/centers 全对）；截图中的"重复副歌行"是歌词本身的重复结构。`RenderTargetBitmap` 自截图对含自定义 DrawingContext 绘制的页面输出全黑，不可作为验证手段。
- 验证：物理尺寸 PrintWindow 抓全窗口 → 歌词区确认均匀行距、左对齐同一起点、活动行白色放大居中、行序符合歌曲结构；458 项测试通过。

## P5-1 页面切换卡顿/列表滚动卡顿/FullPlay 转场卡顿/歌词小且无模糊 — 已修复（2026-10-10 第四轮）

- 现象（用户报告四项）：①侧栏切换页面卡顿掉帧，点击后约半秒才跳转，库/专辑页尤甚；②歌曲列表滚动不能跑满显示器帧数，已加载过的封面滚动一下又重新加载；③进入 FullPlay 页转场动画卡顿；④歌词太小太细，非活动行没有模糊、活动行没有"清晰"对比，动效衔接不流畅。
- 根因：
  1. [ShellPage.axaml.cs](../../src/PrismWave.Avalonia/Views/Shell/ShellPage.axaml.cs) 每次导航都经 `RoutePageFactory.Create` 全量重实例化页面（控件树重建 + 首次测量布局全在点击处理内同步完成）；[AlbumsPage.axaml](../../src/PrismWave.Avalonia/Views/Library/AlbumsPage.axaml) 用 ListBox+WrapPanel 无虚拟化；[SearchPage.axaml](../../src/PrismWave.Avalonia/Views/Search/SearchPage.axaml) 用 ItemsControl 无虚拟化。
  2. [StableCoverImage.cs](../../src/PrismWave.Avalonia/Controls/Media/StableCoverImage.cs) 零缓存——容器回收或页面重建即重新下载+全尺寸解码；且各列表用原始大图解码（网络封面常 1500px+）。
  3. [FullPlayPage.axaml.cs](../../src/PrismWave.Avalonia/Views/Player/FullPlayPage.axaml.cs) `Loaded` 即同步为全部歌词行做 FormattedText shaping（每行 2 个），阻塞转场首帧。
  4. [LyricsSceneController.cs](../../src/PrismWave.Core/Infrastructure/Lyrics/LyricsSceneController.cs) 字号 34/SemiBold、非活动行 #888 偏小暗；`BlurAmount` 字段存在但渲染端从未使用（Avalonia 11.3 无 BlurEffect 公开实现）；[LyricsStage.cs](../../src/PrismWave.Avalonia/Controls/Lyrics/LyricsStage.cs) 用 16ms DispatcherTimer 驱动动画，非 vsync 对齐。
- 修复：
  1. ShellPage 增加主路由页面实例缓存（`_pageCache`，Home/Search/Library/Albums/Artists/Favorites/Settings 七个主路由缓存复用；嵌套详情页与 FullPlay/Hits 沉浸路由不缓存）；AlbumsPage 改 `fa:ItemsRepeater`+`fa:UniformGridLayout`（MinItemWidth 168）实现布局虚拟化；SearchPage 结果列表改 `fa:ItemsRepeater`+`fa:StackLayout`。
  2. 新建 [CoverBitmapCache.cs](../../src/PrismWave.Avalonia/Controls/Media/CoverBitmapCache.cs) 进程级解码位图 LRU（上限 128MB，key=`{maxEdge}|{source}`，仅 UI 线程访问）；StableCoverImage 加载前先查缓存，命中直接赋 `Source` 免下载/解码/淡入；全站 15 处使用面按显示尺寸设 `DecodeMaxEdge`（列表 256、页头 512、背景 1280）等比降采样。
  3. FullPlay `Loaded` 中歌词刷新延迟 280ms（`TransitionSettleMilliseconds`，覆盖 240ms 进入转场 + 余量）执行，带 revision 防过期；转场首帧不再被 shaping 阻塞。
  4. 字号 34→42（`LyricsSceneController.CurrentFontSize`，非活动行比例 30/42 同步）、SemiBold→Bold、基色 #888→#A8A8B0、行高 68→84（间距 30）；新建 [LyricsLinePresenter.cs](../../src/PrismWave.Avalonia/Controls/Lyrics/LyricsLinePresenter.cs) 子视觉方案：非活动行用 RenderTargetBitmap 离屏预渲染的 17 采样模糊位图，与清晰文本按 `blurMix` 交叉淡化（活动行 blurMix=0 纯清晰绘制，过渡期间逐帧插值无跳变）；缩放经 `MatrixTransform`（行向量约定 `T(-锚点)×S×T(+锚点)` 锚定行高中点）；动画驱动改 `TopLevel.RequestAnimationFrame` vsync 帧循环（`NeedsFrames` 为 false 即停，无动画零开销），逐行 culling 仅可见行参与绘制。
- 修复中回归与纠正：Avalonia 11.3.12 无 `Avalonia.Media.Effects.BlurEffect`（NuGet XML 文档查证），原计划子视觉挂 BlurEffect 动画 Radius 不可行，改走预渲染模糊位图回退路径（接口形状不变）；`ScaleTransform` 无 CenterX/CenterY 改用 MatrixTransform 手动组合。
- 验证：构建 0 错误 0 警告；458 项测试全部通过；computer-use 实机复测：页面切换即时响应（库/专辑页不再卡顿）、专辑网格滚动流畅且封面不闪不重载、FullPlay 进入转场平滑、歌词大号粗体且非活动行模糊/活动行清晰、手动浏览可平滑回位（在线歌曲 "maggots for brains - Olivia Rodrigo" 实测）。

## P5-2 歌词完全错位 + 模糊渲染重影（P5-1 歌词重构的回归）— 已修复（2026-10-10 第五轮）

- 现象（用户截图反馈）：非活动歌词行整体向右下漂移、压缩着挤向活动行并与活动行重叠；行内容呈"半清晰半模糊"的重影态（P5-1 首轮验证时未滚动到多行可见状态，截图验证不充分，漏检）。
- 根因：[LyricsLinePresenter.cs](../../src/PrismWave.Avalonia/Controls/Lyrics/LyricsLinePresenter.cs) 用 `RenderTransform`（MatrixTransform）做逐帧定位/缩放，但 **Avalonia 的 `RenderTransformOrigin` 默认值是元素中心 (0.5,0.5)**（与 WPF 的 (0,0) 不同），且 Panel 默认 `ArrangeOverride` 把每个 presenter Arrange 成整个舞台大小 → 实际变换 E = T(舞台中心) × M × T(-舞台中心)，在缩放矩阵 M 之外叠加了 (舞台中心 × (1-scale)) 的平移：非活动行（scale≈0.71）统一右移约 100px、下移约 70px，行距被压缩、与活动行叠成重影；交叉淡化中"清晰文本"与"模糊位图"同受该位移影响，视觉上呈错位模糊。次因：文本绘制在 presenter 本地 (0,0)，旧实现设计的 28px 左内边距丢失（左缘贴舞台边）。
- 修复：
  1. presenter 构造函数显式 `RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute)`，矩阵恢复纯 `T(0,-锚点)×S×T(0,+锚点+top)`。
  2. [LyricsStage.cs](../../src/PrismWave.Avalonia/Controls/Lyrics/LyricsStage.cs) 补 `MeasureOverride`（按减去内边距的约束测量子视觉）与 `ArrangeOverride`（统一 Arrange 在 (HorizontalPadding, 0)，缩放关于本地 (0,0)，左缘对齐不随 scale 漂移），恢复 28px 左内边距。
- 教训：P4-2 的"矩阵顺序"陷阱之外又新增一个 Avalonia 变换陷阱——`RenderTransformOrigin` 默认中心。凡用 RenderTransform 做逐帧像素级定位的控件必须显式归零 origin。
- 验证：构建 0 错误；458 项测试全部通过；computer-use 实机复测（英文歌两屏、两不同当前行）：5 项预期全部满足——各行左缘同一 x 对齐、当前行白色大号加粗清晰、非当前行灰色缩小模糊（离当前行越远越模糊）、行距均匀无重叠无重影、当前行垂直居中。

## P5-3 歌词行距过宽、模糊感不足（P5-1 参数微调）— 已修复（2026-10-10 第五轮）

- 现象（用户反馈）：歌词行间距太宽；非活动行模糊程度不足（只轻微发虚）。
- 调整：[LyricsStage.cs](../../src/PrismWave.Avalonia/Controls/Lyrics/LyricsStage.cs) `LineGap` 30→14、`MinimumLineHeight` 84→72（单行行中心间距 114→86，收紧约 25%）；[LyricsLinePresenter.cs](../../src/PrismWave.Avalonia/Controls/Lyrics/LyricsLinePresenter.cs) `BlurTaps` 扩散半径 ±4.5→±9 并改为 25 采样（权重总和归一，中心权重 0.20→0.16）、`BlurPadding` 8→12、`MaxBlurAmount` 1.9→1.6（相邻行更早饱和为纯模糊）。
- 验证：构建 0 错误；computer-use 实机复测：行距紧凑均匀、非当前行模糊感明显（相邻行明显发虚、远行近乎弥散）、当前行锐利清晰、无重影错位。

## P5-4 模糊呈离散重影而非高斯弥散（用户二次反馈"根本不是高斯模糊"）— 已修复（2026-10-10 第五轮）

- 现象（用户截图）：非活动行呈 2~3 层可辨认的离散重复轮廓（多次曝光感），不是连续柔和的高斯弥散。
- 根因：P5-1/P5-3 的"多圈偏移叠加"方案本质缺陷——仅 17/25 个离散采样点、间距 2.5~4.5px，每个采样都是一份低透明度的清晰文本复制；采样稀疏时叠加结果就是离散重影，加权无法弥补采样密度不足，永远做不出连续高斯。
- 修复（[LyricsLinePresenter.cs](../../src/PrismWave.Avalonia/Controls/Lyrics/LyricsLinePresenter.cs) 重写 BuildBlurredBitmap 为真模糊算法）：文本先离屏渲染到 RenderTargetBitmap → `CopyPixels` 拷出 premultiplied BGRA → 4 通道各做 3 次可分离盒式模糊（半径 9，等效高斯 σ≈7；滑动窗口 O(1)/像素，边界 clamp；线性滤波对 premultiplied 空间数学上合法）→ 写回 WriteableBitmap（`Lock()` + 逐行 Marshal.Copy）。每行仅构建一次（blurMix>0.02 懒构建），逐帧渲染仍只是一次 blit；BlurPadding 12→24（≥3σ）避免高斯尾部截断。
- 修复中纠正：`AlphaFormat.Premul`（非 Premultiplied，反射 NuGet 程序集确认）。
- 验证：构建 0 错误；458 项测试全部通过；computer-use 实机复测（"maggots for brains" 两个不同当前行）：非当前行为平滑雾状弥散、无可辨认重复轮廓；模糊强度随距离递增（相邻行轻模糊、远行化为雾块）；当前行锐利清晰；行距均匀无重叠。

## P5-5 模糊呈"运动模糊"感而非均匀晕开（用户三次反馈）— 已修复（2026-10-10 第五轮）

- 现象（用户反馈）：P5-4 的盒式模糊虽消除了离散重影，但呈"像在运动的模糊"（方向性拖影感），不是想要的"歌词均匀晕开"。
- 根因：大图上直接做盒式模糊，方形核 + 歌词行横向尺寸远大于纵向 → 水平方向晕开显著比垂直方向长，视觉呈水平拖影；盒式核的平底形状也会留方向性痕迹。
- 修复（[LyricsLinePresenter.cs](../../src/PrismWave.Avalonia/Controls/Lyrics/LyricsLinePresenter.cs) 改为降采样管线）：文本离屏渲染 → 拷出像素 → **box 平均降采样 4x** → 小图 2 次盒式模糊（r=3）→ **双线性放大回原尺寸** → 写回 WriteableBitmap。多级核卷积（box 平均 × 盒式 × 双线性）等效各向同性高斯（σ≈11），放大插值天然抹掉核形状痕迹，产生 Apple Music 式均匀晕开的雾状弥散；计算量降为 1/16，仍每行懒构建一次。BlurPadding 24→36（≥3σ）。
- 验证：构建 0 错误；458 项测试全部通过；computer-use 实机复测（两个不同当前行）：非当前行各方向一致弥散、无水平拖影、无块状/条带伪影；当前行锐利清晰；行距均匀。

## P5-6 "运动模糊"真因：相邻行残留清晰文本层（用户四次反馈"没变化"）— 已修复（2026-10-10 第五轮）

- 现象（用户反馈）：P5-5 降采样管线后观感依旧"像在运动模糊"，改模糊算法无效果。
- 根因（与前两轮不同，这才是真因）：[LyricsSceneController.cs](../../src/PrismWave.Core/Infrastructure/Lyrics/LyricsSceneController.cs) `ResolveTargetVisual` 中**相邻行（distance≤1）blurAmount=1.0**、远行 1.9；而 Presenter 的归一化基准 `MaxBlurAmount=1.6` → 相邻行稳态 blurMix=0.625，渲染为 **37.5% 透明度的清晰文本 + 62.5% 的雾** 交叉叠加——"锐利字核 + 横向延伸雾晕"正是运动模糊的典型观感（远行 mix=1 纯雾无此问题，所以只有紧邻两行带拖影感，改模糊位图管线当然"没变化"）。
- 修复（[LyricsLinePresenter.cs](../../src/PrismWave.Avalonia/Controls/Lyrics/LyricsLinePresenter.cs)）：
  1. `MaxBlurAmount` 1.6→**1.0**（= 场景非活动行最小值）：所有非活动行稳态 blurMix=1，纯模糊位图、零清晰层。
  2. 过渡期（0.32s 切行动画）清晰层透明度由线性 `(1-mix)` 改为平方 `(1-mix)²`，锐利残影快速消隐，中途拖影感最小化。
- 验证：构建 0 错误；458 项测试全部通过；computer-use 实机复测（两个不同当前行稳态）：当前行紧邻的上下行呈纯雾状弥散带、**无锐利笔画核心叠雾**（无运动模糊残影）；远行纯雾；当前行清晰锐利。
