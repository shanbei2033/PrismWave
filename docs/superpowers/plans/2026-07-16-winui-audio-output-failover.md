# WinUI Audio Output Failover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every supported local audio format play without an mpv-owned window, with MPV, WASAPI shared, and WASAPI exclusive routes plus deterministic automatic fallback.

**Architecture:** Construct libmpv with an immutable audio route before initialization, treat `MPV_EVENT_PLAYBACK_RESTART` as the successful-start boundary, and place the replaceable engine behind a generation-guarded host. `PlaybackService` preserves its queue state while the host rebuilds the native engine and advances a pure, testable fallback chain.

**Tech Stack:** C# 13, .NET 10, WinUI 3, libmpv client API, xUnit, CommunityToolkit.Mvvm.

## Global Constraints

- Persist the existing mode IDs: `compatibility`, `wasapi_shared`, and `wasapi_exclusive`.
- Default to `wasapi_shared` when the setting is missing or invalid.
- Shared fallback is `WASAPI shared → MPV`.
- Exclusive fallback is `WASAPI exclusive → WASAPI shared → MPV`.
- MPV mode does not force an audio output driver and has no additional fallback.
- Never persist a temporary fallback route over the user's preferred route.
- Keep the existing DSD/BASS path and online source-recovery policy unchanged.
- Disable mpv video, embedded cover-art display, automatic cover-art discovery, subtitles, and forced windows before `mpv_initialize`.
- A local load remains `Buffering` until `MPV_EVENT_PLAYBACK_RESTART`.
- A local load that has not started after five seconds advances to the next route.
- Preserve unrelated dirty Flutter files and untracked artifacts.

---

## File Map

- `src/PrismWave.WinUI/Models/AudioOutputModels.cs`: persisted IDs, route enum, display options, normalization, and fallback policy.
- `src/PrismWave.WinUI/Infrastructure/Audio/IPlaybackEngine.cs`: playback-start event contract.
- `src/PrismWave.WinUI/Infrastructure/Audio/IPlaybackEngineFactory.cs`: injectable native-engine creation boundary.
- `src/PrismWave.WinUI/Infrastructure/Audio/MpvPlaybackEngineFactory.cs`: production factory.
- `src/PrismWave.WinUI/Infrastructure/Audio/MpvPlaybackEngine.cs`: immutable pre-initialize route and libmpv event handling.
- `src/PrismWave.WinUI/Infrastructure/Audio/AudioOutputFailoverState.cs`: pure fallback state machine.
- `src/PrismWave.WinUI/Infrastructure/Audio/MpvPlaybackEngineHost.cs`: engine replacement, disposal, and stale-event filtering.
- `src/PrismWave.WinUI/Services/Implementations/PlaybackService.cs`: playback snapshot restoration, local watchdog, and route fallback.
- `src/PrismWave.WinUI/Services/Implementations/SettingsService.cs`: shared-mode default and invalid-value normalization.
- `src/PrismWave.WinUI/Services/Contracts/IPlaybackService.cs`: active route and fallback status.
- `src/PrismWave.WinUI/ViewModels/Settings/SettingsViewModel.cs`: readable choices and active-route presentation.
- `src/PrismWave.WinUI/Views/Settings/SettingsPage.xaml`: labeled mode picker and route status.
- `tests/PrismWave.WinUI.Tests/AudioOutputPolicyTests.cs`: normalization and fallback chain.
- `tests/PrismWave.WinUI.Tests/MpvPlaybackEngineStructureTests.cs`: pre-initialize audio-only configuration and playback-start event.
- `tests/PrismWave.WinUI.Tests/MpvPlaybackEngineHostTests.cs`: replacement, disposal, and stale callback rejection.
- `tests/PrismWave.WinUI.Tests/PlaybackServiceAudioOutputStructureTests.cs`: service watchdog and snapshot integration.
- `tests/PrismWave.WinUI.Tests/AudioOutputSettingsTests.cs`: settings migration and XAML binding.
- `tests/PrismWave.WinUI.Tests/BundledLibMpvCodecTests.cs`: real libmpv sequential-load probe.
- `tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj`: links the new pure source files.
- `ai_handoff.md`: records the final route behavior and verification evidence.

---

### Task 1: Define the output policy and migrate the default

**Files:**
- Create: `src/PrismWave.WinUI/Models/AudioOutputModels.cs`
- Create: `tests/PrismWave.WinUI.Tests/AudioOutputPolicyTests.cs`
- Modify: `src/PrismWave.WinUI/Services/Implementations/SettingsService.cs`
- Modify: `src/PrismWave.WinUI/ViewModels/Settings/SettingsViewModel.cs`
- Modify: `tests/PrismWave.WinUI.Tests/SettingsMigrationTests.cs`
- Modify: `tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj`

**Interfaces:**
- Produces: `AudioOutputRoute`, `AudioOutputModeOptionModel`, and `AudioOutputPolicy`.
- `AudioOutputPolicy.NormalizeModeId(string?)` always returns a persisted ID.
- `AudioOutputPolicy.BuildFallbackChain(string?)` returns a non-empty route list.

