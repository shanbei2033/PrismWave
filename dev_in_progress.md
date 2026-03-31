# PrismWave In-Progress Features

这个文档用于记录当前处于开发中、暂时搁置、或后续准备继续推进的功能。

## 1. 在线逐字歌词 / QRC 歌词支持

- 状态：暂时搁置
- 优先级：中
- 当前结论：功能链路已部分接入，但在线来源稳定性不足，当前阶段暂不继续投入

### 目标

让 PrismWave 支持真正的逐字歌词高亮，而不是仅依赖普通 LRC 的整句进度 fallback。

### 原计划方向

1. 保留现有普通歌词高亮 fallback
2. 扩展解析器以支持：
   - Enhanced LRC
   - QRC
3. 新增支持逐字歌词的在线来源
4. 在歌词搜索结果中标记歌词类型：
   - `LRCLIB`
   - `QRC`
   - `ELRC`
   - `TXT`

### 当前已完成

1. 已实现双轨制歌词渲染
   - 有逐字时间轴时走真逐字高亮
   - 没有逐字时间轴时走 fallback

2. 已扩展歌词模型
   - 新增逐字段片段数据结构
   - 支持缓存逐字片段数据

3. 已扩展解析器
   - 支持 Enhanced LRC
   - 支持 QRC
   - 支持本地 `.qrc` 文件解析

4. 已接入新的在线歌词源尝试
   - 保留 `LRCLIB`
   - 新增 `QQ Music` 来源尝试获取逐字歌词

5. 已增加调试日志
   - `lyrics.search`
   - `lyrics.loaded`
   - `lyrics.render`

6. 已在歌词搜索结果中显示标签
   - 当前按识别结果显示单标签

### 当前阻塞点

1. 实际测试中，在线搜索结果里几乎无法稳定拿到 `QRC`
2. 即使接入了新的来源，逐字歌词命中率仍然很低
3. 需要进一步确认：
   - 是源本身没有稳定返回逐字歌词
   - 还是接口字段、编码、响应结构仍有边界情况未覆盖

### 为什么先搁置

1. 当前投入产出比不高
2. 用户侧暂时无法稳定获得可用的逐字歌词结果
3. 继续深挖需要较多时间验证第三方来源稳定性，不适合当前阶段优先推进

### 后续恢复开发时的建议顺序

1. 先用真实接口样本固定一批 QRC / ELRC 测试数据
2. 单独验证不同来源的响应结构和字段命名
3. 优先确认哪一个在线源能稳定提供逐字歌词
4. 再恢复 UI 层面的进一步增强

### 涉及的核心文件

- `app/lib/src/models/lyric_line.dart`
- `app/lib/src/models/lyrics_document.dart`
- `app/lib/src/models/online_lyrics_search_result.dart`
- `app/lib/src/services/lyrics_reader.dart`
- `app/lib/src/services/online_lyrics_service.dart`
- `app/lib/src/state/library_state.dart`
- `app/lib/src/ui/fullplay_page.dart`
- `app/lib/src/controllers/library_controller.dart`
- `app/lib/src/controllers/playback_controller.dart`
- `app/lib/src/providers.dart`

### 恢复开发时优先关注的日志

- `lyrics.search -> ...`
- `lyrics.loaded -> ...`
- `lyrics.render -> ...`

