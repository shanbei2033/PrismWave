# PrismWave Daily Home Rotation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make each generated home edition prefer songs absent from the entire previous day's home and reduce reuse from the preceding seven days.

**Architecture:** Implement rotation in the separate `prismwave-hits` generator. Existing dated schema 8 archives are deterministic history; selection excludes yesterday first and penalizes reuse from days 2-7. Only Top 100 may reuse the minimum number of yesterday tracks when required; sections publish fewer than 20 tracks instead of repeating yesterday.

**Tech Stack:** Python 3.11, `unittest`, GitHub Actions, schema 8 JSON.

## Global Constraints

- Do not change PrismWave WinUI or Flutter client behavior.
- Keep schema version 8 and add only the optional `rotationSnapshot` root field.
- Preserve source ranking, artist caps, spacing, direct-play filtering, and deterministic date seeding.
- Use `home/home_recommendations-YYYY-MM-DD.json` as the only history store.
- Normal candidate volume must produce zero overlap with the entire previous day's home.

---

### Task 1: Prepare the Generator Repository

**Files:**
- Repository: `D:/Project/prismwave-hits`
- Branch: `codex/daily-home-rotation`

**Interfaces:**
- Consumes: `shanbei2033/prismwave-hits` main branch.
- Produces: isolated generator branch with a passing baseline.

- [ ] Clone the repository and create `codex/daily-home-rotation`.
- [ ] Run `python -m unittest discover -s tests -v` and record the passing baseline.

### Task 2: Add Rotation-History API with TDD

**Files:**
- Modify: `D:/Project/prismwave-hits/tests/test_build_home_diversity.py`
- Modify: `D:/Project/prismwave-hits/scripts/build_home.py`

**Interfaces:**
- Produces: `RotationHistory`, `payload_track_identities(payload)`, and `load_rotation_history(home_dir, edition_date, history_days=7)`.

- [ ] Write failing tests proving yesterday and days 2-7 are loaded from `topPlaylist.tracks` plus every `sections[].tracks`.
- [ ] Write a failing test proving missing, malformed, and schema < 8 archives are ignored.
- [ ] Run the focused tests and verify failure is caused by the missing API.
- [ ] Implement:

```python
@dataclass(frozen=True)
class RotationHistory:
    yesterday_keys: frozenset[str]
    recent_age_by_key: dict[str, int]
    history_days_loaded: int
```

- [ ] Re-run focused tests and verify PASS.

### Task 3: Add Fresh-First Selection with TDD

**Files:**
- Modify: `D:/Project/prismwave-hits/tests/test_build_home_diversity.py`
- Modify: `D:/Project/prismwave-hits/scripts/build_home.py`

**Interfaces:**
- Extends: `build_diverse_playlist(..., yesterday_track_keys=None, recent_age_by_key=None)`.

- [ ] Write a failing test proving zero yesterday overlap when at least `limit` fresh candidates satisfy artist constraints.
- [ ] Write a failing test proving only the exact shortage is filled from yesterday.
- [ ] Write a failing test proving age 2 has a larger penalty than age 7.
- [ ] Run tests and verify RED.
- [ ] Extend `build_diverse_playlist` to partition fresh and yesterday candidates, select fresh first, and continue from yesterday only for a shortage.
- [ ] Add `allow_yesterday_fallback`; keep it enabled for Top 100 and disable it for all source/style sections.
- [ ] Subtract these penalties inside `diverse_candidate_score`: day 2 `0.24`, day 3 `0.18`, day 4 `0.13`, day 5 `0.09`, day 6 `0.05`, day 7 `0.02`.
- [ ] Run focused and full tests and verify GREEN.

### Task 4: Apply Rotation to Every Home Module

**Files:**
- Modify: `D:/Project/prismwave-hits/tests/test_build_home_diversity.py`
- Modify: `D:/Project/prismwave-hits/scripts/build_home.py`

**Interfaces:**
- Consumes: one `RotationHistory` loaded by `main()`.
- Produces: rotated Top 100, source sections, style sections, and `rotationSnapshot`.

- [ ] Write failing tests for Top 100, `global-hot`, `streamable-now`, all channel sections, and every `style-*` section.
- [ ] Assert `streamable-now` still contains only tracks with `audioUrl`.
- [ ] Run module tests and verify RED.
- [ ] Pass history into `build_diverse_playlist` from top, source-section, and style-section builders without changing their existing predicates.
- [ ] Add optional diagnostics:

```json
{
  "historyDaysLoaded": 7,
  "yesterdayTrackCount": 286,
  "previousDayOverlapCount": 0,
  "recentReuseCount": 18,
  "fallbackReuseCount": 0
}
```

- [ ] Run module and full tests and verify GREEN.

### Task 5: Verify Consecutive Editions

**Files:**
- Modify: `D:/Project/prismwave-hits/tests/test_build_home_diversity.py`

**Interfaces:**
- Produces: deterministic two-day regression coverage.

- [ ] Generate a synthetic day-one payload, save it as yesterday's archive, and generate day two from the same candidate pool.
- [ ] Assert zero overlap for `topPlaylist`, `global-hot`, `streamable-now`, `world-charts`, `listener-trends`, `audius-trending`, and all `style-*` sections.
- [ ] Run `python -m unittest discover -s tests -v`.
- [ ] Run `python -m py_compile scripts/build_home.py scripts/build_hits.py tests/test_build_home_diversity.py`.
- [ ] Run `git diff --check` and review only generator/test changes.

### Task 6: Commit and Verify the Consumer

**Files:**
- Commit: `D:/Project/prismwave-hits/scripts/build_home.py`
- Commit: `D:/Project/prismwave-hits/tests/test_build_home_diversity.py`
- Verify: `D:/Project/PrismWave/src/PrismWave.WinUI/Services/Implementations/SampleOnlineHomeService.cs`

**Interfaces:**
- Produces: generator commit ready to push and a locally launchable WinUI consumer.

- [ ] Commit generator changes as `feat: rotate daily home recommendations`.
- [ ] Run `dotnet test tests/PrismWave.WinUI.Tests/PrismWave.WinUI.Tests.csproj -p:Platform=x64` in the PrismWave workspace.
- [ ] Run `dotnet build src/PrismWave.WinUI/PrismWave.WinUI.csproj -p:Platform=x64`.
- [ ] Launch the existing WinUI demo after all verification succeeds.
