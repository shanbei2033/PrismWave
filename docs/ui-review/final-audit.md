# PrismWave 首页 UI 最终验收

## 运行与自动化结果

- x64 build：成功，0 warnings，0 errors。
- tests：80 passed，0 failed，0 skipped。
- 禁用模式扫描：未发现 `Canvas`、负 Margin、ZIndex、超大固定总宽度、最近播放或私人雷达。
- 独立代码审查：无 Critical/Important；3 项测试严谨性建议均已修正并复验。
- 默认启动物理窗口：1600x900；一次性测试尺寸标记在读取后自动删除。
- 当前 demo：默认 1600x900、首页、导航展开，保持运行。

## 七项布局对照

1. 左侧导航：`ShellPage.xaml` 使用原生 `NavigationView`；`OpenPaneLength=220`、`CompactPaneLength=48`，原生 Pane Toggle 与菜单项各占模板行，`IsSettingsVisible=True` 将设置入口固定到底部。无负 Margin、Canvas 或叠加按钮。
2. 标题与刷新：`HomePage.xaml` 根 Grid 为 `Auto,*` 两行；`PageHeader` 在 Row 0，`ScrollViewer` 在 Row 1。标题栏 `Margin=0,18,24,12`，刷新按钮 40x40，右侧 24px 安全边距。
3. 歌曲信息：原卡片墙已按批准的阶段 4 改为 `TrendingSongList` 榜单行。行 `MinHeight=64`、封面 48x48，标题和歌手均为 `NoWrap + CharacterEllipsis`，父级无 Clip。
4. 设置入口：使用 `NavigationView` 原生 SettingsItem 模板，与其他导航项共享图标、Padding、Hover 和折叠态布局；删除了额外 Border 与不对称 Margin。
5. 播放栏：`BottomPlayerBar.xaml` 外层三列均为 `*`；中央 Grid 行为 `Auto,10,Auto`，控制按钮在 Row 0，进度在 Row 2。播放 52、上一首/下一首 40、模式/队列 36，播放栏 token 高度 132。
6. 右侧裁切：批准的榜单布局替代横向歌曲卡片墙；宽窗口为两列，窄窗口自动单列，不再依赖窗口总宽度或超大固定容器。Hero、榜单和滚动条在 1280/1440/1600/1920 均无永久裁切。
7. 整体约束：Shell 内容与播放栏为 `*,Auto` 两行，Home 标题与滚动区为 `Auto,*` 两行；播放栏不覆盖页面，最后一行可滚动到播放栏上方。

## 截图证据

> 运行截图接口按系统 125% DPI 返回逻辑像素；下面尺寸均由 Win32 窗口矩形确认的物理尺寸。

- `09-responsive-1280.png`：1280x720，展开态。
- `10-final-home.png`：1600x900，展开态。
- `11-final-home-collapsed.png`：1600x900，折叠态。
- `12-final-home-1440.png`：1440x900，展开态，榜单单列。
- `13-final-home-1920.png`：1920x1080，展开态，榜单两列。
- `14-final-page-bottom.png`：页面底部与播放栏间距。
- `15-final-ranking-hover.png`：全球热门行 Hover。
- `16-final-player.png`：加载歌曲后的完整播放栏。

## 视觉检查结论

- 导航展开/折叠均无重叠，设置入口位置稳定。
- 刷新按钮未覆盖垂直滚动条。
- Hero 文本在 1280 档正常换行，封面拼贴不越界。
- 榜单标题、歌手、时长和更多按钮无半行裁切。
- 编辑精选与流派探索可完整滚动到播放栏上方。
- 播放控制与进度条不重叠，中央区域在各尺寸保持视觉居中。
