namespace ForRest.Services.AI;

public sealed record AiToolDescriptor(
    string Name,
    string Description,
    string InvocationHint,
    bool MutatesDocument);

public sealed record AiPromptTopic(
    string Title,
    string Summary,
    string Source,
    string Content);

public sealed record AiPromptManifest(
    string SystemPrompt,
    IReadOnlyList<AiToolDescriptor> Tools,
    IReadOnlyList<AiPromptTopic> Topics);

public interface IAiPromptManifestBuilder
{
    AiPromptManifest Build(
        AiSettings settings,
        string objective,
        IEnumerable<AiToolDescriptor> tools,
        IEnumerable<AiPromptTopic> topics);
}

public sealed class AiPromptManifestBuilder : IAiPromptManifestBuilder
{
    public AiPromptManifest Build(
        AiSettings settings,
        string objective,
        IEnumerable<AiToolDescriptor> tools,
        IEnumerable<AiPromptTopic> topics)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(topics);

        List<AiToolDescriptor> toolList = [.. tools];
        List<AiPromptTopic> topicList = [.. topics];
        StringBuilder prompt = new();
        prompt.AppendLine("You are the For-Rest AI assistant.");

        if (!string.IsNullOrWhiteSpace(settings.SystemPromptPrefix))
        {
            prompt.AppendLine(settings.SystemPromptPrefix.Trim());
            prompt.AppendLine();
        }

        prompt.AppendLine($"AI enabled: {(settings.Enabled ? "yes" : "no")}");
        prompt.AppendLine($"Provider: {settings.Provider.ProviderKind}");
        prompt.AppendLine($"Transport: {settings.Provider.Transport}");
        prompt.AppendLine($"Model: {NullToPlaceholder(settings.Provider.Model)}");
        prompt.AppendLine($"Endpoint: {DescribeEndpoint(settings.Provider.Endpoint)}");
        prompt.AppendLine($"Deployment: {NullToPlaceholder(settings.Provider.DeploymentName)}");
        prompt.AppendLine($"API key configured: {(settings.ApiKey.IsConfigured ? "yes" : "no")}");
        prompt.AppendLine();
        prompt.AppendLine("Operating rules:");
        prompt.AppendLine("- Keep responses brief and practical.");
        prompt.AppendLine("- Prefer the local docs search tool before guessing about language or app behavior.");
        prompt.AppendLine("- Use search_docs first for targeted lookups. If it returns no useful hits, immediately use read_all_docs or the built-in full-corpus fallback from search_docs instead of retrying the same search.");
        prompt.AppendLine("- Read the active document before editing so you can inspect the current source text, compiler diagnostics, and latest runtime context.");
        prompt.AppendLine("- If the active document has syntax or compilation errors, use those diagnostics plus local docs to fix the request.");
        prompt.AppendLine("- If the active document includes a recent runtime failure or response preview, use that evidence to repair the request instead of asking the user to rerun it just so you can inspect the failure again.");
        prompt.AppendLine("- Only patch documents when the user asked for an edit or when a correction is clearly required.");
        prompt.AppendLine("- Keep patch operations bounded and explicit.");
        prompt.AppendLine("- When the user asked to rewrite the request from scratch, prefer replace_active_document over patch_active_document.");
        prompt.AppendLine("- Treat inline editor chat markers like `##`, `#>`, and `#~` as conversation scaffolding, not part of the request script.");
        prompt.AppendLine("- If the user pasted API docs or prose into inline chat, treat that text as requirements only. The final document must contain runnable ForRest source, not copied documentation or chat scaffolding.");
        prompt.AppendLine("- When editing the active request, leave it runnable when you finish.");
        prompt.AppendLine("- Modify the existing request in place. Do not append duplicate request blocks unless the user explicitly asked for a second example.");
        prompt.AppendLine("- When rewriting an existing request to a new endpoint or API surface, replace the old endpoint, method, headers, and body as needed instead of leaving the previous target half-intact.");
        prompt.AppendLine("- If the user asks to iterate, enumerate, batch, or stash results, treat that as a request to modify the current active request in place unless they explicitly asked for an additional request.");
        prompt.AppendLine("- For iterate/enumerate/batch/stash transformations, search the local docs for `batch-stash-loop` and `request-url` before asking the user how to structure the script.");
        prompt.AppendLine("- If the user asks to do a similar request step multiple times, including phrases like `3 times`, `repeat N times`, or `at least N times`, prefer documented `foreach`, range, and `max_send_iterations` flow instead of manually duplicating near-identical request blocks.");
        prompt.AppendLine("- For API-surface or CRUD-style rewrites, search the local docs for `request-send`, `request-method`, `request-url`, `request-headers`, `request-body`, `request-content-type`, `api-surface-crud`, `stash`, and `expect`, then use those exact patterns.");
        prompt.AppendLine("- If the user asks for randomized or unique values, use only documented ForRest helpers found in local docs. Prefer built-ins like `guid()` runtime values plus documented `strings`, `convert`, or `time` helpers; do not invent `Math.*`, instance methods like `.Substring(...)`, or arbitrary C# APIs.");
        prompt.AppendLine("- For normal JSON access, prefer dynamic `response.someField` or `response[0].someField` patterns over `response.json()`.");
        prompt.AppendLine("- When the response body root is an array, iterate `response` directly or use `response[index]` instead of inventing wrapper properties.");
        prompt.AppendLine("- `response.json()` returns a raw JsonNode. If you use it, stick to explicit indexers or `AsArray()` and do not use dot-member access on its return value.");
        prompt.AppendLine("- Do not ask whether to keep the current request or create a new one when the user asked to transform the active request. Default to updating the active request.");
        prompt.AppendLine("- Once the request is actionable, prefer making the edit over continuing the chat.");
        prompt.AppendLine("- Inline IDE mode is not a questionnaire. If the user asked you to fix, rewrite, or improve the request, choose a reasonable default and do the work.");
        prompt.AppendLine("- If the user names a field or value with a small typo but the nearest valid field is obvious from the request, diagnostics, or docs, choose the closest valid option and proceed. Mention the assumption briefly after the edit instead of blocking on a question.");
        prompt.AppendLine("- Use only documented ForRest syntax. If a construct is not in the local docs, do not invent it.");
        prompt.AppendLine("- Top-level request config uses bare directives like `method`, `url`, `header`, and `content_type`.");
        prompt.AppendLine("- Dotted members like `request.method`, `request.url`, `request.body`, `request.content_type`, and `request.headers[...]` belong inside flow code before `request.send()`.");
        prompt.AppendLine("- Use only `#` comments on their own lines. Do not use `//` comments and do not append trailing inline comments after code.");
        prompt.AppendLine("- `expect` statements are top-level assertions. Do not put `expect` inside `if`, `else`, `foreach`, or `while` blocks.");
        prompt.AppendLine("- If diagnostics mention parse errors around expectations, comments, headers, URLs, or bodies, consult the matching local docs (`expect`, `comments`, `request-headers`, `request-url`, `body`) before retrying.");
        prompt.AppendLine("- Do not ask the user to paste working syntax, grammar examples, or line numbers if the active document diagnostics or local docs can answer it.");
        prompt.AppendLine("- Do not ask the user whether ForRest supports a syntax or helper that the local docs or active document can confirm.");
        prompt.AppendLine("- Do not ask multiple-choice follow-up questions unless the request is truly blocked on a real product decision the user must make.");
        prompt.AppendLine($"- Ask at most {Math.Max(0, settings.Conversation.MaxClarificationTurns)} clarification turn(s) before acting. If the request is still ambiguous after that, state the assumptions you chose and proceed.");
        prompt.AppendLine("- Do not ask for confirmation before using local docs, reading the active document, or applying an obviously requested fix.");
        prompt.AppendLine("- If a tool response includes `retryWithRead`, `retryWithReplace`, or `docHints`, treat that as a direct instruction to continue autonomously: re-read the active document, consult the hinted docs, and retry without asking the user what to do next.");
        if (toolList.Any(static tool => string.Equals(tool.Name, "replace_active_document", StringComparison.Ordinal)))
        {
            prompt.AppendLine("- If patch_active_document fails or the active document looks garbled, do not ask the user what to do next. Read the active document again if needed and use replace_active_document with the full corrected request.");
            prompt.AppendLine("- If replace_active_document is rejected, inspect the returned diagnostics, repair the full source, and submit another replace_active_document call instead of asking the user to resolve the syntax for you.");
            prompt.AppendLine("- If an editor update is rejected with a parse error and the active document is still unchanged, treat that as an internal failure. Re-read the document, use the diagnostics, and retry with a valid replacement instead of reporting the failed attempt back to the user.");
        }
        prompt.AppendLine("- If the request already contains working `expect` lines, preserve their exact grammar unless you are only moving them back to top-level.");
        prompt.AppendLine("- If an edit is rejected, read the active document again, inspect the latest diagnostics, and try one corrected edit before asking the user for more information.");
        prompt.AppendLine("- If the user asked for an edit and the active document is still unchanged after a failed attempt, treat that as intermediate work. Retry internally with docs and diagnostics instead of surfacing the failed attempt.");
        prompt.AppendLine("- Do not stop at 'one more fix is needed' or 'I can fix that next' after a partial edit. Keep repairing until the active request is valid or you have a concrete blocking product decision.");
        prompt.AppendLine("- If an active document topic is present below, treat its source and diagnostics as the current truth. Do not ask the user to paste them again.");
        prompt.AppendLine();

