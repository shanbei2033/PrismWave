# PrismWave 开发文档（Flutter 方案）

## 1. 项目定位

**项目名称：PrismWave**

PrismWave 是一个 **现代化、美观、高性能的本地音乐播放器**，使用 Flutter 构建桌面 UI，视觉风格统一为玻璃拟态（Glassmorphism），并以 WASAPI 为核心输出后端，提供稳定、低延迟的本地播放体验。

核心目标：

1. 播放能力稳定可靠，切歌快、进度准、设备切换平滑。
2. 界面观感现代化，玻璃拟态层次清晰，动效克制高级。
3. 支持主流有损/无损格式及 DSD：`mp3/aac/wav/flac/ogg/ape/dsd`。
4. 保持一致的播放逻辑（播放列表上下文 + loop/single/shuffle）。

---

## 2. 为什么可以用 Flutter

可以，且适合本项目：

- Flutter 在 Windows 桌面端渲染性能稳定，构建玻璃拟态 UI 成本低（`BackdropFilter`、渐变、阴影、动画体系成熟）。
- Flutter 对复杂列表、状态驱动 UI、动画过渡支持完善，适合音乐播放器的多页面 + 高频交互场景。
- 通过 `FFI + Rust/C++` 可接入高性能原生音频链路，避免纯 Dart 音频能力在格式和底层设备控制上的限制。
- 可在单代码库中保持 UI 一致性，后续扩展到 macOS/Linux 也更容易。

结论：**Flutter 负责 UI 与交互，原生音频核心负责解码与 WASAPI 输出**，是本项目的推荐实现。

---

## 3. 范围与非目标

### 3.1 本期范围（V1）

- 本地目录扫描与媒体库构建（Library/Artists/Albums/Favorites）。
- 音频播放控制（播放、暂停、上一首、下一首、拖动进度、音量）。
- 播放模式（`loop/single/shuffle`）。
- 输出设备选择与设备变化恢复。
- 玻璃拟态 UI（浅色/深色主题）。

### 3.2 非目标（V1 不做）

- 在线流媒体服务接入。
- 账号系统、歌单云同步。
- 音乐社交能力。

---

## 4. 总体架构（Flutter + Native Audio Core）

### 4.1 分层

1. **Presentation 层（Flutter）**
   - 页面、组件、主题、动画、快捷键。
2. **State 层（Riverpod）**
   - `PlaybackState`、媒体库状态、筛选与搜索状态。
3. **Domain 层（Dart）**
   - 播放命令编排、列表上下文构建、业务规则。
4. **Native Audio Core（Rust，FFI）**
   - 解码、重采样、缓冲、WASAPI、设备管理。
5. **Storage/Indexer（Dart + Isolate）**
   - 本地扫描、Tag 读取、封面缓存、索引持久化。

### 4.2 关键设计原则

- 唯一播放链路：`File -> Decoder -> PCM/DoP -> WASAPI`。
- `playFromPlaylist(track, playlist)` 为唯一建链入口。
- `next/previous/onTrackEnded` 只依赖 `currentPlaylist + currentIndex + playbackMode`。
- UI 主线程不直接承担解码/重采样，保证滚动和动画流畅。

---

## 5. 音频引擎设计

### 5.1 格式支持

| 格式 | 处理方式 | 输出 |
|---|---|---|
| mp3 | 解码为 PCM | WASAPI |
| aac | 解码为 PCM | WASAPI |
| wav | PCM 直读/统一解码 | WASAPI |
| flac | 无损解码为 PCM | WASAPI |
| ogg | Vorbis/Opus 解码为 PCM | WASAPI |
| ape | Monkey's Audio 解码为 PCM | WASAPI |
| dsd (dsf/dff) | DoP 优先，PCM 回退 | WASAPI |

### 5.2 DSD 支持策略

- 优先路径：设备支持 DoP 时进行 DoP 输出。
- 回退路径：设备不支持 DoP 时，进行高质量 DSD->PCM 转换。
- UI 显示当前链路：`DSD(DoP)` / `DSD->PCM`。

### 5.3 WASAPI 策略

- 默认 `Shared Mode`（兼容优先）。
- 可切 `Exclusive Mode`（低延迟/独占音频设备）。
- Exclusive 初始化失败自动回退 Shared。
- 监听默认设备变化与热插拔，自动重建会话并恢复播放。

### 5.4 Native Core 技术建议

- 语言：Rust（稳定、性能高、便于跨平台扩展）。
- FFI 桥：`flutter_rust_bridge`。
- 解码：`FFmpeg/libav`（统一容器/编码支持）。
- 输出：Rust 侧封装 WASAPI（含设备枚举、格式协商、缓冲管理）。

---

## 6. 播放状态模型与行为

```dart
class Playlist {
  final String type; // library | artist | album | favorites
  final String name;
  final List<Track> tracks;
}

class PlaybackState {
  final Track? currentTrack;
  final Playlist? currentPlaylist;
  final int currentIndex;
  final PlaybackMode playbackMode; // loop | single | shuffle
  final bool isPlaying;
  final bool isLoading;
  final Duration currentTime;
  final Duration duration;
  final double volume; // 0..1
}
```