- [ ] **Step 1: Write failing policy tests**

```csharp
using PrismWave_WinUI.Models;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class AudioOutputPolicyTests
{
    [Theory]
    [InlineData(null, AudioOutputPolicy.WasapiSharedId)]
    [InlineData("", AudioOutputPolicy.WasapiSharedId)]
    [InlineData("unknown", AudioOutputPolicy.WasapiSharedId)]
    [InlineData("compatibility", AudioOutputPolicy.CompatibilityId)]
    [InlineData("wasapi_shared", AudioOutputPolicy.WasapiSharedId)]
    [InlineData("wasapi_exclusive", AudioOutputPolicy.WasapiExclusiveId)]
    public void NormalizeModeId_ReturnsStablePersistedId(string? value, string expected) =>
        Assert.Equal(expected, AudioOutputPolicy.NormalizeModeId(value));

    [Fact]
    public void SharedMode_FallsBackToMpv() =>
        Assert.Equal(
            [AudioOutputRoute.WasapiShared, AudioOutputRoute.Mpv],
            AudioOutputPolicy.BuildFallbackChain(AudioOutputPolicy.WasapiSharedId));

    [Fact]
    public void ExclusiveMode_FallsBackThroughSharedToMpv() =>
        Assert.Equal(
            [AudioOutputRoute.WasapiExclusive, AudioOutputRoute.WasapiShared, AudioOutputRoute.Mpv],
            AudioOutputPolicy.BuildFallbackChain(AudioOutputPolicy.WasapiExclusiveId));

    [Fact]
    public void CompatibilityMode_UsesOnlyMpv() =>
        Assert.Equal(
            [AudioOutputRoute.Mpv],
            AudioOutputPolicy.BuildFallbackChain(AudioOutputPolicy.CompatibilityId));
}
```

- [ ] **Step 2: Run the policy tests and confirm the missing-type failure**

Run:

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~AudioOutputPolicyTests
```

Expected: compilation fails because `AudioOutputPolicy` and `AudioOutputRoute` do not exist.

- [ ] **Step 3: Implement the policy model**

```csharp
namespace PrismWave_WinUI.Models;

public enum AudioOutputRoute
{
    Mpv,
    WasapiShared,
    WasapiExclusive
}

public sealed record AudioOutputModeOptionModel(
    string Id,
    string DisplayName,
    string Description);

public static class AudioOutputPolicy
{
    public const string CompatibilityId = "compatibility";
    public const string WasapiSharedId = "wasapi_shared";
    public const string WasapiExclusiveId = "wasapi_exclusive";

    public static IReadOnlyList<AudioOutputModeOptionModel> Options { get; } =
    [
        new(CompatibilityId, "MPV（自动）", "由 MPV 自动选择可用的音频输出。"),
        new(WasapiSharedId, "WASAPI 共享", "默认模式，可与其他应用同时播放。"),
        new(WasapiExclusiveId, "WASAPI 独占", "独占设备；失败后依次回退到共享和 MPV。")
    ];

    public static string NormalizeModeId(string? value) =>
        value?.Trim().ToLowerInvariant().Replace('-', '_') switch
        {
            CompatibilityId => CompatibilityId,
            WasapiExclusiveId or "wasapiexclusive" => WasapiExclusiveId,
            WasapiSharedId or "wasapishared" => WasapiSharedId,
            _ => WasapiSharedId
        };

    public static IReadOnlyList<AudioOutputRoute> BuildFallbackChain(string? value) =>
        NormalizeModeId(value) switch
        {
            WasapiExclusiveId =>
                [AudioOutputRoute.WasapiExclusive, AudioOutputRoute.WasapiShared, AudioOutputRoute.Mpv],
            CompatibilityId => [AudioOutputRoute.Mpv],
            _ => [AudioOutputRoute.WasapiShared, AudioOutputRoute.Mpv]
        };

