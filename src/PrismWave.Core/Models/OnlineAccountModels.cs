namespace PrismWave_WinUI.Models;

public enum OnlineProviderAuthState
{
    Disconnected,
    WaitingForScan,
    Scanned,
    Authenticated,
    Expired,
    Failed,
}

public sealed class OnlineLoginChallenge
{
    public OnlineLoginChallenge(
        string providerKey,
        string qrPayload,
        byte[]? qrImageBytes,
        DateTimeOffset expiresAt,
        long revision)
    {
        ProviderKey = providerKey;
        QrPayload = qrPayload;
        QrImageBytes = qrImageBytes ?? [];
        ExpiresAt = expiresAt;
        Revision = revision;
    }

    public string ProviderKey { get; }

    public string QrPayload { get; }

    public byte[] QrImageBytes { get; }

    public DateTimeOffset ExpiresAt { get; }

    public long Revision { get; }

    public override string ToString() =>
        $"OnlineLoginChallenge {{ ProviderKey = {ProviderKey}, ExpiresAt = {ExpiresAt:O}, Revision = {Revision}, QrData = [REDACTED] }}";
}

public sealed record OnlineAccountSnapshot(
    string ProviderKey,
    OnlineProviderAuthState State,
    string? DisplayName = null,
    string? AvatarUrl = null,
    string? StatusMessage = null)
{
    public override string ToString() =>
        $"OnlineAccountSnapshot {{ ProviderKey = {ProviderKey}, State = {State}, Account = [REDACTED] }}";
}

public sealed class OnlineProviderSession
{
    public OnlineProviderSession(
        string providerKey,
        IReadOnlyDictionary<string, string> cookies,
        long sessionRevision = 0)
    {
        ProviderKey = providerKey;
        Cookies = cookies;
        SessionRevision = sessionRevision;
    }

    public string ProviderKey { get; }

    public IReadOnlyDictionary<string, string> Cookies { get; }

    public long SessionRevision { get; }

    public string CookieHeader => string.Join("; ", Cookies.Select(static pair => $"{pair.Key}={pair.Value}"));

    public override string ToString() =>
        $"OnlineProviderSession {{ ProviderKey = {ProviderKey}, Credentials = [REDACTED] }}";
}

public sealed class ProviderCredential
{
    public ProviderCredential(string providerKey, string secret)
    {
        ProviderKey = providerKey;
        Secret = secret;
    }

    public string ProviderKey { get; }

    public string Secret { get; }

    public override string ToString() =>
        $"ProviderCredential {{ ProviderKey = {ProviderKey}, Secret = [REDACTED] }}";
}
