using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace ForRest.Tests.Services;

[TestClass]
public sealed class RequestAuthenticationServiceTests
{
    [TestMethod]
    public async Task PrepareAsync_applies_client_credentials_token_to_custom_header()
    {
        RequestAuthenticationService service = new(
            NullLogger<RequestAuthenticationService>.Instance,
            new HttpClient(new StubMessageHandler("""{"access_token":"token-123","expires_in":3600}""")));
        PreparedRequest request = new()
        {
            Uri = new Uri("https://api.example.test/data"),
            Auth = new()
            {
                Mode = AuthMode.OAuthClientCredentials,
                TokenUrl = "https://login.example.test/oauth2/v2.0/token",
                ClientId = "desktop-client",
                ClientSecret = "super-secret",
                Scopes = "api://forrest/.default",
                HeaderName = "X-Access-Token",
                Scheme = string.Empty,
                ApiKeyLocation = ApiKeyLocation.Header,
            },
        };

        AuthenticatedPreparedRequest result = await service.PrepareAsync(request);

        Assert.AreEqual("token-123", result.Request.Headers.Single(static item => item.Key == "X-Access-Token").Value);
        StringAssert.Contains(result.Request.RawRequest, "X-Access-Token: token-123");
    }

    [TestMethod]
    public async Task PrepareAsync_places_oauth_token_in_query_string_when_requested()
    {
        RequestAuthenticationService service = new(
            NullLogger<RequestAuthenticationService>.Instance,
            new HttpClient(new StubMessageHandler("""{"access_token":"token-xyz","expires_in":3600}""")));
        PreparedRequest request = new()
        {
            Uri = new Uri("https://api.example.test/data?existing=1"),
            Auth = new()
            {
                Mode = AuthMode.OAuthClientCredentials,
                TokenUrl = "https://login.example.test/oauth2/v2.0/token",
                ClientId = "desktop-client",
                ClientSecret = "super-secret",
                QueryParameterName = "access_token",
                Scheme = string.Empty,
                ApiKeyLocation = ApiKeyLocation.Query,
            },
        };

        AuthenticatedPreparedRequest result = await service.PrepareAsync(request);

        Assert.AreEqual("existing=1&access_token=token-xyz", result.Request.Uri.Query.TrimStart('?'));
    }

    [TestMethod]
    public async Task PrepareAsync_builds_negotiate_transport_from_default_credentials()
    {
        RequestAuthenticationService service = new(NullLogger<RequestAuthenticationService>.Instance);
        PreparedRequest request = new()
        {
            Uri = new Uri("https://api.example.test/data"),
            Auth = new()
            {
                Mode = AuthMode.Negotiate,
                UseDefaultCredentials = true,
            },
        };

        AuthenticatedPreparedRequest result = await service.PrepareAsync(request);

        Assert.IsTrue(result.Transport.UseDefaultCredentials);
        Assert.IsNull(result.Transport.Credentials);
    }

    [TestMethod]
    public async Task PrepareAsync_authorization_code_builds_authorize_url_with_pkce_and_exchanges_code()
    {
        CapturingMessageHandler handler = new("""{"access_token":"tok-ac","expires_in":3600,"refresh_token":"refresh-1"}""");
        RecordingBroker broker = new("auth-code-123");
        RequestAuthenticationService service = new(NullLogger<RequestAuthenticationService>.Instance, new HttpClient(handler), broker);
        PreparedRequest request = new()
        {
            Uri = new Uri("https://api.example.test/data"),
            Auth = new()
            {
                Mode = AuthMode.OAuthAuthorizationCode,
                AuthorizationUrl = "https://login.example.test/authorize",
                TokenUrl = "https://login.example.test/token",
                ClientId = "public-client",
                RedirectUri = "http://127.0.0.1:5005/callback",
                Scopes = "openid api",
                UsePkce = true,
            },
        };

        AuthenticatedPreparedRequest result = await service.PrepareAsync(request);

        Assert.AreEqual("Bearer tok-ac", result.Request.Headers.Single(static item => item.Key == "Authorization").Value);
        Assert.AreEqual("tok-ac", result.AcquiredAccessToken);

        string authorizeUrl = broker.Context!.AuthorizationUrl;
        StringAssert.Contains(authorizeUrl, "https://login.example.test/authorize?");
        StringAssert.Contains(authorizeUrl, "response_type=code");
        StringAssert.Contains(authorizeUrl, "client_id=public-client");
        StringAssert.Contains(authorizeUrl, "code_challenge=");
        StringAssert.Contains(authorizeUrl, "code_challenge_method=S256");
        StringAssert.Contains(authorizeUrl, "redirect_uri=http%3A%2F%2F127.0.0.1%3A5005%2Fcallback");
        StringAssert.Contains(authorizeUrl, "state=");

        StringAssert.Contains(handler.LastBody!, "grant_type=authorization_code");
        StringAssert.Contains(handler.LastBody!, "code=auth-code-123");
        StringAssert.Contains(handler.LastBody!, "code_verifier=");
        StringAssert.Contains(handler.LastBody!, "redirect_uri=http");
    }