    public static string GetRouteDisplayName(AudioOutputRoute route) => route switch
    {
        AudioOutputRoute.WasapiExclusive => "WASAPI 独占",
        AudioOutputRoute.WasapiShared => "WASAPI 共享",
        _ => "MPV（自动）"
    };
}
```

Link `Models/AudioOutputModels.cs` in the test project. In `SettingsService`, normalize both loaded and migrated settings and change the migration fallback from `wasapi_exclusive` to `wasapi_shared`:

```csharp
var loaded = LoadExisting() ?? CreateFromMigration(migrationService.Load());
Current = loaded with
{
    AudioOutputMode = AudioOutputPolicy.NormalizeModeId(loaded.AudioOutputMode)
};
```

```csharp
GetString(values, "audio.outputMode", AudioOutputPolicy.WasapiSharedId)
```

Initialize `SettingsViewModel._audioOutputMode` with `AudioOutputPolicy.WasapiSharedId`.

- [ ] **Step 4: Add migration assertions and run the focused tests**

Add assertions that missing and invalid legacy values produce `wasapi_shared`, while existing valid IDs are preserved. Run:

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter "FullyQualifiedName~AudioOutputPolicyTests|FullyQualifiedName~SettingsMigrationTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit the policy**

```powershell
git add src/PrismWave.WinUI/Models/AudioOutputModels.cs src/PrismWave.WinUI/Services/Implementations/SettingsService.cs src/PrismWave.WinUI/ViewModels/Settings/SettingsViewModel.cs tests/PrismWave.WinUI.Tests/AudioOutputPolicyTests.cs tests/PrismWave.WinUI.Tests/SettingsMigrationTests.cs tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj
git commit -m "feat(winui): define audio output fallback policy"
```

---

### Task 2: Make libmpv audio-only and gate readiness on playback restart

**Files:**
- Create: `src/PrismWave.WinUI/Infrastructure/Audio/IPlaybackEngineFactory.cs`
- Create: `src/PrismWave.WinUI/Infrastructure/Audio/MpvPlaybackEngineFactory.cs`
- Create: `tests/PrismWave.WinUI.Tests/MpvPlaybackEngineStructureTests.cs`
- Modify: `src/PrismWave.WinUI/Infrastructure/Audio/IPlaybackEngine.cs`
- Modify: `src/PrismWave.WinUI/Infrastructure/Audio/MpvPlaybackEngine.cs`
- Modify: `tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj`

**Interfaces:**
- Consumes: `AudioOutputRoute` from Task 1.
- Produces: `IPlaybackEngineFactory.Create(AudioOutputRoute route, string outputDevice)`.
- Replaces: `MediaOpened` with `PlaybackStarted`; it fires once per load after `MPV_EVENT_PLAYBACK_RESTART`.

- [ ] **Step 1: Write failing engine structure tests**

```csharp
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class MpvPlaybackEngineStructureTests
{
    private static readonly string Source = File.ReadAllText(FindSource());

    [Theory]
    [InlineData("SetOption(\"audio-display\", \"no\")")]
    [InlineData("SetOption(\"video\", \"no\")")]
    [InlineData("SetOption(\"force-window\", \"no\")")]
    [InlineData("SetOption(\"cover-art-auto\", \"no\")")]
    [InlineData("SetOption(\"sub-auto\", \"no\")")]
    public void Constructor_DisablesEveryVideoAndCoverWindowPath(string statement) =>
        Assert.Contains(statement, Source, StringComparison.Ordinal);

    [Fact]
    public void OutputOptions_AreAppliedBeforeMpvInitialize()
    {
        var output = Source.IndexOf("ApplyOutputOptions(route, outputDevice)", StringComparison.Ordinal);
        var initialize = Source.IndexOf("mpv_initialize(_handle)", StringComparison.Ordinal);
        Assert.InRange(output, 0, initialize - 1);
    }

    [Fact]
    public void PlaybackStarted_IsDrivenByPlaybackRestart()
    {
        Assert.Contains("MpvEventPlaybackRestartId = 21", Source, StringComparison.Ordinal);
        Assert.Contains("PlaybackStarted?.Invoke", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigureOutput(", Source, StringComparison.Ordinal);
    }

    private static string FindSource() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "src", "PrismWave.WinUI",
        "Infrastructure", "Audio", "MpvPlaybackEngine.cs"));
}
```

- [ ] **Step 2: Run the structure tests and observe failure**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~MpvPlaybackEngineStructureTests
```

Expected: assertions fail because video suppression and playback-restart handling are absent.

- [ ] **Step 3: Change the engine contract and factory**

Use this contract:

```csharp
public interface IPlaybackEngine : IDisposable
{
    double PositionSeconds { get; }
    double DurationSeconds { get; }
    bool IsPlaying { get; }
    string? Error { get; }
    event EventHandler? PlaybackEnded;
    event EventHandler<PlaybackLoadEventArgs>? PlaybackStarted;
    event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;
    event EventHandler? StateChanged;
    bool Load(TrackModel track, double volume, bool autoplay, out string? error);
    bool Load(TrackModel track, double volume, bool autoplay, long loadSequence, string sourceKey, out string? error);
    void Play();
    void Pause();
    void Stop();
    void Seek(double seconds);
    void SetVolume(double volume);
}
```

Create the factory boundary:

```csharp
public interface IPlaybackEngineFactory
{
    IPlaybackEngine Create(AudioOutputRoute route, string outputDevice);
}

public sealed class MpvPlaybackEngineFactory : IPlaybackEngineFactory
{
    public IPlaybackEngine Create(AudioOutputRoute route, string outputDevice) =>
        new MpvPlaybackEngine(route, outputDevice);
}
```

- [ ] **Step 4: Configure the immutable route before initialization**

Change the engine constructor to accept the route and set the common options before `mpv_initialize`:

