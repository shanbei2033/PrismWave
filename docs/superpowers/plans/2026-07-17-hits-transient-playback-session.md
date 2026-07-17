# HITS Transient Playback Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make HITS a temporary playback session that never replaces the bottom player bar, stops on every exit path, restores the pre-HITS track/queue/position/play intent, and removes the title divider.

**Architecture:** `PlaybackService` remains the only audio-engine owner and additionally implements a narrow `IHitsPlaybackSession`. A token/revision guard freezes the primary public session while the same MPV host temporarily plays HITS through WASAPI shared; ending HITS stops the transient load, restores the user's output route, reloads the captured primary session, and only then republishes primary state. `HitsStatusViewModel` depends only on the HITS session interface.

**Tech Stack:** C# 13, .NET 10, WinUI 3, CommunityToolkit.Mvvm, libmpv, xUnit.

## Global Constraints

- Keep exactly one `MpvPlaybackEngineHost`; do not create a second `PlaybackService` or a second persistent audio host.
- HITS may temporarily select `wasapi_shared`, but must not call `ISettingsService.SaveAsync` or alter persisted audio settings.
- During HITS, `IPlaybackService.CurrentTrack`, `Queue`, `Mode`, and player-bar presentation stay on the pre-HITS session.
- Restore the original position and play intent: playing resumes, paused stays paused, no track returns to `Idle`.
- All exit paths are idempotent and stale callbacks are rejected by revision.
- Use test-first red/green cycles for every behavior change.
- Preserve unrelated Flutter workspace changes and merge locally into `WinUI`; do not push.

---

## File Map

**Create**

- `src/PrismWave.WinUI/Models/PlaybackSessionModels.cs` — immutable primary-session snapshot.
- `src/PrismWave.WinUI/Services/Contracts/IHitsPlaybackSession.cs` — HITS-only playback surface.
- `src/PrismWave.WinUI/Infrastructure/Audio/TransientPlaybackSessionGuard.cs` — token/revision and idempotent snapshot ownership.
- `src/PrismWave.WinUI/Services/Implementations/PlaybackService.HitsSession.cs` — transient HITS implementation using the existing MPV host.
- `tests/PrismWave.WinUI.Tests/TransientPlaybackSessionGuardTests.cs` — pure token/snapshot tests.
- `tests/PrismWave.WinUI.Tests/HitsPlaybackIsolationTests.cs` — HITS VM and player-bar isolation behavior.

**Modify**

- `tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj` — link new testable production files.
- `src/PrismWave.WinUI/Services/Implementations/PlaybackService.cs` — partial class, callback routing, primary-operation gate, restore hooks.
- `src/PrismWave.WinUI/ViewModels/Hits/HitsStatusViewModel.cs` — use `IHitsPlaybackSession`, add idempotent end command, remove settings writes.
- `src/PrismWave.WinUI/Infrastructure/AppServices.cs` — register the same `PlaybackService` under both interfaces.
- `src/PrismWave.WinUI/Views/Hits/HitsStatusPage.xaml.cs` — unload cleanup.
- `src/PrismWave.WinUI/Views/Hits/HitsStatusPage.xaml` — remove title divider.
- `src/PrismWave.WinUI/Views/Shell/ShellPage.xaml.cs` — end HITS before the bar becomes interactive.
- `tests/PrismWave.WinUI.Tests/HitsServiceTests.cs` — replace shared-player fake with HITS-session fake.
- `tests/PrismWave.WinUI.Tests/HitsImmersivePageStructureTests.cs` — divider and exit-path regression tests.
- `tests/PrismWave.WinUI.Tests/PlaybackServiceAudioOutputStructureTests.cs` — one-host and temporary-route assertions.

---

### Task 1: Add the session snapshot, HITS contract, and revision guard

**Files:**
- Create: `src/PrismWave.WinUI/Models/PlaybackSessionModels.cs`
- Create: `src/PrismWave.WinUI/Services/Contracts/IHitsPlaybackSession.cs`
- Create: `src/PrismWave.WinUI/Infrastructure/Audio/TransientPlaybackSessionGuard.cs`
- Create: `tests/PrismWave.WinUI.Tests/TransientPlaybackSessionGuardTests.cs`
- Modify: `tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj`

**Interfaces:**
- Produces: `PlaybackSessionSnapshot`, `IHitsPlaybackSession`, `TransientPlaybackSessionGuard.Begin`, `IsCurrent`, and `TryEnd`.

- [ ] **Step 1: Link the future files and write failing guard tests**

