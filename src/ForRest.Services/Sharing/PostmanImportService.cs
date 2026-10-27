using System.Text.Json;

namespace ForRest.Services.Sharing;

public interface IPostmanImportService
{
    /// <summary>Returns true when the JSON looks like a Postman collection (v2.0/v2.1).</summary>
    bool LooksLikePostmanCollection(string? json);

    /// <summary>
    /// Converts a Postman collection into portable .frs documents. Folders are flattened into the
    /// document name ("folder / request"). Collection-level variables become one synthetic document
    /// named "Collection variables" and are copied into each imported request, because
    /// <c>{{var}}</c> syntax is shared between Postman and .frs so the values carry over directly.
    /// </summary>
    IReadOnlyList<PortableWorkspaceDocument> Convert(string json, out string collectionName);
}

public sealed class PostmanImportService : IPostmanImportService
{
    #region Public Methods

    public bool LooksLikePostmanCollection(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return IsPostmanCollection(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public IReadOnlyList<PortableWorkspaceDocument> Convert(string json, out string collectionName)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The content is not valid JSON, so it cannot be imported as a Postman collection.", exception);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (!IsPostmanCollection(root))
            {
                throw new FormatException("The JSON does not look like a Postman collection (missing info.schema or info._postman_id).");
            }

            collectionName = ReadString(root, "info", "name") ?? "Imported collection";

            List<PortableWorkspaceDocument> documents = [];
            HashSet<string> usedLocations = new(StringComparer.OrdinalIgnoreCase);
            var variableLines = BuildCollectionVariableLines(root);
            PortableWorkspaceDocument? variablesDocument = BuildCollectionVariablesDocument(variableLines, usedLocations);
            if (variablesDocument is not null)
            {
                documents.Add(variablesDocument);
            }

            if (root.TryGetProperty("item", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
            {
                ConvertItems(items, [], documents, usedLocations, variableLines);
            }

            return documents;
        }
    }

    #endregion

    #region Private Methods

    private static bool IsPostmanCollection(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("info", out JsonElement info) || info.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string schema = ReadString(info, "schema") ?? string.Empty;
        return schema.Contains("schema.getpostman.com", StringComparison.OrdinalIgnoreCase)
            || info.TryGetProperty("_postman_id", out _);
    }

    private static void ConvertItems(
        JsonElement items,
        List<string> folderPath,
        List<PortableWorkspaceDocument> documents,
        HashSet<string> usedLocations,
        IReadOnlyList<string> variableLines)
    {
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string itemName = ReadString(item, "name") ?? "Unnamed";
            if (item.TryGetProperty("item", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
            {
                folderPath.Add(itemName);
                ConvertItems(children, folderPath, documents, usedLocations, variableLines);
                folderPath.RemoveAt(folderPath.Count - 1);
                continue;
            }

            if (!item.TryGetProperty("request", out JsonElement request))
            {
                continue;
            }

            string documentName = folderPath.Count == 0
                ? itemName
                : $"{string.Join(" / ", folderPath)} / {itemName}";
            string source = ConvertRequest(request, documentName, variableLines);
            documents.Add(new(documentName, BuildUniqueLocation(documentName, usedLocations), source));
        }
    }

    private static string ConvertRequest(JsonElement request, string documentName, IReadOnlyList<string> variableLines)
    {
        string method = (ReadString(request, "method") ?? "GET").ToUpperInvariant();
        string url = ResolveUrl(request);

        List<(string Name, string Value)> headers = [];
        string? contentType = null;
        if (request.TryGetProperty("header", out JsonElement headerArray) && headerArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement header in headerArray.EnumerateArray())
            {
                if (header.ValueKind != JsonValueKind.Object || IsDisabled(header))
                {
                    continue;
                }

                string key = ReadString(header, "key") ?? string.Empty;
                string value = ReadString(header, "value") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = value;
                    continue;
                }

                headers.Add((key, value));
            }
        }

        (List<string> BodyLines, bool JsonBody, string? BodyContentType, List<string> Notes) body = BuildBody(request, contentType);

        List<string> lines =
        [
            $"name \"{FrsScriptWriter.Escape(documentName)}\"",
            $"method {method}",
            $"url \"{FrsScriptWriter.Escape(url)}\"",
        ];

        if (variableLines.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(variableLines);
        }

        string? effectiveContentType = body.BodyContentType ?? contentType;
        if (effectiveContentType is not null && !body.JsonBody)
        {
            lines.Add($"content_type \"{FrsScriptWriter.Escape(effectiveContentType)}\"");
        }

        if (headers.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(headers.Select(static header => FrsScriptWriter.HeaderLine(header.Name, header.Value)));
        }

        List<string> authLines = BuildAuth(request, variableLines);
        if (authLines.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(authLines);
        }

        if (body.BodyLines.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(body.BodyLines);
        }

        if (body.Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(body.Notes);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ResolveUrl(JsonElement request)
    {
        if (!request.TryGetProperty("url", out JsonElement url))
        {
            return string.Empty;
        }

        if (url.ValueKind == JsonValueKind.String)
        {
            return url.GetString() ?? string.Empty;
        }

        if (url.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        string? raw = ReadString(url, "raw");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        string protocol = ReadString(url, "protocol") ?? "https";
        string host = url.TryGetProperty("host", out JsonElement hostElement)
            ? JoinParts(hostElement, ".")
            : string.Empty;
        string path = url.TryGetProperty("path", out JsonElement pathElement)
            ? JoinParts(pathElement, "/")
            : string.Empty;

        string composed = string.IsNullOrEmpty(path) ? $"{protocol}://{host}" : $"{protocol}://{host}/{path}";
        List<string> queryPairs = [];
        if (url.TryGetProperty("query", out JsonElement query) && query.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement parameter in query.EnumerateArray())
            {
                if (parameter.ValueKind != JsonValueKind.Object || IsDisabled(parameter))
                {
                    continue;
                }

                string key = ReadString(parameter, "key") ?? string.Empty;
                string value = ReadString(parameter, "value") ?? string.Empty;
                if (!string.IsNullOrEmpty(key))
                {
                    queryPairs.Add($"{key}={value}");
                }
            }
        }

        return queryPairs.Count == 0 ? composed : $"{composed}?{string.Join("&", queryPairs)}";
    }

    private static string JoinParts(JsonElement element, string separator)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(
            separator,
            element.EnumerateArray()
                .Where(static part => part.ValueKind == JsonValueKind.String)
                .Select(static part => part.GetString())
                .Where(static part => !string.IsNullOrEmpty(part)));
    }

    private static (List<string> BodyLines, bool JsonBody, string? BodyContentType, List<string> Notes) BuildBody(
        JsonElement request,
        string? contentType)
    {
        List<string> notes = [];
        if (!request.TryGetProperty("body", out JsonElement body) || body.ValueKind != JsonValueKind.Object)
        {
            return ([], false, null, notes);
        }

        string mode = ReadString(body, "mode") ?? string.Empty;
        switch (mode.ToLowerInvariant())
        {
            case "raw":
            {
                string raw = ReadString(body, "raw") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return ([], false, null, notes);
                }

                string? language = ReadString(body, "options", "raw", "language");
                bool jsonBody = string.Equals(language, "json", StringComparison.OrdinalIgnoreCase)
                    || (language is null
                        && ((contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false)
                            || (contentType is null && FrsScriptWriter.LooksLikeJson(raw))));

                return ([.. FrsScriptWriter.BodyBlock(raw, jsonBody)], jsonBody, null, notes);
            }
            case "urlencoded":
            {
                List<string> pairs = [];
                if (body.TryGetProperty("urlencoded", out JsonElement fields) && fields.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement field in fields.EnumerateArray())
                    {
                        if (field.ValueKind != JsonValueKind.Object || IsDisabled(field))
                        {
                            continue;
                        }

                        string key = ReadString(field, "key") ?? string.Empty;
                        string value = ReadString(field, "value") ?? string.Empty;
                        if (!string.IsNullOrEmpty(key))
                        {
                            pairs.Add($"{key}={value}");
                        }
                    }
                }

                if (pairs.Count == 0)
                {
                    return ([], false, null, notes);
                }

                return (
                    [.. FrsScriptWriter.BodyBlock(string.Join("&", pairs), asJson: false)],
                    false,
                    "application/x-www-form-urlencoded",
                    notes);
            }
            case "formdata":
            {
                List<string> keys = [];
                if (body.TryGetProperty("formdata", out JsonElement fields) && fields.ValueKind == JsonValueKind.Array)
                {
                    keys.AddRange(fields.EnumerateArray()
                        .Where(static field => field.ValueKind == JsonValueKind.Object)
                        .Select(static field => ReadString(field, "key") ?? string.Empty)
                        .Where(static key => !string.IsNullOrEmpty(key)));
                }

                notes.Add(keys.Count == 0
                    ? "# note: multipart form-data body was not imported (unsupported)"
                    : $"# note: multipart form-data body was not imported (unsupported): {string.Join(", ", keys)}");
                return ([], false, null, notes);
            }
            case "file":
                notes.Add("# note: file body was not imported (unsupported)");
                return ([], false, null, notes);
            default:
                return ([], false, null, notes);
        }
    }

