using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;

namespace PrismWave_WinUI.Services.Implementations;

public sealed class OnlineAccountService : IOnlineAccountService, IDisposable
{
    private const string NeteaseProvider = "netease";
    private const string QqProvider = "qq";

    private readonly object _sync = new();
    private readonly IProviderCredentialStore _credentialStore;
    private readonly Dictionary<string, ProviderState> _states = new(StringComparer.OrdinalIgnoreCase)
    {
        [NeteaseProvider] = new(NeteaseProvider),
        [QqProvider] = new(QqProvider),
    };
    private readonly Dictionary<string, IOnlineLoginAdapter> _adapters;
    private readonly bool _ownsNeteaseClient;
    private readonly bool _ownsQqClient;
    private readonly HttpClient _neteaseClient;
    private readonly HttpClient _qqClient;
    private readonly Func<DateTimeOffset> _utcNow;

    public OnlineAccountService(
        IProviderCredentialStore credentialStore,
        HttpClient? neteaseClient = null,
        HttpClient? qqClient = null,
        Func<double>? random = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _credentialStore = credentialStore;
        _ownsNeteaseClient = neteaseClient is null;
        _ownsQqClient = qqClient is null;
        _neteaseClient = neteaseClient ?? CreateProtocolClient();
        _qqClient = qqClient ?? CreateProtocolClient();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _adapters = new(StringComparer.OrdinalIgnoreCase)
        {
            [NeteaseProvider] = new NeteaseQrLoginAdapter(_neteaseClient, _utcNow),
            [QqProvider] = new QqQrLoginAdapter(_qqClient, random ?? Random.Shared.NextDouble, _utcNow),
        };
    }

    public event EventHandler<OnlineAccountSnapshot>? AccountChanged;

    public async Task<OnlineLoginChallenge> CreateChallengeAsync(
        string providerKey,
        CancellationToken cancellationToken)
    {
        var (key, state, adapter) = GetProvider(providerKey);
        long revision;
        OnlineAccountSnapshot previousSnapshot;
        OnlineAccountSnapshot cancellationSnapshot;
        lock (_sync)
        {
            previousSnapshot = state.Snapshot;
            cancellationSnapshot = previousSnapshot.State == OnlineProviderAuthState.Authenticated
                ? previousSnapshot
                : new OnlineAccountSnapshot(key, OnlineProviderAuthState.Disconnected);
            revision = ++state.Revision;
            state.ChallengeContext = null;
            state.Challenge = null;
            state.Snapshot = new(key, OnlineProviderAuthState.WaitingForScan);
        }

        RaiseAccountChanged(GetSnapshot(key));

        AdapterChallenge created;
        try
        {
            created = await adapter.CreateChallengeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RestoreSnapshotIfCurrent(state, revision, cancellationSnapshot);
            throw;
        }
        catch
        {
            SetFailureIfCurrent(state, revision);
            throw new InvalidOperationException($"Unable to create a {key} login challenge.");
        }

        OnlineLoginChallenge challenge;
        OnlineAccountSnapshot snapshot;
        lock (_sync)
        {
            if (state.Revision != revision)
            {
                throw new OperationCanceledException("The login challenge was superseded.");
            }

            challenge = new(
                key,
                created.QrPayload,
                created.QrImageBytes,
                created.ExpiresAt,
                revision);
            state.Challenge = challenge;
            state.ChallengeContext = created.Context;
            state.Snapshot = new(key, OnlineProviderAuthState.WaitingForScan);
            snapshot = state.Snapshot;
        }

        RaiseAccountChanged(snapshot);
        return challenge;
    }

