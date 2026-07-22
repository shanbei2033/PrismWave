using PrismWave_WinUI.Infrastructure.Animation;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class MotionPolicyTests
{
    [Fact]
    public void ShouldAnimateInteraction_RemainsAvailableWhenDecorativeEffectsAreReduced()
    {
        Assert.True(MotionPolicy.ShouldAnimateInteraction());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldAnimate_RespectsSystemPreference(
        bool systemAnimationsEnabled,
        bool expected)
    {
        Assert.Equal(expected, MotionPolicy.ShouldAnimate(systemAnimationsEnabled));
    }
}