```csharp
[Fact]
public void Begin_ClonesQueueAndOwnsOneActiveRevision()
{
    var queue = new List<TrackModel> { Track("main") };
    var snapshot = new PlaybackSessionSnapshot(queue[0], queue, PlaybackMode.Shuffle, 42, 180, true);
    var guard = new TransientPlaybackSessionGuard();

    var revision = guard.Begin(snapshot);
    queue.Clear();

    Assert.True(guard.IsCurrent(revision));
    Assert.Single(guard.Snapshot!.Queue);
}

[Fact]
public void TryEnd_IsIdempotentAndRejectsStaleRevision()
{
    var guard = new TransientPlaybackSessionGuard();
    var first = guard.Begin(Snapshot("first"));
    var second = guard.Begin(Snapshot("second"));

    Assert.False(guard.TryEnd(first, out _));
    Assert.True(guard.TryEnd(second, out var restored));
    Assert.Equal("second", restored!.Track!.Id);
    Assert.False(guard.TryEnd(second, out _));
}
```

- [ ] **Step 2: Run the guard tests and verify RED**

Run:

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~TransientPlaybackSessionGuardTests
```

Expected: compilation fails because the snapshot, interface, and guard do not exist.

- [ ] **Step 3: Add the exact contracts and minimal guard**

```csharp
public sealed record PlaybackSessionSnapshot(
    TrackModel? Track,
    IReadOnlyList<TrackModel> Queue,
    PlaybackMode Mode,
    double PositionSeconds,
    double DurationSeconds,
    bool ShouldResume);

public interface IHitsPlaybackSession
{
    bool IsActive { get; }
    TrackModel? CurrentTrack { get; }
    double PositionSeconds { get; }
    double DurationSeconds { get; }
    bool IsLoading { get; }
    bool IsPlaying { get; }
    string? Error { get; }
    event EventHandler? StateChanged;
    long Begin();
    void Play(TrackModel track);
    void Pause();
    void Resume();
    void Seek(double seconds);
    void Stop();
    void End();
}

public sealed class TransientPlaybackSessionGuard
{
    private long _revision;
    public long ActiveRevision { get; private set; }
    public PlaybackSessionSnapshot? Snapshot { get; private set; }

    public long Begin(PlaybackSessionSnapshot snapshot)
    {
        Snapshot = snapshot with { Queue = snapshot.Queue.ToArray() };
        ActiveRevision = ++_revision;
        return ActiveRevision;
    }

    public bool IsCurrent(long revision) => revision != 0 && revision == ActiveRevision;

    public bool TryEnd(long revision, out PlaybackSessionSnapshot? snapshot)
    {
        snapshot = null;
        if (!IsCurrent(revision) || Snapshot is null)
        {
            return false;
        }

        snapshot = Snapshot;
        Snapshot = null;
        ActiveRevision = 0;
        _revision++;
        return true;
    }
}
```

- [ ] **Step 4: Run tests and verify GREEN**

Run the Task 1 filter. Expected: all `TransientPlaybackSessionGuardTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add src/PrismWave.WinUI/Models/PlaybackSessionModels.cs src/PrismWave.WinUI/Services/Contracts/IHitsPlaybackSession.cs src/PrismWave.WinUI/Infrastructure/Audio/TransientPlaybackSessionGuard.cs tests/PrismWave.WinUI.Tests/TransientPlaybackSessionGuardTests.cs tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj
git commit -m "feat(winui): add transient playback session contracts"
```

### Task 2: Route HITS through the existing engine without mutating primary state

**Files:**
- Create: `src/PrismWave.WinUI/Services/Implementations/PlaybackService.HitsSession.cs`
- Modify: `src/PrismWave.WinUI/Services/Implementations/PlaybackService.cs`
- Modify: `tests/PrismWave.WinUI.Tests/PlaybackServiceAudioOutputStructureTests.cs`

**Interfaces:**
- Consumes: `IHitsPlaybackSession`, `PlaybackSessionSnapshot`, `TransientPlaybackSessionGuard`.
- Produces: `PlaybackService : IPlaybackService, IHitsPlaybackSession` with exactly one MPV host.

- [ ] **Step 1: Write failing structure tests for one-host transient routing**

```csharp
[Fact]
public void HitsSession_UsesExistingHostAndFreezesPrimaryState()
{
    var main = Read("Services", "Implementations", "PlaybackService.cs");
    var hits = Read("Services", "Implementations", "PlaybackService.HitsSession.cs");

    Assert.Contains("partial class PlaybackService", main);
    Assert.Contains("IHitsPlaybackSession", main);
    Assert.Contains("CapturePrimaryPlaybackSession", hits);
    Assert.Contains("_mpvHost.ResetPreference(\"wasapi_shared\"", hits);
    Assert.Contains("RestorePrimaryPlaybackSession", hits);
    Assert.DoesNotContain("new MpvPlaybackEngineHost", hits);
    Assert.DoesNotContain("SaveAsync", hits);
}

