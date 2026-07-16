using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.ViewModels.Player;

public static class LyricsTimeline
{
    public static int FindActiveIndex(IReadOnlyList<double> startTimes, double positionSeconds)
    {
        if (startTimes.Count == 0)
        {
            return -1;
        }

        var low = 0;
        var high = startTimes.Count - 1;
        var active = 0;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (startTimes[middle] <= positionSeconds)
            {
                active = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return active;
    }

    public static int FindActiveIndex(
        IReadOnlyList<LyricLineDisplayModel> lyrics,
        double positionSeconds)
    {
        if (lyrics.Count == 0)
        {
            return -1;
        }

        var low = 0;
        var high = lyrics.Count - 1;
        var active = 0;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (lyrics[middle].TimeSeconds <= positionSeconds)
            {
                active = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return active;
    }
}
