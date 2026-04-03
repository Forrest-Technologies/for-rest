#pragma warning disable OPENAI001
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.AI.OpenAI;
using ForRest.Scripting;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.OpenAI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using System.ClientModel;

namespace ForRest.Services.AI;

public sealed record AiPreparedRuntime(
    AiPromptManifest PromptManifest,
    IReadOnlyList<AiSettingsIssue> Issues,
    AIAgent? Agent,
    AiDebugTraceBuffer DebugTrace);

public interface IAiRuntimeFactory
{
    AiPreparedRuntime Prepare(
        AiSettings settings,
        string objective,
        IAiActiveDocumentHost? activeDocumentHost = null,
        string? prompt = null);
}

public sealed class AgentFrameworkAiRuntimeFactory : IAiRuntimeFactory
{
    private const string AgentName = "ForRestAssistant";
    private const string PromptContextTopicTitle = "ForRest prompt context";
    private const int MaxPreflightPromptTopics = 4;
    private const int MaxPreflightPatternLines = 18;
    private const int MaxPreflightPatternLength = 900;
    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RepeatedExecutionRegex = new(
        @"\b(?:repeat|repeatedly|rerun|multiple\s+times)\b|\b(?:at\s+least\s+|up\s+to\s+)?(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+times\b|\b(?:once|twice|thrice)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly IAiSettingsValidator _settingsValidator;
    private readonly IAiToolCatalog _toolCatalog;
    private readonly IAiActiveDocumentToolCatalog _activeDocumentToolCatalog;
    private readonly IAiActiveDocumentToolService _activeDocumentToolService;
    private readonly IAiPromptManifestBuilder _promptManifestBuilder;
    private readonly IAiKnowledgeCatalog _knowledgeCatalog;
    private readonly IAiDocumentationSearchService _documentationSearchService;
    private readonly IAiDocumentPatchService _documentPatchService;

    public AgentFrameworkAiRuntimeFactory(
        IAiSettingsValidator settingsValidator,
        IAiToolCatalog toolCatalog,
        IAiPromptManifestBuilder promptManifestBuilder,
        IAiKnowledgeCatalog knowledgeCatalog,
        IAiDocumentationSearchService documentationSearchService,
        IAiDocumentPatchService documentPatchService)
    {
        _settingsValidator = settingsValidator;
        _toolCatalog = toolCatalog;
        _activeDocumentToolCatalog = new AiActiveDocumentToolCatalog();
        _activeDocumentToolService = new AiActiveDocumentToolService(documentPatchService);
        _promptManifestBuilder = promptManifestBuilder;
        _knowledgeCatalog = knowledgeCatalog;
        _documentationSearchService = documentationSearchService;
        _documentPatchService = documentPatchService;
    }

    public AiPreparedRuntime Prepare(
        AiSettings settings,
        string objective,
        IAiActiveDocumentHost? activeDocumentHost = null,
        string? prompt = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AiDebugTraceBuffer debugTrace = new();
        List<AiSettingsIssue> issues = [.. _settingsValidator.Validate(settings)];
        IReadOnlyList<AiToolDescriptor> tools =
        [
            .. _toolCatalog.GetTools(settings, activeDocumentHost),
            .. _activeDocumentToolCatalog.GetTools(settings, activeDocumentHost)
        ];
        IReadOnlyList<AiPromptTopic> topics = BuildPromptTopics(settings, objective, prompt, activeDocumentHost, debugTrace);
        AiPromptManifest manifest = _promptManifestBuilder.Build(settings, objective, tools, topics);
        debugTrace.AddSection(
            "Runtime preparation summary",
            string.Join(
                Environment.NewLine,
                [
                    $"Provider: {settings.Provider.ProviderKind}",
                    $"Transport: {settings.Provider.Transport}",
                    $"Model: {settings.Provider.Model}",
                    $"System prompt override: {(string.IsNullOrWhiteSpace(settings.SystemPromptPrefix) ? "(blank)" : settings.SystemPromptPrefix.Trim())}",
                    $"Docs search enabled: {settings.Tools.EnableDocsSearch}",
                    $"Document patch enabled: {settings.Tools.EnableDocumentPatch}",
                    $"Registered tools: {string.Join(", ", tools.Select(static tool => tool.Name))}",
                    $"Selected topics: {string.Join(", ", topics.Select(static topic => $"{topic.Title} ({topic.Source})"))}",
                    $"Settings issues: {(issues.Count == 0 ? "none" : string.Join(" | ", issues.Select(static issue => $"{issue.Code}: {issue.Message}")))}",
                ]));
        debugTrace.AddSection("System prompt handed to agent", manifest.SystemPrompt);
        if (issues.Any(static issue => issue.Severity == AiSettingsIssueSeverity.Error) ||
            !settings.ApiKey.HasUsableValue)
        {
            return new(manifest, issues, Agent: null, debugTrace);
        }

        AITool[] runtimeTools = BuildRuntimeTools(settings, activeDocumentHost, debugTrace);
        AIAgent agent = settings.Provider.ProviderKind switch
        {
            AiProviderKind.AzureOpenAI => CreateAzureAgent(settings, manifest.SystemPrompt, runtimeTools),
            _ => CreateOpenAiAgent(settings, manifest.SystemPrompt, runtimeTools),
        };

        debugTrace.AddLine($"Prepared agent: {agent.Name ?? AgentName}");
        return new(manifest, issues, agent, debugTrace);
    }

    private IReadOnlyList<AiPromptTopic> BuildPromptTopics(
        AiSettings settings,
        string objective,
        string? prompt,
        IAiActiveDocumentHost? activeDocumentHost,
        AiDebugTraceBuffer debugTrace)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AiActiveDocumentSnapshot? activeDocument = activeDocumentHost?.GetActiveDocument();
        List<AiPromptTopic> topics = settings.Tools.EnableDocsSearch
            ? [.. SelectEmbeddedPromptTopics(_knowledgeCatalog.GetTopics())]
            : [.. _knowledgeCatalog.GetTopics()];
        debugTrace.AddLine($"Base prompt topics: {string.Join(", ", topics.Select(static topic => $"{topic.Title} ({topic.Source})"))}");

        IReadOnlyList<AiPromptTopic> preflightTopics = BuildPreflightPromptTopics(settings, objective, prompt, activeDocument, debugTrace);
        if (preflightTopics.Count > 0)
        {
            topics.InsertRange(Math.Min(1, topics.Count), preflightTopics);
        }

        if (activeDocument is null)
        {
            debugTrace.AddLine("Active document snapshot: none");
            return topics;
        }

        List<string> diagnostics =
        [
            .. activeDocument.Diagnostics.Select(
                static diagnostic => $"{diagnostic.Severity.ToUpperInvariant()} L{diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}")
        ];

        string content = string.Join(
            Environment.NewLine,
            [
                $"Document id: {activeDocument.DocumentId}",
                $"Title: {activeDocument.Title}",
                $"Language: {activeDocument.Language}",
                diagnostics.Count == 0
                    ? "Diagnostics: none"
                    : $"Diagnostics:{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}",
                BuildRuntimeContextBlock(activeDocument.RuntimeContext),
                "Source:",
                activeDocument.SourceText,
            ]);

        topics.Insert(
            0,
            new(
                "Active document",
                "The current request document plus its latest compiler diagnostics and runtime context. Use this instead of asking the user to paste the script or error list again.",
                "active-document",
                content));
        debugTrace.AddSection(
            "Active document snapshot",
            string.Join(
                Environment.NewLine,
                [
                    $"DocumentId: {activeDocument.DocumentId}",
                    $"Title: {activeDocument.Title}",
                    $"Language: {activeDocument.Language}",
                    $"Diagnostics: {activeDocument.Diagnostics.Count}",
                    $"Runtime status: {activeDocument.RuntimeContext?.Status ?? "(none)"}",
                    "Source:",
                    activeDocument.SourceText,
                ]));

        return topics;
    }

