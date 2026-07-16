using System.Net;
using System.Text;
using PrismWave_WinUI.Models;
using PrismWave_WinUI.Services.Contracts;
using PrismWave_WinUI.Services.Implementations;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class OnlineAccountServiceTests
{
    [Fact]
    public async Task Netease_Challenge_And_Poll_Map_All_Qr_States_And_Persist_Minimal_Cookies()
    {
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"qr-key\"}"),
            Response(HttpStatusCode.OK, "{\"code\":801}"),
            Response(HttpStatusCode.OK, "{\"code\":802}"),
            Response(HttpStatusCode.OK, "{\"code\":803,\"cookie\":\"MUSIC_U=music-secret; __csrf=csrf-secret; ignored=nope\"}"));
        var store = new MemoryCredentialStore();
        var service = CreateService(store, handler, new ScriptedHttpHandler());

        var challenge = await service.CreateChallengeAsync("netease", CancellationToken.None);

        Assert.Equal("netease", challenge.ProviderKey);
        Assert.Equal("https://music.163.com/login?codekey=qr-key", challenge.QrPayload);
        Assert.Empty(challenge.QrImageBytes);
        Assert.Equal(OnlineProviderAuthState.WaitingForScan, service.GetSnapshot("netease").State);

        Assert.Equal(OnlineProviderAuthState.WaitingForScan, (await service.PollAsync("netease", CancellationToken.None)).State);
        Assert.Equal(OnlineProviderAuthState.Scanned, (await service.PollAsync("netease", CancellationToken.None)).State);
        Assert.Equal(OnlineProviderAuthState.Authenticated, (await service.PollAsync("netease", CancellationToken.None)).State);

        var saved = Assert.Single(store.Saved);
        Assert.Equal("netease", saved.ProviderKey);
        Assert.Contains("MUSIC_U", saved.Secret, StringComparison.Ordinal);
        Assert.Contains("__csrf", saved.Secret, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored", saved.Secret, StringComparison.Ordinal);

        var requests = handler.Requests;
        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.EndsWith("/api/login/qrcode/unikey", requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("type=3", requests[0].Body);
        Assert.Equal("key=qr-key&type=3", requests[1].Body);
    }

    [Fact]
    public async Task Netease_Expired_Status_Does_Not_Persist_A_Credential()
    {
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"data\":{\"unikey\":\"qr-key\"}}"),
            Response(HttpStatusCode.OK, "{\"code\":800}"));
        var store = new MemoryCredentialStore();
        var service = CreateService(store, handler, new ScriptedHttpHandler());

        await service.CreateChallengeAsync("netease", CancellationToken.None);
        var snapshot = await service.PollAsync("netease", CancellationToken.None);

        Assert.Equal(OnlineProviderAuthState.Expired, snapshot.State);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task Netease_Rejects_An_803_Response_Without_A_Primary_Authentication_Cookie()
    {
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"qr-key\"}"),
            Response(HttpStatusCode.OK, "{\"code\":803,\"cookie\":\"__csrf=weak; NMTID=tracking\"}"));
        var store = new MemoryCredentialStore();
        var service = CreateService(store, handler, new ScriptedHttpHandler());

        await service.CreateChallengeAsync("netease", CancellationToken.None);
        var snapshot = await service.PollAsync("netease", CancellationToken.None);

        Assert.Equal(OnlineProviderAuthState.Failed, snapshot.State);
        Assert.Empty(store.Saved);
        Assert.Null(await service.GetSessionAsync("netease", CancellationToken.None));
    }

    [Fact]
    public async Task Lazy_Load_Rejects_And_Removes_Netease_Credential_Without_Music_U_Or_Music_A()
    {
        var store = new MemoryCredentialStore();
        store.Seed("netease", "{\"__csrf\":\"weak\",\"NMTID\":\"tracking\"}");
        var service = CreateService(store, new ScriptedHttpHandler(), new ScriptedHttpHandler());

        var session = await service.GetSessionAsync("netease", CancellationToken.None);

        Assert.Null(session);
        Assert.Equal(OnlineProviderAuthState.Expired, service.GetSnapshot("netease").State);
        Assert.Contains("netease", store.Deleted);
        Assert.False(store.ContainsCredential("netease"));
    }

    [Fact]
    public async Task Qq_Challenge_Poll_Uses_Hash33_And_Normalizes_Redirect_Cookies()
    {
        var show = Response(HttpStatusCode.OK, new byte[] { 0x89, 0x50, 0x4e, 0x47 }, "image/png");
        show.Headers.TryAddWithoutValidation("Set-Cookie", "qrsig=abc; Path=/; HttpOnly");
        var success = Response(HttpStatusCode.OK,
            "ptuiCB('0','0','https://graph.qq.com/step','0','Login success','Nick')");
        var redirect = Response(HttpStatusCode.Redirect, string.Empty);
        redirect.Headers.Location = new Uri("https://graph.qq.com/final");
        redirect.Headers.TryAddWithoutValidation("Set-Cookie", "p_uin=o12345; Path=/; HttpOnly");
        var final = Response(HttpStatusCode.OK, string.Empty);
        final.Headers.TryAddWithoutValidation("Set-Cookie", "p_skey=qq-secret; Path=/; Secure");
        var qqHandler = new ScriptedHttpHandler(show, success, redirect, final);
        var store = new MemoryCredentialStore();
        var service = CreateService(store, new ScriptedHttpHandler(), qqHandler);

        var challenge = await service.CreateChallengeAsync("qq", CancellationToken.None);
        var snapshot = await service.PollAsync("qq", CancellationToken.None);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, challenge.QrImageBytes);
        Assert.Equal(OnlineProviderAuthState.Authenticated, snapshot.State);
        Assert.Equal("Nick", snapshot.DisplayName);

        var showRequest = qqHandler.Requests[0];
        Assert.Equal("ssl.ptlogin2.qq.com", showRequest.Uri.Host);
        Assert.Contains("appid=716027609", showRequest.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("pt_3rd_aid=100497308", showRequest.Uri.Query, StringComparison.Ordinal);
        Assert.Equal("https://y.qq.com/", showRequest.Referer);

        var pollRequest = qqHandler.Requests[1];
        Assert.Contains("ptqrtoken=108966", pollRequest.Uri.Query, StringComparison.Ordinal);
        Assert.Equal("qrsig=abc", pollRequest.Cookie);
        Assert.Equal("https://y.qq.com/", pollRequest.Referer);

        var credential = Assert.Single(store.Saved);
        Assert.Contains("uin", credential.Secret, StringComparison.Ordinal);
        Assert.Contains("qqmusic_key", credential.Secret, StringComparison.Ordinal);
        Assert.Contains("qm_keyst", credential.Secret, StringComparison.Ordinal);
        Assert.DoesNotContain("qrsig", credential.Secret, StringComparison.Ordinal);

        var session = await service.GetSessionAsync("qq", CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal("o12345", session.Cookies["uin"]);
        Assert.Equal("qq-secret", session.Cookies["qqmusic_key"]);
        Assert.Equal("qq-secret", session.Cookies["qm_keyst"]);
        Assert.DoesNotContain("qq-secret", session.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://attacker.example/steal")]
    [InlineData("http://graph.qq.com/insecure")]
    [InlineData("https://evilqq.com/steal")]
    public async Task Qq_Rejects_Untrusted_Callback_Before_Sending_Any_Credential(string callbackUrl)
    {
        var show = Response(HttpStatusCode.OK, new byte[] { 1 }, "image/png");
        show.Headers.TryAddWithoutValidation("Set-Cookie", "qrsig=qr-secret; Path=/; HttpOnly");
        var callback = Response(HttpStatusCode.OK,
            $"ptuiCB('0','0','{callbackUrl}','0','Login success','Nick')");
        var qq = new ScriptedHttpHandler(
            show,
            callback,
            Response(HttpStatusCode.OK, string.Empty));
        var store = new MemoryCredentialStore();
        var service = CreateService(store, new ScriptedHttpHandler(), qq);

        await service.CreateChallengeAsync("qq", CancellationToken.None);
        var snapshot = await service.PollAsync("qq", CancellationToken.None);

        Assert.Equal(OnlineProviderAuthState.Failed, snapshot.State);
        Assert.Equal(2, qq.Requests.Count);
        Assert.Empty(store.Saved);
        Assert.DoesNotContain("qr-secret", snapshot.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Qq_Rejects_Untrusted_Secondary_Redirect_Without_Leaking_Accumulated_Cookies()
    {
        var show = Response(HttpStatusCode.OK, new byte[] { 1 }, "image/png");
        show.Headers.TryAddWithoutValidation("Set-Cookie", "qrsig=qr-secret; Path=/; HttpOnly");
        var callback = Response(HttpStatusCode.OK,
            "ptuiCB('0','0','https://graph.qq.com/step','0','Login success','Nick')");
        var redirect = Response(HttpStatusCode.Redirect, string.Empty);
        redirect.Headers.Location = new Uri("https://attacker.example/steal");
        redirect.Headers.TryAddWithoutValidation("Set-Cookie", "p_uin=o123; Domain=.qq.com; Path=/; Secure");
        redirect.Headers.TryAddWithoutValidation("Set-Cookie", "p_skey=qq-secret; Domain=.qq.com; Path=/; Secure");
        var qq = new ScriptedHttpHandler(
            show,
            callback,
            redirect,
            Response(HttpStatusCode.OK, string.Empty));
        var service = CreateService(new MemoryCredentialStore(), new ScriptedHttpHandler(), qq);

        await service.CreateChallengeAsync("qq", CancellationToken.None);
        var snapshot = await service.PollAsync("qq", CancellationToken.None);

        Assert.Equal(OnlineProviderAuthState.Failed, snapshot.State);
        Assert.Equal(3, qq.Requests.Count);
        Assert.DoesNotContain(qq.Requests, request => request.Uri.Host == "attacker.example");
    }

    [Fact]
    public async Task Qq_QrSignature_Is_HostOnly_And_Is_Not_Sent_To_Graph_Qq_Com()
    {
        var show = Response(HttpStatusCode.OK, new byte[] { 1 }, "image/png");
        show.Headers.TryAddWithoutValidation("Set-Cookie", "qrsig=qr-secret; Path=/; HttpOnly");
        var callback = Response(HttpStatusCode.OK,
            "ptuiCB('0','0','https://graph.qq.com/step','0','Login success','Nick')");
        var final = Response(HttpStatusCode.OK, string.Empty);
        final.Headers.TryAddWithoutValidation("Set-Cookie", "p_uin=o123; Domain=.qq.com; Path=/; Secure");
        final.Headers.TryAddWithoutValidation("Set-Cookie", "p_skey=qq-secret; Domain=.qq.com; Path=/; Secure");
        var qq = new ScriptedHttpHandler(show, callback, final);
        var service = CreateService(new MemoryCredentialStore(), new ScriptedHttpHandler(), qq);

        await service.CreateChallengeAsync("qq", CancellationToken.None);
        var snapshot = await service.PollAsync("qq", CancellationToken.None);

        Assert.Equal(OnlineProviderAuthState.Authenticated, snapshot.State);
        Assert.DoesNotContain("qrsig", qq.Requests[2].Cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qr-secret", qq.Requests[2].Cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Qq_Response_Cannot_Promote_QrSignature_To_A_Domain_Cookie()
    {
        var show = Response(HttpStatusCode.OK, new byte[] { 1 }, "image/png");
        show.Headers.TryAddWithoutValidation("Set-Cookie", "qrsig=original-secret; Path=/; HttpOnly");
        var callback = Response(HttpStatusCode.OK,
            "ptuiCB('0','0','https://graph.qq.com/step','0','Login success','Nick')");
        callback.Headers.TryAddWithoutValidation(
            "Set-Cookie",
            "qrsig=promoted-secret; Domain=.qq.com; Path=/; Secure");
        var final = Response(HttpStatusCode.OK, string.Empty);
        final.Headers.TryAddWithoutValidation("Set-Cookie", "p_uin=o123; Domain=.qq.com; Path=/; Secure");
        final.Headers.TryAddWithoutValidation("Set-Cookie", "p_skey=qq-secret; Domain=.qq.com; Path=/; Secure");
        var qq = new ScriptedHttpHandler(show, callback, final);
        var service = CreateService(new MemoryCredentialStore(), new ScriptedHttpHandler(), qq);

        await service.CreateChallengeAsync("qq", CancellationToken.None);
        var snapshot = await service.PollAsync("qq", CancellationToken.None);

        Assert.Equal(OnlineProviderAuthState.Authenticated, snapshot.State);
        Assert.DoesNotContain("qrsig", qq.Requests[2].Cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promoted-secret", qq.Requests[2].Cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("original-secret", qq.Requests[2].Cookie, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("66", OnlineProviderAuthState.WaitingForScan)]
    [InlineData("67", OnlineProviderAuthState.Scanned)]
    [InlineData("65", OnlineProviderAuthState.Expired)]
    public async Task Qq_Poll_Maps_Status_Codes(string code, OnlineProviderAuthState expected)
    {
        var show = Response(HttpStatusCode.OK, new byte[] { 1 }, "image/png");
        show.Headers.TryAddWithoutValidation("Set-Cookie", "qrsig=abc; Path=/");
        var qq = new ScriptedHttpHandler(show, Response(HttpStatusCode.OK, $"ptuiCB('{code}','0','','0','','')"));
        var service = CreateService(new MemoryCredentialStore(), new ScriptedHttpHandler(), qq);

        await service.CreateChallengeAsync("qq", CancellationToken.None);
        var snapshot = await service.PollAsync("qq", CancellationToken.None);

        Assert.Equal(expected, snapshot.State);
    }

    [Fact]
    public async Task GetSession_Lazily_Loads_And_Validates_Qq_Credential_Only_Once()
    {
        var store = new MemoryCredentialStore();
        store.Seed("qq", "{\"uin\":\"o7\",\"qqmusic_key\":\"key\",\"qm_keyst\":\"key\"}");
        var netease = new ScriptedHttpHandler();
        var qq = new ScriptedHttpHandler(Response(
            HttpStatusCode.OK,
            "{\"code\":0,\"req_1\":{\"code\":0,\"data\":{\"nick\":\"User\"}}}"));
        var service = CreateService(store, netease, qq);

        Assert.Equal(OnlineProviderAuthState.Disconnected, service.GetSnapshot("qq").State);
        var first = await service.GetSessionAsync("qq", CancellationToken.None);
        var second = await service.GetSessionAsync("qq", CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, store.LoadCount);
        Assert.Empty(netease.Requests);
        var validation = Assert.Single(qq.Requests);
        Assert.Equal(HttpMethod.Post, validation.Method);
        Assert.Equal("u.y.qq.com", validation.Uri.Host);
        Assert.EndsWith("/cgi-bin/musicu.fcg", validation.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("music.UserInfo.userInfoServer", validation.Body, StringComparison.Ordinal);
        Assert.Contains("qqmusic_key=key", validation.Cookie, StringComparison.Ordinal);
        Assert.Equal(OnlineProviderAuthState.Authenticated, service.GetSnapshot("qq").State);
    }

    [Fact]
    public async Task Persisted_Netease_Session_Must_Pass_Account_Validation()
    {
        var store = new MemoryCredentialStore();
        store.Seed("netease", "{\"MUSIC_U\":\"secret\"}");
        var netease = new ScriptedHttpHandler(Response(
            HttpStatusCode.OK,
            "{\"code\":200,\"account\":{\"id\":7},\"profile\":{\"nickname\":\"User\"}}"));
        var service = CreateService(store, netease, new ScriptedHttpHandler());

        var session = await service.GetSessionAsync("netease", CancellationToken.None);

        Assert.NotNull(session);
        var request = Assert.Single(netease.Requests);
        Assert.EndsWith("/api/nuser/account/get", request.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("MUSIC_U=secret", request.Cookie, StringComparison.Ordinal);
        Assert.Equal(OnlineProviderAuthState.Authenticated, service.GetSnapshot("netease").State);
    }

    [Theory]
    [InlineData("netease")]
    [InlineData("qq")]
    public async Task Persisted_Session_Validation_Failure_Expires_And_Removes_The_Credential(string provider)
    {
        var store = new MemoryCredentialStore();
        store.Seed(
            provider,
            provider == "netease"
                ? "{\"MUSIC_U\":\"secret\"}"
                : "{\"uin\":\"o7\",\"qqmusic_key\":\"key\",\"qm_keyst\":\"key\"}");
        var failed = Response(
            HttpStatusCode.OK,
            provider == "netease"
                ? "{\"code\":301,\"account\":null}"
                : "{\"code\":0,\"req_1\":{\"code\":1000,\"data\":null}}");
        var netease = provider == "netease" ? new ScriptedHttpHandler(failed) : new ScriptedHttpHandler();
        var qq = provider == "qq" ? new ScriptedHttpHandler(failed) : new ScriptedHttpHandler();
        var service = CreateService(store, netease, qq);

        var session = await service.GetSessionAsync(provider, CancellationToken.None);

        Assert.Null(session);
        Assert.Equal(OnlineProviderAuthState.Expired, service.GetSnapshot(provider).State);
        Assert.Contains(provider, store.Deleted);
        Assert.False(store.ContainsCredential(provider));
    }

    [Fact]
    public async Task Login_Session_Is_Already_Verified_And_Authentication_Failure_Revalidates_Only_Once()
    {
        var refresh = Response(HttpStatusCode.OK, "{\"code\":200}");
        refresh.Headers.TryAddWithoutValidation("Set-Cookie", "MUSIC_U=refreshed-secret; Path=/; HttpOnly");
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"key\"}"),
            Response(HttpStatusCode.OK, "{\"code\":803,\"cookie\":\"MUSIC_U=secret\"}"),
            refresh,
            Response(HttpStatusCode.OK, "{\"code\":200,\"account\":{\"id\":7}}"));
        var store = new MemoryCredentialStore();
        var service = CreateService(store, handler, new ScriptedHttpHandler());
        await service.CreateChallengeAsync("netease", CancellationToken.None);
        await service.PollAsync("netease", CancellationToken.None);

        var original = await service.GetSessionAsync("netease", CancellationToken.None);
        Assert.NotNull(original);
        Assert.Equal(2, handler.Requests.Count);

        var recovered = await service.HandleAuthenticationFailureAsync("netease", CancellationToken.None);
        var expired = await service.HandleAuthenticationFailureAsync("netease", CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.NotSame(original, recovered);
        Assert.True(recovered.SessionRevision > original.SessionRevision);
        Assert.Equal("refreshed-secret", recovered.Cookies["MUSIC_U"]);
        Assert.Null(expired);
        Assert.Equal(4, handler.Requests.Count);
        Assert.EndsWith("/api/login/token/refresh", handler.Requests[2].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("refreshed-secret", store.Saved[^1].Secret, StringComparison.Ordinal);
        Assert.Equal(OnlineProviderAuthState.Expired, service.GetSnapshot("netease").State);
        Assert.False(store.ContainsCredential("netease"));
    }

    [Fact]
    public async Task Authentication_Failure_With_Invalid_Revalidation_Expires_Immediately()
    {
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"key\"}"),
            Response(HttpStatusCode.OK, "{\"code\":803,\"cookie\":\"MUSIC_U=secret\"}"),
            Response(HttpStatusCode.OK, "{\"code\":301,\"account\":null}"));
        var store = new MemoryCredentialStore();
        var service = CreateService(store, handler, new ScriptedHttpHandler());
        await service.CreateChallengeAsync("netease", CancellationToken.None);
        await service.PollAsync("netease", CancellationToken.None);

        var session = await service.HandleAuthenticationFailureAsync("netease", CancellationToken.None);

        Assert.Null(session);
        Assert.Equal(OnlineProviderAuthState.Expired, service.GetSnapshot("netease").State);
        Assert.False(store.ContainsCredential("netease"));
    }

    [Fact]
    public async Task SignOut_Invalidates_An_InFlight_Authentication_Revalidation_Result()
    {
        var validation = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"key\"}"),
            Response(HttpStatusCode.OK, "{\"code\":803,\"cookie\":\"MUSIC_U=secret\"}"),
            validation);
        var store = new MemoryCredentialStore();
        var service = CreateService(store, handler, new ScriptedHttpHandler());
        await service.CreateChallengeAsync("netease", CancellationToken.None);
        await service.PollAsync("netease", CancellationToken.None);

        var revalidating = service.HandleAuthenticationFailureAsync("netease", CancellationToken.None);
        await handler.WaitForRequestCountAsync(3);
        var signingOut = service.SignOutAsync("netease", CancellationToken.None);
        validation.SetResult(Response(HttpStatusCode.OK, "{\"code\":200,\"account\":{\"id\":7}}"));

        Assert.Null(await revalidating);
        await signingOut;
        Assert.Equal(OnlineProviderAuthState.Disconnected, service.GetSnapshot("netease").State);
        Assert.False(store.ContainsCredential("netease"));
    }

    [Fact]
    public async Task Explicit_Invalidation_Expires_And_Removes_The_Session_Without_Validation_Request()
    {
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"key\"}"),
            Response(HttpStatusCode.OK, "{\"code\":803,\"cookie\":\"MUSIC_U=secret\"}"));
        var store = new MemoryCredentialStore();
        var service = CreateService(store, handler, new ScriptedHttpHandler());
        await service.CreateChallengeAsync("netease", CancellationToken.None);
        await service.PollAsync("netease", CancellationToken.None);

        await service.InvalidateSessionAsync("netease", CancellationToken.None);

        Assert.Null(await service.GetSessionAsync("netease", CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(OnlineProviderAuthState.Expired, service.GetSnapshot("netease").State);
        Assert.False(store.ContainsCredential("netease"));
    }

    [Fact]
    public async Task SignOut_Deletes_Credential_And_Invalidates_Memory_Session()
    {
        var store = new MemoryCredentialStore();
        store.Seed("netease", "{\"MUSIC_U\":\"secret\"}");
        var service = CreateService(
            store,
            new ScriptedHttpHandler(Response(HttpStatusCode.OK, "{\"code\":200,\"account\":{\"id\":7}}")),
            new ScriptedHttpHandler());
        Assert.NotNull(await service.GetSessionAsync("netease", CancellationToken.None));

        await service.SignOutAsync("netease", CancellationToken.None);

        Assert.Contains("netease", store.Deleted);
        Assert.Null(await service.GetSessionAsync("netease", CancellationToken.None));
        Assert.Equal(OnlineProviderAuthState.Disconnected, service.GetSnapshot("netease").State);
    }

    [Fact]
    public async Task SignOut_Delete_Failure_Does_Not_Pretend_The_Account_Is_Disconnected()
    {
        var store = new MemoryCredentialStore
        {
            DeleteError = new InvalidOperationException("vault-error-with-secret"),
        };
        store.Seed("netease", "{\"MUSIC_U\":\"secret\"}");
        var service = CreateService(
            store,
            new ScriptedHttpHandler(Response(HttpStatusCode.OK, "{\"code\":200,\"account\":{\"id\":7}}")),
            new ScriptedHttpHandler());
        var session = Assert.IsType<OnlineProviderSession>(
            await service.GetSessionAsync("netease", CancellationToken.None));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SignOutAsync("netease", CancellationToken.None));

        Assert.DoesNotContain("vault-error", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(OnlineProviderAuthState.Authenticated, service.GetSnapshot("netease").State);
        Assert.Same(session, await service.GetSessionAsync("netease", CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_Challenge_Creation_Restores_Disconnected_And_Raises_Event()
    {
        var handler = new ScriptedHttpHandler(
            (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)(static async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            }));
        var service = CreateService(new MemoryCredentialStore(), handler, new ScriptedHttpHandler());
        var states = new List<OnlineProviderAuthState>();
        service.AccountChanged += (_, snapshot) => states.Add(snapshot.State);
        using var cancellation = new CancellationTokenSource();
        var creating = service.CreateChallengeAsync("netease", cancellation.Token);
        await handler.WaitForRequestCountAsync(1);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creating);

        Assert.Equal(OnlineProviderAuthState.Disconnected, service.GetSnapshot("netease").State);
        Assert.Equal(OnlineProviderAuthState.Disconnected, states[^1]);
    }

    [Fact]
    public async Task Cancelling_A_Replacement_Challenge_Never_Leaves_Waiting_State_Without_Context()
    {
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"first\"}"),
            (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)(static async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            }));
        var service = CreateService(new MemoryCredentialStore(), handler, new ScriptedHttpHandler());
        await service.CreateChallengeAsync("netease", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var replacing = service.CreateChallengeAsync("netease", cancellation.Token);
        await handler.WaitForRequestCountAsync(2);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replacing);

        Assert.Equal(OnlineProviderAuthState.Disconnected, service.GetSnapshot("netease").State);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PollAsync("netease", CancellationToken.None));
    }

    [Fact]
    public async Task Failed_Challenge_Creation_Ends_In_Failed_State_Without_Leaking_Response_Details()
    {
        var handler = new ScriptedHttpHandler(
            (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)(static (_, _) =>
                throw new HttpRequestException("MUSIC_U=secret")));
        var service = CreateService(new MemoryCredentialStore(), handler, new ScriptedHttpHandler());
        var states = new List<OnlineProviderAuthState>();
        service.AccountChanged += (_, snapshot) => states.Add(snapshot.State);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateChallengeAsync("netease", CancellationToken.None));

        Assert.Equal(OnlineProviderAuthState.Failed, service.GetSnapshot("netease").State);
        Assert.Equal(OnlineProviderAuthState.Failed, states[^1]);
        Assert.DoesNotContain("secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Superseded_Poll_Cannot_Overwrite_Newer_Challenge_State()
    {
        var oldPoll = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"old\"}"),
            oldPoll,
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"new\"}"));
        var service = CreateService(new MemoryCredentialStore(), handler, new ScriptedHttpHandler());

        await service.CreateChallengeAsync("netease", CancellationToken.None);
        var staleTask = service.PollAsync("netease", CancellationToken.None);
        await handler.WaitForRequestCountAsync(2);
        var current = await service.CreateChallengeAsync("netease", CancellationToken.None);
        oldPoll.SetResult(Response(HttpStatusCode.OK, "{\"code\":803,\"cookie\":\"MUSIC_U=stale\"}"));
        await staleTask;

        Assert.Equal("https://music.163.com/login?codekey=new", current.QrPayload);
        Assert.Equal(OnlineProviderAuthState.WaitingForScan, service.GetSnapshot("netease").State);
    }

    [Fact]
    public async Task Superseded_Poll_Cannot_Leave_A_Stale_Credential_After_Save()
    {
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"old\"}"),
            Response(HttpStatusCode.OK, "{\"code\":803,\"cookie\":\"MUSIC_U=stale\"}"),
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"new\"}"));
        var store = new MemoryCredentialStore
        {
            SaveRelease = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var service = CreateService(store, handler, new ScriptedHttpHandler());

        await service.CreateChallengeAsync("netease", CancellationToken.None);
        var stalePoll = service.PollAsync("netease", CancellationToken.None);
        await store.WaitForSaveAsync();
        await service.CreateChallengeAsync("netease", CancellationToken.None);
        store.SaveRelease.SetResult();
        await stalePoll;

        Assert.False(store.ContainsCredential("netease"));
        Assert.Equal(OnlineProviderAuthState.WaitingForScan, service.GetSnapshot("netease").State);
    }

    [Fact]
    public async Task SignOut_Wins_Against_An_InFlight_Lazy_Load()
    {
        var store = new MemoryCredentialStore
        {
            LoadResult = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var service = CreateService(store, new ScriptedHttpHandler(), new ScriptedHttpHandler());

        var loading = service.GetSessionAsync("qq", CancellationToken.None);
        await store.WaitForLoadAsync();
        var signingOut = service.SignOutAsync("qq", CancellationToken.None);
        store.LoadResult.SetResult(new ProviderCredential(
            "qq",
            "{\"uin\":\"o7\",\"qqmusic_key\":\"stale\",\"qm_keyst\":\"stale\"}"));

        Assert.Null(await loading);
        await signingOut;
        Assert.False(store.ContainsCredential("qq"));
        Assert.Equal(OnlineProviderAuthState.Disconnected, service.GetSnapshot("qq").State);
    }

    [Fact]
    public async Task Cancelled_Poll_Does_Not_Change_State()
    {
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"key\"}"),
            (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)(static async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            }));
        var service = CreateService(new MemoryCredentialStore(), handler, new ScriptedHttpHandler());
        await service.CreateChallengeAsync("netease", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.PollAsync("netease", cancellation.Token));

        Assert.Equal(OnlineProviderAuthState.WaitingForScan, service.GetSnapshot("netease").State);
    }

    [Fact]
    public async Task Cancelled_Qq_Poll_Does_Not_Change_State()
    {
        var show = Response(HttpStatusCode.OK, new byte[] { 1 }, "image/png");
        show.Headers.TryAddWithoutValidation("Set-Cookie", "qrsig=abc; Path=/");
        var handler = new ScriptedHttpHandler(
            show,
            (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)(static async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            }));
        var service = CreateService(new MemoryCredentialStore(), new ScriptedHttpHandler(), handler);
        await service.CreateChallengeAsync("qq", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.PollAsync("qq", cancellation.Token));

        Assert.Equal(OnlineProviderAuthState.WaitingForScan, service.GetSnapshot("qq").State);
    }

    [Fact]
    public async Task Locally_Expired_Challenge_Raises_AccountChanged_Outside_The_Poll_Request()
    {
        var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"key\"}"));
        var service = CreateService(
            new MemoryCredentialStore(),
            handler,
            new ScriptedHttpHandler(),
            () => now);
        var states = new List<OnlineProviderAuthState>();
        service.AccountChanged += (_, snapshot) => states.Add(snapshot.State);
        await service.CreateChallengeAsync("netease", CancellationToken.None);
        now = now.AddMinutes(6);

        var snapshot = await service.PollAsync("netease", CancellationToken.None);

        Assert.Equal(OnlineProviderAuthState.Expired, snapshot.State);
        Assert.Equal(OnlineProviderAuthState.Expired, states[^1]);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Provider_Failure_Exposes_A_Sanitized_Status_Only()
    {
        var handler = new ScriptedHttpHandler(
            Response(HttpStatusCode.OK, "{\"code\":200,\"unikey\":\"key\"}"),
            (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)(static (_, _) =>
                throw new HttpRequestException("cookie=MUSIC_U=super-secret&uin=12345")));
        var service = CreateService(new MemoryCredentialStore(), handler, new ScriptedHttpHandler());
        await service.CreateChallengeAsync("netease", CancellationToken.None);

        var snapshot = await service.PollAsync("netease", CancellationToken.None);

        Assert.Equal(OnlineProviderAuthState.Failed, snapshot.State);
        Assert.DoesNotContain("super-secret", snapshot.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("12345", snapshot.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Production_Credential_Store_Uses_PasswordVault_And_Not_Settings()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "PrismWave.WinUI", "Services", "Implementations", "PasswordVaultCredentialStore.cs"));

        Assert.Contains("PasswordVault", source, StringComparison.Ordinal);
        Assert.Contains("PrismWave.OnlineAccount", source, StringComparison.Ordinal);
        Assert.Contains("CredentialNotFoundHResult", source, StringComparison.Ordinal);
        Assert.DoesNotContain("settings.json", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SettingsService", source, StringComparison.Ordinal);
    }

    private static OnlineAccountService CreateService(
        IProviderCredentialStore store,
        HttpMessageHandler netease,
        HttpMessageHandler qq,
        Func<DateTimeOffset>? utcNow = null) => new(
            store,
            new HttpClient(netease, disposeHandler: false),
            new HttpClient(qq, disposeHandler: false),
            random: () => 0.25,
            utcNow: utcNow);

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository source file.");
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] body, string contentType)
        => new(status) { Content = new ByteArrayContent(body) { Headers = { ContentType = new(contentType) } } };

    private sealed class ScriptedHttpHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = [];
        private readonly TaskCompletionSource _requestChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ScriptedHttpHandler(params object[] responses)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response switch
                {
                    HttpResponseMessage message => (_, _) => Task.FromResult(message),
                    TaskCompletionSource<HttpResponseMessage> source => (_, _) => source.Task,
                    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback => callback,
                    _ => throw new ArgumentException("Unsupported scripted response.", nameof(responses)),
                });
            }
        }

        public List<CapturedRequest> Requests { get; } = [];

        public async Task WaitForRequestCountAsync(int count)
        {
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (Requests.Count < count && DateTime.UtcNow < timeout)
            {
                await Task.Delay(10);
            }

            Assert.True(Requests.Count >= count, $"Expected {count} requests, got {Requests.Count}.");
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                body,
                request.Headers.Referrer?.ToString() ?? string.Empty,
                request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : string.Empty));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No scripted response remains.");
            }

            return await _responses.Dequeue()(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body, string Referer, string Cookie);

    private sealed class MemoryCredentialStore : IProviderCredentialStore
    {
        private readonly Dictionary<string, string> _credentials = new(StringComparer.OrdinalIgnoreCase);

        public List<ProviderCredential> Saved { get; } = [];
        public List<string> Deleted { get; } = [];
        public int LoadCount { get; private set; }
        public TaskCompletionSource<ProviderCredential?>? LoadResult { get; init; }
        public TaskCompletionSource? SaveRelease { get; init; }
        public Exception? DeleteError { get; init; }

        public void Seed(string providerKey, string secret) => _credentials[providerKey] = secret;

        public async Task<ProviderCredential?> LoadAsync(string providerKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            if (LoadResult is not null)
            {
                return await LoadResult.Task.WaitAsync(cancellationToken);
            }

            return _credentials.TryGetValue(providerKey, out var secret)
                ? new ProviderCredential(providerKey, secret)
                : null;
        }

        public async Task SaveAsync(ProviderCredential credential, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _credentials[credential.ProviderKey] = credential.Secret;
            Saved.Add(credential);
            if (SaveRelease is not null)
            {
                await SaveRelease.Task.WaitAsync(cancellationToken);
            }
        }

        public Task DeleteAsync(string providerKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DeleteError is not null)
            {
                throw DeleteError;
            }

            _credentials.Remove(providerKey);
            Deleted.Add(providerKey);
            return Task.CompletedTask;
        }

        public bool ContainsCredential(string providerKey) => _credentials.ContainsKey(providerKey);

        public async Task WaitForLoadAsync()
        {
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (LoadCount == 0 && DateTime.UtcNow < timeout)
            {
                await Task.Delay(10);
            }

            Assert.True(LoadCount > 0, "The credential load did not start.");
        }

        public async Task WaitForSaveAsync()
        {
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (Saved.Count == 0 && DateTime.UtcNow < timeout)
            {
                await Task.Delay(10);
            }

            Assert.NotEmpty(Saved);
        }
    }
}
