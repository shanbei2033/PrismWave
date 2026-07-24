# Home Playlist Cover Consistency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Synchronize the playback bar's resolved cover to every Home online playlist entry with the same normalized title and artist.

**Architecture:** `HomeViewModel` owns a session-scoped cover override map keyed by normalized title and artist. It consumes the same `ICoverService.ResolveCoverPath` result as `PlaybackViewModel`, reapplies overrides whenever playback or cover state changes, and rebuilds immutable Home sections plus their derived collections so XAML bindings refresh.

**Tech Stack:** C# 13, .NET 10, CommunityToolkit.Mvvm, WinUI 3, xUnit

## Global Constraints

- Match only when normalized title and normalized artist both match.
- Synchronize Home online playlists and Home-loaded online album tracks only.
- Never overwrite local library or user-managed local covers.
- Do not issue network requests during synchronization.
- Preserve immutable `HomeTrackModel` records.

---

### Task 1: Cover synchronization behavior

**Files:**
- Modify: `tests/PrismWave.WinUI.Tests/HomeViewModelPlaybackTests.cs`
- Modify: `src/PrismWave.WinUI/ViewModels/Home/HomeViewModel.cs`

**Interfaces:**
- Consumes: `IPlaybackService.StateChanged`, `IPlaybackService.CurrentTrack`, `ICoverService.ResolveCoverPath(TrackModel)` and `ICoverService.CoverChanged`
- Produces: a `HomeViewModel(IOnlineHomeService, IPlaybackService, ICoverService)` constructor and synchronized `TopPlaylist`, `Sections`, `SelectedPlaylist`, `GlobalTrendingTracks`, `ChannelSections`, `GenreSections`, and `SelectedAlbumTracks`

- [ ] **Step 1: Write failing same-song synchronization tests**

Add tests that construct duplicate `Mr. Brightside` entries in `global-hot` and `style-rock`, plus a same-title entry by another artist. Publish a resolved remote `TrackModel` through the fake playback service and resolve `https://cover.test/bar.jpg` through a fake cover service. Assert both The Killers entries use the bar cover while the other artist keeps its original cover.

```csharp
[Fact]
public void PlaybackCover_SynchronizesSameTitleAndArtistAcrossHomePlaylists()
{
    // global and rock copies: Mr. Brightside / The Killers
    // control copy: Mr. Brightside / Another Artist
    // publish remote playback state with resolved bar cover
    // assert matching copies changed and control copy did not
}
```

- [ ] **Step 2: Write failing refresh and local-track tests**

Add one test that raises `HomeChanged` with raw service covers after synchronization and asserts the override is reapplied. Add one test that publishes a local track and asserts no Home cover changes.

- [ ] **Step 3: Run targeted tests and verify RED**

Run:

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore --filter "FullyQualifiedName~HomeViewModelPlaybackTests"
```

Expected: the new synchronization assertions fail because `HomeViewModel` neither receives `ICoverService` nor reacts to playback cover changes.

- [ ] **Step 4: Implement the title-and-artist override map**

In `HomeViewModel`, inject `ICoverService`, subscribe to playback and cover events, and add a private normalized key:

```csharp
private readonly record struct TrackCoverKey(string Title, string Artist);

private static bool TryCreateCoverKey(string title, string artist, out TrackCoverKey key)
{
    var normalizedTitle = NormalizeIdentityText(title);
    var normalizedArtist = NormalizeIdentityText(artist);
    key = new TrackCoverKey(normalizedTitle, normalizedArtist);
    return normalizedTitle.Length > 0 && normalizedArtist.Length > 0;
}
```

`SynchronizeCurrentTrackCover` must ignore null/local tracks and blank resolved covers, then store the cover and call `ApplyCoverOverrides`.

- [ ] **Step 5: Rebuild immutable Home model graphs**

Add helpers that return `track with { CoverUrl = overridePath }` only for matching keys, rebuild every `HomeSectionModel`, restore `SelectedPlaylist` by section ID, update `SelectedAlbumTracks`, and rebuild derived Home collections from the synchronized `TopPlaylist` and `Sections`.

Call the same helpers from `RefreshFromService` and after online album tracks load so session overrides survive refreshes.

- [ ] **Step 6: Run targeted tests and verify GREEN**

Run the command from Step 3. Expected: every `HomeViewModelPlaybackTests` test passes.

### Task 2: Application wiring and end-to-end verification

**Files:**
- Modify: `src/PrismWave.WinUI/Infrastructure/AppServices.cs`
- Verify: `src/PrismWave.WinUI/Views/Home/TopPlaylistPage.xaml`
- Verify: `tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj`

**Interfaces:**
- Consumes: the three-argument `HomeViewModel` constructor from Task 1
- Produces: production dependency injection using the existing shared `CoverService` instance

- [ ] **Step 1: Wire the shared cover service**

Change application composition to:

```csharp
var home = new HomeViewModel(onlineHomeService, playbackService, coverService);
```

Do not create a second cover service.

- [ ] **Step 2: Run the complete automated suite**

```powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore
```

Expected: zero failed tests.

- [ ] **Step 3: Build the x64 WinUI application**

```powershell
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-restore
```

Expected: build succeeds with zero warnings and zero errors.

- [ ] **Step 4: Verify the Demo visually**

Launch the latest build, open Home → Rock, click `Play all`, wait for provider resolution, and compare the first-row cover with the playback-bar cover. Both must render the same resolved image. Navigate away and back to Rock and confirm the synchronized cover remains.

- [ ] **Step 5: Preserve the existing worktree**

Inspect `git status --short` and report only files changed for this task. Do not stage or commit implementation files because the WinUI source tree contains existing untracked user work that must remain untouched.
