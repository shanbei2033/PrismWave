# PrismWave R501_fix2

## English

- Fixed online Home cold-start failures caused by stale `prismwave-hits` daily JSON. The remote payload has been updated to schema 7 Top 100 with cover URLs.
- Added a bundled Top 100 fallback payload so Home can still open when the network or remote daily JSON is unavailable.
- Online Home now caches recommendations by Beijing date and shows a yellow warning when live recommendations are unavailable.
- Added Settings > Online with a refresh button for fetching today's chart.
- Added frameless Windows edge resizing.
- Updated the app and installer version to `R501_fix2`.

## 中文

- 修复在线首页冷启动时因 `prismwave-hits` 远程每日 JSON 仍是旧格式而加载失败的问题；远程数据已更新为 schema 7 Top100，并包含封面。
- 新增内置 Top100 兜底数据，网络或远程每日 JSON 不可用时首页仍可进入。
- 在线首页改为按北京时间日期缓存推荐；实时推荐不可用时显示黄色告警。
- 设置页新增"在线"分类，并提供"拉取今日榜单"刷新按钮。
- Windows 无边框窗口现在支持从边缘自由拉伸。
- 应用与安装包版本同步为 `R501_fix2`。
