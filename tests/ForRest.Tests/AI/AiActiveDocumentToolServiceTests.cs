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
                ]));

        string response = service.ReadActiveDocument(BuildSettings(), host);

        using JsonDocument document = JsonDocument.Parse(response);
        Assert.IsTrue(document.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.AreEqual("request-1", document.RootElement.GetProperty("document").GetProperty("documentId").GetString());
        Assert.AreEqual("name \"Example\"", document.RootElement.GetProperty("document").GetProperty("sourceText").GetString());
        Assert.AreEqual("error", document.RootElement.GetProperty("document").GetProperty("diagnostics")[0].GetProperty("severity").GetString());
        Assert.AreEqual("Unexpected token 'time'.", document.RootElement.GetProperty("document").GetProperty("diagnostics")[0].GetProperty("message").GetString());
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
        StringAssert.Contains(response, "left the request invalid");
        StringAssert.Contains(response, "\"diagnostics\"");
        StringAssert.Contains(response, "Unexpected token \\u0027expect\\u0027.");
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
        StringAssert.Contains(response, "left the request invalid");
        StringAssert.Contains(response, "Unexpected token \\u0027expect\\u0027.");
        Assert.AreEqual("abc", host.CurrentDocument?.SourceText);
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
        StringAssert.Contains(response, "name \\u0022Post Echo\\u0022\\nmethod POST");
        Assert.AreEqual("name \"Post Echo\"\nmethod POST", host.CurrentDocument?.SourceText);
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
    }
}
