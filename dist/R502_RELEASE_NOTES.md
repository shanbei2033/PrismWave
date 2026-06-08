# PrismWave R502

## English

- Split online chart fallback states into two clear cases:
  - If today's chart has not been generated yet, PrismWave shows yesterday's chart and marks only the chart detail title with an update-time notice.
  - If the remote JSON cannot be fetched or parsed, PrismWave shows the yellow unavailable warning.
- Removed chart status icons from the Home banner so Home stays visually clean.
- Added the reusable `chart_notice.svg` status icon for chart detail notices.
- Updated the app and installer version to `R502`.

## 中文

- 将在线榜单兜底状态拆成两类：
  - 今日榜单尚未生成时，默认显示昨日榜单，并且只在榜单详情页标题旁显示更新时间提示。
  - 远程 JSON 无法拉取或不可用时，才显示黄色不可用告警。
- 首页榜单卡片不再显示状态叹号，保持首页视觉干净。
- 新增 `chart_notice.svg`，用于榜单详情页状态提示。
- 应用与安装包版本同步为 `R502`。