    private static IEnumerable<AiPromptTopic> SelectEmbeddedPromptTopics(IEnumerable<AiPromptTopic> topics)
    {
        ArgumentNullException.ThrowIfNull(topics);

        List<AiPromptTopic> selected = topics
            .Where(static topic => string.Equals(topic.Title, PromptContextTopicTitle, StringComparison.Ordinal))
            .Take(1)
            .ToList();
        if (selected.Count > 0)
        {
            return selected;
        }

        return topics.Take(1);
    }

    private IReadOnlyList<AiPromptTopic> BuildPreflightPromptTopics(
        AiSettings settings,
        string objective,
        string? prompt,
        AiActiveDocumentSnapshot? activeDocument,
        AiDebugTraceBuffer debugTrace)
    {
        if (!settings.Tools.EnableDocsSearch || !IsComplexEditPrompt(objective, prompt, activeDocument))
        {
            debugTrace.AddLine("Preflight docs: skipped because docs search is disabled or the prompt is not a complex edit.");
            return [];
        }

        List<(string Query, AiKnowledgeSearchHit Hit)> selectedDocs = [];
        HashSet<string> seenDocumentIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string query in BuildTargetedDocQueries(objective, prompt, activeDocument))
        {
            IReadOnlyList<AiKnowledgeSearchHit> hits = _documentationSearchService.Search(query, maxResults: 1);
            if (hits.Count == 0 || AiDocumentationSearchService.ShouldFallbackToFullDocs(query, hits))
            {
                debugTrace.AddLine($"Preflight docs query '{query}': no strong local hit.");
                continue;
            }

            AiKnowledgeSearchHit hit = hits[0];
            if (!seenDocumentIds.Add(hit.Document.Id))
            {
                debugTrace.AddLine($"Preflight docs query '{query}': skipped duplicate topic '{hit.Document.Title}'.");
                continue;
            }

            selectedDocs.Add((query, hit));
            debugTrace.AddLine($"Preflight docs query '{query}': selected '{hit.Document.Title}' ({hit.Document.Id}).");
            if (selectedDocs.Count >= MaxPreflightPromptTopics)
            {
                break;
            }
        }

