using ForRest.Services.AI;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class AgentFrameworkAiRuntimeFactoryTests
{
    [TestMethod]
    public void Prepare_returns_manifest_and_no_agent_when_settings_are_invalid()
    {
        IAiRuntimeFactory factory = CreateFactory();
        AiSettings settings = new()
        {
            Enabled = true,
            Provider = new AiProviderSettings
            {
                ProviderKind = AiProviderKind.OpenAI,
                Model = string.Empty,
            },
        };

        AiPreparedRuntime runtime = factory.Prepare(settings, "Answer questions about ForRest.");

        Assert.IsNull(runtime.Agent);
        Assert.IsTrue(runtime.Issues.Any(static issue => issue.Code == "ai.openai.model.required"));
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "ForRest prompt context");
    }

    [TestMethod]
    public void Prepare_builds_openai_agent_with_local_tools()
    {
        IAiRuntimeFactory factory = CreateFactory();
        AiSettings settings = new()
        {
            Enabled = true,
            Provider = new AiProviderSettings
            {
                ProviderKind = AiProviderKind.OpenAI,
                Transport = AiConversationTransport.ChatCompletions,
                Model = "gpt-4.1-mini",
            },
            ApiKey = new AiSecretSetting
            {
                Value = "test-key",
                IsConfigured = true,
            },
        };

        AiPreparedRuntime runtime = factory.Prepare(settings, "Patch the current request.");

        Assert.IsNotNull(runtime.Agent);
        Assert.AreEqual(0, runtime.Issues.Count);
        CollectionAssert.AreEquivalent(
            new[] { "search_docs", "read_all_docs", "patch_document" },
            runtime.PromptManifest.Tools.Select(static tool => tool.Name).ToArray());
    }

    [TestMethod]
    public void Prepare_builds_azure_openai_agent_with_response_transport()
    {
        IAiRuntimeFactory factory = CreateFactory();
        AiSettings settings = new()
        {
            Enabled = true,
            Provider = new AiProviderSettings
            {
                ProviderKind = AiProviderKind.AzureOpenAI,
                Transport = AiConversationTransport.Responses,
                Endpoint = "https://example.openai.azure.com",
                Model = "gpt-4.1-mini",
                DeploymentName = "forrest-agent",
            },
            ApiKey = new AiSecretSetting
            {
                Value = "azure-key",
                IsConfigured = true,
            },
        };

        AiPreparedRuntime runtime = factory.Prepare(settings, "Answer syntax questions.");

        Assert.IsNotNull(runtime.Agent);
        Assert.IsFalse(runtime.Issues.Any(static issue => issue.Code == "ai.transport.responses.chat-fallback"));
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "Provider: AzureOpenAI");
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "Transport: Responses");
    }

    [TestMethod]
    public void Prepare_keeps_openai_response_transport_enabled()
    {
        IAiRuntimeFactory factory = CreateFactory();
        AiSettings settings = new()
        {
            Enabled = true,
            Provider = new AiProviderSettings
            {
                ProviderKind = AiProviderKind.OpenAI,
                Transport = AiConversationTransport.Responses,
                Model = "gpt-4.1-mini",
            },
            ApiKey = new AiSecretSetting
            {
                Value = "test-key",
                IsConfigured = true,
            },
        };

        AiPreparedRuntime runtime = factory.Prepare(settings, "Answer syntax questions.");

        Assert.IsNotNull(runtime.Agent);
        Assert.IsFalse(runtime.Issues.Any(static issue => issue.Code == "ai.transport.responses.chat-fallback"));
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "Provider: OpenAI");
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "Transport: Responses");
    }

    [TestMethod]
    public void Prepare_uses_active_document_host_tools_when_a_host_is_available()
    {
        IAiRuntimeFactory factory = CreateFactory();
        AiSettings settings = new()
        {
            Enabled = true,
            Provider = new AiProviderSettings
            {
                ProviderKind = AiProviderKind.OpenAI,
                Transport = AiConversationTransport.ChatCompletions,
                Model = "gpt-4.1-mini",
            },
            ApiKey = new AiSecretSetting
            {
                Value = "test-key",
                IsConfigured = true,
            },
        };

        AiPreparedRuntime runtime = factory.Prepare(settings, "Update the active request.", new FakeActiveDocumentHost());

        Assert.IsNotNull(runtime.Agent);
        CollectionAssert.AreEquivalent(
            new[] { "search_docs", "read_all_docs", "read_active_document", "patch_active_document", "replace_active_document" },
            runtime.PromptManifest.Tools.Select(static tool => tool.Name).ToArray());
        Assert.IsFalse(runtime.PromptManifest.Tools.Any(static tool => tool.Name == "patch_document"));
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "Do not ask the user to paste working syntax");
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "Do not use `//` comments");
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "`expect` statements are top-level assertions");
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "Ask at most 2 clarification turn");
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "read_all_docs or the built-in full-corpus fallback");
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "latest runtime context");
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "Treat inline editor chat markers");
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "If patch_active_document fails or the active document looks garbled");
        Assert.AreEqual("Active document", runtime.PromptManifest.Topics[0].Title);
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "Unexpected token 'time'.");
        StringAssert.Contains(runtime.PromptManifest.Topics[0].Content, "Runtime error: Cannot perform runtime binding on a null reference.");
        StringAssert.Contains(runtime.PromptManifest.Topics[0].Content, "\"data\": null");
        StringAssert.Contains(runtime.PromptManifest.SystemPrompt, "name \"Example\"");
    }

    private static IAiRuntimeFactory CreateFactory()
    {
        IAiKnowledgeCatalog knowledgeCatalog = new ForRestAiKnowledgeCatalog();
        return new AgentFrameworkAiRuntimeFactory(
            new AiSettingsValidator(),
            new AiLocalToolCatalog(),
            new AiPromptManifestBuilder(),
            knowledgeCatalog,
            new AiDocumentationSearchService(knowledgeCatalog.GetDocuments()),
            new AiDocumentPatchService());
    }

    private sealed class FakeActiveDocumentHost : IAiActiveDocumentHost
    {
        public AiActiveDocumentSnapshot? GetActiveDocument()
        {
            return new AiActiveDocumentSnapshot(
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
                    """[{ "id": "1", "data": null }]"""));
        }

        public AiActiveDocumentUpdateResult UpdateActiveDocument(AiActiveDocumentSnapshot document, string updatedText)
        {
            return AiActiveDocumentUpdateResult.Success();
        }
    }
}
