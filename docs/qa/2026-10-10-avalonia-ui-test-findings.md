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

- 非活动歌词行无模糊、卡拉OK 整行 clip 非逐字、封面双击搜封面（CoverSearchDialog）未做。
