# HITS 临时播放会话与无分隔线设计

日期：2026-07-17  
目标分支：`WinUI`

## 目标

HITS 是一个临时的沉浸式收听模式，不属于全局播放队列。进入 HITS 时保留主页播放器的歌曲、队列、位置、循环模式和播放意图；退出 HITS 时立即停止直播并恢复进入前状态。底部播放栏在整个 HITS 生命周期中不得显示 HITS 的歌曲、封面、歌词或队列。

同时删除 HITS 标题区域下方的 1 DIP 分隔线，不改变标题栏高度、拖动区域和返回按钮布局。

## 已确认的用户行为

- 原歌曲正在播放：进入 HITS 后暂时挂起；退出后从原位置自动继续。
- 原歌曲已经暂停：退出后仍保持暂停，并保留原位置。
- 进入前没有歌曲：退出后回到 `Idle`，播放栏仍显示未选择歌曲。
- HITS 退出包括返回按钮、Esc、其他导航强制关闭、无动画关闭、页面卸载和窗口关闭。
- 重复退出和快速进出必须幂等，旧 HITS 回调不得覆盖已恢复的主页播放状态。
- HITS 临时使用 `WASAPI shared` 及既有 MPV 回退链，但不得写入或改变用户保存的音频输出偏好。

## 根因

当前 `AppServices` 将同一个 `PlaybackService` 同时注入 `PlaybackViewModel` 和 `HitsStatusViewModel`。HITS 调用公开的 `Play` 后，`PlaybackService` 会替换 `CurrentTrack`、清空原队列并发布 `StateChanged`；播放栏因此同步成 HITS。页面卸载只停止计时器和释放背景，没有结束会话。HITS 还会把 `AudioOutputMode` 持久化为 `wasapi_shared`。

标题横线来自 `HitsTitleBar` 的 `BorderBrush="#20FFFFFF"` 和 `BorderThickness="0,0,0,1"`。

## 方案比较

### 采用：单音频引擎的受令牌约束临时会话

`PlaybackService` 保持唯一的音频引擎宿主。进入 HITS 时捕获并冻结主会话，停止当前后端，临时将同一宿主切换到共享输出路由，再通过独立的 HITS 会话接口驱动直播。退出时先停止 HITS，再恢复用户当前输出偏好和主会话。

优点：不会创建第二个音频引擎，不与 WASAPI 独占设备争抢；播放栏的数据源保持主会话；恢复逻辑集中在播放器内部，可以正确处理远程解析、DSD、暂停状态和异步 Seek。

### 不采用：第二个独立播放器实例

隔离最直观，但主播放器仅暂停时可能仍占有 WASAPI 独占设备，HITS 的共享实例无法打开。若额外销毁和重建主实例，生命周期与资源竞争比单宿主临时会话更复杂。

### 不采用：退出时简单 `Play + Seek` 恢复

实现较少，但 `Play` 会重建队列并强制自动播放；远程异步加载、暂停歌曲、DSD 和旧回调都可能丢位置或误播放，且 HITS 期间播放栏仍会被覆盖。

## 架构

### 主播放服务

为 `PlaybackService` 增加内部临时会话控制能力，并保持现有 `IPlaybackService` 的主会话语义不变：

- 捕获 `Track`、复制后的 `Queue`、`PlaybackMode`、位置、时长、播放/暂停意图和恢复 revision。
- 临时会话期间冻结公开的 `CurrentTrack`、`Queue`、`Mode`、位置和播放栏通知。
- 停止 MPV/DSD 后端并取消主会话未完成的解析、启动监视和旧 load revision。
- 临时将同一个 `MpvPlaybackEngineHost` 切换为 `wasapi_shared`，不调用 `SettingsService.SaveAsync`。
- 根据 session token 和 revision 路由 MPV 的 started/failed/ended/state 回调，拒绝旧主会话和旧 HITS 会话事件。

### HITS 播放会话

新增窄接口 `IHitsPlaybackSession`，只暴露 HITS 所需状态和操作：

- `IsActive`、`IsLoading`、`IsPlaying`、`CurrentTrack`、`PositionSeconds`、`Error`。
- `Begin`、`Play`、`Pause`、`Resume`、`Seek`、`Stop`、`End`。
- 独立 `StateChanged` 事件供 `HitsStatusViewModel` 更新直播状态，不向 `PlaybackViewModel` 发布 HITS 曲目信息。

具体实现复用 `PlaybackService` 的唯一引擎宿主，而不是创建第二个 `PlaybackService`。

### HITS ViewModel 与页面生命周期

- `HitsStatusViewModel` 改为依赖 `IHitsPlaybackSession`，不再直接调用全局 `IPlaybackService`，也不再保存音频输出设置。
- `PrepareHitsSession` 首先开始临时会话，再按节目时间播放并 Seek。
- Off-air 只停止 HITS 会话内的音频，不清空主播放器。
- `EndHitsSession` 清理 pending seek、复位 `IsSessionActive`/`IsPaused`，并幂等结束临时会话。
- Shell 在 HITS 覆盖层开始关闭、播放栏重新可见之前调用结束入口；`HitsStatusPage.Unloaded` 再执行一次幂等兜底。

## 恢复顺序

1. 递增 HITS revision，取消 HITS 加载并拒绝迟到回调。
2. 停止 HITS 音频并清理 HITS 状态。
3. 将唯一引擎宿主恢复为用户当前保存的输出模式和设备。
4. 恢复主队列、模式、歌曲和时长。
5. 本地/远程曲目重新走既有加载与解析流程；媒体真正打开后恢复位置。
6. DSD 曲目通过原 DSD 后端恢复并 Seek。
7. 按捕获的播放意图自动播放或保持暂停。
8. 临时会话标志清除后只发布一次主会话状态，使播放栏恢复可交互且不闪现 HITS。

## 失败与边界处理

- HITS 打开失败：结束临时会话并恢复主播放器，不把主播放器标记为失败。
- 主曲远程 URL 已过期：按原曲身份重新解析，不复用 HITS 前的临时 URL。
- 快速返回或重复卸载：仅当前 token 可以恢复；后续 `End` 是空操作。
- HITS 正在切歌时退出：取消当前 HITS revision，迟到回调不得启动播放。
- 应用关闭：停止 HITS，不需要重新启动主音频；正常导航退出仍执行完整恢复。
- 用户原本没有歌曲：停止 HITS 后恢复 `Idle`，队列为空。

## UI 修改

从 `HitsTitleBar` 删除底边框画刷和底边厚度。保留 68 DIP 标题栏高度、左右 Margin、拖动区域、返回按钮和系统窗口按钮安全区。

## 测试策略

按测试驱动顺序先加入失败测试：

- 标题栏不存在底边框。
- 开始 HITS 后主播放器的 Track、Queue、Mode 和位置保持冻结。
- HITS Track 不发布给 `PlaybackViewModel`。
- 原歌曲播放时退出会从捕获位置继续。
- 原歌曲暂停时退出仍暂停。
- 无原歌曲时退出回到 Idle。
- HITS 停止发生在主播放器恢复之前。
- Off-air 只停止 HITS。
- 会话输出覆盖不修改 `settings.json` 中的音频模式。
- Back、Esc、强制导航和 Unloaded 都调用同一幂等结束入口。
- 重复 End、快速进出和旧回调不能覆盖新会话。
- 远程歌曲和 DSD 歌曲走各自原恢复后端。

完成后运行全量测试、x64 构建、`git diff --check`，并在真实 Demo 中验证：主页播放歌曲进入 HITS，播放栏身份不变；退出后 HITS 停止且原歌曲按进入前状态恢复。

