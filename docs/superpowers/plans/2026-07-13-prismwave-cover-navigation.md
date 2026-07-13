# PrismWave Cover Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (\`- [ ]\`) syntax for tracking.

**Goal:** Replace the current fade/short-translation navigation with a 280ms full-width right-to-left cover transition for every PrismWave page while the old page stays stationary.

**Architecture:** \`ShellPage\` owns a clipped two-Frame content host. One Frame remains the current stationary page and the other navigates to the incoming page, is positioned one host-width to the right through Windows Composition, and becomes current after its 280ms animation completes. A transition revision invalidates delayed callbacks, and a new request completes the active transition before starting the latest route.

**Tech Stack:** WinUI 3 XAML, Windows App SDK Composition APIs, C# 13/.NET 10, xUnit static XAML/code contracts, Windows UI Automation and real-window screenshots.

## Global Constraints

- Every page navigation moves right-to-left; back navigation is not reversed.
- The old page remains stationary and fully visible until covered.
- The incoming page moves exactly the current content-host width in 280ms.
- NavigationView, title bar, QueuePane, and BottomPlayerBar do not move.
- Startup and same-page navigation do not animate.
- Repeated navigation completes the active transition and starts only the newest target.
- \`LowEffects\` and Windows animation settings do not disable this navigation feedback.
- Do not change page layout, colors, radii, data, or business behavior.

---

### Task 1: Add the clipped two-Frame content host

**Files:**
- Modify: \`src/PrismWave.WinUI/Views/Shell/ShellPage.xaml\`
- Modify: \`tests/PrismWave.WinUI.Tests/ShellNavigationXamlTests.cs\`

**Interfaces:**
- Consumes: the existing \`NavigationView\`, \`QueuePane\`, and \`BottomPlayerBar\` layout.
- Produces: named elements \`PageTransitionHost\`, \`PageTransitionClip\`, \`PrimaryContentFrame\`, and \`SecondaryContentFrame\`.

- [ ] **Step 1: Write the failing XAML contract test**

Add a test that parses \`ShellPage.xaml\`, asserts the named host contains exactly two Frame descendants, asserts a named RectangleGeometry clip, and verifies QueuePane is outside the transition host.

\`\`\`csharp
[Fact]
public void Shell_UsesClippedDualFrameCoverNavigationHost()
{
    var document = XDocument.Load(FindRepositoryFile(
        "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml"));
    var xamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
    var host = Assert.Single(document.Descendants(), element =>
        element.Attribute(xamlName)?.Value == "PageTransitionHost");
    var frames = host.Descendants().Where(element => element.Name.LocalName == "Frame").ToArray();

    Assert.Equal(2, frames.Length);
    Assert.Contains(frames, frame => frame.Attribute(xamlName)?.Value == "PrimaryContentFrame");
    Assert.Contains(frames, frame => frame.Attribute(xamlName)?.Value == "SecondaryContentFrame");
    Assert.Single(host.Descendants(), element =>
        element.Name.LocalName == "RectangleGeometry" &&
        element.Attribute(xamlName)?.Value == "PageTransitionClip");
    Assert.DoesNotContain(host.Descendants(), element => element.Name.LocalName == "QueuePane");
}
\`\`\`

- [ ] **Step 2: Run the focused test and confirm failure**

Run:

\`\`\`powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --filter Shell_UsesClippedDualFrameCoverNavigationHost --no-restore
\`\`\`

Expected: FAIL because \`PageTransitionHost\` does not exist.

- [ ] **Step 3: Replace the single Frame with the dual host**

Keep the outer content Grid and QueuePane position unchanged. Replace \`ContentFrame\` with:

\`\`\`xml
<Grid
    x:Name="PageTransitionHost"
    SizeChanged="PageTransitionHost_SizeChanged">
    <Grid.Clip>
        <RectangleGeometry x:Name="PageTransitionClip" />
    </Grid.Clip>
    <Frame x:Name="PrimaryContentFrame" />
    <Frame
        x:Name="SecondaryContentFrame"
        Visibility="Collapsed"
        IsHitTestVisible="False" />
</Grid>
\`\`\`

Leave QueuePane as the next child of the outer Grid so it stays above the page host and never moves.

- [ ] **Step 4: Run the focused test and confirm pass**

Run the Step 2 command.

Expected: PASS.

---

### Task 2: Implement fixed-direction Composition cover navigation

**Files:**
- Modify: \`src/PrismWave.WinUI/Views/Shell/ShellPage.xaml.cs\`
- Modify: \`tests/PrismWave.WinUI.Tests/ShellNavigationXamlTests.cs\`

**Interfaces:**
- Consumes: the four named XAML elements from Task 1 and the existing route-to-Page mapping.
- Produces: \`StartCoverNavigation\`, \`BeginCoverAnimation\`, \`CompleteActiveTransition\`, and \`ResetFrame\` methods with revision-guarded callbacks.

- [ ] **Step 1: Replace the old fade test with failing cover-transition contracts**

\`\`\`csharp
[Fact]
public void Shell_UsesFixedDirectionFullWidthCoverTransition()
{
    var code = File.ReadAllText(FindRepositoryFile(
        "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));

    Assert.Contains("TimeSpan.FromMilliseconds(280)", code, StringComparison.Ordinal);
    Assert.Contains("PageTransitionHost.ActualWidth", code, StringComparison.Ordinal);
    Assert.Contains("CreateScalarKeyFrameAnimation", code, StringComparison.Ordinal);
    Assert.Contains("StartAnimation(\"Offset.X\"", code, StringComparison.Ordinal);
    Assert.Contains("CompleteActiveTransition(superseded: true)", code, StringComparison.Ordinal);
    Assert.Contains("transitionRevision != _navigationTransitionRevision", code, StringComparison.Ordinal);
    Assert.DoesNotContain("DoubleAnimation", code, StringComparison.Ordinal);
    Assert.DoesNotContain("AnimationsEnabled", code, StringComparison.Ordinal);
}
\`\`\`

- [ ] **Step 2: Run the focused test and confirm failure**

Run:

\`\`\`powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --filter Shell_UsesFixedDirectionFullWidthCoverTransition --no-restore
\`\`\`

Expected: FAIL because the current code uses 180/220ms opacity and Y translation.

- [ ] **Step 3: Implement two-Frame orchestration**

In \`ShellPage.xaml.cs\`:

- Remove \`UISettings\`, Storyboard, DoubleAnimation, opacity, and Y-translation state.
- Initialize current and incoming Frame references after \`InitializeComponent()\`.
- Navigate the initial Frame directly without animation.
- Complete an active transition before accepting a different route.
- Navigate the incoming Frame with \`SuppressNavigationTransitionInfo\`, make it visible, and enqueue animation start after first layout.
- Set only the incoming Composition visual offset to \`(PageTransitionHost.ActualWidth, 0, 0)\`.
- Animate \`Offset.X\` to zero in 280ms with Fluent cubic-bezier easing.
- Keep the current Frame at offset zero.
- On completion, collapse and clear the old Frame, swap Frame references, restore hit testing, and focus the new page.
- Log \`navigation.cover.requested\`, \`prepared\`, \`started\`, \`completed\`, \`superseded\`, and \`failed\`.

The completion guard must be:

\`\`\`csharp
if (!_isTransitionActive || transitionRevision != _navigationTransitionRevision)
{
    return;
}
\`\`\`

The animation core must be:

\`\`\`csharp
var animation = compositor.CreateScalarKeyFrameAnimation();
animation.InsertKeyFrame(1f, 0f, easing);
animation.Duration = TimeSpan.FromMilliseconds(280);
visual.StartAnimation("Offset.X", animation);
\`\`\`

- [ ] **Step 4: Clip to live size and finish on resize**

Implement \`PageTransitionHost_SizeChanged\` to update \`PageTransitionClip.Rect\` to \`(0, 0, width, height)\`. If a transition is active, complete it before accepting the new width.

- [ ] **Step 5: Run Shell tests and build**

\`\`\`powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --filter ShellNavigationXamlTests --no-restore
dotnet build src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64
\`\`\`

Expected: all Shell tests PASS; build succeeds with 0 warnings and 0 errors.

---

### Task 3: Verify runtime behavior and regression safety

**Files:**
- Create: \`docs/ui-review/12-cover-home-start.png\`
- Create: \`docs/ui-review/13-cover-home-middle.png\`
- Create: \`docs/ui-review/14-cover-home-end.png\`
- Create: \`docs/ui-review/15-cover-rapid-final.png\`

**Interfaces:**
- Consumes: the x64 PrismWave build and \`navigation.cover.*\` logs.
- Produces: screenshot and log evidence for the accepted animation contract.

- [ ] **Step 1: Run the complete test suite**

\`\`\`powershell
dotnet test tests\PrismWave.WinUI.Tests\PrismWave.WinUI.Tests.csproj --no-restore
git diff --check
\`\`\`

Expected: 88 or more tests pass; no whitespace errors from feature files.

- [ ] **Step 2: Launch the current x64 build**

\`\`\`powershell
dotnet run --project src\PrismWave.WinUI\PrismWave.WinUI.csproj -p:Platform=x64 --no-build
\`\`\`

Expected: PrismWave opens visibly and remains responsive.

- [ ] **Step 3: Verify every navigation destination**

Use Windows UI automation to navigate through Home, Search, Library, Albums, Artists, Favorites, HITS, Settings, TopPlaylist or AlbumDetail, and FullPlay. Verify the navigation pane and bottom player do not move and no previous page remains after completion.

- [ ] **Step 4: Capture animation and rapid-navigation evidence**

Arm a screenshot watcher on \`navigation.cover.started\`, invoke navigation, and capture immediately, around 140ms, and after 300ms. Then invoke at least three destinations within one animation window and capture the final page after 350ms.

Expected:

- Start: the old page fills the host and the incoming page begins at the right edge.
- Middle: the old page stays fixed while the incoming page covers part of it.
- End: the incoming page fills the host with no blank area.
- Rapid final: only the last requested destination remains.

- [ ] **Step 5: Inspect logs and leave the demo open**

Confirm each successful route has \`requested -> prepared -> started -> completed\`, rapid navigation records \`superseded\`, and there are no \`navigation.cover.failed\` entries. Leave the verified PrismWave process running.

