using System.Text.Json;

namespace ForRest.Services.AI;

public sealed record AiActiveDocumentDiagnostic(
    string Severity,
    string Message,
    int Line,
    int Column);

public sealed record AiActiveDocumentSnapshot(
    string DocumentId,
    string Title,
    string Language,
    string SourceText,
    IReadOnlyList<AiActiveDocumentDiagnostic> Diagnostics);

public sealed record AiActiveDocumentUpdateResult(bool Succeeded, string Message)
{
    public static AiActiveDocumentUpdateResult Success()
    {
        return new(true, string.Empty);
    }

    public static AiActiveDocumentUpdateResult Failure(string message)
    {
        return new(false, message);
    }
}

public interface IAiActiveDocumentHost
{
    AiActiveDocumentSnapshot? GetActiveDocument();

    AiActiveDocumentUpdateResult UpdateActiveDocument(AiActiveDocumentSnapshot document, string updatedText);
}

public interface IAiActiveDocumentToolCatalog
{
    IReadOnlyList<AiToolDescriptor> GetTools(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost);
}

public sealed class AiActiveDocumentToolCatalog : IAiActiveDocumentToolCatalog
{
    public IReadOnlyList<AiToolDescriptor> GetTools(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled ||
            activeDocumentHost is null ||
            !settings.Tools.EnableDocumentPatch)
        {
            return [];
        }

        return
        [
            new(
                "read_active_document",
                "Read the active document from the host canvas, including current source text and compiler diagnostics.",
                "Call this before patching so the agent can inspect the current document state, syntax errors, and whether a full rewrite is safer. The returned source text excludes inline chat scaffolding like ## prompts and #> replies.",
                MutatesDocument: false),
            new(
                "patch_active_document",
                "Apply bounded text edits to the active document currently open in the host canvas.",
                "Provide a JSON array of AiTextEdit objects for targeted edits after reading the active document and diagnostics. If patching fails or the source looks garbled, do not ask the user for confirmation; use replace_active_document with the full corrected request instead.",
                MutatesDocument: true),
            new(
                "replace_active_document",
                "Replace the entire active document with new source text.",
                "Use this when the user asked to rewrite the whole request or when the current structure is broken enough that targeted edits are more error-prone than a full replacement. This is the default fallback when patch_active_document fails.",
                MutatesDocument: true),
        ];
    }
}

public interface IAiActiveDocumentToolService
{
    string ReadActiveDocument(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost);

    string PatchActiveDocument(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost, string editsJson);

    string ReplaceActiveDocument(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost, string updatedSourceText);
}

public sealed class AiActiveDocumentToolService : IAiActiveDocumentToolService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IAiDocumentPatchService _documentPatchService;

    public AiActiveDocumentToolService(IAiDocumentPatchService documentPatchService)
    {
        _documentPatchService = documentPatchService;
    }

    public string ReadActiveDocument(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            return Serialize(new
            {
                succeeded = false,
                errors = new[] { "AI is disabled." },
            });
        }

        if (activeDocumentHost is null)
        {
            return Serialize(new
            {
                succeeded = false,
                errors = new[] { "No active document host is available." },
            });
        }

        AiActiveDocumentSnapshot? document = activeDocumentHost.GetActiveDocument();
        if (document is null)
        {
            return Serialize(new
            {
                succeeded = false,
                errors = new[] { "No active document is selected." },
            });
        }

        return Serialize(new
        {
            succeeded = true,
            document,
            errors = Array.Empty<string>(),
        });
    }

    public string PatchActiveDocument(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost, string editsJson)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            return SerializeFailure("AI is disabled.");
        }

        if (activeDocumentHost is null)
        {
            return SerializeFailure("No active document host is available.");
        }

        AiActiveDocumentSnapshot? document = activeDocumentHost.GetActiveDocument();
        if (document is null)
        {
            return SerializeFailure("No active document is selected.");
        }

        AiTextEdit[] edits;
        try
        {
            edits = JsonSerializer.Deserialize<AiTextEdit[]>(editsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return SerializeFailure("The editsJson payload must be a JSON array of AiTextEdit objects.");
        }

        if (edits.Length > settings.Tools.MaxPatchOperations)
        {
            return SerializeFailure($"The patch contains {edits.Length} edits, which exceeds the configured maximum of {settings.Tools.MaxPatchOperations}.");
        }

        if (edits.Sum(static edit => (edit.Replacement ?? string.Empty).Length) > settings.Tools.MaxPatchCharacters)
        {
            return SerializeFailure($"The patch exceeds the configured maximum of {settings.Tools.MaxPatchCharacters} replacement characters.");
        }

        AiDocumentPatchResult patchResult = _documentPatchService.Apply(
            new(document.DocumentId, document.SourceText, edits));

        if (!patchResult.Succeeded)
        {
            return Serialize(new
            {
                succeeded = false,
                documentId = document.DocumentId,
                patchedText = document.SourceText,
                retryWithReplace = true,
                errors = patchResult.Errors,
            });
        }

        AiActiveDocumentUpdateResult updateResult = activeDocumentHost.UpdateActiveDocument(document, patchResult.PatchedText);
        if (!updateResult.Succeeded)
        {
            return Serialize(new
            {
                succeeded = false,
                documentId = document.DocumentId,
                patchedText = patchResult.PatchedText,
                retryWithReplace = true,
                errors = new[] { updateResult.Message },
            });
        }

        return Serialize(new
        {
            succeeded = true,
            documentId = document.DocumentId,
            patchedText = patchResult.PatchedText,
            errors = Array.Empty<string>(),
        });
    }

    public string ReplaceActiveDocument(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost, string updatedSourceText)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            return SerializeFailure("AI is disabled.");
        }

        if (activeDocumentHost is null)
        {
            return SerializeFailure("No active document host is available.");
        }

        AiActiveDocumentSnapshot? document = activeDocumentHost.GetActiveDocument();
        if (document is null)
        {
            return SerializeFailure("No active document is selected.");
        }

        if ((updatedSourceText ?? string.Empty).Length > settings.Tools.MaxPatchCharacters)
        {
            return SerializeFailure($"The replacement exceeds the configured maximum of {settings.Tools.MaxPatchCharacters} characters.");
        }

        AiActiveDocumentUpdateResult updateResult = activeDocumentHost.UpdateActiveDocument(document, updatedSourceText ?? string.Empty);
        if (!updateResult.Succeeded)
        {
            return SerializeFailure(updateResult.Message);
        }

        return Serialize(new
        {
            succeeded = true,
            documentId = document.DocumentId,
            patchedText = updatedSourceText ?? string.Empty,
            errors = Array.Empty<string>(),
        });
    }

    private static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string SerializeFailure(params string[] errors)
    {
        return Serialize(new
        {
            succeeded = false,
            errors,
        });
    }
}
