using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.OpenAI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace ForRest.Services.AI;

public sealed record AiPreparedRuntime(
    AiPromptManifest PromptManifest,
    IReadOnlyList<AiSettingsIssue> Issues,
    AIAgent? Agent);

public interface IAiRuntimeFactory
{
    AiPreparedRuntime Prepare(AiSettings settings, string objective);
}

public sealed class AgentFrameworkAiRuntimeFactory : IAiRuntimeFactory
{
    private const string AgentName = "ForRestAssistant";
    private readonly IAiSettingsValidator _settingsValidator;
    private readonly IAiToolCatalog _toolCatalog;
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
        _promptManifestBuilder = promptManifestBuilder;
        _knowledgeCatalog = knowledgeCatalog;
        _documentationSearchService = documentationSearchService;
        _documentPatchService = documentPatchService;
    }

    public AiPreparedRuntime Prepare(AiSettings settings, string objective)
    {
        ArgumentNullException.ThrowIfNull(settings);

        List<AiSettingsIssue> issues = [.. _settingsValidator.Validate(settings)];
        IReadOnlyList<AiToolDescriptor> tools = _toolCatalog.GetTools(settings);
        IReadOnlyList<AiPromptTopic> topics = _knowledgeCatalog.GetTopics();
        AiPromptManifest manifest = _promptManifestBuilder.Build(settings, objective, tools, topics);
        if (issues.Any(static issue => issue.Severity == AiSettingsIssueSeverity.Error) ||
            !settings.ApiKey.HasUsableValue)
        {
            return new(manifest, issues, Agent: null);
        }

        if (settings.Provider.Transport == AiConversationTransport.Responses)
        {
            issues.Add(new(
                AiSettingsIssueSeverity.Warning,
                "ai.transport.responses.chat-fallback",
                "The current preview runtime prepares a chat-client agent even when Responses is selected. Keep the Responses setting for future enablement, but expect chat-based execution today."));
        }

        AITool[] runtimeTools = BuildRuntimeTools(settings);
        AIAgent agent = settings.Provider.ProviderKind switch
        {
            AiProviderKind.AzureOpenAI => CreateAzureAgent(settings, manifest.SystemPrompt, runtimeTools),
            _ => CreateOpenAiAgent(settings, manifest.SystemPrompt, runtimeTools),
        };

        return new(manifest, issues, agent);
    }

    private AITool[] BuildRuntimeTools(AiSettings settings)
    {
        List<AITool> tools = [];
        if (settings.Tools.EnableDocsSearch)
        {
            tools.Add(AIFunctionFactory.Create((Func<string, int, string>)SearchDocs));
        }

        if (settings.Tools.EnableDocumentPatch)
        {
            tools.Add(AIFunctionFactory.Create((Func<string, string, string, string>)PatchDocument));
        }

        return [.. tools];

        [Description("Search the canonical local ForRest docs and language reference.")]
        string SearchDocs(
            [Description("The docs query to search for.")] string query,
            [Description("Maximum number of search hits to return.")] int maxResults)
        {
            int boundedResults = Math.Clamp(maxResults, 1, settings.Tools.MaxSearchResults);
            IReadOnlyList<AiKnowledgeSearchHit> hits = _documentationSearchService.Search(query, boundedResults);
            if (hits.Count == 0)
            {
                return "No matching ForRest docs were found.";
            }

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
    }

    private static AIAgent CreateAzureAgent(AiSettings settings, string instructions, IReadOnlyList<AITool> runtimeTools)
    {
        AzureOpenAIClient client = new(new Uri(settings.Provider.Endpoint), new ApiKeyCredential(settings.ApiKey.Value));
        string deployment = string.IsNullOrWhiteSpace(settings.Provider.DeploymentName)
            ? settings.Provider.Model
            : settings.Provider.DeploymentName;
        List<AITool> tools = [.. runtimeTools];

        return settings.Provider.Transport switch
        {
            AiConversationTransport.ChatCompletions => client
                .GetChatClient(deployment)
                .AsAIAgent(instructions: instructions, name: AgentName, tools: tools),
            _ => client
                .GetChatClient(deployment)
                .AsAIAgent(instructions: instructions, name: AgentName, tools: tools),
        };
    }

    private static AIAgent CreateOpenAiAgent(AiSettings settings, string instructions, IReadOnlyList<AITool> runtimeTools)
    {
        OpenAIClient client = CreateOpenAiClient(settings);
        List<AITool> tools = [.. runtimeTools];

        return settings.Provider.Transport switch
            {
                AiConversationTransport.ChatCompletions => client
                    .GetChatClient(settings.Provider.Model)
                    .AsAIAgent(instructions: instructions, name: AgentName, tools: tools),
            _ => client
                .GetChatClient(settings.Provider.Model)
                .AsAIAgent(instructions: instructions, name: AgentName, tools: tools),
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
