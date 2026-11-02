using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class CurlFileInputTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("-d @fixture.txt")]
    [DataRow("-d@fixture.txt")]
    [DataRow("--data=@fixture.txt")]
    [DataRow("--data-ascii @fixture.txt")]
    [DataRow("--data-binary @fixture.bin")]
    [DataRow("--data-binary @-")]
    [DataRow("--json @fixture.json")]
    [DataRow("--data-urlencode @fixture.txt")]
    [DataRow("--data-urlencode field@fixture.txt")]
    [DataRow("--header @headers.txt")]
    [DataRow("-H@headers.txt")]
    [DataRow("--cookie cookies.txt")]
    [DataRow("-bcookies.txt")]
    [DataRow("--cookie -")]
    [DataRow("-F field=value")]
    [DataRow("-Ffield=@fixture.txt")]
    [DataRow("--form=field=value")]
    [DataRow("--form-string field=@literal")]
    [DataRow("-T fixture.bin")]
    [DataRow("-Tfixture.bin")]
    [DataRow("--upload-file=fixture.bin")]
    public void Unsupported_input_sources_are_rejected_instead_of_changing_the_request(string option)
    {
        var exception = Assert.ThrowsExactly<FormatException>(() => new CurlImportService().ConvertToScript(
            $"curl https://fixture.test/ {option}", out _));

        StringAssert.Contains(exception.Message, "import");
    }

    [TestMethod]
    public void Raw_data_keeps_a_literal_at_prefix()
    {
        var source = new CurlImportService().ConvertToScript("curl --data-raw @literal https://fixture.test/", out _);
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());

        Assert.IsTrue(compilation.Succeeded);
        Assert.AreEqual("@literal", compilation.Payload!.Request.Body.RawContent.Trim());
    }

    [TestMethod]
    [DataRow("=a b", "a%20b")]
    [DataRow("=a=b", "a%3Db")]
    [DataRow("=name@fixture", "name%40fixture")]
    [DataRow("name=a@b", "name=a%40b")]
    [DataRow("a b", "a%20b")]
    public void Urlencoded_literal_values_follow_curl_separator_rules(string value, string expected)
    {
        var source = new CurlImportService().ConvertToScript($"curl --data-urlencode '{value}' https://fixture.test/", out _);
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());

        Assert.IsTrue(compilation.Succeeded);
        Assert.AreEqual(expected, compilation.Payload!.Request.Body.RawContent.Trim());
    }

    #endregion
}
