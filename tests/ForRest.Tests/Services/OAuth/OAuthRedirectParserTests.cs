using ForRest.Services.AI.OAuth;

namespace ForRest.Tests.Services.OAuth;

[TestClass]
public sealed class OAuthRedirectParserTests
{
    [TestMethod]
    public void Parse_extracts_code_and_state_from_redirect_url()
    {
        var result = OAuthRedirectParser.Parse("http://127.0.0.1:5050/callback?code=abc123&state=xyz");

        Assert.AreEqual("abc123", result.Code);
        Assert.AreEqual("xyz", result.State);
    }

    [TestMethod]
    public void Parse_treats_bare_code_as_code_with_null_state()
    {
        var result = OAuthRedirectParser.Parse("raw-code-value");

        Assert.AreEqual("raw-code-value", result.Code);
        Assert.IsNull(result.State);
    }

    [TestMethod]
    public void Parse_url_decodes_values()
    {
        var result = OAuthRedirectParser.Parse("http://127.0.0.1/cb?code=a%2Fb%2Bc&state=s%20t");

        Assert.AreEqual("a/b+c", result.Code);
        Assert.AreEqual("s t", result.State);
    }

    [TestMethod]
    public void ParseAndValidate_succeeds_on_matching_state()
    {
        var result = OAuthRedirectParser.ParseAndValidate("http://127.0.0.1/cb?code=c1&state=expected", "expected");

        Assert.AreEqual("c1", result.Code);
    }

    [TestMethod]
    public void ParseAndValidate_rejects_mismatched_state()
    {
        Assert.ThrowsExactly<OAuthAuthorizationException>(
            () => OAuthRedirectParser.ParseAndValidate("http://127.0.0.1/cb?code=c1&state=wrong", "expected"));
    }

    [TestMethod]
    public void Parse_surfaces_provider_error()
    {
        Assert.ThrowsExactly<OAuthAuthorizationException>(
            () => OAuthRedirectParser.Parse("http://127.0.0.1/cb?error=access_denied&error_description=User%20declined"));
    }

    [TestMethod]
    public void Parse_throws_when_url_has_no_code()
    {
        Assert.ThrowsExactly<OAuthAuthorizationException>(
            () => OAuthRedirectParser.Parse("http://127.0.0.1/cb?state=only"));
    }

    [TestMethod]
    public void Parse_throws_on_empty_input()
    {
        Assert.ThrowsExactly<OAuthAuthorizationException>(() => OAuthRedirectParser.Parse("   "));
    }
}
