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
using System.ClientModel.Primitives;

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
        string? prompt = null,
        IAiWorkspaceHost? workspaceHost = null);
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
    private readonly IAiWorkspaceToolCatalog _workspaceToolCatalog;
    private readonly IAiWorkspaceToolService _workspaceToolService;
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
        _workspaceToolCatalog = new AiWorkspaceToolCatalog();
        _workspaceToolService = new AiWorkspaceToolService();
        _promptManifestBuilder = promptManifestBuilder;
        _knowledgeCatalog = knowledgeCatalog;
        _documentationSearchService = documentationSearchService;
        _documentPatchService = documentPatchService;
    }

    public AiPreparedRuntime Prepare(
        AiSettings settings,
        string objective,
        IAiActiveDocumentHost? activeDocumentHost = null,
        string? prompt = null,
        IAiWorkspaceHost? workspaceHost = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AiDebugTraceBuffer debugTrace = new();
        List<AiSettingsIssue> issues = [.. _settingsValidator.Validate(settings)];
        IReadOnlyList<AiToolDescriptor> tools =
        [
            .. _toolCatalog.GetTools(settings, activeDocumentHost),
            .. _activeDocumentToolCatalog.GetTools(settings, activeDocumentHost),
            .. _workspaceToolCatalog.GetTools(settings, workspaceHost)
        ];
        IReadOnlyList<AiPromptTopic> topics = BuildPromptTopics(settings, objective, prompt, activeDocumentHost, workspaceHost, debugTrace);
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

        AITool[] runtimeTools = BuildRuntimeTools(settings, activeDocumentHost, workspaceHost, debugTrace);
        AIAgent agent = settings.Provider.ProviderKind switch
        {
            AiProviderKind.AzureOpenAI => CreateAzureAgent(settings, manifest.SystemPrompt, runtimeTools),
            _ => CreateOpenAiCompatibleAgent(
                settings,
                manifest.SystemPrompt,
                runtimeTools,
                AiProviderDefaults.GetDefaultEndpoint(settings.Provider.ProviderKind)),
        };

        debugTrace.AddLine($"Prepared agent: {agent.Name ?? AgentName}");
        return new(manifest, issues, agent, debugTrace);
    }

    private IReadOnlyList<AiPromptTopic> BuildPromptTopics(
        AiSettings settings,
        string objective,
        string? prompt,
        IAiActiveDocumentHost? activeDocumentHost,
        IAiWorkspaceHost? workspaceHost,
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

        AiWorkspaceContext? workspaceAssistantContext = workspaceHost?.GetWorkspaceContext();
        if (workspaceAssistantContext is not null)
        {
            string workspaceOverview = string.Join(
                Environment.NewLine,
                [
                    $"Workspace: {workspaceAssistantContext.WorkspaceName} (ID: {workspaceAssistantContext.WorkspaceId})",
                    $"Scripts in workspace ({workspaceAssistantContext.Scripts.Count}):",
                    .. workspaceAssistantContext.Scripts.Select(
                        static script => $"  - [{script.ScriptId}] {script.Name}: {script.Method} {script.UrlTemplate}"),
                    "Use list_workspace_scripts / read_workspace_script for full source before editing, and create_workspace_script / update_workspace_script to change the workspace.",
                ]);
            topics.Insert(
                0,
                new(
                    "Workspace overview",
                    "Every request script in the active workspace with its id, name, method, and URL. Operate across these scripts instead of asking the user to paste them.",
                    "workspace-overview",
                    workspaceOverview));
            debugTrace.AddSection("Workspace assistant overview", workspaceOverview);
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

        var redactedSource = AiActiveDocumentToolService.RedactSecrets(activeDocument.SourceText);

        AiWorkspaceContext? workspaceContext = activeDocumentHost?.GetWorkspaceContext();
        string workspaceBlock = workspaceContext is not null
            ? string.Join(
                Environment.NewLine,
                [
                    $"Workspace: {workspaceContext.WorkspaceName} (ID: {workspaceContext.WorkspaceId})",
                    $"Scripts in workspace ({workspaceContext.Scripts.Count}):",
                    .. workspaceContext.Scripts.Select(
                        static script => $"  - {script.Name}: {script.Method} {script.UrlTemplate}"),
                ])
            : "Workspace: (unknown)";

        string content = string.Join(
            Environment.NewLine,
            [
                $"Document id: {activeDocument.DocumentId}",
                $"Title: {activeDocument.Title}",
                $"Language: {activeDocument.Language}",
                workspaceBlock,
                diagnostics.Count == 0
                    ? "Diagnostics: none"
                    : $"Diagnostics:{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}",
                BuildRuntimeContextBlock(activeDocument.RuntimeContext),
                "Source:",
                redactedSource,
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
        // Regex.Count counts matches without materializing the full MatchCollection
        // and Match objects — IsComplexEditPrompt runs on every AI prompt and only
        // needs the count, so the prior `.Matches(...).Count` was pure waste.
        int urlCount = UrlRegex.Count(candidate);
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
        bool advancedFlowPrompt =
            ContainsAny(normalized,
                "retry", "backoff", "on error", "on status", "error handler", "status handler",
                "parallel", "concurrent", "simultaneously",
                "pipe", "pipeline", "sequential",
                "define", "call", "subroutine", "reusable",
                "switch", "case", "branch",
                "snapshot", "named send", "as \"",
                "extract json", "extract header", "extract regex",
                "stash columns", "declare columns",
                "import", "scenario",
                "secret");

        return apiSurfacePrompt || batchPrompt || docHeavyPrompt || multiMutationPrompt || advancedFlowPrompt;
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
        int urlCount = UrlRegex.Count(signalText);
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

        bool retryPrompt = ContainsAny(normalized, "retry", "backoff", "delay", "retries");
        bool errorHandlerPrompt = ContainsAny(normalized, "on error", "error handler", "catch error", "handle error");
        bool statusHandlerPrompt = ContainsAny(normalized, "on status", "status handler", "429", "401", "rate limit");
        bool parallelPrompt = ContainsAny(normalized, "parallel", "concurrent", "simultaneously");
        bool pipePrompt = ContainsAny(normalized, "pipe", "pipeline", "sequential chain");
        bool defineCallPrompt = ContainsAny(normalized, "define", "call", "subroutine", "reusable");
        bool namedSendPrompt = ContainsAny(normalized, "named send", "as \"", "labeled send", "label send");
        bool snapshotPrompt = ContainsAny(normalized, "snapshot", "save snapshot");
        bool stashColumnsPrompt = ContainsAny(normalized, "stash columns", "declare columns");
        bool switchPrompt = ContainsAny(normalized, "switch", "case", "branch on");
        bool extractFlowPrompt = ContainsAny(normalized, "extract json", "extract header", "extract regex", "extract from");
        bool collectionPrompt = ContainsAny(normalized, ".where(", ".select(", ".first(", ".any(", ".count(", "collection", ".orderby(");
        bool secretPrompt = ContainsAny(normalized, "secret");

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
        AddQuery(queries, "retry-flow", retryPrompt);
        AddQuery(queries, "on-error", errorHandlerPrompt);
        AddQuery(queries, "on-status", statusHandlerPrompt);
        AddQuery(queries, "parallel-sends", parallelPrompt);
        AddQuery(queries, "pipe-syntax", pipePrompt);
        AddQuery(queries, "define-call", defineCallPrompt);
        AddQuery(queries, "named-send", namedSendPrompt);
        AddQuery(queries, "snapshot-save", snapshotPrompt);
        AddQuery(queries, "stash-columns", stashColumnsPrompt);
        AddQuery(queries, "switch", switchPrompt);
        AddQuery(queries, "extract-flow", extractFlowPrompt);
        AddQuery(queries, "collection-methods", collectionPrompt);
        AddQuery(queries, "secret", secretPrompt);

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

    private AITool[] BuildRuntimeTools(
        AiSettings settings,
        IAiActiveDocumentHost? activeDocumentHost,
        IAiWorkspaceHost? workspaceHost,
        AiDebugTraceBuffer debugTrace)
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
            tools.Add(AIFunctionFactory.Create((Func<string, string, string>)CreateWorkspaceScript));
        }
        else if (settings.Tools.EnableDocumentPatch)
        {
            tools.Add(AIFunctionFactory.Create((Func<string, string, string, string>)PatchDocument));
        }

        if (settings.Tools.EnableDocumentPatch && workspaceHost is not null)
        {
            tools.Add(AIFunctionFactory.Create((Func<string>)ListWorkspaceScripts));
            tools.Add(AIFunctionFactory.Create((Func<string, string>)ReadWorkspaceScript));
            tools.Add(AIFunctionFactory.Create((Func<string, string, string>)CreateWorkspaceScriptInWorkspace));
            tools.Add(AIFunctionFactory.Create((Func<string, string, string>)UpdateWorkspaceScript));
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

        [Description("Create a new request script in the current workspace.")]
        string CreateWorkspaceScript(
            [Description("The name for the new script.")] string name,
            [Description("The complete ForRest source text for the new script.")] string sourceText)
        {
            return TraceToolCall(
                "create_workspace_script",
                string.Join(
                    Environment.NewLine,
                    [
                        $"name: {name}",
                        "sourceText:",
                        sourceText,
                    ]),
                () => _activeDocumentToolService.CreateWorkspaceScript(settings, activeDocumentHost, name, sourceText),
                maxResultLength: 1600);
        }

        [Description("List every request script in the current workspace with its id, name, method, and URL template.")]
        string ListWorkspaceScripts()
        {
            return TraceToolCall(
                "list_workspace_scripts",
                "(no arguments)",
                () => _workspaceToolService.ListScripts(settings, workspaceHost),
                maxResultLength: 2400);
        }

        [Description("Read the full source and compiler diagnostics for a single workspace script by id.")]
        string ReadWorkspaceScript(
            [Description("The id of the workspace script to read, as returned by list_workspace_scripts.")] string scriptId)
        {
            return TraceToolCall(
                "read_workspace_script",
                $"scriptId: {scriptId}",
                () => _workspaceToolService.ReadScript(settings, workspaceHost, scriptId),
                maxResultLength: 3200);
        }

        [Description("Create a new request script in the current workspace from a name and full ForRest source text.")]
        string CreateWorkspaceScriptInWorkspace(
            [Description("The name for the new script.")] string name,
            [Description("The complete ForRest source text for the new script.")] string sourceText)
        {
            return TraceToolCall(
                "create_workspace_script",
                string.Join(
                    Environment.NewLine,
                    [
                        $"name: {name}",
                        "sourceText:",
                        sourceText,
                    ]),
                () => _workspaceToolService.CreateScript(settings, workspaceHost, name, sourceText),
                maxResultLength: 1600);
        }

        [Description("Replace the entire source of an existing workspace script identified by id.")]
        string UpdateWorkspaceScript(
            [Description("The id of the workspace script to rewrite, as returned by list_workspace_scripts.")] string scriptId,
            [Description("The complete replacement ForRest source text for the script.")] string updatedSourceText)
        {
            return TraceToolCall(
                "update_workspace_script",
                string.Join(
                    Environment.NewLine,
                    [
                        $"scriptId: {scriptId}",
                        "updatedSourceText:",
                        updatedSourceText,
                    ]),
                () => _workspaceToolService.UpdateScript(settings, workspaceHost, scriptId, updatedSourceText),
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

    private static AIAgent CreateOpenAiCompatibleAgent(
        AiSettings settings,
        string instructions,
        IReadOnlyList<AITool> runtimeTools,
        string? fallbackEndpoint)
    {
        OpenAIClient client = CreateOpenAiClient(settings, fallbackEndpoint);
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

    private static OpenAIClient CreateOpenAiClient(AiSettings settings, string? fallbackEndpoint)
    {
        string endpoint = string.IsNullOrWhiteSpace(settings.Provider.Endpoint)
            ? fallbackEndpoint ?? string.Empty
            : settings.Provider.Endpoint;

        IReadOnlyDictionary<string, string> headers = settings.Provider.CustomHeaders;
        bool hasHeaders = headers.Count > 0;
        bool needsGrokCompat = settings.Provider.ProviderKind == AiProviderKind.Grok;

        if (string.IsNullOrWhiteSpace(endpoint) && !hasHeaders && !needsGrokCompat)
        {
            return new OpenAIClient(settings.ApiKey.Value);
        }

        OpenAIClientOptions options = new();
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            options.Endpoint = new Uri(endpoint);
        }

        if (hasHeaders)
        {
            options.AddPolicy(new CustomHeaderPolicy(headers), PipelinePosition.PerTry);
        }

        if (needsGrokCompat)
        {
            // xAI's /v1/chat/completions and /v1/responses accept a strict
            // subset of the OpenAI payload. The OpenAI SDK sends fields
            // (parallel_tool_calls, store, response_format, reasoning on
            // non-reasoning models, metadata, previous_response_id,
            // text.format) that xAI 400s on. Rewrite the outbound body
            // once, before transport, to keep the request working.
            options.AddPolicy(new GrokCompatibilityPolicy(), PipelinePosition.BeforeTransport);
        }

        return new OpenAIClient(new ApiKeyCredential(settings.ApiKey.Value), options);
    }

    private sealed class CustomHeaderPolicy(IReadOnlyDictionary<string, string> headers) : PipelinePolicy
    {
        private readonly IReadOnlyDictionary<string, string> _headers = headers;

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            ApplyHeaders(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            ApplyHeaders(message);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }

        private void ApplyHeaders(PipelineMessage message)
        {
            if (message.Request is null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> header in _headers)
            {
                if (string.IsNullOrWhiteSpace(header.Key))
                {
                    continue;
                }

                message.Request.Headers.Set(header.Key, header.Value ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// Pipeline policy that rewrites outbound Chat Completions / Responses
    /// request bodies so they're compatible with xAI's stricter payload
    /// surface. Applied only when the provider is Grok so we don't touch
    /// any other OpenAI-compatible vendor.
    /// </summary>
    public sealed class GrokCompatibilityPolicy : PipelinePolicy
    {
        /// <summary>
        /// AsyncLocal buffer that captures the most recent outbound
        /// request body this policy processed on the current logical
        /// call stack. The runtime factory sets a fresh buffer at the
        /// start of each turn and reads it back into the debug trace
        /// regardless of whether the call succeeded. Bounded to 32 KiB
        /// so a runaway tool schema can't blow up the trace.
        /// </summary>
        internal static readonly AsyncLocal<StringBuilder?> LastOutboundBody = new();

        private const int MaxCapturedBodyLength = 32 * 1024;

        /// <summary>
        /// Public entry point so unit tests can exercise the sanitizer
        /// without spinning up a full pipeline. Returns the rewritten
        /// body, or <c>null</c> when no modification was needed.
        /// </summary>
        public static byte[]? SanitizeRequestBody(byte[] bodyBytes)
        {
            return TryBuildSanitizedBody(bodyBytes ?? [], out byte[]? rewritten) ? rewritten : null;
        }

        /// <summary>
        /// Top-level field names that xAI's chat and responses endpoints
        /// unconditionally reject regardless of model. `reasoning` is
        /// *not* in this list — see <see cref="IsNonReasoningModel"/>
        /// for the model-aware handling.
        /// </summary>
        private static readonly string[] UnconditionalStripTopLevelFields =
        [
            "parallel_tool_calls",
            "store",
            "response_format",
            "metadata",
            "previous_response_id",
        ];

        /// <summary>
        /// Tool parameter JSON Schema keywords that xAI's schema validator
        /// historically chokes on. Stripped recursively from every tool
        /// definition's <c>parameters</c> block.
        /// </summary>
        private static readonly string[] StripSchemaKeywords =
        [
            "$schema",
            "$id",
            "$defs",
            "definitions",
            "title",
            "examples",
            "default",
        ];

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            TryRewrite(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            await TryRewriteAsync(message);
            await ProcessNextAsync(message, pipeline, currentIndex);
        }

        private static void TryRewrite(PipelineMessage message)
        {
            if (!TryCaptureBody(message, out byte[]? bodyBytes) || bodyBytes is null || bodyBytes.Length == 0)
            {
                return;
            }

            byte[] effectiveBytes = bodyBytes;
            if (TryBuildSanitizedBody(bodyBytes, out byte[]? rewritten) && rewritten is not null)
            {
                ReplaceBody(message, rewritten);
                effectiveBytes = rewritten;
            }

            CaptureForDebugTrace(effectiveBytes);
        }

        private static async Task TryRewriteAsync(PipelineMessage message)
        {
            byte[]? bodyBytes = await TryCaptureBodyAsync(message);
            if (bodyBytes is null || bodyBytes.Length == 0)
            {
                return;
            }

            byte[] effectiveBytes = bodyBytes;
            if (TryBuildSanitizedBody(bodyBytes, out byte[]? rewritten) && rewritten is not null)
            {
                ReplaceBody(message, rewritten);
                effectiveBytes = rewritten;
            }

            CaptureForDebugTrace(effectiveBytes);
        }

        private static void CaptureForDebugTrace(byte[] bytes)
        {
            StringBuilder? buffer = LastOutboundBody.Value;
            if (buffer is null)
            {
                // No active diagnostic scope — nothing to capture. This is
                // the common production path.
                return;
            }

            try
            {
                string text = System.Text.Encoding.UTF8.GetString(bytes);
                if (text.Length > MaxCapturedBodyLength)
                {
                    text = text[..MaxCapturedBodyLength] + "…";
                }

                buffer.Clear();
                buffer.Append(text);
            }
            catch
            {
                // Ignore — diagnostic capture must never mutate the
                // outbound request.
            }
        }

        private static bool TryCaptureBody(PipelineMessage message, out byte[]? bytes)
        {
            bytes = null;
            if (message.Request?.Content is not BinaryContent content)
            {
                return false;
            }

            try
            {
                using MemoryStream buffer = new();
                content.WriteTo(buffer, default);
                bytes = buffer.ToArray();
                return bytes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<byte[]?> TryCaptureBodyAsync(PipelineMessage message)
        {
            if (message.Request?.Content is not BinaryContent content)
            {
                return null;
            }

            try
            {
                using MemoryStream buffer = new();
                await content.WriteToAsync(buffer, default);
                return buffer.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static void ReplaceBody(PipelineMessage message, byte[] bytes)
        {
            if (message.Request is null)
            {
                return;
            }

            message.Request.Content = BinaryContent.Create(BinaryData.FromBytes(bytes));
            message.Request.Headers.Set("Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static bool TryBuildSanitizedBody(byte[] bodyBytes, out byte[]? rewritten)
        {
            rewritten = null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(bodyBytes);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                string? model = null;
                if (doc.RootElement.TryGetProperty("model", out JsonElement modelElement) &&
                    modelElement.ValueKind == JsonValueKind.String)
                {
                    model = modelElement.GetString();
                }

                bool isNonReasoningModel = IsNonReasoningModel(model);

                bool modified = false;
                using MemoryStream output = new();
                using (Utf8JsonWriter writer = new(output))
                {
                    writer.WriteStartObject();
                    foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                    {
                        string name = property.Name;

                        if (Array.Exists(
                                UnconditionalStripTopLevelFields,
                                field => string.Equals(field, name, StringComparison.OrdinalIgnoreCase)))
                        {
                            modified = true;
                            continue;
                        }

                        // `reasoning` is only invalid on non-reasoning
                        // models (e.g. grok-4-fast-non-reasoning). On
                        // grok-4-fast-reasoning / grok-4 / future
                        // reasoning variants it's a legitimate request
                        // parameter and must be preserved.
                        if (string.Equals(name, "reasoning", StringComparison.OrdinalIgnoreCase))
                        {
                            if (isNonReasoningModel)
                            {
                                modified = true;
                                continue;
                            }

                            property.WriteTo(writer);
                            continue;
                        }

                        // Special-case the Responses API `text` object:
                        // xAI rejects the `text.format` subtree.
                        if (string.Equals(name, "text", StringComparison.OrdinalIgnoreCase) &&
                            property.Value.ValueKind == JsonValueKind.Object)
                        {
                            if (TryRewriteTextField(property.Value, writer, out bool textModified))
                            {
                                modified |= textModified;
                                continue;
                            }
                        }

                        // Sanitize every tool's parameter schema so xAI's
                        // stricter JSON Schema validator doesn't 400 on
                        // OpenAI-SDK-generated `$defs` / `additionalProperties`
                        // / `strict` artifacts.
                        if (string.Equals(name, "tools", StringComparison.OrdinalIgnoreCase) &&
                            property.Value.ValueKind == JsonValueKind.Array)
                        {
                            if (TryRewriteToolsArray(property.Value, writer, out bool toolsModified))
                            {
                                modified |= toolsModified;
                                continue;
                            }
                        }

                        property.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                }

                if (!modified)
                {
                    return false;
                }

                rewritten = output.ToArray();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// A Grok model is "non-reasoning" if its id explicitly says so.
        /// xAI publishes reasoning and non-reasoning variants with parallel
        /// names like <c>grok-4-fast-reasoning</c> and
        /// <c>grok-4-fast-non-reasoning</c>. We only strip the
        /// <c>reasoning</c> request parameter when the model id contains
        /// the non-reasoning marker — otherwise we assume reasoning is
        /// legal and leave the parameter alone.
        /// </summary>
        public static bool IsNonReasoningModel(string? model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return false;
            }

            return model.Contains("non-reasoning", StringComparison.OrdinalIgnoreCase)
                || model.Contains("non_reasoning", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryRewriteTextField(JsonElement textElement, Utf8JsonWriter writer, out bool modified)
        {
            modified = false;
            writer.WritePropertyName("text");
            writer.WriteStartObject();
            foreach (JsonProperty property in textElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "format", StringComparison.OrdinalIgnoreCase))
                {
                    modified = true;
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
            return true;
        }

        private static bool TryRewriteToolsArray(JsonElement toolsElement, Utf8JsonWriter writer, out bool modified)
        {
            modified = false;
            writer.WritePropertyName("tools");
            writer.WriteStartArray();
            foreach (JsonElement tool in toolsElement.EnumerateArray())
            {
                if (tool.ValueKind != JsonValueKind.Object)
                {
                    tool.WriteTo(writer);
                    continue;
                }

                writer.WriteStartObject();
                foreach (JsonProperty property in tool.EnumerateObject())
                {
                    if (string.Equals(property.Name, "function", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.Object)
                    {
                        writer.WritePropertyName("function");
                        if (WriteSanitizedToolFunction(property.Value, writer))
                        {
                            modified = true;
                        }

                        continue;
                    }

                    // Top-level Responses API tools are flat (no
                    // `function` wrapper); sanitize them in place.
                    if (string.Equals(property.Name, "parameters", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.Object)
                    {
                        writer.WritePropertyName("parameters");
                        if (WriteSanitizedSchema(property.Value, writer))
                        {
                            modified = true;
                        }

                        continue;
                    }

                    // xAI rejects top-level `strict` on tools.
                    if (string.Equals(property.Name, "strict", StringComparison.OrdinalIgnoreCase))
                    {
                        modified = true;
                        continue;
                    }

                    property.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            return true;
        }

        private static bool WriteSanitizedToolFunction(JsonElement functionElement, Utf8JsonWriter writer)
        {
            bool modified = false;
            writer.WriteStartObject();
            foreach (JsonProperty property in functionElement.EnumerateObject())
            {
                // xAI's Chat Completions tool shape rejects `strict: true`
                // at the function level even though OpenAI supports it.
                if (string.Equals(property.Name, "strict", StringComparison.OrdinalIgnoreCase))
                {
                    modified = true;
                    continue;
                }

                if (string.Equals(property.Name, "parameters", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Object)
                {
                    writer.WritePropertyName("parameters");
                    if (WriteSanitizedSchema(property.Value, writer))
                    {
                        modified = true;
                    }

                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
            return modified;
        }

        /// <summary>
        /// Recursively writes a JSON Schema subtree, stripping keywords
        /// xAI rejects. Keeps <c>type</c>, <c>properties</c>,
        /// <c>items</c>, <c>required</c>, <c>description</c>, <c>enum</c>,
        /// and everything else the validator actually needs.
        /// </summary>
        private static bool WriteSanitizedSchema(JsonElement element, Utf8JsonWriter writer)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                element.WriteTo(writer);
                return false;
            }

            bool modified = false;
            writer.WriteStartObject();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string name = property.Name;

                if (Array.Exists(
                        StripSchemaKeywords,
                        keyword => string.Equals(keyword, name, StringComparison.OrdinalIgnoreCase)))
                {
                    modified = true;
                    continue;
                }

                // xAI's strict validator rejects `additionalProperties:
                // false`. Rewrite to `true` (looser) rather than omitting
                // so custom tool definitions that rely on the key still
                // round-trip.
                if (string.Equals(name, "additionalProperties", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind == JsonValueKind.False)
                    {
                        writer.WriteBoolean("additionalProperties", true);
                        modified = true;
                        continue;
                    }

                    property.WriteTo(writer);
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    writer.WritePropertyName(name);
                    if (WriteSanitizedSchema(property.Value, writer))
                    {
                        modified = true;
                    }

                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    writer.WritePropertyName(name);
                    writer.WriteStartArray();
                    foreach (JsonElement item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            if (WriteSanitizedSchema(item, writer))
                            {
                                modified = true;
                            }
                        }
                        else
                        {
                            item.WriteTo(writer);
                        }
                    }

                    writer.WriteEndArray();
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
            return modified;
        }
    }
}
#pragma warning restore OPENAI001
