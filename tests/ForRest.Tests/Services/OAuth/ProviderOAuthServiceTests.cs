using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web;
using ForRest.Services.AI;
using ForRest.Services.AI.OAuth;

namespace ForRest.Tests.Services.OAuth;

[TestClass]
public sealed class ProviderOAuthServiceTests
{
    [TestMethod]
    public void StartAuthorization_builds_url_with_required_pkce_parameters()
    {
        var service = CreateService(out _, out _);

        var session = service.StartAuthorization(AiProviderKind.OpenAI, OAuthCompletionMode.ManualPaste);

        var uri = new Uri(session.AuthorizationUrl);
        var query = HttpUtility.ParseQueryString(uri.Query);

        Assert.AreEqual("code", query["response_type"]);
        Assert.IsFalse(string.IsNullOrEmpty(query["client_id"]));
        Assert.IsFalse(string.IsNullOrEmpty(query["redirect_uri"]));
        Assert.IsFalse(string.IsNullOrEmpty(query["scope"]));
        Assert.AreEqual(session.State, query["state"]);
        Assert.AreEqual(session.Pkce.CodeChallenge, query["code_challenge"]);
        Assert.AreEqual("S256", query["code_challenge_method"]);
    }

    [TestMethod]
    public void StartAuthorization_uses_provided_config_override()
    {
        var service = CreateService(out _, out _);
        var config = new OAuthProviderConfig
        {
            Provider = AiProviderKind.Custom,
            AuthorizationEndpoint = "https://auth.example.test/authorize",
            TokenEndpoint = "https://auth.example.test/token",
            ClientId = "my-client",
            Scopes = ["a", "b"],
            FixedRedirectUri = "https://app.example.test/cb",
        };

        var session = service.StartAuthorization(AiProviderKind.Custom, OAuthCompletionMode.ManualPaste, config);

        var query = HttpUtility.ParseQueryString(new Uri(session.AuthorizationUrl).Query);
        Assert.AreEqual("my-client", query["client_id"]);
        Assert.AreEqual("a b", query["scope"]);
        Assert.AreEqual("https://app.example.test/cb", session.RedirectUri);
        Assert.AreEqual("https://app.example.test/cb", query["redirect_uri"]);
    }

    [TestMethod]
    public void ParsePastedRedirect_validates_state()
    {
        var service = CreateService(out _, out _);
        var session = service.StartAuthorization(AiProviderKind.Grok, OAuthCompletionMode.ManualPaste);

        var good = service.ParsePastedRedirect(session, $"http://127.0.0.1/cb?code=good&state={session.State}");
        Assert.AreEqual("good", good.Code);

        Assert.ThrowsExactly<OAuthAuthorizationException>(
            () => service.ParsePastedRedirect(session, "http://127.0.0.1/cb?code=good&state=bad"));
    }

    [TestMethod]
    public async Task CompleteAuthorization_posts_pkce_exchange_and_parses_token()
    {
        FakeHandler? handler = null;
        var service = CreateService(out handler, out var store, tokenJson:
            """
            {"access_token":"AT-1","refresh_token":"RT-1","expires_in":3600,"token_type":"Bearer","scope":"openid api","id_token":"ID-1"}
            """);

        var session = service.StartAuthorization(AiProviderKind.Grok, OAuthCompletionMode.ManualPaste);
        var token = await service.CompleteAuthorization(session, "auth-code-123");

        // Assert request body.
        var form = ParseForm(handler!.LastRequestBody!);
        Assert.AreEqual("authorization_code", form["grant_type"]);
        Assert.AreEqual("auth-code-123", form["code"]);
        Assert.AreEqual(session.Pkce.CodeVerifier, form["code_verifier"]);
        Assert.AreEqual(session.Config.ClientId, form["client_id"]);
        Assert.AreEqual(session.RedirectUri, form["redirect_uri"]);
        Assert.AreEqual(session.Config.TokenEndpoint, handler.LastRequestUri!.ToString());

        // Assert parsed token.
        Assert.AreEqual("AT-1", token.AccessToken);
        Assert.AreEqual("RT-1", token.RefreshToken);
        Assert.AreEqual("Bearer", token.TokenType);
        Assert.AreEqual("openid api", token.Scope);
        Assert.AreEqual("ID-1", token.IdToken);
        Assert.IsTrue(token.ExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(50));

        // Persisted.
        var loaded = await store.Load(AiProviderKind.Grok);
        Assert.AreEqual("AT-1", loaded!.AccessToken);
    }

    [TestMethod]
    public async Task RefreshToken_posts_refresh_grant_and_carries_refresh_token_forward()
    {
        var service = CreateService(out var handler, out _, tokenJson:
            """
            {"access_token":"AT-2","expires_in":1800,"token_type":"Bearer"}
            """);

        var existing = new ProviderOAuthToken
        {
            Provider = AiProviderKind.OpenAI,
            AccessToken = "AT-old",
            RefreshToken = "RT-old",
            ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-10),
        };

        var refreshed = await service.RefreshToken(AiProviderKind.OpenAI, existing);

        var form = ParseForm(handler!.LastRequestBody!);
        Assert.AreEqual("refresh_token", form["grant_type"]);
        Assert.AreEqual("RT-old", form["refresh_token"]);