    [TestMethod]
    public async Task PrepareAsync_authorization_code_without_pkce_omits_challenge_and_verifier()
    {
        CapturingMessageHandler handler = new("""{"access_token":"tok-2","expires_in":3600}""");
        RecordingBroker broker = new("code-2");
        RequestAuthenticationService service = new(NullLogger<RequestAuthenticationService>.Instance, new HttpClient(handler), broker);
        PreparedRequest request = new()
        {
            Uri = new Uri("https://api.example.test/data"),
            Auth = new()
            {
                Mode = AuthMode.OAuthAuthorizationCode,
                AuthorizationUrl = "https://login.example.test/authorize",
                TokenUrl = "https://login.example.test/token",
                ClientId = "confidential-client",
                ClientSecret = "shh",
                RedirectUri = "http://127.0.0.1:5099/callback",
                UsePkce = false,
            },
        };

        await service.PrepareAsync(request);

        Assert.IsFalse(broker.Context!.AuthorizationUrl.Contains("code_challenge", StringComparison.Ordinal));
        Assert.IsFalse(handler.LastBody!.Contains("code_verifier", StringComparison.Ordinal));
        StringAssert.Contains(handler.LastBody!, "client_secret=shh");
    }

    [TestMethod]
    public async Task PrepareAsync_authorization_code_caches_token_and_does_not_reprompt()
    {
        CapturingMessageHandler handler = new("""{"access_token":"tok-cache","expires_in":3600}""");
        RecordingBroker broker = new("code-cache");
        RequestAuthenticationService service = new(NullLogger<RequestAuthenticationService>.Instance, new HttpClient(handler), broker);
        PreparedRequest request = new()
        {
            Uri = new Uri("https://api.example.test/data"),
            Auth = new()
            {
                Mode = AuthMode.OAuthAuthorizationCode,
                AuthorizationUrl = "https://login.example.test/authorize",
                TokenUrl = "https://login.example.test/token",
                ClientId = "public-client",
                RedirectUri = "http://127.0.0.1:5006/callback",
            },
        };

        await service.PrepareAsync(request);
        await service.PrepareAsync(request);

        Assert.AreEqual(1, broker.Invocations, "interactive sign-in should happen once, then the cached token is reused");
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task PrepareAsync_authorization_code_non_loopback_redirect_without_custom_broker_fails_clearly()
    {
        RequestAuthenticationService service = new(
            NullLogger<RequestAuthenticationService>.Instance,
            new HttpClient(new StubMessageHandler("{}")));
        PreparedRequest request = new()
        {
            Uri = new Uri("https://api.example.test/data"),
            Auth = new()
            {
                Mode = AuthMode.OAuthAuthorizationCode,
                AuthorizationUrl = "https://login.example.test/authorize",
                TokenUrl = "https://login.example.test/token",
                ClientId = "public-client",
                RedirectUri = "https://oauth.pstmn.io/v1/callback",
            },
        };

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.PrepareAsync(request));
        StringAssert.Contains(exception.Message, "non-loopback");
    }

    private sealed class RecordingBroker(string code) : IInteractiveAuthorizationBroker
    {
        public InteractiveAuthorizationContext? Context { get; private set; }

        public int Invocations { get; private set; }

        public Task<string> AcquireAuthorizationCode(InteractiveAuthorizationContext context, CancellationToken cancellationToken)
        {
            Context = context;
            Invocations++;
            return Task.FromResult(code);
        }
    }

    private sealed class CapturingMessageHandler(string content) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StubMessageHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };

            return Task.FromResult(response);
        }
    }
}