```csharp
public MpvPlaybackEngine(AudioOutputRoute route, string outputDevice)
{
    var nativeDirectory = Path.Combine(AppContext.BaseDirectory, "Native");
    if (Directory.Exists(nativeDirectory))
    {
        SetDllDirectory(nativeDirectory);
    }

    _handle = mpv_create();
    if (_handle == IntPtr.Zero)
    {
        throw new InvalidOperationException("mpv_create failed.");
    }

    SetOption("terminal", "no");
    SetOption("sub-auto", "no");
    SetOption("cover-art-auto", "no");
    SetOption("audio-display", "no");
    SetOption("video", "no");
    SetOption("force-window", "no");
    SetOption("cache-secs", "12");
    SetOption("cache-on-disk", "no");
    SetOption("audio-client-name", "PrismWave");
    ApplyOutputOptions(route, outputDevice);

    var initializeResult = mpv_initialize(_handle);
    if (initializeResult < 0)
    {
        throw new InvalidOperationException($"mpv_initialize failed: {ErrorString(initializeResult)}");
    }

    // Start the existing event thread after successful initialization.
}
```

Apply routes through `mpv_set_option_string`, not runtime properties:

```csharp
private void ApplyOutputOptions(AudioOutputRoute route, string outputDevice)
{
    var device = string.IsNullOrWhiteSpace(outputDevice) ? "auto" : outputDevice.Trim();
    if (route is AudioOutputRoute.WasapiShared or AudioOutputRoute.WasapiExclusive)
    {
        SetOption("ao", "wasapi");
    }

    SetOption("audio-exclusive", route == AudioOutputRoute.WasapiExclusive ? "yes" : "no");
    if (route == AudioOutputRoute.WasapiExclusive)
    {
        SetOption("wasapi-exclusive-buffer", "50000");
    }

    SetOption("audio-device", device);
}
```

Delete `ConfigureOutput` and `NormalizeOutputMode`.

- [ ] **Step 5: Promote file-loaded context and fire readiness on playback restart**

Add `MpvEventPlaybackRestartId = 21`. On `MpvEventFileLoadedId`, promote the pending context but leave `_loaded` false. On playback restart, atomically mark that context started, set `_loaded = true`, clear `Error`, and invoke `PlaybackStarted` once. Add a `Started` flag to `EngineLoadContext` so seeks and buffering resumes cannot emit a second start callback.

Wrap each event-loop iteration in `try/catch` and write `mpv event loop error: {exception}` to `StartupLog` so a callback failure cannot silently kill the event thread.

- [ ] **Step 6: Run engine tests and build the WinUI project**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~MpvPlaybackEngineStructureTests
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore
```

Expected: selected tests pass; build succeeds with zero warnings and zero errors.

- [ ] **Step 7: Commit the engine boundary**

```powershell
git add src/PrismWave.WinUI/Infrastructure/Audio tests/PrismWave.WinUI.Tests/MpvPlaybackEngineStructureTests.cs tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj
git commit -m "fix(winui): initialize mpv as an audio-only engine"
```

---

### Task 3: Add a generation-guarded replaceable engine host

**Files:**
- Create: `src/PrismWave.WinUI/Infrastructure/Audio/AudioOutputFailoverState.cs`
- Create: `src/PrismWave.WinUI/Infrastructure/Audio/MpvPlaybackEngineHost.cs`
- Create: `tests/PrismWave.WinUI.Tests/MpvPlaybackEngineHostTests.cs`
- Modify: `tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj`

**Interfaces:**
- Consumes: `AudioOutputPolicy`, `IPlaybackEngine`, and `IPlaybackEngineFactory`.
- Produces: `MpvPlaybackEngineHost.Engine`, `.ActiveRoute`, `.ActiveRouteLabel`, `.FallbackReason`, `.ResetPreference(...)`, and `.TryFallback(...)`.
- Forwards: `PlaybackStarted`, `PlaybackFailed`, `PlaybackEnded`, and `StateChanged` only from the current generation.

- [ ] **Step 1: Write failing fallback-state tests**

```csharp
[Fact]
public void Exclusive_AdvancesToSharedThenMpvAndStops()
{
    var state = new AudioOutputFailoverState(AudioOutputPolicy.WasapiExclusiveId);
    Assert.Equal(AudioOutputRoute.WasapiExclusive, state.ActiveRoute);
    Assert.True(state.TryAdvance("exclusive failed"));
    Assert.Equal(AudioOutputRoute.WasapiShared, state.ActiveRoute);
    Assert.True(state.TryAdvance("shared failed"));
    Assert.Equal(AudioOutputRoute.Mpv, state.ActiveRoute);
    Assert.False(state.TryAdvance("mpv failed"));
    Assert.Equal("shared failed", state.FallbackReason);
}

[Fact]
public void ResetPreference_RestoresTheRequestedFirstRoute()
{
    var state = new AudioOutputFailoverState(AudioOutputPolicy.WasapiExclusiveId);
    state.TryAdvance("exclusive failed");
    state.Reset(AudioOutputPolicy.WasapiSharedId);
    Assert.Equal(AudioOutputRoute.WasapiShared, state.ActiveRoute);
    Assert.Null(state.FallbackReason);
}
```

- [ ] **Step 2: Write failing host tests with a fake factory**

Test these cases with fake engines that expose event-raising methods:

```csharp
[Fact]
public void TryFallback_DisposesOldEngineAndCreatesNextRoute()
{
    var factory = new FakePlaybackEngineFactory();
    using var host = new MpvPlaybackEngineHost(
        factory,
        AudioOutputPolicy.WasapiExclusiveId,
        "auto");
    var oldEngine = factory.Created[0];

    Assert.True(host.TryFallback("exclusive device rejected"));

    Assert.True(oldEngine.IsDisposed);
    Assert.Equal(AudioOutputRoute.WasapiShared, host.ActiveRoute);
    Assert.Equal(2, factory.Created.Count);
}

