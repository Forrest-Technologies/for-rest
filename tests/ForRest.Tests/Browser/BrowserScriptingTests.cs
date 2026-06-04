using System.Linq;
using ForRest.Browser;
using ForRest.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ForRest.Tests.Browser;

[TestClass]
public sealed class BrowserScriptingTests
{
    [TestMethod]
    public async Task Script_can_drive_browser_through_bridge()
    {
        RecordingBrowserBridge bridge = new() { TextResult = "Welcome" };
        RoslynScriptEngine engine = new(NullLogger<RoslynScriptEngine>.Instance);

        ScriptExecutionResult result = await engine.Run(new ScriptExecutionRequest
        {
            Script =
                """
                await browser.navigate("https://app.test/login");
                await browser.type("#email", "demo");
                await browser.click("role=button:Sign in");
                var heading = await browser.getText("#welcome");
                console.Log(heading);
                """,
            BrowserBridge = bridge,
        });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        CollectionAssert.Contains(bridge.Calls, "navigate:https://app.test/login");
        CollectionAssert.Contains(bridge.Calls, "type:css=#email=demo");
        CollectionAssert.Contains(bridge.Calls, "click:role=button:Sign in");
        Assert.IsTrue(result.ConsoleEntries.Any(entry => entry.Message.Contains("Welcome")));
    }

    [TestMethod]
    public async Task Browser_calls_fail_clearly_when_no_pane_connected()
    {
        RoslynScriptEngine engine = new(NullLogger<RoslynScriptEngine>.Instance);

        ScriptExecutionResult result = await engine.Run(new ScriptExecutionRequest
        {
            Script = "await browser.navigate(\"https://x.test\");",
        });

        Assert.IsTrue(result.ErrorMessage.Contains("No browser pane is connected"));
    }

    [TestMethod]
    public async Task Payloads_global_is_available_to_raw_scripts()
    {
        // Regression: the script preamble previously omitted `payloads`, so any script (including the
        // output of build_attack_script) that referenced it failed to compile.
        RoslynScriptEngine engine = new(NullLogger<RoslynScriptEngine>.Instance);

        ScriptExecutionResult result = await engine.Run(new ScriptExecutionRequest
        {
            Script =
                """
                var list = payloads.Category("xss");
                console.Log(list.Count.ToString());
                """,
        });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.IsTrue(result.ConsoleEntries.Any(entry => entry.Message.Trim().Length > 0 && entry.Message.Trim() != "0"));
    }
}