[Fact]
public void HostCallbacks_AreRoutedByTransientRevision()
{
    var main = Read("Services", "Implementations", "PlaybackService.cs");
    Assert.Contains("TryHandleHitsPlaybackStarted", main);
    Assert.Contains("TryHandleHitsPlaybackFailed", main);
    Assert.Contains("TryHandleHitsPlaybackEnded", main);
    Assert.Contains("TryRefreshHitsPosition", main);
}
```

- [ ] **Step 2: Run the structure tests and verify RED**

Run:

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~PlaybackServiceAudioOutputStructureTests
```

Expected: assertions fail because no HITS partial exists and callbacks are not routed.

- [ ] **Step 3: Implement the HITS partial and minimal hooks**

The partial owns separate HITS fields and explicit interface operations:

```csharp
private readonly TransientPlaybackSessionGuard _hitsSessionGuard = new();
private readonly PlaybackLoadEventGuard _hitsLoadEventGuard = new();
private EventHandler? _hitsStateChanged;
private TrackModel? _hitsTrack;
private long _hitsRevision;
private bool _hitsIsLoading;
private bool _hitsIsPlaying;
private double _hitsPositionSeconds;
private double _hitsDurationSeconds;
private string? _hitsError;

long IHitsPlaybackSession.Begin() => BeginHitsSession();
void IHitsPlaybackSession.Play(TrackModel track) => PlayHitsTrack(track);
void IHitsPlaybackSession.Pause() => PauseHitsPlayback();
void IHitsPlaybackSession.Resume() => ResumeHitsPlayback();
void IHitsPlaybackSession.Seek(double seconds) => SeekHitsPlayback(seconds);
void IHitsPlaybackSession.Stop() => StopHitsPlayback(clearTrack: true);
void IHitsPlaybackSession.End() => EndHitsSession();
```

`BeginHitsSession` captures the primary state, marks the guard active before stopping old engines, cancels outstanding primary loads, stops MPV/DSD, switches the existing host to shared mode, and leaves all public primary properties unchanged. `PlayHitsTrack` calls `_mpvHost.Engine.Load` directly with `_hitsLoadEventGuard`, never `Play`.

`EndHitsSession` performs this order exactly:

```csharp
private void EndHitsSession()
{
    var revision = _hitsRevision;
    if (!_hitsSessionGuard.IsCurrent(revision)) return;
    StopHitsPlayback(clearTrack: true);
    if (!_hitsSessionGuard.TryEnd(revision, out var snapshot) || snapshot is null) return;
    var settings = _settingsService.Current;
    _mpvHost.ResetPreference(settings.AudioOutputMode, settings.AudioOutputDevice);
    RestorePrimaryPlaybackSession(snapshot);
}
```

Main callbacks return immediately when their HITS counterpart accepts the active revision. Public mutating commands return without changing primary fields while HITS is active. Restore applies `_pendingRecoverySeekSeconds`; the DSD load branch consumes it with `_dsdEngine.Seek` before publishing state.

- [ ] **Step 4: Run structure tests and x64 compile**

Run:

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~PlaybackServiceAudioOutputStructureTests
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore
```

Expected: structure tests pass; build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```powershell
git add src/PrismWave.WinUI/Services/Implementations/PlaybackService.cs src/PrismWave.WinUI/Services/Implementations/PlaybackService.HitsSession.cs tests/PrismWave.WinUI.Tests/PlaybackServiceAudioOutputStructureTests.cs
git commit -m "feat(winui): isolate HITS in a transient playback session"
```

### Task 3: Move HITS ViewModel off the global player bar

**Files:**
- Create: `tests/PrismWave.WinUI.Tests/HitsPlaybackIsolationTests.cs`
- Modify: `src/PrismWave.WinUI/ViewModels/Hits/HitsStatusViewModel.cs`
- Modify: `src/PrismWave.WinUI/Infrastructure/AppServices.cs`
- Modify: `tests/PrismWave.WinUI.Tests/HitsServiceTests.cs`

**Interfaces:**
- Consumes: `IHitsPlaybackSession`.
- Produces: `EndHitsSessionCommand` and HITS-only state changes.

- [ ] **Step 1: Write failing ViewModel isolation tests**

```csharp
[Fact]
public async Task PrepareAndEnd_UsesOnlyHitsSessionAndRestoresInactiveState()
{
    var session = new FakeHitsPlaybackSession();
    var vm = new HitsStatusViewModel(ReadyHitsService(), session);

    await vm.PrepareHitsSessionCommand.ExecuteAsync(null);
    vm.EndHitsSessionCommand.Execute(null);

    Assert.Equal(1, session.BeginCount);
    Assert.Equal(1, session.PlayCount);
    Assert.Equal(1, session.EndCount);
    Assert.False(vm.IsSessionActive);
    Assert.False(vm.IsPaused);
}

