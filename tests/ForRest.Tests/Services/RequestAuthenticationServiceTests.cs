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
