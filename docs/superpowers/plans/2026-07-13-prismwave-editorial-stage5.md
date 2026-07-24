# PrismWave Editorial Feature Stage 5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace only the existing `streamable-now` card strip with one responsive editorial feature using the same tracks and playback command.

**Architecture:** `HomeViewModel` selects `streamable-now` as `EditorialSection` and excludes it from `FollowingSections`. A dedicated `EditorialFeature` control derives its title, count, first-track artwork, and featured-track copy from that section, then presents a wide media/copy spread that stacks vertically below 760 control pixels.

**Tech Stack:** WinUI 3 XAML, dependency properties, Grid VisualStates, CommunityToolkit relay commands, xUnit XAML/source contract tests, Windows UI Automation.

## Constraints

- Modify only the editorial projection and section.
- Preserve the source section, track order, first-track playback, and full section queue behavior.
- Do not modify Hero, global ranking, navigation, genre sections, or bottom player.
- Use one large real cover and a separate readable copy column; do not overlay text on artwork.
- Do not wrap the whole feature in an outlined card or add a card collection.
- No Canvas, negative Margin, fixed total width, gradient, glow, or unrelated data changes.
- Wide layout: artwork left, copy right. Compact layout: artwork above copy.
- Save the accepted runtime screenshot as `docs/ui-review/06-editorial-section.png`.
- Stop before Stage 6.

---

### Task 1: Add failing editorial contracts

- [x] Add `EditorialFeatureXamlTests.cs` for content hierarchy, unframed structure, command binding, and responsive states.
- [x] Assert `HomePage` mounts the feature between ranking and remaining sections.
- [x] Assert `HomeViewModel` projects `streamable-now` and excludes it from `FollowingSections` without removing canonical `Sections`.
- [x] Run focused tests and confirm red state.

### Task 2: Add the isolated editorial projection

- [x] Add an `EditorialSection` observable property with an empty fallback.
- [x] Select `streamable-now`, falling back to the first non-global section.
- [x] Keep canonical `Sections` intact and omit global/editorial only from the generic following renderer.

### Task 3: Build the responsive editorial feature

- [x] Add `EditorialFeature.xaml` and code-behind with `Section` and `PlayCommand` dependency properties.
- [x] Derive title, count, featured title/artist, cover, and command parameter from the existing section.
- [x] Build an unframed two-column Grid with a 300x220 media area and separate copy hierarchy.
- [x] Attach Wide/Compact states to direct `LayoutRoot`; stack the copy below artwork below 760 control pixels.
- [x] Mount the feature in `HomePage` before `FollowingSections`.

### Task 4: Verify and visually accept Stage 5

- [x] Run focused tests and x64 build with zero warnings/errors.
- [x] Launch Home, scroll to editorial, and inspect the wide layout.
- [x] Inspect a narrow window and confirm vertical stacking without text/art overlap.
- [x] Save and inspect `06-editorial-section.png`.
- [x] Run the complete test suite and stop before Stage 6.
