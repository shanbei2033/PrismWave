namespace PrismWave_WinUI.Infrastructure.Http;

/// <summary>
/// Provides a shared <see cref="HttpClient"/> instance for services that do not require
/// dedicated cookie containers or custom handlers. Sharing reduces per-client overhead
/// (connection pool, SSL state) which can save several MB of memory.
/// </summary>
internal static class SharedHttpClient
{
    private static readonly Lazy<HttpClient> Instance = new(() => new HttpClient());

    /// <summary>
    /// Returns the shared <see cref="HttpClient"/> when the caller did not supply one.
    /// If <paramref name="provided"/> is non-null it is returned as-is.
    /// </summary>
    public static HttpClient Resolve(HttpClient? provided) => provided ?? Instance.Value;
}
