using System.Collections.Generic;
using System.Linq;

namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class ForRestLanguageCatalogTests
{
    [TestMethod]
    public void GetEntries_exposes_supported_flow_and_request_topics()
    {
        IReadOnlyList<ForRestLanguageHelpEntry> entries = ForRestLanguageCatalog.GetEntries();

        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.Any(static entry => entry.Key == "request"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "request-send"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "timeout"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "redirects"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "ssl"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "history"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "max-send-iterations"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "auth-mode"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "auth-client-credentials"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "auth-negotiate"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "stash"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "request-url"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "request-method"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "request-body"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "request-content-type"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "extract-regex"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "range-literal"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "range-function"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "batch-stash-loop"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "api-surface-crud"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "workspace-execute"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "logic-aliases"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "strings-replace"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "convert-to-bool"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "time-parse"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "let"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "log-warn-error"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "tests-api"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "runtime-functions"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "string-interpolation"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "break-continue"));
        Assert.AreEqual(entries.Count, entries.Select(static entry => entry.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void BuildMonacoCatalogJson_serializes_labels_examples_and_completion_metadata()
    {
        string json = ForRestLanguageCatalog.BuildMonacoCatalogJson();

        StringAssert.Contains(json, "\"label\":\"request.send()\"");
        StringAssert.Contains(json, "\"label\":\"timeout\"");
        StringAssert.Contains(json, "\"label\":\"ssl\"");
        StringAssert.Contains(json, "\"label\":\"history\"");
        StringAssert.Contains(json, "\"label\":\"stash\"");
        StringAssert.Contains(json, "\"label\":\"request.url\"");
        StringAssert.Contains(json, "\"label\":\"request.method\"");
        StringAssert.Contains(json, "\"label\":\"request.body\"");
        StringAssert.Contains(json, "\"label\":\"request.content_type\"");
        StringAssert.Contains(json, "\"key\":\"batch-stash-loop\"");
        StringAssert.Contains(json, "\"key\":\"api-surface-crud\"");
        StringAssert.Contains(json, "\"label\":\"workspace.execute()\"");
        StringAssert.Contains(json, "\"label\":\"expect ... regex\"");
        StringAssert.Contains(json, "\"label\":\"[0..9]\"");
        StringAssert.Contains(json, "\"label\":\"strings.Replace / strings.Split\"");
        StringAssert.Contains(json, "\"label\":\"convert.ToBool / convert.ToInt\"");
        StringAssert.Contains(json, "\"label\":\"time.Parse / time.Format\"");
        StringAssert.Contains(json, "\"kind\":\"Method\"");
        StringAssert.Contains(json, "\"example\":\"let attempts = [0..2]");
        StringAssert.Contains(json, "\"key\":\"let\"");
        StringAssert.Contains(json, "\"key\":\"log-warn-error\"");
        StringAssert.Contains(json, "\"key\":\"tests-api\"");
        StringAssert.Contains(json, "\"key\":\"runtime-functions\"");
    }

    [TestMethod]
    public void BuildMarkdownReference_includes_request_auth_and_flow_surfaces()
    {
        string markdown = ForRestLanguageCatalog.BuildMarkdownReference();

        StringAssert.Contains(markdown, "Canonical source: `ForRestLanguageCatalog`.");
        StringAssert.Contains(markdown, "| `ssl` | Control certificate validation |");
        StringAssert.Contains(markdown, "| `history` | Persist the run to history |");
        StringAssert.Contains(markdown, "| `max_send_iterations` | Bound `request.send()` loops |");
        StringAssert.Contains(markdown, "request.ssl");
        StringAssert.Contains(markdown, "request.url");
        StringAssert.Contains(markdown, "request.method");
        StringAssert.Contains(markdown, "request.body");
        StringAssert.Contains(markdown, "request.content_type");
        StringAssert.Contains(markdown, "loop + stash pattern");
        StringAssert.Contains(markdown, "CRUD send loop");
        StringAssert.Contains(markdown, "oauth_integrated_windows");
        StringAssert.Contains(markdown, "workspace.execute(\"Request Name\")");
        StringAssert.Contains(markdown, "strings.Replace / strings.Split");
        StringAssert.Contains(markdown, "convert.ToBool / convert.ToInt");
        StringAssert.Contains(markdown, "time.Parse / time.Format");
        StringAssert.Contains(markdown, "switch / case / default");
        StringAssert.Contains(markdown, "| `let` |");
        StringAssert.Contains(markdown, "log / warn / error");
        StringAssert.Contains(markdown, "tests.Assert / tests.Equal");
        StringAssert.Contains(markdown, "guid() / now()");
        StringAssert.Contains(markdown, "break / continue");
    }

    [TestMethod]
    public void BuildPromptContext_compacts_the_same_source_of_truth()
    {
        string prompt = ForRestLanguageCatalog.BuildPromptContext();

        StringAssert.Contains(prompt, "Request surface");
        StringAssert.Contains(prompt, "`timeout`: Set the request timeout in milliseconds.");
        StringAssert.Contains(prompt, "`ssl`: Control certificate validation for the request transport.");
        StringAssert.Contains(prompt, "`history`: Persist the response to execution history.");
        StringAssert.Contains(prompt, "`request.send()`: Send the current request, update the global response, and return the current response snapshot.");
        StringAssert.Contains(prompt, "`request.method`: Mutate the outgoing HTTP method from flow code.");
        StringAssert.Contains(prompt, "`request.body`: Mutate the outgoing raw request body from flow code.");
        StringAssert.Contains(prompt, "`request.content_type`: Mutate the outgoing content type from flow code.");
        StringAssert.Contains(prompt, "Auth modes");
        StringAssert.Contains(prompt, "oauth_client_credentials");
        StringAssert.Contains(prompt, "workspace.execute()");
        StringAssert.Contains(prompt, "stash");
        StringAssert.Contains(prompt, "max_send_iterations");
        StringAssert.Contains(prompt, "Assertions");
        StringAssert.Contains(prompt, "`expect`: Assert on status, headers, body content, or JSON selectors.");
        StringAssert.Contains(prompt, "CRUD send loop");
        StringAssert.Contains(prompt, "verifying and stashing each step");
        StringAssert.Contains(prompt, "strings.Replace / strings.Split");
        StringAssert.Contains(prompt, "convert.ToBool / convert.ToInt");
        StringAssert.Contains(prompt, "time.Parse / time.Format");
        StringAssert.Contains(prompt, "loop + stash pattern");
        StringAssert.Contains(prompt, "`let`: Declare a local variable inside flow code.");
        StringAssert.Contains(prompt, "`log / warn / error`: Write messages to the execution console.");
        StringAssert.Contains(prompt, "`tests.Assert / tests.Equal`: Programmatic test assertions inside flow code.");
        StringAssert.Contains(prompt, "`guid() / now() / utc_now() / random()`: Built-in runtime variable seed functions.");
        StringAssert.Contains(prompt, "`break / continue`: Exit or skip iterations in loops and retry blocks.");
        StringAssert.Contains(prompt, "If a requested feature is not listed, treat it as unsupported");
    }
}
