namespace PrismWave_WinUI.Infrastructure.Animation;

public static class MotionPolicy
{
    public static bool ShouldAnimateInteraction()
    {
        return true;
    }

    public static bool ShouldAnimate(bool systemAnimationsEnabled, bool lowEffects)
    {
        return systemAnimationsEnabled && !lowEffects;
    }
}
