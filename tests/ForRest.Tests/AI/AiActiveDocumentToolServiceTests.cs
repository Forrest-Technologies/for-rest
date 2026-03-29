using System.Text.Json;
using ForRest.Services.AI;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class AiActiveDocumentToolServiceTests
{
    [TestMethod]
    public void ReadActiveDocument_returns_active_document_snapshot_without_raw_text_parameters()
    {
        AiActiveDocumentToolService service = new(new AiDocumentPatchService());
        FakeActiveDocumentHost host = new(
            new AiActiveDocumentSnapshot(
                "request-1",
                "Example Request",
                "forrest",
                "name \"Example\""));

        string response = service.ReadActiveDocument(BuildSettings(), host);

        using JsonDocument document = JsonDocument.Parse(response);
        Assert.IsTrue(document.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.AreEqual("request-1", document.RootElement.GetProperty("document").GetProperty("documentId").GetString());
        Assert.AreEqual("name \"Example\"", document.RootElement.GetProperty("document").GetProperty("sourceText").GetString());
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
                "abc"));

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
                "abc"));

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
}