[Fact]
public void RetiredEngineEvents_AreIgnored()
{
    var factory = new FakePlaybackEngineFactory();
    using var host = new MpvPlaybackEngineHost(
        factory,
        AudioOutputPolicy.WasapiSharedId,
        "auto");
    var starts = 0;
    host.PlaybackStarted += (_, _) => starts++;
    var retired = factory.Created[0];
    host.TryFallback("shared failed");

    retired.RaisePlaybackStarted(1, "old");
    factory.Created[1].RaisePlaybackStarted(2, "new");

    Assert.Equal(1, starts);
}
```

- [ ] **Step 3: Run host tests and confirm missing-type failures**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~MpvPlaybackEngineHostTests
```

Expected: compilation fails because the host and failover state do not exist.

- [ ] **Step 4: Implement the pure fallback state**

```csharp
public sealed class AudioOutputFailoverState
{
    private IReadOnlyList<AudioOutputRoute> _routes;
    private int _index;

    public AudioOutputFailoverState(string preferredModeId) => Reset(preferredModeId);
    public string PreferredModeId { get; private set; } = AudioOutputPolicy.WasapiSharedId;
    public AudioOutputRoute ActiveRoute => _routes[_index];
    public string? FallbackReason { get; private set; }

    public void Reset(string preferredModeId)
    {
        PreferredModeId = AudioOutputPolicy.NormalizeModeId(preferredModeId);
        _routes = AudioOutputPolicy.BuildFallbackChain(PreferredModeId);
        _index = 0;
        FallbackReason = null;
    }

    public bool TryAdvance(string reason)
    {
        if (_index + 1 >= _routes.Count)
        {
            return false;
        }

        _index++;
        FallbackReason = reason;
        return true;
    }
}
```

- [ ] **Step 5: Implement the engine host**

The host creates one engine, captures each engine and generation in forwarding delegates, rejects callbacks unless both still match, disposes the current engine before creating a replacement, and publishes route labels through `AudioOutputPolicy.GetRouteDisplayName`.

Use these public members exactly:

```csharp
public IPlaybackEngine Engine { get; private set; }
public long Generation { get; private set; }
public AudioOutputRoute ActiveRoute => _failover.ActiveRoute;
public string ActiveRouteLabel => AudioOutputPolicy.GetRouteDisplayName(ActiveRoute);
public string? FallbackReason => _failover.FallbackReason;
public string PreferredModeId => _failover.PreferredModeId;
public string OutputDevice { get; private set; }
public event EventHandler<PlaybackLoadEventArgs>? PlaybackStarted;
public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;
public event EventHandler? PlaybackEnded;
public event EventHandler? StateChanged;
public bool ResetPreference(string modeId, string outputDevice);
public bool TryFallback(string reason);
```

`ResetPreference` returns false when normalized mode and device are unchanged. Both replacement paths call `Engine.Stop()`, `Engine.Dispose()`, increment `Generation`, create the new engine, subscribe forwarding delegates, and write the preferred/active/device/reason route to `StartupLog`.

- [ ] **Step 6: Run host and policy tests**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter "FullyQualifiedName~MpvPlaybackEngineHostTests|FullyQualifiedName~AudioOutputPolicyTests"
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit the host**

```powershell
git add src/PrismWave.WinUI/Infrastructure/Audio/AudioOutputFailoverState.cs src/PrismWave.WinUI/Infrastructure/Audio/MpvPlaybackEngineHost.cs tests/PrismWave.WinUI.Tests/MpvPlaybackEngineHostTests.cs tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj
git commit -m "feat(winui): add replaceable mpv output host"
```

---

### Task 4: Integrate fallback, watchdog, and snapshot restoration

**Files:**
- Create: `tests/PrismWave.WinUI.Tests/PlaybackServiceAudioOutputStructureTests.cs`
- Modify: `src/PrismWave.WinUI/Services/Implementations/PlaybackService.cs`
- Modify: `src/PrismWave.WinUI/Infrastructure/AppServices.cs`

**Interfaces:**
- Consumes: `MpvPlaybackEngineHost`.
- Preserves: current `IPlaybackService` playback, DSD, online recovery, and queue behavior.
- Adds: a five-second cancellable watchdog for local loads only.

- [ ] **Step 1: Write failing service structure tests**