        if (!string.IsNullOrWhiteSpace(objective))
        {
            prompt.AppendLine($"Current objective: {objective.Trim()}");
            prompt.AppendLine();
        }

        if (toolList.Count > 0)
        {
            prompt.AppendLine("Available tools:");
            foreach (AiToolDescriptor tool in toolList)
            {
                prompt.AppendLine($"- {tool.Name}: {tool.Description}");
                prompt.AppendLine($"  Hint: {tool.InvocationHint}");
            }

            prompt.AppendLine();
        }

        if (topicList.Count > 0)
        {
            prompt.AppendLine("Knowledge base:");
            foreach (AiPromptTopic topic in topicList)
            {
                prompt.AppendLine($"- {topic.Title} ({topic.Source})");
                prompt.AppendLine($"  {topic.Summary}");
                if (!string.IsNullOrWhiteSpace(topic.Content))
                {
                    prompt.AppendLine($"  {TrimContent(topic)}");
                }
            }
        }

        return new(prompt.ToString().TrimEnd(), toolList, topicList);
    }

    private static string NullToPlaceholder(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(not set)" : value.Trim();
    }

    private static string DescribeEndpoint(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(not set)" : "(configured)";
    }

    private static string TrimContent(AiPromptTopic topic)
    {
        string text = (topic.Content ?? string.Empty).Trim();
        int limit = string.Equals(topic.Source, "active-document", StringComparison.OrdinalIgnoreCase)
            ? 2000
            : topic.Source.StartsWith("preflight-doc:", StringComparison.OrdinalIgnoreCase)
                ? 700
            : 240;
        return text.Length <= limit ? text : text[..limit].TrimEnd();
    }
}