    public async Task<OnlineAccountSnapshot> PollAsync(
        string providerKey,
        CancellationToken cancellationToken)
    {
        var (key, state, adapter) = GetProvider(providerKey);
        long revision;
        object context;
        OnlineAccountSnapshot? locallyExpired = null;
        lock (_sync)
        {
            revision = state.Revision;
            context = state.ChallengeContext
                ?? throw new InvalidOperationException($"No active {key} login challenge.");

            if (state.Challenge is { } challenge && challenge.ExpiresAt <= _utcNow())
            {
                state.Snapshot = new(key, OnlineProviderAuthState.Expired);
                locallyExpired = state.Snapshot;
            }
        }

        if (locallyExpired is not null)
        {
            RaiseAccountChanged(locallyExpired);
            return locallyExpired;
        }

        AdapterPollResult result;
        try
        {
            result = await adapter.PollAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return SetFailureIfCurrent(state, revision);
        }

        lock (_sync)
        {
            if (state.Revision != revision || !ReferenceEquals(state.ChallengeContext, context))
            {
                return state.Snapshot;
            }
        }

        if (result.State == OnlineProviderAuthState.Authenticated)
        {
            if (result.Cookies is null || result.Cookies.Count == 0)
            {
                return SetFailureIfCurrent(state, revision);
            }

            var authenticatedSession = new OnlineProviderSession(key, result.Cookies, revision);
            var encoded = ProviderCredentialCodec.Encode(result.Cookies);
            await state.CredentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_sync)
                {
                    if (state.Revision != revision || !ReferenceEquals(state.ChallengeContext, context))
                    {
                        return state.Snapshot;
                    }
                }

                await _credentialStore.SaveAsync(new ProviderCredential(key, encoded), cancellationToken)
                    .ConfigureAwait(false);

                OnlineAccountSnapshot authenticatedSnapshot;
                var becameStale = false;
                lock (_sync)
                {
                    if (state.Revision != revision || !ReferenceEquals(state.ChallengeContext, context))
                    {
                        becameStale = true;
                        authenticatedSnapshot = state.Snapshot;
                    }
                    else
                    {
                        state.Session = authenticatedSession;
                        state.CredentialsLoaded = true;
                        state.SessionVerified = true;
                        state.AuthenticationRecoveryAttempted = false;
                        state.Challenge = null;
                        state.ChallengeContext = null;
                        state.Snapshot = new(
                            key,
                            OnlineProviderAuthState.Authenticated,
                            result.DisplayName,
                            result.AvatarUrl,
                            result.StatusMessage);
                        authenticatedSnapshot = state.Snapshot;
                    }
                }

                if (becameStale)
                {
                    await _credentialStore.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
                    return authenticatedSnapshot;
                }

                RaiseAccountChanged(authenticatedSnapshot);
                return authenticatedSnapshot;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return SetFailureIfCurrent(state, revision);
            }
            finally
            {
                state.CredentialGate.Release();
            }
        }

        OnlineAccountSnapshot snapshot;
        lock (_sync)
        {
            if (state.Revision != revision || !ReferenceEquals(state.ChallengeContext, context))
            {
                return state.Snapshot;
            }

            state.Snapshot = new(key, result.State, result.DisplayName, result.AvatarUrl, result.StatusMessage);
            snapshot = state.Snapshot;
        }
        RaiseAccountChanged(snapshot);
        return snapshot;
    }

    public OnlineAccountSnapshot GetSnapshot(string providerKey)
    {
        var (_, state, _) = GetProvider(providerKey);
        lock (_sync)
        {
            return state.Snapshot;
        }
    }

    public async Task<OnlineProviderSession?> GetSessionAsync(
        string providerKey,
        CancellationToken cancellationToken)
    {
        var (key, state, adapter) = GetProvider(providerKey);
        lock (_sync)
        {
            if (state.Session is not null || state.CredentialsLoaded)
            {
                return state.Session;
            }
        }

        await state.CredentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long revision;
            lock (_sync)
            {
                if (state.Session is not null || state.CredentialsLoaded)
                {
                    return state.Session;
                }

                revision = state.Revision;
            }

            ProviderCredential? credential;
            try
            {
                credential = await _credentialStore.LoadAsync(key, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                credential = null;
            }

            var cookies = credential is null
                ? null
                : ProviderCredentialCodec.Decode(key, credential.Secret);
            if (credential is not null && cookies is null)
            {
                await ExpireSessionUnderCredentialGateAsync(key, state, revision, cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }

            if (cookies is null)
            {
                lock (_sync)
                {
                    if (state.Revision == revision && !state.CredentialsLoaded)
                    {
                        state.CredentialsLoaded = true;
                    }
                }

                return null;
            }

            var candidate = new OnlineProviderSession(key, cookies, revision + 1);
            bool isValid;
            try
            {
                isValid = await adapter.ValidateSessionAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                isValid = false;
            }

            if (!isValid)
            {
                await ExpireSessionUnderCredentialGateAsync(key, state, revision, cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }

            lock (_sync)
            {
                if (state.Revision != revision || state.CredentialsLoaded)
                {
                    return state.Session;
                }

                state.Revision = candidate.SessionRevision;
                state.CredentialsLoaded = true;
                state.Session = candidate;
                state.SessionVerified = true;
                state.AuthenticationRecoveryAttempted = false;
                state.Snapshot = new(key, OnlineProviderAuthState.Authenticated);

                return state.Session;
            }
        }
        finally
        {
            state.CredentialGate.Release();
            var snapshot = GetSnapshot(key);
            RaiseAccountChanged(snapshot);
        }
    }

    public async Task<OnlineProviderSession?> HandleAuthenticationFailureAsync(
        string providerKey,
        CancellationToken cancellationToken)
    {
        var (key, state, adapter) = GetProvider(providerKey);
        await state.CredentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        OnlineAccountSnapshot? changedSnapshot = null;
        try
        {
            OnlineProviderSession? session;
            long revision;
            var mustExpireWithoutValidation = false;
            lock (_sync)
            {
                session = state.Session;
                revision = state.Revision;
                if (session is null)
                {
                    return null;
                }

                if (state.AuthenticationRecoveryAttempted)
                {
                    mustExpireWithoutValidation = true;
                }
                else
                {
                    state.AuthenticationRecoveryAttempted = true;
                }
            }

            IReadOnlyDictionary<string, string>? recoveredCookies = null;
            if (!mustExpireWithoutValidation)
            {
                try
                {
                    recoveredCookies = await adapter.RecoverSessionAsync(session, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    recoveredCookies = null;
                }
            }

            if (recoveredCookies is not null)
            {
                var recovered = new OnlineProviderSession(key, recoveredCookies, revision + 1);
                bool isValid;
                try
                {
                    isValid = await adapter.ValidateSessionAsync(recovered, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    isValid = false;
                }

                if (isValid)
                {
                    lock (_sync)
                    {
                        if (state.Revision != revision || !ReferenceEquals(state.Session, session))
                        {
                            return null;
                        }
                    }

                    await _credentialStore.SaveAsync(
                            new ProviderCredential(key, ProviderCredentialCodec.Encode(recoveredCookies)),
                            cancellationToken)
                        .ConfigureAwait(false);

                    lock (_sync)
                    {
                        if (state.Revision != revision || !ReferenceEquals(state.Session, session))
                        {
                            return null;
                        }

                        state.Revision = recovered.SessionRevision;
                        state.Session = recovered;
                        state.SessionVerified = true;
                        state.Snapshot = state.Snapshot with { State = OnlineProviderAuthState.Authenticated };
                        changedSnapshot = state.Snapshot;
                        return recovered;
                    }
                }
            }

            lock (_sync)
            {
                if (state.Revision != revision || !ReferenceEquals(state.Session, session))
                {
                    return null;
                }
            }

            changedSnapshot = await ExpireSessionUnderCredentialGateAsync(
                    key,
                    state,
                    revision,
                    cancellationToken)
                .ConfigureAwait(false);
            return null;
        }
        finally
        {
            state.CredentialGate.Release();
            if (changedSnapshot is not null)
            {
                RaiseAccountChanged(changedSnapshot);
            }
        }
    }

    public async Task InvalidateSessionAsync(string providerKey, CancellationToken cancellationToken)
    {
        var (key, state, _) = GetProvider(providerKey);
        long revision;
        lock (_sync)
        {
            revision = ++state.Revision;
            state.Challenge = null;
            state.ChallengeContext = null;
        }

        await state.CredentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        OnlineAccountSnapshot snapshot;
        try
        {
            snapshot = await ExpireSessionUnderCredentialGateAsync(
                    key,
                    state,
                    revision,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            state.CredentialGate.Release();
        }

        RaiseAccountChanged(snapshot);
    }

    public async Task SignOutAsync(string providerKey, CancellationToken cancellationToken)
    {
        var (key, state, _) = GetProvider(providerKey);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            state.Revision++;
            state.Challenge = null;
            state.ChallengeContext = null;
        }

        await state.CredentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                await _credentialStore.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                throw new InvalidOperationException("Credential storage operation failed.");
            }

            lock (_sync)
            {
                state.Session = null;
                state.CredentialsLoaded = true;
                state.SessionVerified = false;
                state.AuthenticationRecoveryAttempted = false;
                state.Snapshot = new(key, OnlineProviderAuthState.Disconnected);
            }
        }
        finally
        {
            state.CredentialGate.Release();
        }
        RaiseAccountChanged(GetSnapshot(key));
    }

    public void Dispose()
    {
        if (_ownsNeteaseClient)
        {
            _neteaseClient.Dispose();
        }

        if (_ownsQqClient)
        {
            _qqClient.Dispose();
        }
    }

    private static HttpClient CreateProtocolClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private (string Key, ProviderState State, IOnlineLoginAdapter Adapter) GetProvider(string providerKey)
    {
        var key = providerKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!_states.TryGetValue(key, out var state) || !_adapters.TryGetValue(key, out var adapter))
        {
            throw new ArgumentOutOfRangeException(nameof(providerKey), "Only netease and qq accounts are supported.");
        }

        return (key, state, adapter);
    }

    private OnlineAccountSnapshot SetFailureIfCurrent(ProviderState state, long revision)
    {
        OnlineAccountSnapshot snapshot;
        lock (_sync)
        {
            if (state.Revision != revision)
            {
                return state.Snapshot;
            }

            state.Snapshot = new(
                state.ProviderKey,
                OnlineProviderAuthState.Failed,
                StatusMessage: "Login service is temporarily unavailable.");
            snapshot = state.Snapshot;
        }

        RaiseAccountChanged(snapshot);
        return snapshot;
    }

    private void RestoreSnapshotIfCurrent(
        ProviderState state,
        long revision,
        OnlineAccountSnapshot previousSnapshot)
    {
        OnlineAccountSnapshot? restored = null;
        lock (_sync)
        {
            if (state.Revision == revision)
            {
                state.Challenge = null;
                state.ChallengeContext = null;
                state.Snapshot = previousSnapshot;
                restored = state.Snapshot;
            }
        }

        if (restored is not null)
        {
            RaiseAccountChanged(restored);
        }
    }

    private async Task<OnlineAccountSnapshot> ExpireSessionUnderCredentialGateAsync(
        string providerKey,
        ProviderState state,
        long revision,
        CancellationToken cancellationToken)
    {
        Exception? deleteFailure = null;
        try
        {
            await _credentialStore.DeleteAsync(providerKey, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            deleteFailure = new InvalidOperationException("Credential storage operation failed.");
        }

        OnlineAccountSnapshot snapshot;
        lock (_sync)
        {
            if (state.Revision != revision)
            {
                return state.Snapshot;
            }

            state.Revision++;
            state.Session = null;
            state.CredentialsLoaded = true;
            state.SessionVerified = false;
            state.AuthenticationRecoveryAttempted = true;
            state.Snapshot = new(providerKey, OnlineProviderAuthState.Expired);
            snapshot = state.Snapshot;
        }

        if (deleteFailure is not null)
        {
            throw deleteFailure;
        }

        return snapshot;
    }

    private void RaiseAccountChanged(OnlineAccountSnapshot snapshot) => AccountChanged?.Invoke(this, snapshot);

    private sealed class ProviderState(string providerKey)
    {
        public string ProviderKey { get; } = providerKey;
        public long Revision { get; set; }
        public OnlineLoginChallenge? Challenge { get; set; }
        public object? ChallengeContext { get; set; }
        public OnlineProviderSession? Session { get; set; }
        public bool CredentialsLoaded { get; set; }
        public bool SessionVerified { get; set; }
        public bool AuthenticationRecoveryAttempted { get; set; }
        public SemaphoreSlim CredentialGate { get; } = new(1, 1);
        public OnlineAccountSnapshot Snapshot { get; set; } =
            new(providerKey, OnlineProviderAuthState.Disconnected);
    }
}

internal interface IOnlineLoginAdapter
{
    Task<AdapterChallenge> CreateChallengeAsync(CancellationToken cancellationToken);

    Task<AdapterPollResult> PollAsync(object context, CancellationToken cancellationToken);

    Task<bool> ValidateSessionAsync(OnlineProviderSession session, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>?> RecoverSessionAsync(
        OnlineProviderSession session,
        CancellationToken cancellationToken);
}

internal sealed record AdapterChallenge(
    string QrPayload,
    byte[] QrImageBytes,
    DateTimeOffset ExpiresAt,
    object Context);

internal sealed record AdapterPollResult(
    OnlineProviderAuthState State,
    IReadOnlyDictionary<string, string>? Cookies = null,
    string? DisplayName = null,
    string? AvatarUrl = null,
    string? StatusMessage = null);

internal sealed class NeteaseQrLoginAdapter(HttpClient httpClient, Func<DateTimeOffset> utcNow) : IOnlineLoginAdapter
{
    private static readonly Uri CreateUri = new("https://interface.music.163.com/api/login/qrcode/unikey");
    private static readonly Uri PollUri = new("https://interface.music.163.com/api/login/qrcode/client/login");
    private static readonly Uri AccountUri = new("https://interface.music.163.com/api/nuser/account/get");
    private static readonly Uri RefreshUri = new("https://interface.music.163.com/api/login/token/refresh");

    public async Task<AdapterChallenge> CreateChallengeAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CreateUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["type"] = "3" }),
        };
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = json.RootElement;
        var key = TryGetString(root, "unikey")
            ?? (root.TryGetProperty("data", out var data) ? TryGetString(data, "unikey") : null);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("The provider did not return a QR key.");
        }

        var context = new NeteaseChallengeContext(key);
        return new(
            $"https://music.163.com/login?codekey={Uri.EscapeDataString(key)}",
            [],
            utcNow().AddMinutes(5),
            context);
    }

    public async Task<AdapterPollResult> PollAsync(object context, CancellationToken cancellationToken)
    {
        var challenge = context as NeteaseChallengeContext
            ?? throw new ArgumentException("Invalid NetEase login challenge.", nameof(context));
        using var request = new HttpRequestMessage(HttpMethod.Post, PollUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["key"] = challenge.Key,
                ["type"] = "3",
            }),
        };
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(body);
        var code = TryGetInt32(json.RootElement, "code");
        return code switch
        {
            800 => new(OnlineProviderAuthState.Expired),
            801 => new(OnlineProviderAuthState.WaitingForScan),
            802 => new(OnlineProviderAuthState.Scanned),
            803 => CreateAuthenticatedResult(json.RootElement, response),
            _ => new(OnlineProviderAuthState.Failed, StatusMessage: "Login service is temporarily unavailable."),
        };
    }

    public async Task<bool> ValidateSessionAsync(
        OnlineProviderSession session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AccountUri);
        request.Headers.TryAddWithoutValidation("Cookie", session.CookieHeader);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = json.RootElement;
        if (TryGetInt32(root, "code") != 200)
        {
            return false;
        }

        return IsNonNullObject(root, "account") || IsNonNullObject(root, "profile");
    }

    public async Task<IReadOnlyDictionary<string, string>?> RecoverSessionAsync(
        OnlineProviderSession session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        request.Headers.TryAddWithoutValidation("Cookie", session.CookieHeader);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var refreshed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                CookieUtilities.CollectAllowed(value, refreshed, CookieUtilities.NeteaseCookieNames);
            }
        }

        if (!CookieUtilities.HasNeteaseAuthenticationCookie(refreshed))
        {
            return null;
        }

        var cookies = new Dictionary<string, string>(session.Cookies, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in refreshed)
        {
            cookies[pair.Key] = pair.Value;
        }
        return cookies;
    }

    private static AdapterPollResult CreateAuthenticatedResult(
        JsonElement root,
        HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CookieUtilities.CollectAllowed(
            TryGetString(root, "cookie"),
            cookies,
            CookieUtilities.NeteaseCookieNames);
        if (response.Headers.TryGetValues("Set-Cookie", out var headerValues))
        {
            foreach (var header in headerValues)
            {
                CookieUtilities.CollectAllowed(header, cookies, CookieUtilities.NeteaseCookieNames);
            }
        }

        return !CookieUtilities.HasNeteaseAuthenticationCookie(cookies)
            ? new(OnlineProviderAuthState.Failed, StatusMessage: "Login could not be verified.")
            : new(OnlineProviderAuthState.Authenticated, cookies);
    }

    private static int TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : int.TryParse(value.ToString(), CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsNonNullObject(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object;

    private sealed record NeteaseChallengeContext(string Key);
}

internal sealed class QqQrLoginAdapter(
    HttpClient httpClient,
    Func<double> random,
    Func<DateTimeOffset> utcNow) : IOnlineLoginAdapter
{
    private static readonly Uri Referer = new("https://y.qq.com/");
    private static readonly Uri UserInfoUri = new("https://u.y.qq.com/cgi-bin/musicu.fcg");
    private static readonly Regex CallbackValueRegex = new("'([^']*)'", RegexOptions.Compiled);

    public async Task<AdapterChallenge> CreateChallengeAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(
            "https://ssl.ptlogin2.qq.com/ptqrshow" +
            "?appid=716027609&e=2&l=M&s=3&d=72&v=4" +
            $"&t={random().ToString(CultureInfo.InvariantCulture)}" +
            "&daid=383&pt_3rd_aid=100497308");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Referrer = Referer;
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var qrsig = CookieUtilities.FindCookie(response, "qrsig");
        if (string.IsNullOrWhiteSpace(qrsig))
        {
            throw new InvalidOperationException("The provider did not return a QR session.");
        }

        var image = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new(
            string.Empty,
            image,
            utcNow().AddMinutes(2),
            new QqChallengeContext(qrsig));
    }

    public async Task<AdapterPollResult> PollAsync(object context, CancellationToken cancellationToken)
    {
        var challenge = context as QqChallengeContext
            ?? throw new ArgumentException("Invalid QQ login challenge.", nameof(context));
        var uri = BuildPollUri(challenge.QrSignature, utcNow());
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Referrer = Referer;
        request.Headers.TryAddWithoutValidation("Cookie", $"qrsig={challenge.QrSignature}");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var callback = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var fields = CallbackValueRegex.Matches(callback).Select(static match => match.Groups[1].Value).ToArray();
        if (fields.Length == 0)
        {
            return new(OnlineProviderAuthState.Failed, StatusMessage: "Login service is temporarily unavailable.");
        }

        return fields[0] switch
        {
            "65" => new(OnlineProviderAuthState.Expired),
            "66" => new(OnlineProviderAuthState.WaitingForScan),
            "67" => new(OnlineProviderAuthState.Scanned),
            "0" => await CompleteLoginAsync(fields, challenge.QrSignature, response, cancellationToken).ConfigureAwait(false),
            _ => new(OnlineProviderAuthState.Failed, StatusMessage: "Login service is temporarily unavailable."),
        };
    }

    public async Task<bool> ValidateSessionAsync(
        OnlineProviderSession session,
        CancellationToken cancellationToken)
    {
        var uin = session.Cookies.GetValueOrDefault("uin") ?? string.Empty;
        var payload = JsonSerializer.Serialize(new
        {
            comm = new { ct = 24, cv = 0, uin },
            req_1 = new
            {
                module = "music.UserInfo.userInfoServer",
                method = "GetLoginUserInfo",
                param = new { },
            },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, UserInfoUri)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Referrer = Referer;
        request.Headers.TryAddWithoutValidation("Cookie", session.CookieHeader);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = json.RootElement;
        if (!TryReadCode(root, out var rootCode) || rootCode != 0 ||
            !root.TryGetProperty("req_1", out var userInfo) ||
            !TryReadCode(userInfo, out var userInfoCode) ||
            userInfoCode != 0 ||
            !userInfo.TryGetProperty("data", out var data))
        {
            return false;
        }

        return data.ValueKind == JsonValueKind.Object;
    }

    public Task<IReadOnlyDictionary<string, string>?> RecoverSessionAsync(
        OnlineProviderSession session,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // QQ exposes no refresh token in this QR flow; recovery is an explicit user-info revalidation.
        return Task.FromResult<IReadOnlyDictionary<string, string>?>(session.Cookies);
    }

    internal static int ComputePtQrToken(string qrsig)
    {
        var hash = 0;
        foreach (var character in qrsig)
        {
            hash += (hash << 5) + character;
        }

        return hash & 0x7fffffff;
    }

    private static bool TryReadCode(JsonElement element, out int code)
    {
        code = 0;
        if (!element.TryGetProperty("code", out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.Number
            ? value.TryGetInt32(out code)
            : int.TryParse(value.ToString(), CultureInfo.InvariantCulture, out code);
    }

    private static Uri BuildPollUri(string qrsig, DateTimeOffset now)
    {
        var parameters = new Dictionary<string, string>
        {
            ["u1"] = "https://graph.qq.com/oauth2.0/login_jump",
            ["ptqrtoken"] = ComputePtQrToken(qrsig).ToString(CultureInfo.InvariantCulture),
            ["ptredirect"] = "100",
            ["h"] = "1",
            ["t"] = "1",
            ["g"] = "1",
            ["from_ui"] = "1",
            ["ptlang"] = "2052",
            ["action"] = $"0-0-{now.ToUnixTimeMilliseconds()}",
            ["js_ver"] = "21072115",
            ["js_type"] = "1",
            ["login_sig"] = string.Empty,
            ["pt_uistyle"] = "40",
            ["aid"] = "716027609",
            ["daid"] = "383",
            ["pt_3rd_aid"] = "100497308",
            ["has_onekey"] = "1",
            ["pttype"] = "1",
            ["service"] = "ptqrlogin",
            ["nodirect"] = "0",
        };
        var query = string.Join("&", parameters.Select(static pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"https://ssl.ptlogin2.qq.com/ptqrlogin?{query}");
    }

    private async Task<AdapterPollResult> CompleteLoginAsync(
        IReadOnlyList<string> callbackFields,
        string qrsig,
        HttpResponseMessage pollResponse,
        CancellationToken cancellationToken)
    {
        if (callbackFields.Count < 3 ||
            !Uri.TryCreate(callbackFields[2], UriKind.Absolute, out var next) ||
            !IsTrustedQqUri(next))
        {
            return new(OnlineProviderAuthState.Failed, StatusMessage: "Login could not be verified.");
        }

        var cookieJar = new ScopedCookieJar();
        cookieJar.AddHostOnly("qrsig", qrsig, new Uri("https://ssl.ptlogin2.qq.com/"));
        cookieJar.CollectFromResponse(new Uri("https://ssl.ptlogin2.qq.com/ptqrlogin"), pollResponse);
        var referer = Referer;
        for (var redirectCount = 0; redirectCount < 8; redirectCount++)
        {
            if (!IsTrustedQqUri(next))
            {
                return new(OnlineProviderAuthState.Failed, StatusMessage: "Login could not be verified.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.Referrer = referer;
            var cookieHeader = cookieJar.BuildHeader(next);
            if (!string.IsNullOrWhiteSpace(cookieHeader))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            cookieJar.CollectFromResponse(next, response);

            if (!IsRedirect(response.StatusCode) || response.Headers.Location is null)
            {
                break;
            }

            referer = next;
            var redirected = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(next, response.Headers.Location);
            if (!IsTrustedQqUri(redirected))
            {
                return new(OnlineProviderAuthState.Failed, StatusMessage: "Login could not be verified.");
            }

            next = redirected;
        }

        var normalized = CookieUtilities.NormalizeQqCookies(cookieJar.Snapshot());
        if (normalized.Count == 0)
        {
            return new(OnlineProviderAuthState.Failed, StatusMessage: "Login could not be verified.");
        }

        var displayName = callbackFields.Count > 5 && !string.IsNullOrWhiteSpace(callbackFields[5])
            ? callbackFields[5]
            : null;
        return new(OnlineProviderAuthState.Authenticated, normalized, displayName);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.Moved or
        HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static bool IsTrustedQqUri(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        uri.IsDefaultPort &&
        (IsWithinDomain(uri.IdnHost, "qq.com") || IsWithinDomain(uri.IdnHost, "tencent.com"));

    private static bool IsWithinDomain(string host, string trustedSuffix) =>
        host.Equals(trustedSuffix, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{trustedSuffix}", StringComparison.OrdinalIgnoreCase);

    private sealed record QqChallengeContext(string QrSignature);
}

internal sealed class ScopedCookieJar
{
    private readonly List<ScopedCookie> _cookies = [];

    internal void AddHostOnly(string name, string value, Uri origin, string path = "/") =>
        Upsert(new(name, value, origin.IdnHost, path, Secure: true, HostOnly: true));

    internal void CollectFromResponse(Uri origin, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headerValues))
        {
            return;
        }

        foreach (var header in headerValues)
        {
            var segments = header.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var separator = segments[0].IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = segments[0][..separator].Trim();
            var value = segments[0][(separator + 1)..].Trim();
            if (name.Equals("qrsig", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var domain = origin.IdnHost;
            var hostOnly = true;
            var path = DefaultPath(origin.AbsolutePath);
            var secure = false;
            for (var index = 1; index < segments.Length; index++)
            {
                var attribute = segments[index];
                var attributeSeparator = attribute.IndexOf('=');
                var attributeName = (attributeSeparator < 0 ? attribute : attribute[..attributeSeparator]).Trim();
                var attributeValue = attributeSeparator < 0 ? string.Empty : attribute[(attributeSeparator + 1)..].Trim();
                if (attributeName.Equals("Domain", StringComparison.OrdinalIgnoreCase))
                {
                    var requestedDomain = attributeValue.TrimStart('.');
                    if (!DomainMatches(origin.IdnHost, requestedDomain))
                    {
                        domain = string.Empty;
                        break;
                    }

                    domain = requestedDomain;
                    hostOnly = false;
                }
                else if (attributeName.Equals("Path", StringComparison.OrdinalIgnoreCase) && attributeValue.StartsWith('/'))
                {
                    path = attributeValue;
                }
                else if (attributeName.Equals("Secure", StringComparison.OrdinalIgnoreCase))
                {
                    secure = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(domain))
            {
                Upsert(new(name, value, domain, path, secure, hostOnly));
            }
        }
    }

    internal string BuildHeader(Uri target)
    {
        var values = _cookies
            .Where(cookie => cookie.Matches(target))
            .Select(static cookie => $"{cookie.Name}={cookie.Value}");
        return string.Join("; ", values);
    }

    internal IReadOnlyDictionary<string, string> Snapshot()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cookie in _cookies)
        {
            snapshot[cookie.Name] = cookie.Value;
        }

        return snapshot;
    }

    private void Upsert(ScopedCookie cookie)
    {
        _cookies.RemoveAll(existing =>
            existing.Name.Equals(cookie.Name, StringComparison.OrdinalIgnoreCase) &&
            existing.Domain.Equals(cookie.Domain, StringComparison.OrdinalIgnoreCase) &&
            existing.Path.Equals(cookie.Path, StringComparison.Ordinal));
        _cookies.Add(cookie);
    }

    private static string DefaultPath(string absolutePath)
    {
        var separator = absolutePath.LastIndexOf('/');
        return separator <= 0 ? "/" : absolutePath[..separator];
    }

    private static bool DomainMatches(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);

    private sealed record ScopedCookie(
        string Name,
        string Value,
        string Domain,
        string Path,
        bool Secure,
        bool HostOnly)
    {
        internal bool Matches(Uri target) =>
            (!Secure || target.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
            (HostOnly
                ? target.IdnHost.Equals(Domain, StringComparison.OrdinalIgnoreCase)
                : DomainMatches(target.IdnHost, Domain)) &&
            target.AbsolutePath.StartsWith(Path, StringComparison.Ordinal);
    }
}

internal static class CookieUtilities
{
    internal static readonly HashSet<string> NeteaseCookieNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "MUSIC_U",
        "MUSIC_A",
        "__csrf",
        "NMTID",
    };

    private static readonly string[] QqUinNames =
    [
        "uin", "ptui_loginuin", "luin", "pt2gguin", "superuin", "p_uin", "musicid", "userid", "wxuin",
    ];

    private static readonly string[] QqKeyNames = ["qqmusic_key", "p_skey", "skey", "musickey"];

    internal static string? FindCookie(HttpResponseMessage response, string cookieName)
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CollectAll(response, cookies);
        return cookies.GetValueOrDefault(cookieName);
    }

    internal static void CollectAll(HttpResponseMessage response, IDictionary<string, string> target)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headerValues))
        {
            return;
        }

        foreach (var header in headerValues)
        {
            CollectAllowed(header, target, allowedNames: null);
        }
    }

    internal static void CollectAllowed(
        string? cookieText,
        IDictionary<string, string> target,
        ISet<string>? allowedNames)
    {
        if (string.IsNullOrWhiteSpace(cookieText))
        {
            return;
        }

        foreach (Match match in Regex.Matches(cookieText, @"(?:^|[,;]\s*)(?<name>[^=;,\s]+)=(?<value>[^;,]*)"))
        {
            var name = match.Groups["name"].Value.Trim();
            if (allowedNames is not null && !allowedNames.Contains(name))
            {
                continue;
            }

            var value = match.Groups["value"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                target[name] = value;
            }
        }
    }

    internal static IReadOnlyDictionary<string, string> NormalizeQqCookies(
        IReadOnlyDictionary<string, string> source)
    {
        var uin = QqUinNames.Select(source.GetValueOrDefault).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var key = QqKeyNames.Select(source.GetValueOrDefault).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var qmKeyst = source.GetValueOrDefault("qm_keyst") ?? key;
        if (string.IsNullOrWhiteSpace(uin) || string.IsNullOrWhiteSpace(key))
        {
            return new Dictionary<string, string>();
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["uin"] = uin,
            ["qqmusic_key"] = key,
            ["qm_keyst"] = qmKeyst!,
        };
    }

    internal static string BuildHeader(IReadOnlyDictionary<string, string> cookies) =>
        string.Join("; ", cookies.Select(static pair => $"{pair.Key}={pair.Value}"));

    internal static bool HasNeteaseAuthenticationCookie(IReadOnlyDictionary<string, string> cookies) =>
        !string.IsNullOrWhiteSpace(cookies.GetValueOrDefault("MUSIC_U")) ||
        !string.IsNullOrWhiteSpace(cookies.GetValueOrDefault("MUSIC_A"));
}

internal static class ProviderCredentialCodec
{
    internal static string Encode(IReadOnlyDictionary<string, string> cookies) => JsonSerializer.Serialize(cookies);

    internal static IReadOnlyDictionary<string, string>? Decode(string providerKey, string secret)
    {
        try
        {
            var decoded = JsonSerializer.Deserialize<Dictionary<string, string>>(secret);
            if (decoded is null)
            {
                return null;
            }

            if (providerKey.Equals("qq", StringComparison.OrdinalIgnoreCase))
            {
                return CookieUtilities.NormalizeQqCookies(decoded);
            }

            var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in decoded)
            {
                if (CookieUtilities.NeteaseCookieNames.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    filtered[pair.Key] = pair.Value;
                }
            }

            return CookieUtilities.HasNeteaseAuthenticationCookie(filtered) ? filtered : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
