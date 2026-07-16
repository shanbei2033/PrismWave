using Xunit;

namespace PrismWave_WinUI.Tests;

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

    [Fact]
    public void PlaybackContract_ExposesActiveRouteWithoutBreakingExistingFakes()
    {
        var source = Read("Services", "Contracts", "IPlaybackService.cs");

        Assert.Contains("string ActiveAudioOutputModeLabel => string.Empty;", source, StringComparison.Ordinal);
        Assert.Contains("string? AudioOutputFallbackReason => null;", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments)
    {
        var path = Path.Combine(
            new[]
            {
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "src", "PrismWave.WinUI"
            }.Concat(segments).ToArray());
        return File.ReadAllText(Path.GetFullPath(path));
    }
}
