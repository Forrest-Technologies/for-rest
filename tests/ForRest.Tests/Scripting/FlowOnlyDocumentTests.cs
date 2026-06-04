using ForRest.Browser;
using ForRest.Scripting;

namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class FlowOnlyDocumentTests
{
    private static ForRestScriptCompilationResult Compile(string source) =>
        new ForRestScriptCompiler(new ForRestScriptParser()).Compile(source, new ForRestScriptCompilationOptions());

    [TestMethod]
    public void Browser_only_document_compiles_as_flow_only()
    {
        ForRestScriptCompilationResult result = Compile(
            "await browser.navigate(\"https://app.test\")\nawait browser.click(\"#go\")");

        Assert.IsTrue(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.IsTrue(result.Payload!.Request.FlowOnly);
    }

    [TestMethod]
    public void Recorder_output_compiles()
    {
        BrowserRecordingScriptBuilder builder = new();
        builder.AddNavigation("https://app.test/login");
        builder.AddEventJson("""{"type":"click","selectorKind":"css","selector":"#go"}""");

        ForRestScriptCompilationResult result = Compile(builder.Build());

        Assert.IsTrue(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
    }

    [TestMethod]
    public void Request_document_still_requires_method_and_url()
    {
        // A partial/empty request envelope (url but no method) must still error.
        ForRestScriptCompilationResult result = Compile("url \"https://api.test\"");

        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public void Normal_request_document_is_not_flow_only()
    {
        ForRestScriptCompilationResult result = Compile("method GET\nurl \"https://api.test\"\nexpect status == 200 \"ok\"");

        Assert.IsTrue(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.IsFalse(result.Payload!.Request.FlowOnly);
    }
}