    private static List<string> BuildAuth(JsonElement request, IReadOnlyList<string> variableLines)
    {
        if (!request.TryGetProperty("auth", out JsonElement auth) || auth.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var secrets = new AuthImportSecrets(variableLines, request.GetRawText());
        var lines = BuildAuthLines(auth, secrets);
        return [.. secrets.Lines, .. lines];
    }

    private static List<string> BuildAuthLines(JsonElement auth, AuthImportSecrets secrets)
    {
        string type = ReadString(auth, "type") ?? string.Empty;
        switch (type.ToLowerInvariant())
        {
            case "bearer":
                return [.. FrsScriptWriter.BearerAuthBlock(secrets.Protect("postman_bearer_token", ReadAuthParameter(auth, "bearer", "token")))];
            case "basic":
                return
                [
                    .. FrsScriptWriter.BasicAuthBlock(
                        secrets.Protect("postman_basic_username", ReadAuthParameter(auth, "basic", "username")),
                        secrets.Protect("postman_basic_password", ReadAuthParameter(auth, "basic", "password"))),
                ];
            case "apikey":
            {
                string name = ReadAuthParameter(auth, "apikey", "key") ?? string.Empty;
                var value = secrets.Protect("postman_apikey_value", ReadAuthParameter(auth, "apikey", "value"));
                bool inQuery = string.Equals(ReadAuthParameter(auth, "apikey", "in"), "query", StringComparison.OrdinalIgnoreCase);
                return [.. FrsScriptWriter.ApiKeyAuthBlock(name, value, inQuery)];
            }
            default:
                return [];
        }
    }

    private sealed class AuthImportSecrets(IReadOnlyList<string> variableLines, string requestText)
    {
        #region Private Fields

        private readonly HashSet<string> usedNames = new(
            variableLines.Select(static line => line.Split(' ', 3)[1]), StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Properties

        public List<string> Lines { get; } = [];

        #endregion

        #region Public Methods

        public string Protect(string baseName, string? value)
        {
            value ??= string.Empty;
            if (value.Contains("{{", StringComparison.Ordinal))
            {
                return value;
            }

            var name = baseName;
            var suffix = 2;
            while (!usedNames.Add(name)
                || requestText.Contains("{{" + name + "}}", StringComparison.OrdinalIgnoreCase))
            {
                name = $"{baseName}_{suffix++}";
            }

            Lines.Add($"secret {name} = {JsonSerializer.Serialize(value)}");
            return "{{" + name + "}}";
        }

        #endregion
    }

    /// <summary>Reads an auth parameter from either the v2.1 array-of-{key,value} shape or the v2.0 object shape.</summary>
    private static string? ReadAuthParameter(JsonElement auth, string section, string key)
    {
        if (!auth.TryGetProperty(section, out JsonElement container))
        {
            return null;
        }

        if (container.ValueKind == JsonValueKind.Object)
        {
            return ReadString(container, key);
        }

        if (container.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement parameter in container.EnumerateArray())
        {
            if (parameter.ValueKind == JsonValueKind.Object
                && string.Equals(ReadString(parameter, "key"), key, StringComparison.OrdinalIgnoreCase))
            {
                return ReadString(parameter, "value");
            }
        }

        return null;
    }

    private static List<string> BuildCollectionVariableLines(JsonElement root)
    {
        if (!root.TryGetProperty("variable", out JsonElement variables) || variables.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> lines = [];
        foreach (var variable in variables.EnumerateArray())
        {
            if (variable.ValueKind != JsonValueKind.Object || IsDisabled(variable))
            {
                continue;
            }

            var key = ReadString(variable, "key") ?? string.Empty;
            if (!IsValidVariableName(key))
            {
                continue;
            }

            var value = variable.TryGetProperty("value", out var element)
                ? element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString() ?? string.Empty,
                    JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                    _ => element.GetRawText(),
                }
                : string.Empty;
            var isSecret = string.Equals(ReadString(variable, "type"), "secret", StringComparison.OrdinalIgnoreCase)
                || HttpCredentialHeaders.IsSensitive(key);
            var scope = isSecret ? "secret" : "request";
            lines.Add($"{scope} {key} = {JsonSerializer.Serialize(value)}");
        }

        return lines;
    }

    private static PortableWorkspaceDocument? BuildCollectionVariablesDocument(
        IReadOnlyList<string> variableLines, HashSet<string> usedLocations)
    {
        if (variableLines.Count == 0)
        {
            return null;
        }

        const string documentName = "Collection variables";
        List<string> lines =
        [
            "name \"Collection variables\"",
            "# Reference copy. Edit variable declarations in each imported request.",
            string.Empty,
            .. variableLines,
        ];
        return new(documentName, BuildUniqueLocation(documentName, usedLocations), string.Join(Environment.NewLine, lines));
    }

    private static bool IsValidVariableName(string name)
    {
        if (string.IsNullOrEmpty(name) || (!char.IsAsciiLetter(name[0]) && name[0] != '_'))
        {
            return false;
        }

        return name.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    private static string BuildUniqueLocation(string documentName, HashSet<string> usedLocations)
    {
        string slug = BuildSlug(documentName);
        string baseLocation = $"/requests/imported/{slug}";
        string candidate = baseLocation;
        int suffix = 2;
        while (!usedLocations.Add(candidate))
        {
            candidate = $"{baseLocation}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string BuildSlug(string value)
    {
        char[] slugCharacters = [.. value.ToLowerInvariant().Select(static character => char.IsLetterOrDigit(character) ? character : '-')];
        string slug = string.Join("-", new string(slugCharacters).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? "request" : slug;
    }

    private static bool IsDisabled(JsonElement element)
    {
        return element.TryGetProperty("disabled", out JsonElement disabled)
            && disabled.ValueKind == JsonValueKind.True;
    }

    private static string? ReadString(JsonElement element, params string[] path)
    {
        JsonElement current = element;
        foreach (string segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    #endregion
}
