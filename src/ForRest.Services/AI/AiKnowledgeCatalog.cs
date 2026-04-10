using ForRest.Scripting;

namespace ForRest.Services.AI;

public interface IAiKnowledgeCatalog
{
    IReadOnlyList<AiKnowledgeDocument> GetDocuments();

    IReadOnlyList<AiPromptTopic> GetTopics();
}

public sealed class ForRestAiKnowledgeCatalog : IAiKnowledgeCatalog
{
    private readonly Lazy<IReadOnlyList<AiKnowledgeDocument>> _documents;
    private readonly Lazy<IReadOnlyList<AiPromptTopic>> _topics;

    public ForRestAiKnowledgeCatalog()
    {
        _documents = new(BuildDocuments);
        _topics = new(BuildTopics);
    }

    public IReadOnlyList<AiKnowledgeDocument> GetDocuments() => _documents.Value;

    public IReadOnlyList<AiPromptTopic> GetTopics() => _topics.Value;

    private static IReadOnlyList<AiKnowledgeDocument> BuildDocuments()
    {
        List<AiKnowledgeDocument> documents =
        [
            new(
                "forrest-language-reference",
                "ForRest Script Language Reference",
                "Canonical markdown reference generated from the Monaco help catalog.",
                ForRestLanguageCatalog.BuildMarkdownReference(),
                ["forrest", "language", "reference", "docs", "monaco"],
                SourcePath: "ForRestLanguageCatalog.BuildMarkdownReference()"),
            new(
                "forrest-language-prompt-context",
                "ForRest Prompt Context",
                "Compact system-prompt context generated from the canonical language catalog.",
                ForRestLanguageCatalog.BuildPromptContext(),
                ["forrest", "prompt", "context", "agent", "system-prompt"],
                SourcePath: "ForRestLanguageCatalog.BuildPromptContext()"),
            new(
                "forrest-ai-providers",
                "AI Providers",
                "Reference for every AI provider For-Rest supports out of the box plus custom setup.",
                BuildAiProviderReference(),
                ["ai", "providers", "openai", "azure", "grok", "xai", "groq", "deepseek", "mistral", "openrouter", "gemini", "google", "anthropic", "claude", "custom", "custom-headers", "settings"],
                SourcePath: "AiKnowledgeCatalog.BuildAiProviderReference()"),
        ];

        foreach (ForRestLanguageHelpEntry entry in ForRestLanguageCatalog.GetEntries())
        {
            List<string> tags = [entry.Category, entry.Key, .. entry.SearchTerms, .. entry.HoverTerms];
            documents.Add(new(
                $"catalog:{entry.Key}",
                entry.Title,
                entry.Summary,
                BuildEntryContent(entry),
                tags
                    .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                SourcePath: $"ForRestLanguageCatalog:{entry.Key}"));
        }

        return documents;
    }

    private static IReadOnlyList<AiPromptTopic> BuildTopics()
    {
        List<AiPromptTopic> topics =
        [
            new(
                "ForRest prompt context",
                "Compact syntax reference for the assistant system prompt.",
                "ForRestLanguageCatalog.BuildPromptContext()",
                ForRestLanguageCatalog.BuildPromptContext()),
            new(
                "ForRest language reference",
                "Long-form canonical docs source that matches Monaco help content.",
                "ForRestLanguageCatalog.BuildMarkdownReference()",
                ForRestLanguageCatalog.BuildMarkdownReference()),
            new(
                "AI providers",
                "Supported AI providers, their default endpoints, transports, and custom header usage.",
                "AiKnowledgeCatalog.BuildAiProviderReference()",
                BuildAiProviderReference()),
        ];

        foreach (IGrouping<string, ForRestLanguageHelpEntry> category in ForRestLanguageCatalog
                     .GetEntries()
                     .GroupBy(static entry => entry.Category)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            topics.Add(new(
                $"{category.Key} reference",
                $"Canonical {category.Key.ToLowerInvariant()} entries from the ForRest language catalog.",
                $"ForRestLanguageCatalog:{category.Key}",
                string.Join(
                    Environment.NewLine + Environment.NewLine,
                    category.Select(BuildEntryContent))));
        }

        return topics;
    }

