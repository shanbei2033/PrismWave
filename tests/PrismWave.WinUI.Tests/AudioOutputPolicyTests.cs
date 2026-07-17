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
