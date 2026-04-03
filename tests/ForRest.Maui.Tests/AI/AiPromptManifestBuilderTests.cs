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
                new AiToolDescriptor("read_all_docs", "Read the full docs corpus.", "Load the full docs corpus.", false),
                new AiToolDescriptor("patch_document", "Patch the current document.", "Provide text edits.", true),
            ],
            [
                new AiPromptTopic("AI Settings", "Settings are gated by ai.enabled.", "docs/tracking", "The API key stays hidden from the editor projection."),
            ]);

        StringAssert.Contains(manifest.SystemPrompt, "AI enabled: yes");
        StringAssert.Contains(manifest.SystemPrompt, "API key configured: yes");
        StringAssert.Contains(manifest.SystemPrompt, "search_docs");
        StringAssert.Contains(manifest.SystemPrompt, "read_all_docs");
        StringAssert.Contains(manifest.SystemPrompt, "patch_document");
        StringAssert.Contains(manifest.SystemPrompt, "AI Settings");
        StringAssert.Contains(manifest.SystemPrompt, "Ask at most 2 clarification turn");
        StringAssert.Contains(manifest.SystemPrompt, "read_all_docs or the built-in full-corpus fallback");
        StringAssert.Contains(manifest.SystemPrompt, "Treat inline editor chat markers");
        StringAssert.Contains(manifest.SystemPrompt, "runnable ForRest source");
        StringAssert.Contains(manifest.SystemPrompt, "replace the old endpoint");
        StringAssert.Contains(manifest.SystemPrompt, "batch-stash-loop");
        StringAssert.Contains(manifest.SystemPrompt, "request-url");
        StringAssert.Contains(manifest.SystemPrompt, "3 times");
        StringAssert.Contains(manifest.SystemPrompt, "repeat N times");
        StringAssert.Contains(manifest.SystemPrompt, "request-send");
        StringAssert.Contains(manifest.SystemPrompt, "request-method");
        StringAssert.Contains(manifest.SystemPrompt, "request-body");
        StringAssert.Contains(manifest.SystemPrompt, "request-content-type");
        StringAssert.Contains(manifest.SystemPrompt, "api-surface-crud");
        StringAssert.Contains(manifest.SystemPrompt, "request-headers");
        StringAssert.Contains(manifest.SystemPrompt, "body");
        StringAssert.Contains(manifest.SystemPrompt, "expect");
        StringAssert.Contains(manifest.SystemPrompt, "expect status == 200 \"returns 200\"");
        StringAssert.Contains(manifest.SystemPrompt, "expect header \"Content-Type\" contains \"json\" \"json response\"");
        StringAssert.Contains(manifest.SystemPrompt, "do not invent forms like `expect sent.status == 200`");
        StringAssert.Contains(manifest.SystemPrompt, "Math.*");
        StringAssert.Contains(manifest.SystemPrompt, ".Substring(...)");
        StringAssert.Contains(manifest.SystemPrompt, "closest valid field");
        StringAssert.Contains(manifest.SystemPrompt, "retryWithRead");
        StringAssert.Contains(manifest.SystemPrompt, "retryWithReplace");
        StringAssert.Contains(manifest.SystemPrompt, "docHints");
        Assert.IsFalse(manifest.SystemPrompt.Contains("super-secret", StringComparison.Ordinal));
        Assert.AreEqual(3, manifest.Tools.Count);
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