行为基线：

1. `loop`：线性前后切歌 + 边界回环。
2. `single`：仅自然播放结束时重播当前曲；手动切歌行为同 `loop`。
3. `shuffle`：切歌随机且避免命中当前索引。
4. 搜索过滤后建链，只在过滤结果内切歌。

---

## 7. Flutter UI 设计规范（玻璃拟态）

### 7.1 视觉变量

- 全局背景：多层渐变 + 微噪点。
- 玻璃层：半透明填充 + 高斯模糊 + 细高光描边。
- 阴影：柔和、大半径、低不透明度。
- 文本：高对比主文本 + 中等对比次文本。

建议变量：

```css
--glass-blur: 22px;
--glass-alpha: 0.22;
--glass-border: rgba(255,255,255,0.28);
--accent: #39C0FF;
--accent-2: #4BE1C3;
```

### 7.2 组件要求

- 侧边栏、播放控制栏、信息卡片统一玻璃容器。
- 封面卡支持轻微发光与 hover 抬升（2~4px）。
- 进度条/音量条采用渐变填充，拖动时实时反馈。
- 动效时长：180~260ms，默认 `easeOutCubic`。

### 7.3 性能降级策略

- 提供“低特效模式”：降低 blur 半径，禁用复杂阴影与背景动效。
- 大列表启用 item 缓存与按需构建，防止帧率下降。

---

## 8. 模块划分建议

```text
PrismWave/
  dev.md
  app/
    lib/
      app.dart
      core/
      features/
        library/
        artists/
        albums/
        favorites/
        now_playing/
      shared/
      theme/
    windows/
    pubspec.yaml
  native/
    rust_core/
      Cargo.toml
      src/
        lib.rs
        decoder/
        wasapi/
        pipeline/
  tools/
```

---

## 9. Dart <-> Rust FFI 接口（建议）

### 9.1 Dart 调 Rust

- `load(trackPath)`
- `play()`
- `pause()`
- `seek(milliseconds)`
- `next()` / `previous()`
- `setVolume(double)`
- `setMode(loop|single|shuffle)`
- `setOutputDevice(deviceId)`
- `setOutputPath(shared|exclusive)`

### 9.2 Rust 回调 Dart

- `onPosition(ms)`
- `onBuffering(bool)`
- `onEnded()`
- `onError(code, message)`
- `onDeviceChanged(deviceId)`
- `onFormatInfo(sampleRate, bitDepth, channels, pathMode)`

---

## 10. 性能目标（V1）

- 冷启动到可交互：<= 2.5s（10k 曲目库）。
- 首次点播到出声：<= 180ms。
- 相邻切歌出声：<= 120ms。
- 主界面滚动：稳定 60fps。
- 空闲 CPU：<= 3%，播放时 CPU：<= 10%（Shared 模式）。

---

## 11. 里程碑计划

### M1（2 周）：工程骨架

- Flutter Windows 工程初始化。
- Riverpod 状态模型与页面框架。
- Rust Core 最小链路（wav/mp3 播放）。

### M2（2~3 周）：完整格式与页面

- 增加 aac/flac/ogg/ape。
- 完成 Library/Artists/Albums/Favorites 交互。
- 玻璃拟态 UI 首版落地。

### M3（2 周）：DSD 与设备能力

- DSD（dsf/dff）接入。
- DoP/PCM 回退逻辑。
- 设备枚举、切换、恢复策略。

### M4（2 周）：性能与稳定性

- 大曲库扫描优化、封面缓存优化。
- 高频操作压测（拖进度、切歌、切设备）。
- 错误码体系与恢复策略补齐。

### M5（1~2 周）：发布准备

- 打包与安装流程。
- 日志与诊断导出。
- 回归测试与发布说明。

---

## 12. 测试策略

### 12.1 单元测试

- 播放模式算法与索引边界。
- 播放列表上下文构建。
- 媒体元数据解析。

### 12.2 集成测试

- Dart-Rust FFI 命令与事件回传闭环。
- 设备切换恢复。
- DSD 模式切换正确性。

### 12.3 手工回归

- 页面入口点播一致性。
- 搜索过滤后切歌范围正确性。
- 深浅主题可读性与玻璃层表现。
- 低特效模式下帧率与耗电表现。

---

## 13. 风险与应对

- **DSD 兼容差异**：设备能力探测 + 白名单/黑名单策略。
- **Exclusive 模式失败**：自动回退 Shared 并给出清晰提示。
- **FFI 稳定性风险**：接口收敛、统一错误码、崩溃隔离。
- **玻璃特效性能开销**：低特效模式 + 动效分级。

---

## 14. 当前结论

PrismWave 完全可以基于 Flutter 实现，并且推荐采用 **Flutter（UI） + Rust（音频核心）** 的双层架构。在保证玻璃拟态视觉品质的同时，仍能满足 WASAPI、多格式解码与高性能播放目标，适合作为 Windows 本地高质量音乐播放器的工程方案。