```csharp
public sealed class PlaybackServiceAudioOutputStructureTests
{
    private static readonly string Source = File.ReadAllText(SourcePath());

    [Fact]
    public void Service_UsesReplaceableHostAndPlaybackStartedEvent()
    {
        Assert.Contains("MpvPlaybackEngineHost", Source, StringComparison.Ordinal);
        Assert.Contains("_mpvHost.PlaybackStarted", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("_mpvEngine.ConfigureOutput", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalLoads_ArmFiveSecondWatchdog()
    {
        Assert.Contains("TimeSpan.FromSeconds(5)", Source, StringComparison.Ordinal);
        Assert.Contains("ArmLocalStartupWatchdog", Source, StringComparison.Ordinal);
        Assert.Contains("CancelLocalStartupWatchdog", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteReplacement_PreservesPositionAndAutoplayIntent()
    {
        Assert.Contains("CaptureMpvPlaybackSnapshot", Source, StringComparison.Ordinal);
        Assert.Contains("RestoreMpvPlaybackSnapshot", Source, StringComparison.Ordinal);
    }

    private static string SourcePath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "src", "PrismWave.WinUI",
        "Services", "Implementations", "PlaybackService.cs"));
}
```

- [ ] **Step 2: Run the structure tests and confirm failure**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~PlaybackServiceAudioOutputStructureTests
```

Expected: the new host, event, watchdog, and snapshot symbols are absent.

- [ ] **Step 3: Replace the concrete engine field with the host**

Change the constructor to accept an optional factory without disturbing production registration:

```csharp
public PlaybackService(
    ISettingsService settingsService,
    IOnlinePlaybackResolver onlinePlaybackResolver,
    IPlaybackEngineFactory? playbackEngineFactory = null)
{
    _settingsService = settingsService;
    _onlinePlaybackResolver = onlinePlaybackResolver;
    _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    var settings = _settingsService.Current;
    _mpvHost = new MpvPlaybackEngineHost(
        playbackEngineFactory ?? new MpvPlaybackEngineFactory(),
        settings.AudioOutputMode,
        settings.AudioOutputDevice);
    _mpvHost.PlaybackStarted += (_, args) => Dispatch(() => HandlePlaybackStarted(args));
    _mpvHost.PlaybackFailed += (_, args) => Dispatch(() => HandleMediaFailed(args.Message, args.LoadSequence, args.SourceKey));
    _mpvHost.PlaybackEnded += (_, _) => Dispatch(HandleMediaEnded);
    _mpvHost.StateChanged += (_, _) => Dispatch(RefreshHostStateWhenReady);
    _dsdEngine.PlaybackEnded += (_, _) => Dispatch(HandleMediaEnded);

    _positionTimer = _dispatcherQueue.CreateTimer();
    _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
    _positionTimer.Tick += (_, _) => RefreshPosition();
    _positionTimer.Start();
}
```

Replace `_mpvEngine` calls with `_mpvHost.Engine`. Do not call `ConfigureOutput` after initialization.

- [ ] **Step 4: Add snapshot-based settings rebuild and fallback**

Use a private record:

```csharp
private sealed record MpvPlaybackSnapshot(
    TrackModel Track,
    double PositionSeconds,
    bool Autoplay);
```

`CaptureMpvPlaybackSnapshot` returns null when there is no non-DSD current track. Otherwise it captures `CurrentTrack`, `Math.Max(PositionSeconds, _mpvHost.Engine.PositionSeconds)`, and `IsPlaying || Status == PlaybackStatus.Buffering`.

`ApplyAudioSettings` captures the snapshot, calls `_mpvHost.ResetPreference`, and restores only when replacement occurred. `RestoreMpvPlaybackSnapshot` sets `_pendingRecoverySeekSeconds` when the saved position is positive and calls `LoadMpvTrack(snapshot.Track, snapshot.Autoplay)`.

Before the existing remote recovery decision in `HandleMediaFailed`, attempt local output fallback:

```csharp
if (!failedTrack.IsRemote && TryFallbackAudioOutput(message, failedTrack, loadContext.Autoplay))
{
    return;
}
```

`TryFallbackAudioOutput` captures the current position, calls `_mpvHost.TryFallback(reason)`, reloads the same track on success, and leaves the preferred setting untouched. When no route remains, it returns false so existing final-failure handling publishes the error.

- [ ] **Step 5: Add and cancel the local startup watchdog**

After a successful `Engine.Load`, arm the watchdog only when `track.IsRemote` is false. Capture load revision, load sequence, source key, host generation, and a fresh cancellation token. The delayed callback must recheck all captured values before calling `TryFallbackAudioOutput`:

```csharp
private async Task WatchLocalStartupAsync(
    int revision,
    long loadSequence,
    string sourceKey,
    long engineGeneration,
    TrackModel track,
    bool autoplay,
    CancellationToken cancellationToken)
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        Dispatch(() =>
        {
            if (revision != _loadRevision
                || engineGeneration != _mpvHost.Generation
                || CurrentTrack?.Id != track.Id
                || !_mpvLoadEventGuard.TryAccept(
                    loadSequence,
                    sourceKey,
                    _loadRevision,
                    OnlinePlaybackCandidateKey.Create(track),
                    out _))
            {
                return;
            }

            TryFallbackAudioOutput("Local playback did not start within 5 seconds.", track, autoplay);
        });
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
    }
}
```

Cancel the watchdog on playback start, stop, switch track, mode rebuild, failure, and disposal. Rename `HandleMediaOpened` to `HandlePlaybackStarted`; only this method clears `IsLoading` and changes `Status` to `Playing` or `Paused`.

- [ ] **Step 6: Run focused tests and build**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter "FullyQualifiedName~PlaybackServiceAudioOutputStructureTests|FullyQualifiedName~MpvPlaybackEngineHostTests|FullyQualifiedName~PlaybackLoadEventGuardTests|FullyQualifiedName~RemotePlaybackRecoveryPolicyTests"
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore
```