    private static string BuildAiProviderReference()
    {
        return string.Join(
            Environment.NewLine,
            [
                "# AI providers",
                string.Empty,
                "For-Rest talks to every provider through the OpenAI .NET SDK. Any provider that exposes an OpenAI-compatible `/chat/completions` (and, for some, `/responses`) endpoint works out of the box. Set the provider in `settings.toml` under `[ai]` with `provider = \"...\"` and pick the transport with `api = \"responses\"` or `api = \"chat\"`.",
                string.Empty,
                "## Built-in providers",
                string.Empty,
                "| provider | aliases | default endpoint | preferred transport | notes |",
                "|----------|---------|------------------|---------------------|-------|",
                "| `openai` | — | `https://api.openai.com/v1` | `responses` | Standard OpenAI API. |",
                "| `azure_openai` | `azure`, `azure-openai` | required (per-resource) | `responses` | Also requires `deployment_name`. |",
                "| `grok` | `xai`, `x-ai`, `x_ai`, `x.ai` | `https://api.x.ai/v1` | `responses` | Example model: `grok-4-fast-non-reasoning`. |",
                "| `groq` | — | `https://api.groq.com/openai/v1` | `chat` | Example model: `llama-3.3-70b-versatile`. |",
                "| `deepseek` | `deep-seek`, `deep_seek` | `https://api.deepseek.com/v1` | `chat` | Example model: `deepseek-chat`. |",
                "| `mistral` | `mistralai`, `mistral-ai`, `mistral_ai` | `https://api.mistral.ai/v1` | `chat` | Example model: `mistral-large-latest`. |",
                "| `openrouter` | `open-router`, `open_router` | `https://openrouter.ai/api/v1` | `chat` | Pair with `custom_headers = \"HTTP-Referer: https://your.app; X-Title: YourApp\"` for ranking credit. |",
                "| `gemini` | `google`, `google-gemini`, `google_gemini` | `https://generativelanguage.googleapis.com/v1beta/openai` | `responses` | Example model: `gemini-2.0-flash`. |",
                "| `anthropic` | `claude` | `https://api.anthropic.com/v1` | `chat` | Requires `custom_headers = \"anthropic-version: 2023-06-01\"` and Anthropic's OpenAI-compatible endpoint. |",
                "| `custom` | `openai-compatible`, `openai_compatible` | required | `chat` | For any other OpenAI-compatible host. Always specify `endpoint`. |",
                string.Empty,
                "## Settings surface",
                string.Empty,
                "All keys below live under `[ai]`:",
                string.Empty,
                "- `enabled` — boolean gate for every AI feature.",
                "- `provider` — one of the providers/aliases above.",
                "- `api` — `responses` or `chat` (also accepts `chat_completions`). Defaults are provider-aware.",
                "- `endpoint` — base URL. May be omitted for providers with built-in defaults. For-Rest strips trailing `/responses` or `/chat/completions` so you can paste the full URL from provider docs.",
                "- `model` — required for every non-Azure provider.",
                "- `deployment_name` — required for Azure OpenAI.",
                "- `api_key` — secret credential. Stored in the secure settings store.",
                "- `system_prompt` — optional prefix appended to the built-in assistant prompt.",
                "- `stream_responses` — boolean, controls the inline typewriter reveal.",
                "- `custom_headers` — semicolon-delimited `Name: value` pairs that are injected into every outbound AI request (use newlines if you prefer). Example: `\"X-Api-Key: sk-...; HTTP-Referer: https://for-rest.dev\"`.",
                string.Empty,
                "## Custom headers",
                string.Empty,
                "`custom_headers` is injected via a per-request pipeline policy that calls `request.Headers.Set(name, value)` on every outbound AI call. Use it when a provider needs an extra header that isn't the bearer token (OpenRouter's `HTTP-Referer`, Anthropic's `anthropic-version`, custom gateways that want `X-Custom-Auth`, and so on). Header names are case-insensitive and later entries overwrite earlier ones.",
                string.Empty,
                "## Custom provider example",
                string.Empty,
                "```toml",
                "[ai]",
                "enabled = true",
                "provider = \"custom\"",
                "api = \"chat\"",
                "endpoint = \"https://ai-gateway.example.internal/v1\"",
                "model = \"gpt-4o-mini\"",
                "api_key = \"gateway-secret\"",
                "custom_headers = \"X-Tenant: acme; X-Trace: for-rest\"",
                "```",
            ]);
    }

    private static string BuildEntryContent(ForRestLanguageHelpEntry entry)
    {
        List<string> lines =
        [
            $"Title: {entry.Title}",
            $"Category: {entry.Category}",
            $"Summary: {entry.Summary}",
        ];

        if (!string.IsNullOrWhiteSpace(entry.Documentation))
        {
            lines.Add($"Documentation: {entry.Documentation}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Example))
        {
            lines.Add("Example:");
            lines.Add(entry.Example);
        }

        if (entry.SearchTerms.Count > 0)
        {
            lines.Add($"Search terms: {string.Join(", ", entry.SearchTerms)}");
        }

        if (entry.HoverTerms.Count > 0)
        {
            lines.Add($"Hover terms: {string.Join(", ", entry.HoverTerms)}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
