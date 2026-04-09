using System.Text;
using System.Text.Json;

namespace ForRest.Services;

public interface IOpenApiScaffoldGenerator
{
    string Generate(string openApiJson);
}

public sealed class OpenApiScaffoldGenerator : IOpenApiScaffoldGenerator
{
    #region Private Fields

    private static readonly HashSet<string> methodsWithBody = ["post", "put", "patch"];

    private static readonly Dictionary<string, string> defaultExpectStatus = new(StringComparer.OrdinalIgnoreCase)
    {
        ["get"] = "200",
        ["post"] = "201",
        ["put"] = "200",
        ["patch"] = "200",
        ["delete"] = "204",
    };

    #endregion

    #region Public Methods

    public string Generate(string openApiJson)
    {
        using var doc = JsonDocument.Parse(openApiJson);
        var root = doc.RootElement;

        var baseUrl = ResolveBaseUrl(root);
        var sb = new StringBuilder();
        var first = true;

        if (!root.TryGetProperty("paths", out var paths))
        {
            return string.Empty;
        }

        foreach (var pathEntry in paths.EnumerateObject())
        {
            var pathTemplate = pathEntry.Name;

            foreach (var operationEntry in pathEntry.Value.EnumerateObject())
            {
                var httpMethod = operationEntry.Name;

                if (!IsHttpMethod(httpMethod))
                {
                    continue;
                }

                if (!first)
                {
                    sb.AppendLine();
                }

                first = false;

                var operation = operationEntry.Value;
                AppendOperation(sb, root, baseUrl, pathTemplate, httpMethod, operation);
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Private Methods

    private static void AppendOperation(
        StringBuilder sb,
        JsonElement root,
        string baseUrl,
        string pathTemplate,
        string httpMethod,
        JsonElement operation)
    {
        var operationName = ResolveOperationName(httpMethod, pathTemplate, operation);
        var url = BuildUrl(baseUrl, pathTemplate);
        var method = httpMethod.ToUpperInvariant();

        sb.AppendLine($"name \"{operationName}\"");
        sb.AppendLine($"method {method}");
        sb.AppendLine($"url \"{url}\"");

        if (methodsWithBody.Contains(httpMethod))
        {
            sb.AppendLine("content_type \"application/json\"");
        }

        sb.AppendLine("header \"Accept\" = \"application/json\"");

        AppendQueryParameters(sb, operation);

        if (methodsWithBody.Contains(httpMethod))
        {
            AppendRequestBody(sb, root, operation);
        }

        var expectedStatus = ResolveExpectedStatus(httpMethod, operation);
        var statusLabel = ResolveStatusLabel(method, expectedStatus);
        sb.AppendLine($"expect status == {expectedStatus} \"{statusLabel}\"");
    }

    private static string ResolveBaseUrl(JsonElement root)
    {
        // OpenAPI 3.x: servers[0].url
        if (root.TryGetProperty("servers", out var servers) &&
            servers.GetArrayLength() > 0 &&
            servers[0].TryGetProperty("url", out var serverUrl))
        {
            return serverUrl.GetString()?.TrimEnd('/') ?? "{{baseUrl}}";
        }

        // Swagger 2.x: host + basePath
        var host = root.TryGetProperty("host", out var h) ? h.GetString() : null;
        var basePath = root.TryGetProperty("basePath", out var bp) ? bp.GetString()?.TrimEnd('/') : null;
        var scheme = "https";

        if (root.TryGetProperty("schemes", out var schemes) && schemes.GetArrayLength() > 0)
        {
            scheme = schemes[0].GetString() ?? "https";
        }

        if (host is not null)
        {
            return $"{scheme}://{host}{basePath ?? string.Empty}";
        }

        return "{{baseUrl}}";
    }

    private static string ResolveOperationName(string httpMethod, string pathTemplate, JsonElement operation)
    {
        if (operation.TryGetProperty("summary", out var summary))
        {
            var text = summary.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        if (operation.TryGetProperty("operationId", out var opId))
        {
            var text = opId.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return $"{httpMethod.ToUpperInvariant()} {pathTemplate}";
    }

    private static string BuildUrl(string baseUrl, string pathTemplate)
    {
        // Convert OpenAPI path params {userId} to ForRest variable placeholders {{userId}}
        var url = baseUrl + pathTemplate;
        var result = new StringBuilder(url.Length);

        for (var i = 0; i < url.Length; i++)
        {
            if (url[i] == '{' && (i + 1 >= url.Length || url[i + 1] != '{'))
            {
                result.Append("{{");
                i++;
                while (i < url.Length && url[i] != '}')
                {
                    result.Append(url[i]);
                    i++;
                }

                result.Append("}}");
            }
            else
            {
                result.Append(url[i]);
            }
        }

        return result.ToString();
    }

    private static void AppendQueryParameters(StringBuilder sb, JsonElement operation)
    {
        if (!operation.TryGetProperty("parameters", out var parameters))
        {
            return;
        }

        foreach (var param in parameters.EnumerateArray())
        {
            var location = param.TryGetProperty("in", out var inProp) ? inProp.GetString() : null;

            if (location is not "query")
            {
                continue;
            }

            var name = param.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

            if (name is null)
            {
                continue;
            }

            sb.AppendLine($"# query param: {name}={{{{{name}}}}}");
        }
    }

    private static void AppendRequestBody(StringBuilder sb, JsonElement root, JsonElement operation)
    {
        JsonElement? schema = null;

        // OpenAPI 3.x: requestBody.content."application/json".schema
        if (operation.TryGetProperty("requestBody", out var requestBody) &&
            requestBody.TryGetProperty("content", out var content) &&
            content.TryGetProperty("application/json", out var jsonContent) &&
            jsonContent.TryGetProperty("schema", out var schemaEl))
        {
            schema = ResolveRef(root, schemaEl);
        }

        // Swagger 2.x: parameters with in=body
        if (schema is null && operation.TryGetProperty("parameters", out var parameters))
        {
            foreach (var param in parameters.EnumerateArray())
            {
                var location = param.TryGetProperty("in", out var inProp) ? inProp.GetString() : null;

                if (location is "body" && param.TryGetProperty("schema", out var bodySchema))
                {
                    schema = ResolveRef(root, bodySchema);
                    break;
                }
            }
        }

        if (schema is null)
        {
            return;
        }

        var bodyJson = GenerateExampleJson(root, schema.Value, 0);

        sb.AppendLine("body json \"\"\"");
        sb.AppendLine(bodyJson);
        sb.AppendLine("\"\"\"");
    }

    private static JsonElement ResolveRef(JsonElement root, JsonElement element)
    {
        if (!element.TryGetProperty("$ref", out var refProp))
        {
            return element;
        }

        var refPath = refProp.GetString();

        if (refPath is null || !refPath.StartsWith("#/"))
        {
            return element;
        }

        var segments = refPath[2..].Split('/');
        var current = root;

        foreach (var segment in segments)
        {
            if (!current.TryGetProperty(segment, out var next))
            {
                return element;
            }

            current = next;
        }

        return current;
    }

    private static string GenerateExampleJson(JsonElement root, JsonElement schema, int depth)
    {
        if (depth > 5)
        {
            return "{}";
        }

        var resolved = ResolveRef(root, schema);
        var type = resolved.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "object";

        // Check for example value first
        if (resolved.TryGetProperty("example", out var example))
        {
            return JsonSerializer.Serialize(example, new JsonSerializerOptions { WriteIndented = true });
        }

        return type switch
        {
            "object" => GenerateExampleObject(root, resolved, depth),
            "array" => GenerateExampleArray(root, resolved, depth),
            "string" => ResolveStringExample(resolved),
            "integer" or "number" => "0",
            "boolean" => "false",
            _ => GenerateExampleObject(root, resolved, depth),
        };
    }

    private static string ResolveStringExample(JsonElement schema)
    {
        var format = schema.TryGetProperty("format", out var formatProp) ? formatProp.GetString() : null;

        return format switch
        {
            "date-time" => "\"2026-01-01T00:00:00Z\"",
            "date" => "\"2026-01-01\"",
            "email" => "\"user@example.test\"",
            "uri" or "url" => "\"https://example.test\"",
            "uuid" => "\"00000000-0000-0000-0000-000000000000\"",
            _ => "\"example\"",
        };
    }

    private static string GenerateExampleObject(JsonElement root, JsonElement schema, int depth)
    {
        if (!schema.TryGetProperty("properties", out var properties))
        {
            return "{}";
        }

        var sb = new StringBuilder();
        var indent = new string(' ', (depth + 1) * 2);
        var closingIndent = new string(' ', depth * 2);

        sb.AppendLine("{");

        var entries = new List<string>();

        foreach (var prop in properties.EnumerateObject())
        {
            var value = GenerateExampleJson(root, prop.Value, depth + 1);
            entries.Add($"{indent}\"{prop.Name}\": {value}");
        }

        sb.Append(string.Join(",\n", entries));
        sb.AppendLine();
        sb.Append($"{closingIndent}}}");

        return sb.ToString();
    }

    private static string GenerateExampleArray(JsonElement root, JsonElement schema, int depth)
    {
        if (!schema.TryGetProperty("items", out var items))
        {
            return "[]";
        }

        var indent = new string(' ', (depth + 1) * 2);
        var closingIndent = new string(' ', depth * 2);
        var itemJson = GenerateExampleJson(root, items, depth + 1);

        return $"[\n{indent}{itemJson}\n{closingIndent}]";
    }

    private static string ResolveExpectedStatus(string httpMethod, JsonElement operation)
    {
        if (operation.TryGetProperty("responses", out var responses))
        {
            foreach (var response in responses.EnumerateObject())
            {
                if (response.Name is not "default" && int.TryParse(response.Name, out var code) && code is >= 200 and < 300)
                {
                    return response.Name;
                }
            }
        }

        return defaultExpectStatus.GetValueOrDefault(httpMethod, "200");
    }

    private static string ResolveStatusLabel(string method, string statusCode)
    {
        return statusCode switch
        {
            "200" => "returns 200",
            "201" => "created successfully",
            "202" => "accepted",
            "204" => "no content",
            _ => $"{method} returns {statusCode}",
        };
    }

    private static bool IsHttpMethod(string name)
    {
        return name is "get" or "post" or "put" or "patch" or "delete" or "head" or "options";
    }

    #endregion
}
