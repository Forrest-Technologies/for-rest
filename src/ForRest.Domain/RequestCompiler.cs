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
        var auth = RenderAuth(request.Auth, preview.Variables);
        ApplyAuth(auth, headers, queryParameters, preview.Variables);

        uriBuilder.Query = BuildQueryString(uri.Query, queryParameters);
        var body = RenderBody(request.Body, preview.Variables);
        ApplyUserAgent(request.UserAgent, request.CustomUserAgent, headers, preview.Variables);

        return OperationResult<PreparedRequest>.Success(new()
        {
            Method = request.Method,
            Uri = uriBuilder.Uri,
            Headers = headers,
            Body = body,
            Auth = auth,
            TimeoutMilliseconds = request.TimeoutMilliseconds,
            FollowRedirects = request.FollowRedirects,
            ValidateSsl = request.ValidateSsl,
            UserAgent = request.UserAgent,
            CustomUserAgent = request.CustomUserAgent,
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
        switch (auth.Mode)
        {
            case AuthMode.BearerToken:
                var token = VariableResolver.RenderTemplate(auth.BearerToken, variables);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    UpsertHeader(headers, ResolveHeaderName(auth), BuildAuthValue(auth, token));
                }

                break;
            case AuthMode.Basic:
                var username = VariableResolver.RenderTemplate(auth.Username, variables);
                var password = VariableResolver.RenderTemplate(auth.Password, variables);
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                UpsertHeader(headers, ResolveHeaderName(auth), BuildAuthValue(auth, encoded));
                break;
            case AuthMode.ApiKey:
                var apiKeyName = VariableResolver.RenderTemplate(auth.ApiKeyName, variables);
                var apiKeyValue = VariableResolver.RenderTemplate(auth.ApiKeyValue, variables);
                if (auth.ApiKeyLocation == ApiKeyLocation.Query)
                {
                    UpsertQueryParameter(queryParameters, apiKeyName, apiKeyValue);
                }
                else
                {
                    UpsertHeader(headers, apiKeyName, apiKeyValue);
                }

                break;
            case AuthMode.Header:
                var headerName = VariableResolver.RenderTemplate(ResolveHeaderName(auth), variables);
                var headerValue = VariableResolver.RenderTemplate(auth.HeaderValue, variables);
                if (auth.ApiKeyLocation == ApiKeyLocation.Query)
                {
                    UpsertQueryParameter(queryParameters, ResolveQueryParameterName(auth), BuildAuthValue(auth, headerValue));
                }
                else
                {
                    UpsertHeader(headers, headerName, BuildAuthValue(auth, headerValue));
                }

                break;
            case AuthMode.None:
            case AuthMode.Digest:
            case AuthMode.Ntlm:
            case AuthMode.Negotiate:
            case AuthMode.OAuthClientCredentials:
            case AuthMode.OAuthDeviceCode:
            case AuthMode.OAuthIntegratedWindows:
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
        return body with
        {
            RawContent = VariableResolver.RenderTemplate(body.RawContent, variables),
            ContentType = VariableResolver.RenderTemplate(body.ContentType, variables),
            FormValues =
            [
                .. body.FormValues.Select(
                    item => item with
                    {
                        Key = VariableResolver.RenderTemplate(item.Key, variables),
                        Value = VariableResolver.RenderTemplate(item.Value, variables),
                    }),
            ],
        };
    }

    private static List<KeyValueDefinition> RenderEntries(IEnumerable<KeyValueDefinition> entries, IEnumerable<ResolvedVariable> variables)
    {
        return
        [
            .. entries.Select(
                item => item with
                {
                    Key = VariableResolver.RenderTemplate(item.Key, variables),
                    Value = VariableResolver.RenderTemplate(item.Value, variables),
                }),
        ];
    }

    private static RequestAuthDefinition RenderAuth(RequestAuthDefinition auth, IEnumerable<ResolvedVariable> variables)
    {
        return auth with
        {
            Username = VariableResolver.RenderTemplate(auth.Username, variables),
            Password = VariableResolver.RenderTemplate(auth.Password, variables),
            BearerToken = VariableResolver.RenderTemplate(auth.BearerToken, variables),
            ApiKeyName = VariableResolver.RenderTemplate(auth.ApiKeyName, variables),
            ApiKeyValue = VariableResolver.RenderTemplate(auth.ApiKeyValue, variables),
            HeaderName = VariableResolver.RenderTemplate(auth.HeaderName, variables),
            HeaderValue = VariableResolver.RenderTemplate(auth.HeaderValue, variables),
            QueryParameterName = VariableResolver.RenderTemplate(auth.QueryParameterName, variables),
            Scheme = VariableResolver.RenderTemplate(auth.Scheme, variables),
            Domain = VariableResolver.RenderTemplate(auth.Domain, variables),
            Authority = VariableResolver.RenderTemplate(auth.Authority, variables),
            TokenUrl = VariableResolver.RenderTemplate(auth.TokenUrl, variables),
            ClientId = VariableResolver.RenderTemplate(auth.ClientId, variables),
            ClientSecret = VariableResolver.RenderTemplate(auth.ClientSecret, variables),
            Scopes = VariableResolver.RenderTemplate(auth.Scopes, variables),
            Resource = VariableResolver.RenderTemplate(auth.Resource, variables),
            Audience = VariableResolver.RenderTemplate(auth.Audience, variables),
        };
    }

    private static string ResolveHeaderName(RequestAuthDefinition auth)
    {
        return string.IsNullOrWhiteSpace(auth.HeaderName) ? "Authorization" : auth.HeaderName;
    }

    private static string ResolveQueryParameterName(RequestAuthDefinition auth)
    {
        if (!string.IsNullOrWhiteSpace(auth.QueryParameterName))
        {
            return auth.QueryParameterName;
        }

        return string.IsNullOrWhiteSpace(auth.ApiKeyName) ? "access_token" : auth.ApiKeyName;
    }

    private static string BuildAuthValue(RequestAuthDefinition auth, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string scheme = ResolveAuthScheme(auth);
        return string.IsNullOrWhiteSpace(scheme)
            ? value
            : $"{scheme} {value}";
    }

    private static string ResolveAuthScheme(RequestAuthDefinition auth)
    {
        if (!string.IsNullOrWhiteSpace(auth.Scheme))
        {
            return auth.Scheme.Trim();
        }

        return auth.Mode switch
        {
            AuthMode.Basic => "Basic",
            AuthMode.BearerToken or AuthMode.OAuthClientCredentials or AuthMode.OAuthDeviceCode or AuthMode.OAuthIntegratedWindows
                when auth.ApiKeyLocation == ApiKeyLocation.Header
                     && string.Equals(ResolveHeaderName(auth), "Authorization", StringComparison.OrdinalIgnoreCase) => "Bearer",
            _ => string.Empty,
        };
    }

    private static void UpsertHeader(List<KeyValueDefinition> headers, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        headers.RemoveAll(header => string.Equals(header.Key, key, StringComparison.OrdinalIgnoreCase));
        headers.Add(
            new()
            {
                Key = key,
                Value = value,
            });
    }

    private static void ApplyUserAgent(
        UserAgentKind kind,
        string customValue,
        List<KeyValueDefinition> headers,
        IEnumerable<ResolvedVariable> variables)
    {
        var resolved = UserAgentResolver.Resolve(kind, VariableResolver.RenderTemplate(customValue, variables));
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            UpsertHeader(headers, "User-Agent", resolved);
        }
    }

    private static void UpsertQueryParameter(List<KeyValueDefinition> queryParameters, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        queryParameters.RemoveAll(parameter => string.Equals(parameter.Key, key, StringComparison.OrdinalIgnoreCase));
        queryParameters.Add(
            new()
            {
                Key = key,
                Value = value,
            });
    }

    #endregion
}