        return selectedDocs
            .Select(BuildPreflightPromptTopic)
            .ToArray();
    }

    private static bool IsComplexEditPrompt(string objective, string? prompt, AiActiveDocumentSnapshot? activeDocument)
    {
        string candidate = string.IsNullOrWhiteSpace(prompt) ? objective : prompt;
        if (!AiPromptIntentClassifier.IsLikelyEditPrompt(candidate))
        {
            return false;
        }

        string normalized = NormalizePromptText(candidate);
        string[] tokens = TokenizePrompt(normalized);
        int methodCount = CountDistinctHttpMethods(tokens);
        int urlCount = UrlRegex.Matches(candidate).Count;
        bool explicitLoopPrompt = ContainsAny(normalized, "iterate", "enumerate", "batch", "foreach", "loop", "max_send_iterations", "max send iterations");
        bool repeatedExecutionPrompt = LooksLikeRepeatedExecutionPrompt(normalized);
        bool requestMutationPrompt =
            methodCount > 0 ||
            ContainsAny(normalized, "stash", "request.url", "request.send", "send()", "request.method", "request.body", "request.content_type", "request.headers");
        bool loopCapableActiveDocument = repeatedExecutionPrompt && HasLoopCapableFlow(activeDocument?.SourceText);
        bool apiSurfacePrompt =
            ContainsAny(normalized, "api surface", "crud", "fully test", "full api surface", "test the api", "test this api") ||
            methodCount >= 3;
        bool batchPrompt =
            (explicitLoopPrompt &&
             (ContainsAny(normalized, "stash", "request.url", "request.send", "send()") || requestMutationPrompt || loopCapableActiveDocument)) ||
            (repeatedExecutionPrompt && (requestMutationPrompt || loopCapableActiveDocument));
        bool docHeavyPrompt =
            ContainsAny(normalized, "response body example", "request body example", "request url example", "parameters", "description") &&
            (methodCount >= 2 || urlCount >= 2);
        bool multiMutationPrompt =
            ContainsAny(normalized, "request.send", "request.method", "request.url", "request.body", "request.content_type", "request.headers", "expect") &&
            (methodCount >= 2 || urlCount >= 2);

        return apiSurfacePrompt || batchPrompt || docHeavyPrompt || multiMutationPrompt;
    }

    private static IReadOnlyList<string> BuildTargetedDocQueries(
        string objective,
        string? prompt,
        AiActiveDocumentSnapshot? activeDocument)
    {
        string promptText = string.IsNullOrWhiteSpace(prompt) ? objective : prompt;
        string normalizedPrompt = NormalizePromptText(promptText);
        string signalText = string.Join(
            Environment.NewLine,
            [
                promptText,
                activeDocument?.SourceText ?? string.Empty,
                .. (activeDocument?.Diagnostics ?? []).Select(static diagnostic => diagnostic.Message)
            ]);
        string normalized = NormalizePromptText(signalText);
        string[] tokens = TokenizePrompt(normalized);
        int methodCount = CountDistinctHttpMethods(tokens);
        int urlCount = UrlRegex.Matches(signalText).Count;
        bool explicitLoopPrompt = ContainsAny(
            normalizedPrompt,
            "iterate",
            "enumerate",
            "batch",
            "foreach",
            "loop",
            "max_send_iterations",
            "max send iterations");
        bool repeatedExecutionPrompt = LooksLikeRepeatedExecutionPrompt(normalizedPrompt);
        bool requestMutationPrompt =
            methodCount > 0 ||
            ContainsAny(normalized, "request.send", "request.url", "request.method", "request.body", "request.content_type", "request.headers", "stash");
        bool loopCapableActiveDocument = repeatedExecutionPrompt && HasLoopCapableFlow(activeDocument?.SourceText);

        bool apiSurfacePrompt =
            ContainsAny(normalized, "api surface", "crud", "fully test", "full api surface", "test the api", "test this api") ||
            methodCount >= 3;
        bool batchPrompt =
            explicitLoopPrompt ||
            (repeatedExecutionPrompt && (requestMutationPrompt || loopCapableActiveDocument));
        bool randomizationPrompt = ContainsAny(normalizedPrompt, "random", "randomized", "unique");
        bool headerPrompt = ContainsAny(
            normalized,
            "header",
            "headers",
            "content-type",
            "accept",
            "x-correlation-id",
            "x-workspace",
            "x-environment");
        bool writePayloadPrompt =
            CountMatchingHttpMethods(tokens, "post", "put", "patch") > 0 ||
            ContainsAny(normalized, "request.body", "request.content_type", "body", "payload", "application/json", "content_type");
        bool urlPrompt =
            urlCount > 0 ||
            ContainsAny(normalized, "request.url", "?id=", "endpoint", "/objects/", "filtered list");
        bool stashPrompt = ContainsAny(normalized, "stash", "capture rows", "stash rows", "capture the important fields");
        bool expectPrompt =
            ContainsAny(normalized, "expect", "assert", "verify", "validate", "test") ||
            apiSurfacePrompt ||
            batchPrompt;

        List<string> queries = [];
        AddQuery(queries, "api-surface-crud", apiSurfacePrompt);
        AddQuery(queries, "batch-stash-loop", batchPrompt);
        AddQuery(queries, "runtime", randomizationPrompt);
        AddQuery(queries, "request-headers", headerPrompt);
        AddQuery(queries, "request-content-type", writePayloadPrompt);
        AddQuery(queries, "request-body", writePayloadPrompt);
        AddQuery(queries, "request-url", urlPrompt);
        AddQuery(queries, "request-method", apiSurfacePrompt);
        AddQuery(queries, "stash", stashPrompt);
        AddQuery(queries, "expect", expectPrompt);
        AddQuery(queries, "request-send", !apiSurfacePrompt || batchPrompt || urlPrompt);
        AddQuery(queries, "max-send-iterations", batchPrompt);

        return queries;
    }

    private static void AddQuery(ICollection<string> queries, string query, bool include)
    {
        if (!include || queries.Any(existing => string.Equals(existing, query, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        queries.Add(query);
    }

    private static AiPromptTopic BuildPreflightPromptTopic((string Query, AiKnowledgeSearchHit Hit) selection)
    {
        AiKnowledgeSearchHit hit = selection.Hit;
        string key = TryGetCatalogKey(hit.Document) ?? hit.Document.Id;
        ForRestLanguageHelpEntry? entry = TryGetLanguageEntry(key);
        string content = entry is null
            ? BuildGenericPreflightContent(hit)
            : BuildEntryPreflightContent(entry);

        return new(
            $"Preflight: {hit.Document.Title}",
            $"Auto-loaded local example for `{selection.Query}` before the first edit turn.",
            $"preflight-doc:{key}",
            content);
    }

    private static string BuildGenericPreflightContent(AiKnowledgeSearchHit hit)
    {
        StringBuilder builder = new();
        builder.Append("Summary: ").AppendLine(hit.Document.Summary);
        if (!string.IsNullOrWhiteSpace(hit.Excerpt))
        {
            builder.Append("Excerpt: ").AppendLine(hit.Excerpt.Trim());
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildEntryPreflightContent(ForRestLanguageHelpEntry entry)
    {
        StringBuilder builder = new();
        builder.Append("Summary: ").AppendLine(entry.Summary);
        if (!string.IsNullOrWhiteSpace(entry.Documentation))
        {
            builder.Append("Rule: ").AppendLine(entry.Documentation.Trim());
        }

        string pattern = SelectPreflightPattern(entry);
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            builder.AppendLine("Pattern:");
            builder.AppendLine(TrimPreflightBlock(pattern));
        }

        return builder.ToString().TrimEnd();
    }

    private static string SelectPreflightPattern(ForRestLanguageHelpEntry entry)
    {
        string pattern = string.Equals(entry.Key, "api-surface-crud", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(entry.InsertText)
            ? entry.InsertText
            : entry.Example;

        if (string.IsNullOrWhiteSpace(pattern) &&
            !string.IsNullOrWhiteSpace(entry.InsertText) &&
            !entry.InsertText.Contains("${", StringComparison.Ordinal))
        {
            pattern = entry.InsertText;
        }

        return NormalizeLineEndings(pattern).Trim();
    }

    private static string TrimPreflightBlock(string value)
    {
        string normalized = NormalizeLineEndings(value).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        string[] lines = normalized.Split('\n');
        if (lines.Length > MaxPreflightPatternLines)
        {
            normalized = string.Join(Environment.NewLine, lines.Take(MaxPreflightPatternLines)) + Environment.NewLine + "...";
        }

        return normalized.Length <= MaxPreflightPatternLength
            ? normalized
            : normalized[..MaxPreflightPatternLength].TrimEnd() + " ...";
    }

    private static ForRestLanguageHelpEntry? TryGetLanguageEntry(string key)
    {
        return ForRestLanguageCatalog
            .GetEntries()
            .FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetCatalogKey(AiKnowledgeDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.SourcePath) &&
            document.SourcePath.StartsWith("ForRestLanguageCatalog:", StringComparison.OrdinalIgnoreCase))
        {
            return document.SourcePath["ForRestLanguageCatalog:".Length..];
        }

        if (document.Id.StartsWith("catalog:", StringComparison.OrdinalIgnoreCase))
        {
            return document.Id["catalog:".Length..];
        }

        return null;
    }

    private static string NormalizePromptText(string? value)
    {
        return NormalizeLineEndings(value)
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('–', '-')
            .Replace('—', '-')
            .ToLowerInvariant();
    }

    private static string NormalizeLineEndings(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static bool ContainsAny(string value, params string[] markers)
    {
        return markers.Any(marker => value.Contains(marker, StringComparison.Ordinal));
    }

    private static bool LooksLikeRepeatedExecutionPrompt(string normalizedPrompt)
    {
        return !string.IsNullOrWhiteSpace(normalizedPrompt) &&
               RepeatedExecutionRegex.IsMatch(normalizedPrompt);
    }

    private static bool HasLoopCapableFlow(string? sourceText)
    {
        string normalizedSource = NormalizePromptText(sourceText);
        return ContainsAny(
            normalizedSource,
            "request.send",
            "request.url",
            "request.method",
            "max_send_iterations",
            "foreach",
            "stash.commit",
            "stash.");
    }

    private static string[] TokenizePrompt(string prompt)
    {
        return prompt.Split(
            [' ', '\t', '\r', '\n', '.', ',', '!', '?', ':', ';', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '`'],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static int CountDistinctHttpMethods(IEnumerable<string> tokens)
    {
        return tokens
            .Where(static token => token is "get" or "post" or "put" or "patch" or "delete")
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static int CountMatchingHttpMethods(IEnumerable<string> tokens, params string[] methods)
    {
        HashSet<string> methodSet = new(methods, StringComparer.Ordinal);
        return tokens.Count(methodSet.Contains);
    }

    private static string BuildRuntimeContextBlock(AiActiveDocumentRuntimeContext? runtimeContext)
    {
        if (runtimeContext is null)
        {
            return "Runtime context: none";
        }

        List<string> lines =
        [
            string.IsNullOrWhiteSpace(runtimeContext.Status)
                ? "Runtime status: unknown"
                : $"Runtime status: {runtimeContext.Status}",
            string.IsNullOrWhiteSpace(runtimeContext.ErrorMessage)
                ? "Runtime error: none"
                : $"Runtime error: {runtimeContext.ErrorMessage}",
        ];

        if (!string.IsNullOrWhiteSpace(runtimeContext.ResponseBodyPreview))
        {
            lines.Add("Response preview:");
            lines.Add(TrimRuntimeBlock(runtimeContext.ResponseBodyPreview, 400));
        }

        if (!string.IsNullOrWhiteSpace(runtimeContext.DebugText))
        {
            lines.Add("Runtime debug:");
            lines.Add(TrimRuntimeBlock(runtimeContext.DebugText, 1200));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string TrimRuntimeBlock(string value, int maxLength)
    {
        string normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd() + " ...";
    }

    private AITool[] BuildRuntimeTools(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost, AiDebugTraceBuffer debugTrace)
    {
        List<AITool> tools = [];
        if (settings.Tools.EnableDocsSearch)
        {
            tools.Add(AIFunctionFactory.Create((Func<string, int, string>)SearchDocs));
            tools.Add(AIFunctionFactory.Create((Func<string>)ReadAllDocs));
        }

        if (settings.Tools.EnableDocumentPatch && activeDocumentHost is not null)
        {
            tools.Add(AIFunctionFactory.Create((Func<string>)ReadActiveDocument));
            tools.Add(AIFunctionFactory.Create((Func<string, string>)PatchActiveDocument));
            tools.Add(AIFunctionFactory.Create((Func<string, string>)ReplaceActiveDocument));
        }
        else if (settings.Tools.EnableDocumentPatch)
        {
            tools.Add(AIFunctionFactory.Create((Func<string, string, string, string>)PatchDocument));
        }

        return [.. tools];

        [Description("Search the canonical local ForRest docs and language reference. If no useful hits are found, this tool falls back to the full local docs corpus.")]
        string SearchDocs(
            [Description("The docs query to search for.")] string query,
            [Description("Maximum number of search hits to return.")] int maxResults)
        {
            return TraceToolCall(
                "search_docs",
                string.Join(
                    Environment.NewLine,
                    [
                        $"query: {query}",
                        $"maxResults: {maxResults}",
                    ]),
                () =>
                {
                    if (string.IsNullOrWhiteSpace(query))
                    {
                        return BuildFullDocsCorpus("No docs query was supplied. Returning the full local docs corpus.");
                    }

                    int boundedResults = Math.Clamp(maxResults, 1, settings.Tools.MaxSearchResults);
                    IReadOnlyList<AiKnowledgeSearchHit> hits = _documentationSearchService.Search(query, boundedResults);
                    if (AiDocumentationSearchService.ShouldFallbackToFullDocs(query, hits))
                    {
                        string fallbackPreface = hits.Count == 0
                            ? "No matching ForRest docs were found. Returning the full local docs corpus instead."
                            : "The targeted docs hits were too weak. Returning the full local docs corpus instead.";
                        return BuildFullDocsCorpus(fallbackPreface);
                    }

                    return RenderSearchHits(hits);
                },
                maxResultLength: 2400);
        }

        [Description("Read the entire canonical local ForRest docs corpus in one pass.")]
        string ReadAllDocs()
        {
            return TraceToolCall(
                "read_all_docs",
                "(no arguments)",
                () => BuildFullDocsCorpus("Returning the full local docs corpus."),
                maxResultLength: 2400);
        }

        [Description("Apply bounded non-overlapping text edits to a document source string.")]
        string PatchDocument(
            [Description("The document identifier the patch applies to.")] string documentId,
            [Description("The current document source text to patch.")] string sourceText,
            [Description("A JSON array of edits with startIndex, length, and replacement fields.")] string editsJson)
        {
            return TraceToolCall(
                "patch_document",
                string.Join(
                    Environment.NewLine,
                    [
                        $"documentId: {documentId}",
                        $"sourceLength: {(sourceText ?? string.Empty).Length}",
                        "editsJson:",
                        editsJson,
                    ]),
                () =>
                {
                    AiTextEdit[] edits;
                    try
                    {
                        edits = JsonSerializer.Deserialize<AiTextEdit[]>(editsJson) ?? [];
                    }
                    catch (JsonException)
                    {
                        return JsonSerializer.Serialize(new
                        {
                            succeeded = false,
                            errors = new[] { "The editsJson payload must be a JSON array of AiTextEdit objects." },
                        });
                    }

                    AiDocumentPatchResult result = _documentPatchService.Apply(new(documentId, sourceText ?? string.Empty, edits));
                    return JsonSerializer.Serialize(new
                    {
                        succeeded = result.Succeeded,
                        patchedText = result.PatchedText,
                        errors = result.Errors,
                    });
                });
        }

        [Description("Read the current active document and its compiler diagnostics from the host canvas.")] 
        string ReadActiveDocument()
        {
            return TraceToolCall(
                "read_active_document",
                "(no arguments)",
                () => _activeDocumentToolService.ReadActiveDocument(settings, activeDocumentHost),
                maxResultLength: 3200);
        }

        [Description("Apply bounded edits to the current active document without supplying raw source text.")]
        string PatchActiveDocument(
            [Description("A JSON array of edits with startIndex, length, and replacement fields.")] string editsJson)
        {
            return TraceToolCall(
                "patch_active_document",
                string.Join(
                    Environment.NewLine,
                    [
                        "editsJson:",
                        editsJson,
                    ]),
                () => _activeDocumentToolService.PatchActiveDocument(settings, activeDocumentHost, editsJson),
                maxResultLength: 3200);
        }

        [Description("Replace the entire active document with new source text when a full rewrite is safer than targeted edits.")]
        string ReplaceActiveDocument(
            [Description("The complete replacement source text for the active document.")] string updatedSourceText)
        {
            return TraceToolCall(
                "replace_active_document",
                string.Join(
                    Environment.NewLine,
                    [
                        "updatedSourceText:",
                        updatedSourceText,
                    ]),
                () => _activeDocumentToolService.ReplaceActiveDocument(settings, activeDocumentHost, updatedSourceText),
                maxResultLength: 3200);
        }

        string TraceToolCall(string toolName, string arguments, Func<string> action, int maxResultLength = 1600)
        {
            debugTrace.AddSection($"Tool call: {toolName}", arguments);
            try
            {
                string result = action();
                debugTrace.AddSection(
                    $"Tool result: {toolName}",
                    string.IsNullOrWhiteSpace(result)
                        ? "(empty result)"
                        : AiDebugTraceBuffer.Truncate(result, maxResultLength));
                return result;
            }
            catch (Exception exception)
            {
                debugTrace.AddSection($"Tool exception: {toolName}", exception.ToString());
                throw;
            }
        }
    }

    private string RenderSearchHits(IReadOnlyList<AiKnowledgeSearchHit> hits)
    {
        StringBuilder builder = new();
        for (int index = 0; index < hits.Count; index++)
        {
            AiKnowledgeSearchHit hit = hits[index];
            builder.Append(index + 1)
                .Append(". ")
                .Append(hit.Document.Title)
                .AppendLine();
            builder.Append("Summary: ").AppendLine(hit.Document.Summary);
            builder.Append("Excerpt: ").AppendLine(hit.Excerpt);
            builder.Append("Source: ").AppendLine(hit.Document.SourcePath ?? hit.Document.Id);
            if (index + 1 < hits.Count)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildFullDocsCorpus(string preface)
    {
        IReadOnlyList<AiKnowledgeDocument> documents = _knowledgeCatalog.GetDocuments();
        StringBuilder builder = new();
        if (!string.IsNullOrWhiteSpace(preface))
        {
            builder.AppendLine(preface.Trim());
            builder.AppendLine();
        }

        builder.Append("Documents: ").AppendLine(documents.Count.ToString());
        builder.AppendLine();

        for (int index = 0; index < documents.Count; index++)
        {
            AiKnowledgeDocument document = documents[index];
            builder.Append(index + 1)
                .Append(". ")
                .Append(document.Title)
                .AppendLine();
            builder.Append("Summary: ").AppendLine(document.Summary);
            builder.Append("Source: ").AppendLine(document.SourcePath ?? document.Id);
            builder.AppendLine("Content:");
            builder.AppendLine((document.Content ?? string.Empty).Trim());
            if (index + 1 < documents.Count)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static AIAgent CreateAzureAgent(AiSettings settings, string instructions, IReadOnlyList<AITool> runtimeTools)
    {
        AzureOpenAIClient client = new(new Uri(settings.Provider.Endpoint), new ApiKeyCredential(settings.ApiKey.Value));
        string deployment = string.IsNullOrWhiteSpace(settings.Provider.DeploymentName)
            ? settings.Provider.Model
            : settings.Provider.DeploymentName;
        ChatClientAgentOptions options = CreateAgentOptions(instructions, runtimeTools, deployment);

        return settings.Provider.Transport switch
        {
            AiConversationTransport.ChatCompletions => client
                .GetChatClient(deployment)
                .AsAIAgent(options),
            _ => client
                .GetResponsesClient(deployment)
                .AsAIAgent(options),
        };
    }

    private static AIAgent CreateOpenAiAgent(AiSettings settings, string instructions, IReadOnlyList<AITool> runtimeTools)
    {
        OpenAIClient client = CreateOpenAiClient(settings);
        ChatClientAgentOptions options = CreateAgentOptions(instructions, runtimeTools, settings.Provider.Model);

        return settings.Provider.Transport switch
            {
                AiConversationTransport.ChatCompletions => client
                    .GetChatClient(settings.Provider.Model)
                    .AsAIAgent(options),
            _ => client
                .GetResponsesClient(settings.Provider.Model)
                .AsAIAgent(options),
        };
    }

    private static ChatClientAgentOptions CreateAgentOptions(
        string instructions,
        IReadOnlyList<AITool> runtimeTools,
        string modelId)
    {
        return new()
        {
            Name = AgentName,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = [.. runtimeTools],
                ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId,
            },
        };
    }

    private static OpenAIClient CreateOpenAiClient(AiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Provider.Endpoint))
        {
            return new OpenAIClient(settings.ApiKey.Value);
        }

        return new OpenAIClient(
            new ApiKeyCredential(settings.ApiKey.Value),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(settings.Provider.Endpoint),
            });
    }
}
#pragma warning restore OPENAI001