[Fact]
public async Task HitsPlayback_DoesNotChangePrimaryPlayerPresentation()
{
    var primary = new FakePrimaryPlaybackService(Track("main"));
    var playerBar = CreatePlaybackViewModel(primary);
    var hits = new HitsStatusViewModel(ReadyHitsService(), new FakeHitsPlaybackSession());

    await hits.PrepareHitsSessionCommand.ExecuteAsync(null);

    Assert.Equal("main", primary.CurrentTrack!.Id);
    Assert.Equal("Main song", playerBar.CurrentTitle);
}
```

- [ ] **Step 2: Run HITS tests and verify RED**

Run:

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter "FullyQualifiedName~HitsPlaybackIsolationTests|FullyQualifiedName~HitsServiceTests"
```

Expected: compilation fails because `HitsStatusViewModel` still requires `IPlaybackService` and settings.

- [ ] **Step 3: Replace the dependencies and add the end command**

Constructor:

```csharp
public HitsStatusViewModel(IHitsService hitsService, IHitsPlaybackSession playbackSession)
{
    _hitsService = hitsService;
    _playbackSession = playbackSession;
    _hitsService.StateChanged += (_, _) => RefreshState();
    _playbackSession.StateChanged += (_, _) => HandlePlaybackStateChanged();
    RefreshState();
}
```

Session lifecycle:

```csharp
private Task PrepareHitsSessionAsync()
{
    if (!IsAvailable || CurrentTrack is null) return Task.CompletedTask;
    _playbackSession.Begin();
    IsSessionActive = true;
    IsPaused = false;
    SyncPlayback(forceReload: true);
    return Task.CompletedTask;
}

[RelayCommand]
private void EndHitsSession()
{
    ClearPendingSeek();
    _playbackSession.End();
    IsSessionActive = false;
    IsPaused = false;
    _isResynchronizing = false;
    NotifyPresentationStateChanged();
}
```

Replace toggle calls with explicit `Pause`/`Resume`; replace every HITS `Play`, `Seek`, and `Stop` with the HITS session. Delete both settings-save blocks and the settings dependency.

Register the same object under both interfaces:

```csharp
var playbackService = new PlaybackService(settingsService, onlinePlaybackResolver);
IHitsPlaybackSession hitsPlaybackSession = playbackService;
var hits = new HitsStatusViewModel(hitsService, hitsPlaybackSession);
```

- [ ] **Step 4: Run HITS tests and verify GREEN**

Run the Task 3 filter. Expected: all HITS service/isolation tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/PrismWave.WinUI/ViewModels/Hits/HitsStatusViewModel.cs src/PrismWave.WinUI/Infrastructure/AppServices.cs tests/PrismWave.WinUI.Tests/HitsServiceTests.cs tests/PrismWave.WinUI.Tests/HitsPlaybackIsolationTests.cs
git commit -m "fix(winui): keep HITS out of the player bar"
```

### Task 4: End HITS on every exit path and remove the divider

**Files:**
- Modify: `src/PrismWave.WinUI/Views/Hits/HitsStatusPage.xaml`
- Modify: `src/PrismWave.WinUI/Views/Hits/HitsStatusPage.xaml.cs`
- Modify: `src/PrismWave.WinUI/Views/Shell/ShellPage.xaml.cs`
- Modify: `tests/PrismWave.WinUI.Tests/HitsImmersivePageStructureTests.cs`

**Interfaces:**
- Consumes: `HitsStatusViewModel.EndHitsSessionCommand`.
- Produces: cleanup before the player bar becomes interactive plus unload fallback.

- [ ] **Step 1: Write failing XAML/lifecycle tests**

```csharp
[Fact]
public void TitleBar_HasNoBottomDivider()
{
    var xaml = XDocument.Parse(Read("Views", "Hits", "HitsStatusPage.xaml"));
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var titleBar = xaml.Descendants().Single(node => (string?)node.Attribute(x + "Name") == "HitsTitleBar");
    Assert.Null(titleBar.Attribute("BorderBrush"));
    Assert.Null(titleBar.Attribute("BorderThickness"));
}