        Assert.AreEqual("AT-2", refreshed.AccessToken);
        // Provider omitted refresh_token; prior one should carry forward.
        Assert.AreEqual("RT-old", refreshed.RefreshToken);
        Assert.IsTrue(refreshed.ExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(25));
    }

    [TestMethod]
    public async Task RefreshToken_without_refresh_token_throws()
    {
        var service = CreateService(out _, out _);
        var existing = new ProviderOAuthToken { Provider = AiProviderKind.OpenAI, AccessToken = "x" };

        await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(
            () => service.RefreshToken(AiProviderKind.OpenAI, existing));
    }

    [TestMethod]
    public async Task GetValidToken_returns_cached_non_expired_token_without_http_call()
    {
        var service = CreateService(out var handler, out var store);
        await store.Save(AiProviderKind.Grok, new ProviderOAuthToken
        {
            Provider = AiProviderKind.Grok,
            AccessToken = "valid",
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
        });

        var token = await service.GetValidToken(AiProviderKind.Grok);

        Assert.AreEqual("valid", token!.AccessToken);
        Assert.AreEqual(0, handler!.CallCount);
    }

    [TestMethod]
    public async Task GetValidToken_refreshes_when_expired()
    {
        var service = CreateService(out _, out var store, tokenJson:
            """
            {"access_token":"refreshed","expires_in":3600}
            """);
        await store.Save(AiProviderKind.OpenAI, new ProviderOAuthToken
        {
            Provider = AiProviderKind.OpenAI,
            AccessToken = "stale",
            RefreshToken = "RT",
            ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
        });

        var token = await service.GetValidToken(AiProviderKind.OpenAI);

        Assert.AreEqual("refreshed", token!.AccessToken);
    }

    [TestMethod]
    public async Task CompleteAuthorization_throws_on_error_response()
    {
        var service = CreateService(out _, out _, tokenJson: """{"error":"invalid_grant"}""", statusCode: HttpStatusCode.BadRequest);
        var session = service.StartAuthorization(AiProviderKind.Grok, OAuthCompletionMode.ManualPaste);

        await Assert.ThrowsExactlyAsync<OAuthAuthorizationException>(
            () => service.CompleteAuthorization(session, "code"));
    }

    [TestMethod]
    public async Task Loopback_round_trip_captures_code_from_real_redirect()
    {
        var port = LoopbackRedirectListener.GetFreeLoopbackPort();
        const string state = "state-token";
        using var listener = new LoopbackRedirectListener(port, "/callback", state);
        listener.Start();

        var captureTask = listener.CaptureCode();

        using var client = new HttpClient();
        var redirect = $"{listener.RedirectUri}?code=loopback-code&state={state}";
        using var response = await client.GetAsync(redirect);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var result = await captureTask;
        Assert.AreEqual("loopback-code", result.Code);
        Assert.AreEqual(state, result.State);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompleteAuthorization_does_not_cache_token_when_storage_fails(bool cancelled)
    {
        using var client = new HttpClient(new FakeHandler(
            """{"access_token":"new-token","expires_in":3600}""", HttpStatusCode.OK));
        var store = new FailingTokenStore(null, cancelled);
        var service = new ProviderOAuthService(NullLogger<ProviderOAuthService>.Instance, store, client);
        var session = service.StartAuthorization(AiProviderKind.OpenAI, OAuthCompletionMode.ManualPaste);

        await AssertStorageFailure(() => service.CompleteAuthorization(session, "code"), cancelled);

        Assert.IsNull(await service.GetValidToken(AiProviderKind.OpenAI));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RefreshToken_preserves_cached_token_when_storage_fails(bool cancelled)
    {
        var existing = new ProviderOAuthToken
        {
            Provider = AiProviderKind.OpenAI,
            AccessToken = "previous-token",
            RefreshToken = "previous-refresh-token",
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
        };
        using var client = new HttpClient(new FakeHandler(
            """{"access_token":"new-token","refresh_token":"new-refresh-token","expires_in":3600}""",
            HttpStatusCode.OK));
        var store = new FailingTokenStore(existing, cancelled);
        var service = new ProviderOAuthService(NullLogger<ProviderOAuthService>.Instance, store, client);
        Assert.AreSame(existing, await service.GetValidToken(AiProviderKind.OpenAI));

        await AssertStorageFailure(() => service.RefreshToken(AiProviderKind.OpenAI, existing), cancelled);

        Assert.AreSame(existing, await service.GetValidToken(AiProviderKind.OpenAI));
    }

    #region Helpers

    private static async Task AssertStorageFailure(Func<Task<ProviderOAuthToken>> action, bool cancelled)
    {
        if (cancelled)
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(action);
        }
        else
        {
            await Assert.ThrowsExactlyAsync<IOException>(action);
        }
    }

    private sealed class FailingTokenStore(ProviderOAuthToken? storedToken, bool cancelled) : IProviderTokenStore
    {
        #region Public Methods

        public Task Save(AiProviderKind provider, ProviderOAuthToken token, CancellationToken cancellationToken = default)
            => cancelled
                ? Task.FromException(new OperationCanceledException())
                : Task.FromException(new IOException("Fixture token store unavailable."));

        public Task<ProviderOAuthToken?> Load(AiProviderKind provider, CancellationToken cancellationToken = default)
            => Task.FromResult(storedToken);

        public Task Clear(AiProviderKind provider, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        #endregion
    }

    private static ProviderOAuthService CreateService(
        out FakeHandler handler,
        out InMemoryProviderTokenStore store,
        string tokenJson = """{"access_token":"AT","expires_in":3600}""",
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        handler = new FakeHandler(tokenJson, statusCode);
        store = new InMemoryProviderTokenStore();
        return new ProviderOAuthService(NullLogger<ProviderOAuthService>.Instance, store, new HttpClient(handler));
    }

    private static Dictionary<string, string> ParseForm(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(pair[..idx]);
            var value = Uri.UnescapeDataString(pair[(idx + 1)..].Replace('+', ' '));
            result[key] = value;
        }

        return result;
    }

    private sealed class FakeHandler(string responseJson, HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public string? LastRequestBody { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson),
            };
        }
    }

    #endregion
}
