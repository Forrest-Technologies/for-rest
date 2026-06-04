using ForRest.Services.AI.OAuth;

namespace ForRest.Tests.Services.OAuth;

[TestClass]
public sealed class RedirectUriMatcherTests
{
    [TestMethod]
    public void IsRedirect_matches_when_only_query_differs()
    {
        Assert.IsTrue(RedirectUriMatcher.IsRedirect(
            "http://127.0.0.1:5050/callback?code=abc&state=xyz",
            "http://127.0.0.1:5050/callback"));
    }

    [TestMethod]
    public void IsRedirect_matches_non_loopback_provider_callback()
    {
        Assert.IsTrue(RedirectUriMatcher.IsRedirect(
            "https://oauth.pstmn.io/v1/callback?code=abc&state=xyz",
            "https://oauth.pstmn.io/v1/callback"));
    }

    [TestMethod]
    public void IsRedirect_ignores_trailing_slash_on_path()
    {
        Assert.IsTrue(RedirectUriMatcher.IsRedirect(
            "https://app.example.com/cb/?code=abc",
            "https://app.example.com/cb"));
    }

    [TestMethod]
    public void IsRedirect_is_case_insensitive_on_scheme_and_host()
    {
        Assert.IsTrue(RedirectUriMatcher.IsRedirect(
            "HTTPS://App.Example.COM/cb?code=abc",
            "https://app.example.com/cb"));
    }

    [TestMethod]
    public void IsRedirect_does_not_match_different_path()
    {
        Assert.IsFalse(RedirectUriMatcher.IsRedirect(
            "https://app.example.com/login?next=1",
            "https://app.example.com/cb"));
    }

    [TestMethod]
    public void IsRedirect_does_not_match_different_port()
    {
        Assert.IsFalse(RedirectUriMatcher.IsRedirect(
            "http://127.0.0.1:6060/callback?code=abc",
            "http://127.0.0.1:5050/callback"));
    }

    [TestMethod]
    public void IsRedirect_does_not_match_different_host()
    {
        Assert.IsFalse(RedirectUriMatcher.IsRedirect(
            "https://evil.example.com/cb?code=abc",
            "https://app.example.com/cb"));
    }

    [TestMethod]
    public void IsRedirect_returns_false_for_intermediate_authorize_navigation()
    {
        Assert.IsFalse(RedirectUriMatcher.IsRedirect(
            "https://login.example.com/authorize?client_id=abc&redirect_uri=https://app.example.com/cb",
            "https://app.example.com/cb"));
    }

    [TestMethod]
    [DataRow(null, "https://app.example.com/cb")]
    [DataRow("https://app.example.com/cb?code=abc", null)]
    [DataRow("", "https://app.example.com/cb")]
    [DataRow("not a url", "https://app.example.com/cb")]
    public void IsRedirect_returns_false_for_invalid_inputs(string? navigatedUrl, string? redirectUri)
    {
        Assert.IsFalse(RedirectUriMatcher.IsRedirect(navigatedUrl, redirectUri));
    }
}
