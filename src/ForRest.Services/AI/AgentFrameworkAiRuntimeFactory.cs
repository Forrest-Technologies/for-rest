#pragma warning disable OPENAI001
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
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
    AIAgent? Agent);

public interface IAiRuntimeFactory
{
    AiPreparedRuntime Prepare(AiSettings settings, string objective, IAiActiveDocumentHost? activeDocumentHost = null);
}

public sealed class AgentFrameworkAiRuntimeFactory : IAiRuntimeFactory
{
    private const string AgentName = "ForRestAssistant";
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

    public AiPreparedRuntime Prepare(AiSettings settings, string objective, IAiActiveDocumentHost? activeDocumentHost = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        List<AiSettingsIssue> issues = [.. _settingsValidator.Validate(settings)];
        IReadOnlyList<AiToolDescriptor> tools =
        [
            .. _toolCatalog.GetTools(settings, activeDocumentHost),
            .. _activeDocumentToolCatalog.GetTools(settings, activeDocumentHost)
        ];
        IReadOnlyList<AiPromptTopic> topics = BuildPromptTopics(activeDocumentHost);
        AiPromptManifest manifest = _promptManifestBuilder.Build(settings, objective, tools, topics);
        if (issues.Any(static issue => issue.Severity == AiSettingsIssueSeverity.Error) ||
            !settings.ApiKey.HasUsableValue)
        {
            return new(manifest, issues, Agent: null);
        }

        AITool[] runtimeTools = BuildRuntimeTools(settings, activeDocumentHost);
        AIAgent agent = settings.Provider.ProviderKind switch
        {
            AiProviderKind.AzureOpenAI => CreateAzureAgent(settings, manifest.SystemPrompt, runtimeTools),
            _ => CreateOpenAiAgent(settings, manifest.SystemPrompt, runtimeTools),
        };

        return new(manifest, issues, agent);
    }

    private IReadOnlyList<AiPromptTopic> BuildPromptTopics(IAiActiveDocumentHost? activeDocumentHost)
    {
        List<AiPromptTopic> topics = [.. _knowledgeCatalog.GetTopics()];
        AiActiveDocumentSnapshot? activeDocument = activeDocumentHost?.GetActiveDocument();
        if (activeDocument is null)
        {
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
                "Source:",
                activeDocument.SourceText,
            ]);

        topics.Insert(
            0,
            new(
                "Active document",
                "The current request document and its latest compiler diagnostics. Use this instead of asking the user to paste the script or error list again.",
                "active-document",
                content));

        return topics;
    }

    private AITool[] BuildRuntimeTools(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost)
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
        }

        [Description("Read the entire canonical local ForRest docs corpus in one pass.")]
        string ReadAllDocs()
        {
            return BuildFullDocsCorpus("Returning the full local docs corpus.");
        }

        [Description("Apply bounded non-overlapping text edits to a document source string.")]
        string PatchDocument(
            [Description("The document identifier the patch applies to.")] string documentId,
            [Description("The current document source text to patch.")] string sourceText,
            [Description("A JSON array of edits with startIndex, length, and replacement fields.")] string editsJson)
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

            AiDocumentPatchResult result = _documentPatchService.Apply(new(documentId, sourceText, edits));
            return JsonSerializer.Serialize(new
            {
                succeeded = result.Succeeded,
                patchedText = result.PatchedText,
                errors = result.Errors,
            });
        }

        [Description("Read the current active document and its compiler diagnostics from the host canvas.")] 
        string ReadActiveDocument()
        {
            return _activeDocumentToolService.ReadActiveDocument(settings, activeDocumentHost);
        }

        [Description("Apply bounded edits to the current active document without supplying raw source text.")]
        string PatchActiveDocument(
            [Description("A JSON array of edits with startIndex, length, and replacement fields.")] string editsJson)
        {
            return _activeDocumentToolService.PatchActiveDocument(settings, activeDocumentHost, editsJson);
        }

        [Description("Replace the entire active document with new source text when a full rewrite is safer than targeted edits.")]
        string ReplaceActiveDocument(
            [Description("The complete replacement source text for the active document.")] string updatedSourceText)
        {
            return _activeDocumentToolService.ReplaceActiveDocument(settings, activeDocumentHost, updatedSourceText);
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
