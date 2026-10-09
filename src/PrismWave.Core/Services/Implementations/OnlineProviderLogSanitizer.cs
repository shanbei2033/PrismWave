using System.Text.Json;

namespace PrismWave_WinUI.Services.Implementations;

internal static class OnlineProviderLogSanitizer
{
    public static string Describe(Exception exception)
    {
        var category = exception switch
        {
            OperationCanceledException => "timeout-or-cancelled",
            HttpRequestException => "network-or-http",
            JsonException => "protocol-json",
            _ => "provider"
        };
        return $"category={category},type={exception.GetType().Name}";
    }
}