[Fact]
public void ShellAndPage_EndHitsSessionBeforePlayerBarReturns()
{
    var page = Read("Views", "Hits", "HitsStatusPage.xaml.cs");
    var shell = Read("Views", "Shell", "ShellPage.xaml.cs");
    Assert.Contains("EndHitsSessionCommand.Execute", page);
    Assert.Contains("EndHitsSessionIfNeeded();", shell);
    Assert.True(
        shell.IndexOf("EndHitsSessionIfNeeded();", StringComparison.Ordinal) <
        shell.IndexOf("ShellBottomPlayerBar.IsHitTestVisible = true", StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run the structure tests and verify RED**

Run:

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter FullyQualifiedName~HitsImmersivePageStructureTests
```

Expected: divider and lifecycle assertions fail.

- [ ] **Step 3: Apply the minimal UI and lifecycle changes**

Remove from `HitsTitleBar`:

```xml
BorderBrush="#20FFFFFF"
BorderThickness="0,0,0,1"
```

Add unload fallback:

```csharp
ViewModel.EndHitsSessionCommand.Execute(null);
```

Add to Shell and call at the start of both hide and reset:

```csharp
private void EndHitsSessionIfNeeded()
{
    if (string.Equals(_immersiveRoute, "Hits", StringComparison.Ordinal))
    {
        App.Services.Hits.EndHitsSessionCommand.Execute(null);
    }
}
```

- [ ] **Step 4: Run structure tests and build**

Run the Task 4 filter, then the x64 build. Expected: tests pass and build has 0 errors.

- [ ] **Step 5: Commit**

```powershell
git add src/PrismWave.WinUI/Views/Hits/HitsStatusPage.xaml src/PrismWave.WinUI/Views/Hits/HitsStatusPage.xaml.cs src/PrismWave.WinUI/Views/Shell/ShellPage.xaml.cs tests/PrismWave.WinUI.Tests/HitsImmersivePageStructureTests.cs
git commit -m "fix(winui): stop HITS when leaving immersive mode"
```

### Task 5: Full verification, local merge, and Demo acceptance

**Files:**
- Verify all files changed above.

**Interfaces:**
- Consumes: completed Tasks 1–4.
- Produces: verified `WinUI` merge and an open Demo on HITS/Home acceptance flow.

- [ ] **Step 1: Run fresh full verification**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore
git diff --check
git status --short
```

Expected: every test passes; build reports 0 warnings and 0 errors; no whitespace errors; only plan-scope files differ.

- [ ] **Step 2: Review the cumulative diff against the design**

Confirm all of these from code and tests:

```text
one MPV host; HITS never calls global Play; primary identity/queue frozen;
playing resumes; paused stays paused; no-track returns idle;
no settings save; stale revision rejected; End is idempotent;
Back/Esc/forced navigation/unload clean up; title divider absent.
```

- [ ] **Step 3: Commit any final test-only corrections**

```powershell
git add src/PrismWave.WinUI tests/PrismWave.WinUI.Tests docs/superpowers
git commit -m "test(winui): verify isolated HITS lifecycle"
```

Skip this commit when `git status --short` is already clean.

- [ ] **Step 4: Merge locally into `WinUI` and verify again**

```powershell
git -C D:\Project\PrismWave merge --no-ff codex/winui-hits-session-isolation -m "Merge isolated HITS playback session"
dotnet test D:\Project\PrismWave\tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore
dotnet build D:\Project\PrismWave\src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore
```

Expected: merge succeeds without touching unrelated Flutter changes; merged tests/build pass.

- [ ] **Step 5: Open and leave the Demo running**

Acceptance sequence:

```text
1. Play a Home track and note title/cover/position.
2. Enter HITS; confirm HITS audio plays and the hidden primary presentation is not replaced.
3. Exit HITS; confirm HITS stops and the same Home track resumes near its captured position.
4. Pause the Home track, enter/exit HITS, and confirm it remains paused.
5. Re-enter/exit rapidly and confirm no HITS audio or stale cover remains.
6. Confirm the title divider is absent.
```

Launch:

```powershell
dotnet run --project src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-build
```

Leave the final Demo window open for the user.

