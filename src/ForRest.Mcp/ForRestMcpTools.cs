using System.ComponentModel;
using System.Text.Json;
using ForRest.Scripting;
using ForRest.Services.AI;

namespace ForRest.Mcp;

/// <summary>
/// Concrete tool implementations exposed by the For-Rest MCP server. Tools
/// are plain instance methods decorated with <see cref="DescriptionAttribute"/>
/// so that the MCP SDK's reflection-based registration (via
/// <c>McpServerTool.Create</c>) can pick up parameter descriptions and
/// return types without us hand-writing JSON Schema.
///
/// Passive tools (language reference, docs search, provider list, payloads)
/// work even when no <see cref="IForRestMcpHost"/> is wired because they
/// only read from the local scripting catalog and the AI knowledge corpus.
/// Canvas-aware tools (active document read / replace, workspace list)
/// gracefully degrade to an informative error string when the host is
/// missing so external agents can still introspect the server.
/// </summary>
public sealed class ForRestMcpTools
{
    #region Private Fields

    private readonly IAiKnowledgeCatalog knowledgeCatalog;
    private readonly IAiDocumentationSearchService documentationSearch;
    private readonly Func<IForRestMcpHost?> hostAccessor;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    #endregion

    #region Constructors

    public ForRestMcpTools(
        IAiKnowledgeCatalog knowledgeCatalog,
        IAiDocumentationSearchService documentationSearch,
        Func<IForRestMcpHost?> hostAccessor)
    {
        this.knowledgeCatalog = knowledgeCatalog;
        this.documentationSearch = documentationSearch;
        this.hostAccessor = hostAccessor;
    }

    #endregion

    #region Passive tools

    [Description("Returns the canonical ForRest scripting language reference as markdown. Use this to learn the DSL (request surface, flow, auth modes, retry, delay, payloads, expect).")]
    public string get_instructions()
    {
        return ForRestLanguageCatalog.BuildMarkdownReference();
    }

    [Description("Searches the local ForRest documentation corpus and returns the top matching entries with their content. Use this before guessing about language or app behavior.")]
    public string search_docs(
        [Description("Natural-language query describing what you want to know about For-Rest.")] string query,
        [Description("Maximum number of matching entries to return. Defaults to 5, capped at 20.")] int max_results = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "No query supplied.";
        }

        int limit = max_results < 1 ? 1 : (max_results > 20 ? 20 : max_results);
        IReadOnlyList<AiKnowledgeSearchHit> hits = documentationSearch.Search(query, limit);
        if (hits.Count == 0)
        {
            return $"No documents matched '{query}'.";
        }

        return JsonSerializer.Serialize(
            hits.Select(hit => new
            {
                id = hit.Document.Id,
                title = hit.Document.Title,
                summary = hit.Document.Summary,
                source = hit.Document.SourcePath,
                score = hit.Score,
                excerpt = hit.Excerpt,
                content = hit.Document.Content,
            }),
            JsonOptions);
    }

    [Description("Lists every AI provider preset For-Rest ships out of the box, with default endpoints, transports, and aliases. Useful for external agents helping users configure For-Rest.")]
    public string list_ai_providers()
    {
        // The 'forrest-ai-providers' document is registered in AiKnowledgeCatalog.
        AiKnowledgeDocument? providerDoc = knowledgeCatalog
            .GetDocuments()
            .FirstOrDefault(static doc => doc.Id == "forrest-ai-providers");

        return providerDoc?.Content ?? "AI provider reference is not registered.";
    }

    [Description("Lists every built-in security test payload category exposed to ForRest scripts via the `payloads` namespace, along with the number of payloads in each.")]
    public string list_payload_categories()
    {
        PayloadsApi payloads = new();
        IReadOnlyList<string> categories = payloads.Categories();
        return JsonSerializer.Serialize(
            categories.Select(category => new
            {
                name = category,
                count = payloads.Category(category).Count,
            }),
            JsonOptions);
    }

    [Description("Returns the raw payload list for a given category (sqli, xss, path_traversal, command_injection, ssti, open_redirect, xxe, nosqli, crlf, ssrf, or any custom category).")]
    public string get_payloads(
        [Description("Category name or alias.")] string category)
    {
        PayloadsApi payloads = new();
        IReadOnlyList<string> list = payloads.Category(category);
        if (list.Count == 0)
        {
            return $"No payloads are registered for category '{category}'.";
        }

        return JsonSerializer.Serialize(list, JsonOptions);
    }

    #endregion

    #region Canvas-aware tools

    [Description("Returns a JSON snapshot of the active ForRest request document currently open in the desktop canvas: id, title, language, workspace, and full source text.")]
    public string get_active_request()
    {
        IForRestMcpHost? host = hostAccessor();
        if (host is null)
        {
            return "No desktop canvas host is wired to this MCP server.";
        }

        ForRestMcpDocumentSnapshot? snapshot = host.GetActiveDocument();
        if (snapshot is null)
        {
            return "No active document is open.";
        }

        return JsonSerializer.Serialize(
            new
            {
                id = snapshot.DocumentId,
                title = snapshot.Title,
                language = snapshot.Language,
                workspace = snapshot.WorkspaceName,
                source = snapshot.SourceText,
            },
            JsonOptions);
    }

    [Description("Replaces the entire source of the active ForRest request document with the supplied text. The new source must be valid ForRest script (the compiler runs after the edit).")]
    public string replace_active_request(
        [Description("Full ForRest script to install as the new active document body.")] string new_source)
    {
        IForRestMcpHost? host = hostAccessor();
        if (host is null)
        {
            return "No desktop canvas host is wired to this MCP server.";
        }

        if (string.IsNullOrWhiteSpace(new_source))
        {
            return "new_source must not be empty.";
        }

        ForRestMcpUpdateResult result = host.ReplaceActiveDocument(new_source);
        return result.Succeeded
            ? "Active document replaced."
            : $"Active document replace failed: {result.ErrorMessage}";
    }

    [Description("Lists the workspaces currently loaded in the desktop canvas, each with its scripts (name, HTTP method, URL template). Use this to orient external agents before editing documents.")]
    public string list_workspaces()
    {
        IForRestMcpHost? host = hostAccessor();
        if (host is null)
        {
            return "No desktop canvas host is wired to this MCP server.";
        }

        IReadOnlyList<ForRestMcpWorkspaceSummary> workspaces = host.ListWorkspaces();
        return JsonSerializer.Serialize(
            workspaces.Select(workspace => new
            {
                id = workspace.Id,
                name = workspace.Name,
                scripts = workspace.Scripts.Select(script => new
                {
                    name = script.Name,
                    method = script.Method,
                    url_template = script.UrlTemplate,
                }),
            }),
            JsonOptions);
    }

    #endregion
}
