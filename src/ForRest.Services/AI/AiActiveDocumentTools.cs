using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ForRest.Services.AI;

public sealed record AiActiveDocumentDiagnostic(
    string Severity,
    string Message,
    int Line,
    int Column);

public sealed record AiActiveDocumentRuntimeContext(
    string Status,
    string ErrorMessage,
    string DebugText,
    string ResponseBodyPreview);

public sealed record AiActiveDocumentSnapshot(
    string DocumentId,
    string Title,
    string Language,
    string SourceText,
    IReadOnlyList<AiActiveDocumentDiagnostic> Diagnostics,
    AiActiveDocumentRuntimeContext? RuntimeContext = null,
    AiWorkspaceContext? WorkspaceContext = null);

public sealed record AiWorkspaceContext(
    string WorkspaceId,
    string WorkspaceName,
    IReadOnlyList<AiWorkspaceScriptSummary> Scripts);

public sealed record AiWorkspaceScriptSummary(
    string ScriptId,
    string Name,
    string Method,
    string UrlTemplate);

public sealed record AiActiveDocumentUpdateResult(
    bool Succeeded,
    string Message,
    string UpdatedText,
    IReadOnlyList<AiActiveDocumentDiagnostic> Diagnostics,
    bool RetryWithReplace)
{
    public static AiActiveDocumentUpdateResult Success(string updatedText = "")
    {
        return new(true, string.Empty, updatedText ?? string.Empty, [], false);
    }

    public static AiActiveDocumentUpdateResult Failure(
        string message,
        string updatedText = "",
        IReadOnlyList<AiActiveDocumentDiagnostic>? diagnostics = null,
        bool retryWithReplace = false)
    {
        return new(false, message, updatedText ?? string.Empty, diagnostics ?? [], retryWithReplace);
    }
}

public interface IAiActiveDocumentHost
{
    AiActiveDocumentSnapshot? GetActiveDocument();

    AiActiveDocumentUpdateResult UpdateActiveDocument(AiActiveDocumentSnapshot document, string updatedText);

    AiWorkspaceContext? GetWorkspaceContext();

    AiActiveDocumentUpdateResult CreateScript(string name, string sourceText);

    /// <summary>
    /// Number of successful workspace-level mutations (e.g. scripts created) this host has
    /// performed during the current turn. A host is created per turn, so a non-zero value means
    /// the turn already accomplished real work even when the active document itself was not
    /// touched — letting the executor skip needless autonomous-edit recovery.
    /// </summary>
    int GetWorkspaceMutationCount() => 0;
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
                "Read the active document from the host canvas, including current source text, compiler diagnostics, and latest runtime failure context when available.",
                "Call this before patching so the agent can inspect the current document state, syntax errors, and recent runtime failures before deciding whether a targeted edit or a full rewrite is safer. The returned source text excludes inline chat scaffolding like ## prompts and #> replies.",
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
            new(
                "create_workspace_script",
                "Create a new request script in the current workspace.",
                "Use when the user asks to create a new request or add a script to the workspace. Provide the script name and full ForRest source text. The script is added to the workspace tree as a new request node.",
                MutatesDocument: true),
        ];
    }
}

public interface IAiActiveDocumentToolService
{
    string ReadActiveDocument(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost);

    string PatchActiveDocument(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost, string editsJson);

    string ReplaceActiveDocument(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost, string updatedSourceText);

    string CreateWorkspaceScript(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost, string name, string sourceText);
}

