using ForRest.Models;
using ForRest.Shared;

namespace ForRest.Domain;

public sealed class RequestCompiler(VariableResolver variableResolver)
{
    #region Public Methods

    public OperationResult<PreparedRequest> Prepare(
        RequestDefinition request,
        IEnumerable<VariableDefinition> systemVariables,
        IEnumerable<VariableDefinition> globalVariables,
        IEnumerable<VariableDefinition> workspaceVariables,
        IEnumerable<VariableDefinition> environmentVariables,
        IEnumerable<VariableDefinition> runtimeVariables)
    {
        var preview = variableResolver.Preview(
            request.UrlTemplate,
            systemVariables,
            globalVariables,
            workspaceVariables,
            environmentVariables,
            request.Variables,
            runtimeVariables);

        if (!Uri.TryCreate(preview.RenderedText, UriKind.Absolute, out var uri))
        {
            return OperationResult<PreparedRequest>.Fail($"The request URL is invalid after variable resolution: '{preview.RenderedText}'.");
        }

        var queryParameters = RenderEntries(request.QueryParameters, preview.Variables);
        var uriBuilder = new UriBuilder(uri)
        {
            Query = BuildQueryString(uri.Query, queryParameters),
        };

        var headers = RenderEntries(request.Headers, preview.Variables);
        ApplyAuth(request.Auth, headers, queryParameters, preview.Variables);

        uriBuilder.Query = BuildQueryString(uri.Query, queryParameters);
        var body = RenderBody(request.Body, preview.Variables);

        return OperationResult<PreparedRequest>.Success(new()
        {
            Method = request.Method,
            Uri = uriBuilder.Uri,
            Headers = headers,
            Body = body,
            TimeoutMilliseconds = request.TimeoutMilliseconds,
            FollowRedirects = request.FollowRedirects,
            ValidateSsl = request.ValidateSsl,
            Variables = preview,
            RawRequest = BuildRawRequest(request.Method, uriBuilder.Uri, headers, body),
        });
    }

    #endregion

    #region Private Methods

    private static void ApplyAuth(
        RequestAuthDefinition auth,
        List<KeyValueDefinition> headers,
        List<KeyValueDefinition> queryParameters,
        IEnumerable<ResolvedVariable> variables)
    {
        var renderer = new VariableResolver();
        switch (auth.Mode)
        {
            case AuthMode.BearerToken:
                var token = renderer.RenderTemplate(auth.BearerToken, variables);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    headers.Add(new()
                    {
                        Key = "Authorization",
                        Value = $"Bearer {token}",
                    });
                }

                break;
            case AuthMode.Basic:
                var username = renderer.RenderTemplate(auth.Username, variables);
                var password = renderer.RenderTemplate(auth.Password, variables);
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                headers.Add(new()
                {
                    Key = "Authorization",
                    Value = $"Basic {encoded}",
                });
                break;
            case AuthMode.ApiKey:
                var apiKeyName = renderer.RenderTemplate(auth.ApiKeyName, variables);
                var apiKeyValue = renderer.RenderTemplate(auth.ApiKeyValue, variables);
                if (auth.ApiKeyLocation == ApiKeyLocation.Query)
                {
                    queryParameters.Add(new()
                    {
                        Key = apiKeyName,
                        Value = apiKeyValue,
                    });
                }
                else
                {
                    headers.Add(new()
                    {
                        Key = apiKeyName,
                        Value = apiKeyValue,
                    });
                }

                break;
            case AuthMode.None:
            default:
                break;
        }
    }

    private static string BuildQueryString(string existingQuery, IEnumerable<KeyValueDefinition> queryParameters)
    {
        var pairs = existingQuery.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        pairs.AddRange(
            queryParameters
                .Where(static item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.Key))
                .Select(
                    static item =>
                        $"{WebUtility.UrlEncode(item.Key)}={WebUtility.UrlEncode(item.Value)}"));

        return string.Join("&", pairs);
    }

    private static string BuildRawRequest(HttpMethodKind method, Uri uri, IEnumerable<KeyValueDefinition> headers, RequestBodyDefinition body)
    {
        var builder = new StringBuilder();
        builder.Append(method.ToString().ToUpperInvariant());
        builder.Append(' ');
        builder.Append(uri);
        builder.AppendLine();

        foreach (var header in headers.Where(static item => item.IsEnabled))
        {
            builder.Append(header.Key);
            builder.Append(": ");
            builder.AppendLine(header.Value);
        }

        if (body.Mode != RequestBodyMode.None)
        {
            builder.AppendLine();
            if (body.Mode is RequestBodyMode.FormUrlEncoded or RequestBodyMode.MultipartFormData)
            {
                builder.AppendLine(string.Join("&", body.FormValues.Where(static item => item.IsEnabled).Select(static item => $"{item.Key}={item.Value}")));
            }
            else
            {
                builder.AppendLine(body.RawContent);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static RequestBodyDefinition RenderBody(RequestBodyDefinition body, IEnumerable<ResolvedVariable> variables)
    {
        var renderer = new VariableResolver();
        return body with
        {
            RawContent = renderer.RenderTemplate(body.RawContent, variables),
            ContentType = renderer.RenderTemplate(body.ContentType, variables),
            FormValues =
            [
                .. body.FormValues.Select(
                    item => item with
                    {
                        Key = renderer.RenderTemplate(item.Key, variables),
                        Value = renderer.RenderTemplate(item.Value, variables),
                    }),
            ],
        };
    }

    private static List<KeyValueDefinition> RenderEntries(IEnumerable<KeyValueDefinition> entries, IEnumerable<ResolvedVariable> variables)
    {
        var renderer = new VariableResolver();
        return
        [
            .. entries.Select(
                item => item with
                {
                    Key = renderer.RenderTemplate(item.Key, variables),
                    Value = renderer.RenderTemplate(item.Value, variables),
                }),
        ];
    }

    #endregion
}