Expected: tests pass; build has zero warnings and zero errors.

- [ ] **Step 7: Commit service integration**

```powershell
git add src/PrismWave.WinUI/Services/Implementations/PlaybackService.cs src/PrismWave.WinUI/Infrastructure/AppServices.cs tests/PrismWave.WinUI.Tests/PlaybackServiceAudioOutputStructureTests.cs
git commit -m "fix(winui): rebuild and fallback local audio output"
```

---

### Task 5: Present readable output choices and active-route status

**Files:**
- Create: `tests/PrismWave.WinUI.Tests/AudioOutputSettingsTests.cs`
- Modify: `src/PrismWave.WinUI/Services/Contracts/IPlaybackService.cs`
- Modify: `src/PrismWave.WinUI/Services/Implementations/PlaybackService.cs`
- Modify: `src/PrismWave.WinUI/ViewModels/Settings/SettingsViewModel.cs`
- Modify: `src/PrismWave.WinUI/Views/Settings/SettingsPage.xaml`

**Interfaces:**
- Adds: `IPlaybackService.ActiveAudioOutputModeLabel` and `IPlaybackService.AudioOutputFallbackReason` with safe default interface implementations.
- Binds: typed options by ID while preserving the string in `SettingsSnapshot`.

- [ ] **Step 1: Write failing settings/UI tests**

```csharp
public sealed class AudioOutputSettingsTests
{
    [Fact]
    public void SettingsViewModel_UsesReadableTypedOptions()
    {
        var source = Read("ViewModels", "Settings", "SettingsViewModel.cs");
        Assert.Contains("IReadOnlyList<AudioOutputModeOptionModel>", source, StringComparison.Ordinal);
        Assert.Contains("AudioOutputPolicy.Options", source, StringComparison.Ordinal);
        Assert.Contains("ActiveAudioOutputMode", source, StringComparison.Ordinal);
        Assert.Contains("AudioOutputFallbackReason", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPage_BindsIdButDisplaysReadableName()
    {
        var xaml = Read("Views", "Settings", "SettingsPage.xaml");
        Assert.Contains("DisplayMemberPath=\"DisplayName\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"Id\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding AudioOutputMode, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ActiveAudioOutputMode}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AudioOutputFallbackReason}\"", xaml, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run settings tests and confirm failure**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~AudioOutputSettingsTests
```

Expected: typed-option and active-route assertions fail.

- [ ] **Step 3: Expose active output state**

Add safe defaults to avoid changing unrelated playback fakes:

```csharp
string ActiveAudioOutputModeLabel => string.Empty;
string? AudioOutputFallbackReason => null;
```

Override them in `PlaybackService`:

```csharp
public string ActiveAudioOutputModeLabel => _mpvHost.ActiveRouteLabel;
public string? AudioOutputFallbackReason => _mpvHost.FallbackReason;
```

Raise `StateChanged` after engine replacement and fallback.

- [ ] **Step 4: Bind readable options in the ViewModel and XAML**

Use:

```csharp
public IReadOnlyList<AudioOutputModeOptionModel> AudioOutputModeOptions { get; } =
    AudioOutputPolicy.Options;
public string ActiveAudioOutputMode =>
    string.IsNullOrWhiteSpace(_playbackService.ActiveAudioOutputModeLabel)
        ? AudioOutputPolicy.GetRouteDisplayName(
            AudioOutputPolicy.BuildFallbackChain(AudioOutputMode)[0])
        : _playbackService.ActiveAudioOutputModeLabel;
public string? AudioOutputFallbackReason => _playbackService.AudioOutputFallbackReason;
```

When `AudioOutputMode` changes, also raise `AudioOutputModeDescription`. In `RefreshPlaybackStatus`, raise both active-route properties.

Replace the raw picker with:

```xml
<ComboBox
    Header="Output mode"
    ItemsSource="{Binding AudioOutputModeOptions}"
    DisplayMemberPath="DisplayName"
    SelectedValuePath="Id"
    SelectedValue="{Binding AudioOutputMode, Mode=TwoWay}"
    Width="280"
    HorizontalAlignment="Left" />
<TextBlock Text="{Binding AudioOutputModeDescription}" TextWrapping="Wrap" Style="{StaticResource PrismCaptionTextStyle}" />
<TextBlock Text="{Binding ActiveAudioOutputMode}" FontWeight="SemiBold" />
<TextBlock Text="{Binding AudioOutputFallbackReason}" TextWrapping="Wrap" Foreground="#FFFFB4B4" FontSize="12" />
```