public sealed class AiActiveDocumentToolService(IAiDocumentPatchService documentPatchService) : IAiActiveDocumentToolService
{
    #region Private Fields

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // The default JavaScript encoder escapes single and double quotes
        // as \u0027/\u0022. Tool results round-trip through a second JSON
        // envelope before they reach the model, and reasoning models
        // routinely fail to decode the resulting double-escaped tokens —
        // a parser diagnostic that should look like
        //   expect header "Content-Type" contains "json"
        // arrives as
        //   expect header \\u0022Content-Type\\u0022 contains \\u0022json\\u0022
        // and the model "fixes" the assertion by guessing nonsense forms.
        // The relaxed encoder keeps quotes as plain `"` so the diagnostic
        // stays readable after both encoding hops.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Match the entire `secret <name> = <value>` declaration as a line, so
    // we can redact non-string secret values (identifiers, function calls,
    // future list literals) and round-trip the original right-hand side
    // back into the document after the AI replaces it. Captures:
    //   1: leading whitespace
    //   2: secret name
    //   3: raw value expression text (everything after `=` to end of line)
    private static readonly Regex SecretDeclarationPattern = new(
        @"^(?<indent>[ \t]*)secret[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*=[ \t]*(?<value>.+?)[ \t]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private const string RedactedSecretMarker = "\"***\"";

    #endregion

    #region Public Methods

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

        var redacted = document with { SourceText = RedactSecrets(document.SourceText) };

        return Serialize(new
        {
            succeeded = true,
            document = redacted,
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

        // The AI only ever sees the *redacted* document via
        // ReadActiveDocument, so the start indices it produces are valid
        // against the redacted text — not the live source. Apply the
        // edits to the redacted text first, then splice the original
        // secret declarations back in so credentials survive even when
        // the patch incidentally touches lines around them.
        string redactedSource = RedactSecrets(document.SourceText);
        AiDocumentPatchResult patchResult = documentPatchService.Apply(
            new(document.DocumentId, redactedSource, edits));

        if (!patchResult.Succeeded)
        {
            return Serialize(new
            {
                succeeded = false,
                documentId = document.DocumentId,
                patchedText = redactedSource,
                retryWithRead = true,
                retryWithReplace = true,
                docHints = BuildDocHints(string.Join(Environment.NewLine, patchResult.Errors), document.Diagnostics, redactedSource),
                errors = patchResult.Errors,
            });
        }

        string restoredPatchedText = RestoreOriginalSecrets(document.SourceText, patchResult.PatchedText);
        AiActiveDocumentUpdateResult updateResult = activeDocumentHost.UpdateActiveDocument(document, restoredPatchedText);
        if (!updateResult.Succeeded)
        {
            return Serialize(new
            {
                succeeded = false,
                documentId = document.DocumentId,
                patchedText = string.IsNullOrWhiteSpace(updateResult.UpdatedText) ? patchResult.PatchedText : RedactSecrets(updateResult.UpdatedText),
                retryWithRead = true,
                retryWithReplace = updateResult.RetryWithReplace,
                docHints = BuildDocHints(updateResult.Message, updateResult.Diagnostics, patchResult.PatchedText),
                errors = new[] { updateResult.Message },
                diagnostics = updateResult.Diagnostics,
            });
        }

        return Serialize(new
        {
            succeeded = true,
            documentId = document.DocumentId,
            // patchResult.PatchedText is already operating against the
            // redacted source, so it inherits the `"***"` placeholders
            // for any secret declarations the patch left intact. Echoing
            // it directly keeps the AI from ever seeing the real values.
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

        // The AI only ever sees redacted secret values, so any secret it
        // copies into the rewritten document still has the `"***"` marker
        // (or it deleted the secret line entirely). Splice the original
        // declarations back in before the host commits the new text so
        // user credentials survive AI rewrites.
        string restoredSourceText = RestoreOriginalSecrets(document.SourceText, updatedSourceText ?? string.Empty);

        AiActiveDocumentUpdateResult updateResult = activeDocumentHost.UpdateActiveDocument(document, restoredSourceText);
        if (!updateResult.Succeeded)
        {
            return Serialize(new
            {
                succeeded = false,
                documentId = document.DocumentId,
                patchedText = string.IsNullOrWhiteSpace(updateResult.UpdatedText) ? updatedSourceText ?? string.Empty : updateResult.UpdatedText,
                retryWithRead = true,
                retryWithReplace = updateResult.RetryWithReplace,
                docHints = BuildDocHints(updateResult.Message, updateResult.Diagnostics, updatedSourceText),
                errors = new[] { updateResult.Message },
                diagnostics = updateResult.Diagnostics,
            });
        }

        return Serialize(new
        {
            succeeded = true,
            documentId = document.DocumentId,
            // Echo the redacted form back to the AI so it never sees the
            // original secret values, even though the host now has the
            // restored declarations.
            patchedText = RedactSecrets(restoredSourceText),
            errors = Array.Empty<string>(),
        });
    }

    public string CreateWorkspaceScript(AiSettings settings, IAiActiveDocumentHost? activeDocumentHost, string name, string sourceText)
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

        if (string.IsNullOrWhiteSpace(name))
        {
            return SerializeFailure("Script name is required.");
        }

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return SerializeFailure("Script source text is required.");
        }

        AiActiveDocumentUpdateResult result = activeDocumentHost.CreateScript(name, sourceText);
        if (!result.Succeeded)
        {
            return Serialize(new
            {
                succeeded = false,
                errors = new[] { result.Message },
            });
        }

        return Serialize(new
        {
            succeeded = true,
            scriptName = name,
            errors = Array.Empty<string>(),
        });
    }

    #endregion

    #region Private Methods

    internal static string RedactSecrets(string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
        {
            return sourceText;
        }

        return SecretDeclarationPattern.Replace(sourceText, match =>
        {
            string indent = match.Groups["indent"].Value;
            string name = match.Groups["name"].Value;
            return $"{indent}secret {name} = {RedactedSecretMarker}";
        });
    }

    /// <summary>
    /// Returns a name-keyed map of every <c>secret</c> declaration in
    /// <paramref name="sourceText"/>, capturing the full original
    /// declaration line (including indentation and the unredacted
    /// right-hand side). Used to restore secrets after the AI replaces
    /// the document with text it built from the redacted view.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ExtractSecretDeclarations(string? sourceText)
    {
        Dictionary<string, string> declarations = new(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(sourceText))
        {
            return declarations;
        }

        foreach (Match match in SecretDeclarationPattern.Matches(sourceText))
        {
            string name = match.Groups["name"].Value;
            if (string.IsNullOrEmpty(name) || declarations.ContainsKey(name))
            {
                continue;
            }

            // Store the canonical line text (no trailing whitespace) so it
            // can be substituted back verbatim — preserving indentation,
            // quoting, and any comment-free trailing characters the user
            // wrote.
            declarations[name] = match.Value.TrimEnd();
        }

        return declarations;
    }

    /// <summary>
    /// Re-applies the original secret declarations after the AI returns
    /// a replacement document. The AI only sees redacted secrets, so any
    /// secret it leaves intact still has the <c>"***"</c> marker as its
    /// value, and any secret it deletes outright must be re-inserted so
    /// the user does not silently lose credentials.
    /// </summary>
    internal static string RestoreOriginalSecrets(string? originalSourceText, string? newSourceText)
    {
        IReadOnlyDictionary<string, string> originals = ExtractSecretDeclarations(originalSourceText);
        if (originals.Count == 0)
        {
            return newSourceText ?? string.Empty;
        }

        string updated = newSourceText ?? string.Empty;
        HashSet<string> seen = new(StringComparer.Ordinal);

        // First pass: replace each surviving secret declaration with its
        // original line so the AI cannot mutate the value, even if it
        // tried to write a different placeholder than `"***"`.
        updated = SecretDeclarationPattern.Replace(updated, match =>
        {
            string name = match.Groups["name"].Value;
            if (originals.TryGetValue(name, out string? originalLine))
            {
                seen.Add(name);
                string indent = match.Groups["indent"].Value;
                // Keep whatever indentation the AI placed the line at so
                // surrounding formatting is preserved.
                int firstNonWhitespace = 0;
                while (firstNonWhitespace < originalLine.Length && (originalLine[firstNonWhitespace] == ' ' || originalLine[firstNonWhitespace] == '\t'))
                {
                    firstNonWhitespace++;
                }

                return indent + originalLine[firstNonWhitespace..];
            }

            return match.Value;
        });

        // Second pass: re-insert any secret the AI deleted entirely.
        // Stick the missing declarations at the very top of the document
        // so they take effect for the rest of the script and so the user
        // can immediately see them after a rewrite.
        List<string> missing = [];
        foreach (KeyValuePair<string, string> entry in originals)
        {
            if (!seen.Contains(entry.Key))
            {
                missing.Add(entry.Value);
            }
        }

        if (missing.Count > 0)
        {
            string newline = updated.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string prefix = string.Join(newline, missing) + newline;
            updated = prefix + updated;
        }

        return updated;
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

    private static string[] BuildDocHints(
        string? message,
        IReadOnlyList<AiActiveDocumentDiagnostic>? diagnostics,
        string? candidateText)
    {
        string normalized = NormalizeSearchText(
            string.Join(
                Environment.NewLine,
                [
                    message ?? string.Empty,
                    candidateText ?? string.Empty,
                    .. (diagnostics ?? []).Select(static diagnostic => diagnostic.Message)
                ]));
        HashSet<string> hints = new(StringComparer.OrdinalIgnoreCase);

        if (normalized.Contains("expect", StringComparison.Ordinal) ||
            normalized.Contains("assert", StringComparison.Ordinal) ||
            normalized.Contains("parse the expectation", StringComparison.Ordinal) ||
            normalized.Contains("top-level", StringComparison.Ordinal) ||
            normalized.Contains("request.send", StringComparison.Ordinal) ||
            normalized.Contains("send()", StringComparison.Ordinal) ||
            normalized.Contains("foreach", StringComparison.Ordinal) ||
            normalized.Contains("while", StringComparison.Ordinal))
        {
            hints.Add("batch-stash-loop");
            hints.Add("request-url");
            hints.Add("max-send-iterations");
            hints.Add("request-send");
            hints.Add("stash");
        }

        if (normalized.Contains("expect", StringComparison.Ordinal) ||
            normalized.Contains("assert", StringComparison.Ordinal) ||
            normalized.Contains("parse the expectation", StringComparison.Ordinal))
        {
            hints.Add("expect");
        }

        if (normalized.Contains("url", StringComparison.Ordinal) ||
            normalized.Contains("request.url", StringComparison.Ordinal))
        {
            hints.Add("request-url");
        }

        if (normalized.Contains("method", StringComparison.Ordinal) ||
            normalized.Contains("request.method", StringComparison.Ordinal) ||
            normalized.Contains("post", StringComparison.Ordinal) ||
            normalized.Contains("put", StringComparison.Ordinal) ||
            normalized.Contains("patch", StringComparison.Ordinal) ||
            normalized.Contains("delete", StringComparison.Ordinal))
        {
            hints.Add("request-method");
        }

        if (normalized.Contains("header", StringComparison.Ordinal) ||
            normalized.Contains("request.headers", StringComparison.Ordinal) ||
            normalized.Contains("content-type", StringComparison.Ordinal))
        {
            hints.Add("request-headers");
        }

        if (normalized.Contains("body", StringComparison.Ordinal) ||
            normalized.Contains("payload", StringComparison.Ordinal) ||
            normalized.Contains("application/json", StringComparison.Ordinal) ||
            normalized.Contains("content_type", StringComparison.Ordinal) ||
            normalized.Contains("request.body", StringComparison.Ordinal))
        {
            hints.Add("body");
            hints.Add("request-body");
            hints.Add("content_type");
            hints.Add("request-content-type");
        }

        if (normalized.Contains("comment", StringComparison.Ordinal) ||
            normalized.Contains("//", StringComparison.Ordinal) ||
            normalized.Contains("##", StringComparison.Ordinal))
        {
            hints.Add("comments");
        }

        if (normalized.Contains("stash", StringComparison.Ordinal))
        {
            hints.Add("stash");
        }

        if ((normalized.Contains("request.send", StringComparison.Ordinal) || normalized.Contains("send()", StringComparison.Ordinal)) &&
            (normalized.Contains("request.method", StringComparison.Ordinal) ||
             normalized.Contains("post", StringComparison.Ordinal) ||
             normalized.Contains("put", StringComparison.Ordinal) ||
             normalized.Contains("patch", StringComparison.Ordinal) ||
             normalized.Contains("delete", StringComparison.Ordinal)))
        {
            hints.Add("api-surface-crud");
        }

        if (normalized.Contains("jsonobject", StringComparison.Ordinal) ||
            normalized.Contains("jsonarray", StringComparison.Ordinal) ||
            normalized.Contains("response.json", StringComparison.Ordinal) ||
            normalized.Contains("asarray", StringComparison.Ordinal) ||
            normalized.Contains("does not contain a definition for", StringComparison.Ordinal) ||
            normalized.Contains("json shape", StringComparison.Ordinal))
        {
            hints.Add("response");
            hints.Add("response-json");
            hints.Add("response-array");
        }

        return [.. hints];
    }

    private static string NormalizeSearchText(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace('\u2264', '<')
            .Replace('\u2265', '>')
            .Trim()
            .ToLowerInvariant();
    }

    #endregion
}
