using PrismWave_WinUI.Infrastructure.Audio;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class PlaybackHeaderLogSanitizerTests
{
    [Fact]
    public void FormatHeaderNames_NeverIncludesHeaderValues()
    {
        var headers = new Dictionary<string, string>
        {
            ["Referer"] = "https://origin.test/private-path",
            ["User-Agent"] = "PrismWave secret agent"
        };

        var formatted = PlaybackHeaderLogSanitizer.FormatHeaderNames(headers);

        Assert.Contains("Referer", formatted, StringComparison.Ordinal);
        Assert.Contains("User-Agent", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("private-path", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret agent", formatted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    [InlineData("Proxy-Authorization")]
    [InlineData("X-Api-Key")]
    public void FormatHeaderNames_RedactsSensitiveHeaderNames(string headerName)
    {
        var formatted = PlaybackHeaderLogSanitizer.FormatHeaderNames(
            new Dictionary<string, string> { [headerName] = "top-secret" });

        Assert.Equal("<redacted>", formatted);
        Assert.DoesNotContain(headerName, formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("top-secret", formatted, StringComparison.Ordinal);
    }
}