- [ ] **Step 5: Run settings tests and build**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter "FullyQualifiedName~AudioOutputSettingsTests|FullyQualifiedName~SettingsMigrationTests"
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore
```

Expected: tests pass; build has zero warnings and zero errors.

- [ ] **Step 6: Commit settings presentation**

```powershell
git add src/PrismWave.WinUI/Services/Contracts/IPlaybackService.cs src/PrismWave.WinUI/Services/Implementations/PlaybackService.cs src/PrismWave.WinUI/ViewModels/Settings/SettingsViewModel.cs src/PrismWave.WinUI/Views/Settings/SettingsPage.xaml tests/PrismWave.WinUI.Tests/AudioOutputSettingsTests.cs
git commit -m "feat(winui): show preferred and active audio routes"
```

---

### Task 6: Lock sequential local playback and complete verification

**Files:**
- Modify: `tests/PrismWave.WinUI.Tests/BundledLibMpvCodecTests.cs`
- Modify: `ai_handoff.md`

**Interfaces:**
- Consumes: the bundled `native/libmpv-winui/libmpv-2.dll`.
- Verifies: playback restart on repeated `loadfile replace` calls with all audio-only window-suppression options enabled.

- [ ] **Step 1: Add a failing sequential-load integration test**

Extend `LibMpvProbe` to set `sub-auto=no`, `cover-art-auto=no`, `audio-display=no`, `video=no`, and `force-window=no` before initialization. Add:

```csharp
[Fact]
public void BundledLibMpv_SequentialAudioOnlyLoadsRestartWithoutOpeningVideo()
{
    var dllPath = FindRepositoryFile("native", "libmpv-winui", "libmpv-2.dll");
    var mediaPath = WriteEac3Fixture();
    try
    {
        using var probe = new LibMpvProbe(dllPath);
        var first = probe.PlayToNullAudio(mediaPath, TimeSpan.FromSeconds(4));
        var second = probe.PlayToNullAudio(mediaPath, TimeSpan.FromSeconds(4));
        var third = probe.PlayToNullAudio(mediaPath, TimeSpan.FromSeconds(4));

        Assert.True(first.Started, first.Diagnostic);
        Assert.True(second.Started, second.Diagnostic);
        Assert.True(third.Started, third.Diagnostic);
    }
    finally
    {
        File.Delete(mediaPath);
    }
}
```

Refactor the existing fixture-writing code into `WriteEac3Fixture()` so both tests use the exact same media bytes.

- [ ] **Step 2: Run the integration and audio-output tests**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter "FullyQualifiedName~BundledLibMpvCodecTests|FullyQualifiedName~MpvPlaybackEngineHostTests|FullyQualifiedName~AudioOutputPolicyTests"
```

Expected: all selected tests pass and no mpv window is created by the probe.

- [ ] **Step 3: Run the full automated verification**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore
git diff --check
```

Expected: every test passes; build succeeds with zero warnings and zero errors; `git diff --check` prints no errors.

- [ ] **Step 4: Run Demo acceptance on the real Windows audio device**

Launch:

```powershell
dotnet run --project src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-build
```

Verify in this order:

1. Clear or rename the current `audio.outputMode` setting and restart; Settings shows WASAPI shared.
2. Play `爱与诚.m4a`; no mpv-owned window appears, buffering clears, audio and position advance.
3. Click at least five local tracks rapidly; the final selected track starts and no stale title or loading state remains.
4. Play one MP3, FLAC, WAV, M4A, and OGG file from the real library.
5. Select MPV mode; the current track, queue, position, volume, and pause/play intent survive the rebuild.
6. Select WASAPI exclusive; confirm Settings reports the actual active route.
7. Force exclusive failure by selecting an unavailable device ID; observe exclusive → shared → MPV logs.
8. Select WASAPI shared with the unavailable device; observe shared → MPV logs.
9. Return the output device to `auto` and confirm the preferred route starts directly on the next track.

- [ ] **Step 5: Update handoff documentation**

Record the three persisted modes, default shared route, both fallback chains, five-second local watchdog, `playback-restart` readiness boundary, window-suppression options, automated test count, build result, and the manual files/routes tested in `ai_handoff.md`.

- [ ] **Step 6: Commit final verification updates**

```powershell
git add tests/PrismWave.WinUI.Tests/BundledLibMpvCodecTests.cs ai_handoff.md
git commit -m "test(winui): verify local audio route failover"
```

---

## Final Review Checklist

- [ ] Every accepted mode and fallback chain is covered by `AudioOutputPolicyTests`.
- [ ] Engine options are applied before `mpv_initialize`.
- [ ] No runtime `ConfigureOutput` call remains.
- [ ] `MPV_EVENT_FILE_LOADED` does not clear buffering.
- [ ] `MPV_EVENT_PLAYBACK_RESTART` fires exactly one playback-start callback per load.
- [ ] Retired engine events are ignored.
- [ ] The local watchdog is canceled on every terminal load transition.
- [ ] Temporary fallback does not overwrite the preferred setting.
- [ ] DSD and online candidate recovery tests still pass.
- [ ] Unrelated Flutter changes are not staged or committed.
