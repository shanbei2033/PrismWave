using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class FullPlayPageXamlTests
{
    [Fact]
    public void FullPlay_ExposesBackButtonAndEscapeKeyboardNavigation()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml");
        var document = XDocument.Load(xamlPath);
        var backButton = Assert.Single(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "FullPlayBackButton");
        var escape = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "KeyboardAccelerator" &&
            element.Attribute("Key")?.Value == "Escape");

        Assert.Equal("BackButton_Click", backButton.Attribute("Click")?.Value);
        Assert.Equal("24,50,0,0", backButton.Attribute("Margin")?.Value);
        Assert.Equal("BackKeyboardAccelerator_Invoked", escape.Attribute("Invoked")?.Value);
    }

    [Fact]
    public void FullPlay_LyricsToolbarContainsCompanionToggle()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml"));
        var button = Assert.Single(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "LyricsCompanionButton");

        Assert.Equal("LyricsCompanionButton_Click", button.Attribute("Click")?.Value);
        var fontIcon = Assert.Single(
            button.Elements(),
            element => element.Name.LocalName == "FontIcon");
        Assert.Equal("\uE774", fontIcon.Attribute("Glyph")?.Value);
        var actions = FindByAutomationId(document, "LyricsToolActions");
        Assert.Contains(actions.Descendants(), element => element == button);
    }

    [Fact]
    public void FullPlay_UsesResponsiveTwoColumnImmersiveLayout()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml"));
        var body = FindByAutomationId(document, "FullPlayBody");
        var columns = body.Elements()
            .Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements()
            .ToArray();
        var playerArea = FindByAutomationId(document, "FullPlayPlayerArea");
        var cover = FindByAutomationId(document, "FullPlayCover");

        Assert.Equal(new[] { "42*", "58*" }, columns.Select(column => column.Attribute("Width")?.Value).ToArray());
        Assert.Equal("64", body.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("520", playerArea.Attribute("MaxWidth")?.Value);
        Assert.Equal("420", cover.Parent?.Attribute("Width")?.Value);
        Assert.Equal("420", cover.Parent?.Attribute("Height")?.Value);
        Assert.Equal("UniformToFill", cover.Attribute("Stretch")?.Value);
    }

    [Fact]
    public void FullPlay_DoubleTappingMainCoverOpensSingleCoverSearchDialog()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml");
        var document = XDocument.Load(xamlPath);
        var codeBehind = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));
        var cover = FindByAutomationId(document, "FullPlayCover");
        var interactiveHost = cover.Ancestors().First(element =>
            element.Attribute("DoubleTapped") is not null);

        Assert.Equal("FullPlayCover_DoubleTapped", interactiveHost.Attribute("DoubleTapped")?.Value);
        Assert.Null(interactiveHost.Attribute("ToolTipService.ToolTip"));
        Assert.Contains("_isCoverSearchDialogOpen", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ShowCoverSearchDialogAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackResolution_PreservesExistingCatalogCover()
    {
        var playbackService = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Services", "Implementations", "PlaybackService.cs"));
        var playbackResolution = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Models", "OnlinePlaybackResolution.cs"));

        Assert.Contains(
            "OnlinePlaybackTrack.ApplyResolution(track, resolved)",
            playbackService,
            StringComparison.Ordinal);
        Assert.Contains(
            "CoverPath = track.CoverPath ?? resolution.CoverUrl",
            playbackResolution,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FullPlay_PlayerAreaContainsIndependentTransportAndSeekControls()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml");
        var document = XDocument.Load(xamlPath);
        var codeBehind = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));
        var playerArea = FindByAutomationId(document, "FullPlayPlayerArea");

        Assert.Equal("{Binding CurrentTitle}", FindByAutomationId(document, "FullPlayTrackTitle").Attribute("Text")?.Value);
        Assert.Equal("{Binding CurrentArtist}", FindByAutomationId(document, "FullPlayTrackArtist").Attribute("Text")?.Value);
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "FullPlayTrackAlbum");
        Assert.Equal("{Binding ToggleCurrentFavoriteCommand}", FindByAutomationId(document, "FullPlayFavoriteButton").Attribute("Command")?.Value);
        Assert.Equal("{Binding CycleModeCommand}", FindByAutomationId(document, "FullPlayModeButton").Attribute("Command")?.Value);
        Assert.Equal("{Binding PreviousCommand}", FindByAutomationId(document, "FullPlayPreviousButton").Attribute("Command")?.Value);
        Assert.Equal("{Binding TogglePlayPauseCommand}", FindByAutomationId(document, "FullPlayPlayPauseButton").Attribute("Command")?.Value);
        Assert.Equal("{Binding NextCommand}", FindByAutomationId(document, "FullPlayNextButton").Attribute("Command")?.Value);
        Assert.Contains(playerArea.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "FullPlayQueueButton");

        var seek = FindByAutomationId(document, "FullPlaySeekSlider");
        var volume = FindByAutomationId(document, "FullPlayVolumeSlider");
        Assert.Equal("{Binding PositionSeconds, Mode=OneWay}", seek.Attribute("Value")?.Value);
        Assert.Equal("{Binding DurationSeconds}", seek.Attribute("Maximum")?.Value);
        Assert.Equal("FullPlaySeekSlider_PointerCaptureLost", seek.Attribute("PointerCaptureLost")?.Value);
        Assert.Equal("{Binding Volume, Mode=OneWay}", volume.Attribute("Value")?.Value);
        Assert.Equal("FullPlayVolumeSlider_ValueChanged", volume.Attribute("ValueChanged")?.Value);
        Assert.Contains("ViewModel.Seek(slider.Value)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ViewModel.SetVolume(e.NewValue)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPlay_UsesOneLyricsStageWithoutVirtualizedLineContainers()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml");
        var document = XDocument.Load(xamlPath);
        var codeBehind = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));
        var lyricsStage = document.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == "LyricsStage"));

        Assert.Equal("LyricsStageControl", lyricsStage.Name.LocalName);
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "ListView" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "LyricsList"));
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "KaraokeTextBlock");
        Assert.Contains("LyricsStage.SetLyrics", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollIntoView", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ChangeView", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("LyricsScrollCoordinator", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void LyricsStage_UsesOneVsyncClockAndStableImmersiveTypography()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml"));
        var stagePath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Lyrics", "LyricsStageControl.xaml");
        var stageDocument = XDocument.Load(stagePath);
        var stageCode = File.ReadAllText(Path.ChangeExtension(stagePath, ".xaml.cs"));

        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute("Text")?.Value == "Lyrics");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "LyricsSourceLabel");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attribute("Text")?.Value == "{Binding LyricsProvider}");
        Assert.Single(stageDocument.Descendants(), element =>
            element.Name.LocalName == "CanvasControl" &&
            element.Attribute("Draw")?.Value == "StageCanvas_Draw");
        Assert.Contains("CompositionTarget.Rendering", stageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherQueueTimer", stageCode, StringComparison.Ordinal);
        Assert.Contains("ResolvePrimaryFontSize", stageCode, StringComparison.Ordinal);
        Assert.Contains("FontWeight = FontWeights.SemiBold", stageCode, StringComparison.Ordinal);
        Assert.Contains("Color.FromArgb(255, 136, 136, 136)", stageCode, StringComparison.Ordinal);
        Assert.Contains("Microsoft.UI.Colors.White", stageCode, StringComparison.Ordinal);
        Assert.Contains("GaussianBlurEffect", stageCode, StringComparison.Ordinal);
        Assert.Contains("BorderMode = EffectBorderMode.Soft", stageCode, StringComparison.Ordinal);
        Assert.Contains("CanvasTextLayout", stageCode, StringComparison.Ordinal);
        Assert.Contains("_layoutCache", stageCode, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPlay_LyricsStageSupportsBrowseHitTestSeekAndDelayedReturn()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml");
        var document = XDocument.Load(xamlPath);
        var codeBehind = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));
        var lyricsStage = document.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == "LyricsStage"));

        Assert.Equal("LyricsStage_LyricInvoked", lyricsStage.Attribute("LyricInvoked")?.Value);
        Assert.Equal("LyricsStage_ManualBrowseChanged", lyricsStage.Attribute("ManualBrowseChanged")?.Value);
        Assert.Contains("TimeSpan.FromSeconds(4)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ViewModel.SeekToLyric", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LyricsStage.EndManualBrowse", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPlay_ForwardsCoarsePlaybackSamplesToTheStage()
    {
        var codeBehind = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml.cs"));

        Assert.Contains("LyricsStage.UpdatePlaybackSample", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LyricsPositionUpdateKind.Sample", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LyricsPositionUpdateKind.TrackChanged", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LyricsPositionUpdateKind.PauseResume", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LyricsPositionUpdateKind.OffsetChanged", codeBehind, StringComparison.Ordinal);
        Assert.Contains("nameof(PlaybackViewModel.PositionSeconds)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds(33)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("LyricsPresentationPositionTracker", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginLyricsPresentationUpdates", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPlay_CoalescesBurstLyricsCollectionChangesBeforeRebuildingTheStage()
    {
        var codeBehind = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml.cs"));

        Assert.Contains("if (_lyricsRefreshScheduled)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_lyricsRefreshScheduled = true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_lyricsRefreshScheduled = false", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RefreshLyricsStage(LyricsPositionUpdateKind.TrackChanged)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void LyricsStage_CachesInactiveLineDrawingCommandsOutsideTheFrameLoop()
    {
        var stageCode = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Lyrics", "LyricsStageControl.xaml.cs"));

        Assert.Contains("new CanvasCommandList(StageCanvas)", stageCode, StringComparison.Ordinal);
        Assert.Contains("entry.StaticCommandList", stageCode, StringComparison.Ordinal);
        Assert.Contains("StaticCommandList.Dispose()", stageCode, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPlay_BackgroundUsesCoverDrivenCompositionBlurWithoutSyntheticGradient()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml");
        var document = XDocument.Load(xamlPath);
        var codeBehind = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));
        var backdrop = FindByAutomationId(document, "FullPlayBackdrop");
        var image = FindByAutomationId(document, "FullPlayBackdropImage");
        var blur = FindByAutomationId(document, "FullPlayBackdropBlur");

        Assert.Equal("False", backdrop.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal("{Binding CurrentCoverPath}", image.Attribute("SourcePath")?.Value);
        Assert.Equal("UniformToFill", image.Attribute("Stretch")?.Value);
        Assert.Contains(image.DescendantsAndSelf(), element => element.Name.LocalName == "ScaleTransform");
        Assert.Equal("Grid", blur.Name.LocalName);
        Assert.DoesNotContain(backdrop.Descendants(), element => element.Name.LocalName == "AcrylicBrush");
        Assert.DoesNotContain(backdrop.Descendants(), element => element.Name.LocalName == "LinearGradientBrush");
        Assert.Contains("GaussianBlurEffect", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BlurAmount = 30f", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LoadedImageSurface.StartLoadFromUri", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(200)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_backdropContainer.Clip = compositor.CreateInsetClip()", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void StableCoverImage_CrossfadesLoadedCoverWhileKeepingPreviousFrame()
    {
        var code = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Media", "StableCoverImage.cs"));

        Assert.Contains("DoubleAnimation", code, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(180)", code, StringComparison.Ordinal);
        Assert.Contains("storyboard.Completed", code, StringComparison.Ordinal);
        Assert.True(
            code.IndexOf("storyboard.Completed", StringComparison.Ordinal) <
            code.IndexOf("_currentImage.Source = bitmap", StringComparison.Ordinal),
            "The displayed source must only switch after the pending image has faded in.");
    }

    [Fact]
    public void FullPlay_LyricsToolsExpandUpwardAsAnIndependentOverlay()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml");
        var document = XDocument.Load(xamlPath);
        var codeBehind = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));
        var body = FindByAutomationId(document, "FullPlayBody");
        var toolbar = FindByAutomationId(document, "LyricsToolsOverlay");
        var actions = FindByAutomationId(document, "LyricsToolActions");
        var toggle = FindByAutomationId(document, "LyricsToolsToggleButton");

        Assert.Same(body.Parent, toolbar.Parent);
        Assert.Equal("Right", toolbar.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Bottom", toolbar.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("False", actions.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal("LyricsToolsToggleButton_Click", toggle.Attribute("Click")?.Value);

        foreach (var (automationId, elementName) in new[]
        {
            ("LyricsSourceButton", "LyricsSourceButton"),
            ("LyricsSearchButton", "LyricsSearchButton"),
            ("LyricsOffsetButton", "LyricsOffsetButton")
        })
        {
            var button = Assert.Single(toolbar.Descendants(), element =>
                element.Attribute("AutomationProperties.AutomationId")?.Value == automationId);
            Assert.Equal(elementName, button.Attribute(XName.Get(
                "Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value);
            Assert.Equal("0", button.Attribute("Opacity")?.Value);
            Assert.Equal("18", Assert.Single(button.Descendants(), element =>
                element.Name.LocalName == "TranslateTransform").Attribute("Y")?.Value);
        }

        Assert.Contains("LyricsToolsAnimationDurationMilliseconds = 160", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LyricsToolsStaggerMilliseconds = 25", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_lyricsToolsStoryboard?.Stop()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BeginTime = TimeSpan.FromMilliseconds", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DoubleAnimation", codeBehind, StringComparison.Ordinal);
        Assert.Contains("EnableDependentAnimation = true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LyricsToolActions.IsHitTestVisible = expanding", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPlay_LyricsSourceAndSearchToolsExposeCurrentSourceAndResultQuality()
    {
        var fullPlay = XDocument.Load(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml"));
        var sourceButton = FindByAutomationId(fullPlay, "LyricsSourceButton");
        var searchButton = FindByAutomationId(fullPlay, "LyricsSearchButton");
        var dialog = XDocument.Load(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Dialogs", "LyricsSearchDialog.xaml"));

        Assert.Equal("{Binding ToggleLyricsSourceCommand}", sourceButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding LyricsSourceLabel}", sourceButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("SearchLyricsButton_Click", searchButton.Attribute("Click")?.Value);
        Assert.Single(dialog.Descendants(), element =>
            element.Attribute("Text")?.Value == "{Binding LyricsKindLabel}");
    }

    [Fact]
    public void FullPlay_LyricsOffsetFlyoutValidatesAndAppliesFromTheKeyboard()
    {
        var xamlPath = FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml");
        var document = XDocument.Load(xamlPath);
        var codeBehind = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));
        var offsetButton = FindByAutomationId(document, "LyricsOffsetButton");
        var flyout = Assert.Single(offsetButton.Descendants(), element =>
            element.Name.LocalName == "Flyout");
        var input = Assert.Single(flyout.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == "LyricsOffsetInput");

        Assert.Equal("LeftEdgeAlignedBottom", flyout.Attribute("Placement")?.Value);
        Assert.Equal("LyricsOffsetFlyout_Opened", flyout.Attribute("Opened")?.Value);
        Assert.Equal("LyricsOffsetInput_BeforeTextChanging", input.Attribute("BeforeTextChanging")?.Value);
        Assert.Equal("LyricsOffsetInput_KeyDown", input.Attribute("KeyDown")?.Value);
        Assert.Single(flyout.Descendants(), element =>
            element.Attribute("Click")?.Value == "ApplyLyricsOffsetButton_Click");
        Assert.Contains("ApplyLyricsOffsetAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LyricsOffsetFlyout.Hide()", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPlay_KeepsCoreInteractionMotionAndReleasesTemporaryResources()
    {
        var codeBehind = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml.cs"));
        var stableCover = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Media", "StableCoverImage.cs"));
        var lyricsStage = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Lyrics", "LyricsStageControl.xaml.cs"));
        var shell = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Shell", "ShellPage.xaml.cs"));

        Assert.Contains("MotionPolicy.ShouldAnimateInteraction", codeBehind, StringComparison.Ordinal);
        Assert.Contains("if (!_animationsEnabled)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("MotionPolicy.ShouldAnimate", stableCover, StringComparison.Ordinal);
        Assert.Contains("MotionPolicy.ShouldAnimateInteraction", shell, StringComparison.Ordinal);
        Assert.Contains("ReleaseBackdropResources()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PropertyChanged -= ViewModel_PropertyChanged", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_lyricsReturnTimer.Tick -= LyricsReturnTimer_Tick", codeBehind, StringComparison.Ordinal);
        Assert.Contains("surface.Dispose()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CompositionTarget.Rendering -= CompositionTarget_Rendering", lyricsStage, StringComparison.Ordinal);
        Assert.Contains("EnsureDeviceResources(", lyricsStage, StringComparison.Ordinal);
        Assert.Contains("DisposeLayoutCache()", lyricsStage, StringComparison.Ordinal);
        Assert.Contains("DisposeBrushes()", lyricsStage, StringComparison.Ordinal);
    }

    [Fact]
    public void LyricsStage_CreatesWin2DResourcesOnlyAfterCanvasDeviceIsReady()
    {
        var lyricsStage = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Controls", "Lyrics", "LyricsStageControl.xaml.cs"));
        var loadedStart = lyricsStage.IndexOf(
            "private void LyricsStageControl_Loaded", StringComparison.Ordinal);
        var unloadedStart = lyricsStage.IndexOf(
            "private void LyricsStageControl_Unloaded", loadedStart, StringComparison.Ordinal);
        var createStart = lyricsStage.IndexOf(
            "private void StageCanvas_CreateResources", unloadedStart, StringComparison.Ordinal);
        var ensureStart = lyricsStage.IndexOf(
            "private void EnsureDeviceResources", createStart, StringComparison.Ordinal);
        var loaded = lyricsStage[loadedStart..unloadedStart];
        var createResources = lyricsStage[createStart..ensureStart];

        Assert.Contains("if (StageCanvas.ReadyToDraw)", loaded, StringComparison.Ordinal);
        Assert.Contains("EnsureDeviceResources(StageCanvas)", loaded, StringComparison.Ordinal);
        Assert.Contains("EnsureDeviceResources(sender)", createResources, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPlay_ProvidesActionableEmptyLyricsState()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Views", "Player", "FullPlayPage.xaml"));
        var emptyState = FindByAutomationId(document, "LyricsEmptyState");

        Assert.Equal(
            "{Binding ShowLyricsEmptyState, Converter={StaticResource BoolToVisibilityConverter}}",
            emptyState.Attribute("Visibility")?.Value);
        Assert.Single(emptyState.Descendants(), element =>
            element.Attribute("Text")?.Value == "{Binding LyricsEmptyMessage}");
        Assert.Single(emptyState.Descendants(), element =>
            element.Attribute("Click")?.Value == "SearchLyricsButton_Click");
    }

    private static XElement FindByAutomationId(XDocument document, string automationId)
    {
        return Assert.Single(document.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == automationId);
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository source file.");
    }
}
