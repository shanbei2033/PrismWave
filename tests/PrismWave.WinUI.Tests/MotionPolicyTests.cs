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
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void ShouldAnimate_RespectsSystemAndApplicationPreferences(
        bool systemAnimationsEnabled,
        bool lowEffects,
        bool expected)
    {
        Assert.Equal(expected, MotionPolicy.ShouldAnimate(systemAnimationsEnabled, lowEffects));
    }
}
