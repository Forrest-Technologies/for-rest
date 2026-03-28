using ForRest.Services.AI;

namespace ForRest.Maui.Tests.AI;

public sealed class AiPromptManifestBuilderTests
{
    [TestMethod]
    public void Build_includes_tools_topics_and_masks_secret_state()
    {
        AiSettings settings = new()
        {
            Enabled = true,
            Provider = new AiProviderSettings
            {
                ProviderKind = AiProviderKind.AzureOpenAI,
                Transport = AiConversationTransport.Responses,
                Endpoint = "https://example.openai.azure.com/",
                Model = "gpt-4.1-mini",
                DeploymentName = "agent",
            },
            ApiKey = new AiSecretSetting
            {
                IsConfigured = true,
            },
            SystemPromptPrefix = "Use terse answers.",
        };

        AiPromptManifest manifest = new AiPromptManifestBuilder().Build(
            settings,
            "Update the AI docs",
            [
                new AiToolDescriptor("search_docs", "Search the docs.", "Query the docs.", false),
                new AiToolDescriptor("patch_document", "Patch the current document.", "Provide text edits.", true),
            ],
            [
                new AiPromptTopic("AI Settings", "Settings are gated by ai.enabled.", "docs/tracking", "The API key stays hidden from the editor projection."),
            ]);

        StringAssert.Contains(manifest.SystemPrompt, "AI enabled: yes");
        StringAssert.Contains(manifest.SystemPrompt, "API key configured: yes");
        StringAssert.Contains(manifest.SystemPrompt, "search_docs");
        StringAssert.Contains(manifest.SystemPrompt, "patch_document");
        StringAssert.Contains(manifest.SystemPrompt, "AI Settings");
        Assert.IsFalse(manifest.SystemPrompt.Contains("super-secret", StringComparison.Ordinal));
        Assert.AreEqual(2, manifest.Tools.Count);
        Assert.AreEqual(1, manifest.Topics.Count);
    }

    [TestMethod]
    public void Tool_catalog_returns_empty_when_ai_is_disabled()
    {
        AiSettings settings = new();
        IAiToolCatalog catalog = new AiLocalToolCatalog();

        IReadOnlyList<AiToolDescriptor> tools = catalog.GetTools(settings);

        Assert.AreEqual(0, tools.Count);
    }
}
