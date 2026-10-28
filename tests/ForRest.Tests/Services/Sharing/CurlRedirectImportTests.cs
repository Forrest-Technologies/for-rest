using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class CurlRedirectImportTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("", false)]
    [DataRow("-L", true)]
    [DataRow("--location", true)]
    [DataRow("--no-location", false)]
    [DataRow("-L --no-location", false)]
    [DataRow("--no-location -L", true)]
    [DataRow("-L --no-location --location", true)]
    [DataRow("--location --no-location --no-location", false)]
    public void Prepared_requests_follow_the_last_redirect_option_and_curls_default(string options, bool follow)
    {
        var source = new CurlImportService().ConvertToScript($"curl {options} https://fixture.test/", out _);
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());
        Assert.IsTrue(compilation.Succeeded);
        var prepared = new RequestCompiler(new VariableResolver()).Prepare(compilation.Payload!.Request, [], [], [], [], []);

        Assert.IsTrue(prepared.Succeeded, prepared.ErrorMessage);
        Assert.AreEqual(follow, prepared.Value!.FollowRedirects);
        Assert.DoesNotContain("ignored curl flags", source);
    }

    #endregion
}
