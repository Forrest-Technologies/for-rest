using System.Text.Json;
using ForRest.Services.AI;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class AiActiveDocumentToolServiceTests
{
    [TestMethod]
    public void ReadActiveDocument_returns_active_document_snapshot_with_current_diagnostics()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        FakeActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot(
                "request-1",
                "Example Request",
                "forrest",
                "name \"Example\"",
                [
                    new("error", "Unexpected token 'time'.", 5, 1),
                ],
                new(
                    "Failed",
                    "Cannot perform runtime binding on a null reference.",
                    "Execution state: Failed\nError: Cannot perform runtime binding on a null reference.",
                    """[{ "id": "1", "data": null }]""")));

        string response = service.ReadActiveDocument(BuildSettings(), host);

        using JsonDocument document = JsonDocument.Parse(response);
        Assert.IsTrue(document.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.AreEqual("request-1", document.RootElement.GetProperty("document").GetProperty("documentId").GetString());
        Assert.AreEqual("name \"Example\"", document.RootElement.GetProperty("document").GetProperty("sourceText").GetString());
        Assert.AreEqual("error", document.RootElement.GetProperty("document").GetProperty("diagnostics")[0].GetProperty("severity").GetString());
        Assert.AreEqual("Unexpected token 'time'.", document.RootElement.GetProperty("document").GetProperty("diagnostics")[0].GetProperty("message").GetString());
        Assert.AreEqual("Failed", document.RootElement.GetProperty("document").GetProperty("runtimeContext").GetProperty("status").GetString());
        Assert.AreEqual("Cannot perform runtime binding on a null reference.", document.RootElement.GetProperty("document").GetProperty("runtimeContext").GetProperty("errorMessage").GetString());
    }

    [TestMethod]
    public void PatchActiveDocument_updates_the_host_document_without_raw_source_text_handoff()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        FakeActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot(
                "request-1",
                "Example Request",
                "forrest",
                "abc",
                []));

        string response = service.PatchActiveDocument(
            BuildSettings(),
            host,
            """
            [{"startIndex":0,"length":3,"replacement":"xyz"}]
            """);

        StringAssert.Contains(response, "\"succeeded\":true");
        StringAssert.Contains(response, "\"patchedText\":\"xyz\"");
        Assert.AreEqual("xyz", host.CurrentDocument?.SourceText);
        Assert.AreEqual("xyz", host.LastUpdatedText);
    }

    [TestMethod]
    public void PatchActiveDocument_returns_error_when_host_is_missing()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());

        string response = service.PatchActiveDocument(BuildSettings(), null, "[]");

        StringAssert.Contains(response, "\"succeeded\":false");
        StringAssert.Contains(response, "No active document host is available.");
    }

    [TestMethod]
    public void PatchActiveDocument_rejects_too_many_edits()
    {
        AiSettings settings = BuildSettings();
        settings = settings with
        {
            Tools = settings.Tools with
            {
                MaxPatchOperations = 1,
            },
        };
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        FakeActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot(
                "request-1",
                "Example Request",
                "forrest",
                "abc",
                []));

        string response = service.PatchActiveDocument(
            settings,
            host,
            """
            [
              {"startIndex":0,"length":1,"replacement":"x"},
              {"startIndex":1,"length":1,"replacement":"y"}
            ]
            """);

        StringAssert.Contains(response, "\"succeeded\":false");
        StringAssert.Contains(response, "exceeds the configured maximum");
        Assert.AreEqual("abc", host.CurrentDocument?.SourceText);
    }

    [TestMethod]
    public void PatchActiveDocument_returns_host_validation_failure_without_mutating_document()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        RejectingActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot(
                "request-1",
                "Example Request",
                "forrest",
                "abc",
                []));

        string response = service.PatchActiveDocument(
            BuildSettings(),
            host,
            """
            [{"startIndex":0,"length":3,"replacement":"broken"}]
            """);

        StringAssert.Contains(response, "\"succeeded\":false");
        StringAssert.Contains(response, "\"retryWithRead\":true");
        StringAssert.Contains(response, "\"retryWithReplace\":true");
        StringAssert.Contains(response, "\"patchedText\":\"broken\"");
        StringAssert.Contains(response, "\"docHints\"");
        StringAssert.Contains(response, "batch-stash-loop");
        StringAssert.Contains(response, "request-url");
        StringAssert.Contains(response, "request-send");
        StringAssert.Contains(response, "expect");
        StringAssert.Contains(response, "left the request invalid");
        StringAssert.Contains(response, "\"diagnostics\"");
        StringAssert.Contains(response, "Unexpected token 'expect'.");
        Assert.AreEqual("abc", host.CurrentDocument?.SourceText);
    }

    [TestMethod]
    public void ReplaceActiveDocument_returns_host_validation_failure_with_candidate_diagnostics()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        RejectingActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot(
                "request-1",
                "Example Request",
                "forrest",
                "abc",
                []));

        string response = service.ReplaceActiveDocument(
            BuildSettings(),
            host,
            "broken");

        StringAssert.Contains(response, "\"succeeded\":false");
        StringAssert.Contains(response, "\"retryWithRead\":true");
        StringAssert.Contains(response, "\"retryWithReplace\":true");
        StringAssert.Contains(response, "\"patchedText\":\"broken\"");
        StringAssert.Contains(response, "\"docHints\"");
        StringAssert.Contains(response, "batch-stash-loop");
        StringAssert.Contains(response, "request-url");
        StringAssert.Contains(response, "request-send");
        StringAssert.Contains(response, "expect");
        StringAssert.Contains(response, "left the request invalid");
        StringAssert.Contains(response, "Unexpected token 'expect'.");
        Assert.AreEqual("abc", host.CurrentDocument?.SourceText);
    }

    [TestMethod]
    public void ReplaceActiveDocument_adds_crud_doc_hints_for_api_surface_candidates()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        RejectingActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot(
                "request-1",
                "Example Request",
                "forrest",
                "abc",
                []));

        string response = service.ReplaceActiveDocument(
            BuildSettings(),
            host,
            """
            name "demo"
            method GET
            url "https://api.restful-api.dev/objects"
            max_send_iterations 5

            request.method = "POST"
            request.content_type = "application/json"
            request.body = "{\"name\":\"Validation Widget\"}"
            let sent = request.send()

            expect header "Content-Type" contains "json" "json response"
            """);

        StringAssert.Contains(response, "request-method");
        StringAssert.Contains(response, "request-body");
        StringAssert.Contains(response, "request-content-type");
        StringAssert.Contains(response, "api-surface-crud");
    }

    [TestMethod]
    public void ReplaceActiveDocument_updates_the_host_document_without_offset_math()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        FakeActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot(
                "request-1",
                "Example Request",
                "forrest",
                "abc",
                []));

        string response = service.ReplaceActiveDocument(
            BuildSettings(),
            host,
            "name \"Post Echo\"\nmethod POST");

        StringAssert.Contains(response, "\"succeeded\":true");
        StringAssert.Contains(response, "name \\\"Post Echo\\\"\\nmethod POST");
        Assert.AreEqual("name \"Post Echo\"\nmethod POST", host.CurrentDocument?.SourceText);
    }

    [TestMethod]
    public void ReadActiveDocument_redacts_secret_values_for_string_identifier_and_function_call_forms()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        const string source = """
            secret api_key = "super-secret-token"
            secret runtime_token = lookup_token("prod")
            secret fallback = otherVar
            method GET
            url "https://api.example.test/items"
            """;
        FakeActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot("request-1", "Example", "forrest", source, []));

        string response = service.ReadActiveDocument(BuildSettings(), host);

        StringAssert.Contains(response, "secret api_key = \\\"***\\\"");
        StringAssert.Contains(response, "secret runtime_token = \\\"***\\\"");
        StringAssert.Contains(response, "secret fallback = \\\"***\\\"");
        Assert.IsFalse(response.Contains("super-secret-token", StringComparison.Ordinal));
        Assert.IsFalse(response.Contains("lookup_token", StringComparison.Ordinal));
        Assert.IsFalse(response.Contains("otherVar", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReplaceActiveDocument_restores_original_secret_values_when_ai_returns_redaction_marker()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        const string original = """
            secret api_key = "super-secret-token"
            secret bearer = "abc-123"
            method GET
            url "https://api.example.test/items"
            expect status == 200
            """;
        FakeActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot("request-1", "Example", "forrest", original, []));

        // The AI saw the redacted form via ReadActiveDocument and is now
        // returning a "rewritten" document where it copied the masked
        // values back verbatim — the exact failure mode the user reported.
        const string aiReplacement = """
            secret api_key = "***"
            secret bearer = "***"
            method POST
            url "https://api.example.test/items"
            expect status == 201
            """;

        string response = service.ReplaceActiveDocument(BuildSettings(), host, aiReplacement);

        StringAssert.Contains(response, "\"succeeded\":true");
        Assert.IsNotNull(host.LastUpdatedText);
        StringAssert.Contains(host.LastUpdatedText!, "secret api_key = \"super-secret-token\"");
        StringAssert.Contains(host.LastUpdatedText!, "secret bearer = \"abc-123\"");
        StringAssert.Contains(host.LastUpdatedText!, "method POST");
        StringAssert.Contains(host.LastUpdatedText!, "expect status == 201");
        // The echoed payload going back to the AI must still be redacted
        // so the model never sees the real credential values, even after
        // a successful round-trip.
        Assert.IsFalse(response.Contains("super-secret-token", StringComparison.Ordinal));
        Assert.IsFalse(response.Contains("abc-123", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReplaceActiveDocument_re_inserts_secrets_the_ai_deleted_entirely()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        const string original = """
            secret api_key = "stays-around"
            method GET
            url "https://api.example.test/items"
            """;
        FakeActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot("request-1", "Example", "forrest", original, []));

        // The AI rewrote the document and forgot to keep the secret line
        // (e.g. it decided the request "didn't need it"). Without
        // restoration the credential would silently disappear.
        const string aiReplacement = """
            method POST
            url "https://api.example.test/items"
            """;

        service.ReplaceActiveDocument(BuildSettings(), host, aiReplacement);

        Assert.IsNotNull(host.LastUpdatedText);
        StringAssert.Contains(host.LastUpdatedText!, "secret api_key = \"stays-around\"");
        StringAssert.Contains(host.LastUpdatedText!, "method POST");
    }

    [TestMethod]
    public void PatchActiveDocument_restores_original_secrets_when_patch_targets_redacted_view()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        const string original = """
            secret api_key = "real-secret"
            method GET
            """;
        FakeActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot("request-1", "Example", "forrest", original, []));

        // The AI sees the redacted form (...***...) and crafts a patch
        // that flips method GET → method POST. The patch start index
        // matches the redacted text length, not the real source — the
        // tool service must apply the patch against the redacted view
        // and then splice the original secret back in. Compute the
        // redacted view inline (rather than reaching into the internal
        // RedactSecrets helper) so the test stays decoupled from the
        // implementation surface.
        const string redactedView = "secret api_key = \"***\"\nmethod GET\n";
        int methodIndex = redactedView.IndexOf("method GET", StringComparison.Ordinal);
        Assert.IsTrue(methodIndex >= 0);

        string editsJson = $$"""
            [{"startIndex":{{methodIndex}},"length":10,"replacement":"method POST"}]
            """;

        string response = service.PatchActiveDocument(BuildSettings(), host, editsJson);

        StringAssert.Contains(response, "\"succeeded\":true");
        Assert.IsNotNull(host.LastUpdatedText);
        StringAssert.Contains(host.LastUpdatedText!, "secret api_key = \"real-secret\"");
        StringAssert.Contains(host.LastUpdatedText!, "method POST");
        // patchedText echoed to the AI must keep the redaction marker.
        Assert.IsFalse(response.Contains("real-secret", StringComparison.Ordinal));
    }

    private static AiSettings BuildSettings()
    {
        return new()
        {
            Enabled = true,
            Provider = new AiProviderSettings
            {
                ProviderKind = AiProviderKind.OpenAI,
                Model = "gpt-4.1-mini",
            },
            ApiKey = new AiSecretSetting
            {
                IsConfigured = true,
                Value = "test-key",
            },
        };
    }

    private sealed class FakeActiveDocumentHost(AiActiveDocumentSnapshot? snapshot) : IAiActiveDocumentHost
    {
        public AiActiveDocumentSnapshot? CurrentDocument { get; private set; } = snapshot;

        public string? LastUpdatedText { get; private set; }

        public AiActiveDocumentSnapshot? GetActiveDocument() => CurrentDocument;

        public AiActiveDocumentUpdateResult UpdateActiveDocument(AiActiveDocumentSnapshot document, string updatedText)
        {
            LastUpdatedText = updatedText;
            CurrentDocument = document with
            {
                SourceText = updatedText,
            };
            return AiActiveDocumentUpdateResult.Success();
        }

        public AiWorkspaceContext? GetWorkspaceContext() => null;

        public AiActiveDocumentUpdateResult CreateScript(string name, string sourceText) => AiActiveDocumentUpdateResult.Failure("Not supported in tests.");
    }

    private sealed class RejectingActiveDocumentHost(AiActiveDocumentSnapshot? snapshot) : IAiActiveDocumentHost
    {
        public AiActiveDocumentSnapshot? CurrentDocument { get; private set; } = snapshot;

        public AiActiveDocumentSnapshot? GetActiveDocument() => CurrentDocument;

        public AiActiveDocumentUpdateResult UpdateActiveDocument(AiActiveDocumentSnapshot document, string updatedText)
        {
            return AiActiveDocumentUpdateResult.Failure(
                "The AI edit was rejected because it left the request invalid.",
                updatedText,
                [
                    new("error", "Unexpected token 'expect'.", 4, 1),
                ],
                retryWithReplace: true);
        }

        public AiWorkspaceContext? GetWorkspaceContext() => null;

        public AiActiveDocumentUpdateResult CreateScript(string name, string sourceText) => AiActiveDocumentUpdateResult.Failure("Not supported in tests.");
    }
}
