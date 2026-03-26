using System.Globalization;
using System.Text;

namespace ForRest.Scripting;

public sealed class ForRestScriptCompiler(ForRestScriptParser parser) : IForRestScriptCompiler
{
    #region Public Methods

    public ForRestScriptParseResult Parse(string source)
    {
        return parser.Parse(source);
    }

    public ForRestScriptCompilationResult Compile(string source, ForRestScriptCompilationOptions options)
    {
        var parseResult = parser.Parse(source);
        var diagnostics = parseResult.Diagnostics.ToList();
        if (!parseResult.Succeeded || parseResult.Document is null)
        {
            return new(null, null, diagnostics);
        }

        var document = parseResult.Document;
        var requestVariables = new List<VariableDefinition>();
        var runtimeSeeds = new List<ForRestRuntimeVariableSeed>();

        foreach (var variable in document.Variables)
        {
            switch (variable.Scope)
            {
                case ForRestScriptVariableScope.Request:
                    if (!TryRenderScalar(variable.Expression, out var requestValue))
                    {
                        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, $"Request variable '{variable.Key}' must use a scalar literal value.", 0, 0));
                        continue;
                    }

                    requestVariables.Add(new()
                    {
                        Key = variable.Key,
                        Value = requestValue!,
                        Scope = VariableScope.RequestLocal,
                    });
                    break;
                case ForRestScriptVariableScope.Runtime:
                    if (!TryBuildRuntimeSeed(variable, out var runtimeSeed, out var diagnosticMessage))
                    {
                        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, diagnosticMessage ?? $"Could not compile runtime seed '{variable.Key}'.", 0, 0));
                        continue;
                    }

                    runtimeSeeds.Add(runtimeSeed!);
                    break;
            }
        }

        var flowVariableNames = requestVariables
            .Select(static item => item.Key)
            .Concat(runtimeSeeds.Select(static item => item.Key))
            .ToList();

        var flowScript = ForRestFlowScriptCompiler.Compile(document.Flow, flowVariableNames, diagnostics);

        if (!TryReadRequiredIdentifier(document.Request, "method", out var methodText, diagnostics, "request")
            || !Enum.TryParse<HttpMethodKind>(methodText, true, out var method))
        {
            diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "The request section must declare a valid HTTP method.", 0, 0));
            method = HttpMethodKind.Get;
        }

        if (!TryReadRequiredString(document.Request, "url", out var urlTemplate, diagnostics, "request"))
        {
            diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "The request section must declare a URL.", 0, 0));
            urlTemplate = "https://localhost";
        }

        var body = BuildBody(document);
        var auth = BuildAuth(document, diagnostics);
        var headers = BuildEntries(document.Headers, diagnostics, "headers");
        var queryParameters = BuildEntries(document.QueryParameters, diagnostics, "query");
        var formValues = BuildEntries(document.FormValues, diagnostics, "form");
        var multipartValues = BuildEntries(document.MultipartValues, diagnostics, "multipart");

        if (body.Mode == RequestBodyMode.FormUrlEncoded)
        {
            body = body with
            {
                FormValues = formValues,
            };
        }
        else if (body.Mode == RequestBodyMode.MultipartFormData)
        {
            body = body with
            {
                FormValues = multipartValues,
            };
        }

        var request = new RequestDefinition
        {
            WorkspaceId = options.WorkspaceId,
            Name = TryReadOptionalString(document.Meta, "name") ?? options.DefaultRequestName,
            Method = method,
            UrlTemplate = urlTemplate!,
            QueryParameters = queryParameters,
            Headers = headers,
            Auth = auth,
            Body = body,
            Variables = requestVariables,
            Extractions = BuildExtractions(document.Extractions),
            PreRequestScript = flowScript,
            TestsScript = BuildTestsScript(document.Tests, diagnostics),
            Schedule = BuildSchedule(document.Repeat),
            Retry = BuildRetry(document.Retry),
            TimeoutMilliseconds = Math.Max(1, TryReadOptionalNumber(document.Request, "timeout") ?? 30_000),
            FollowRedirects = TryReadOptionalBoolean(document.Request, "redirects") ?? true,
            ValidateSsl = TryReadOptionalBoolean(document.Request, "ssl") ?? true,
            SaveResponseToHistory = TryReadOptionalBoolean(document.Request, "history") ?? true,
            MaxSendIterations = Math.Max(0, TryReadOptionalNumber(document.Request, "max_send_iterations") ?? 3),
        };

        if (diagnostics.Any(static diagnostic => diagnostic.Severity == ForRestScriptDiagnosticSeverity.Error))
        {
            return new(document, null, diagnostics);
        }

        return new(
            document,
            new()
            {
                SourceText = source,
                Request = request,
                RuntimeSeeds = runtimeSeeds,
            },
            diagnostics);
    }

    #endregion

    #region Private Methods

    private static bool TryBuildRuntimeSeed(
        ForRestScriptVariableDeclaration variable,
        out ForRestRuntimeVariableSeed? seed,
        out string? diagnosticMessage)
    {
        seed = null;
        diagnosticMessage = null;

        switch (variable.Expression)
        {
            case ForRestScriptStringExpression stringExpression:
                seed = new(variable.Key, ForRestRuntimeSeedKind.Literal, stringExpression.Value);
                return true;
            case ForRestScriptNumberExpression numberExpression:
                seed = new(variable.Key, ForRestRuntimeSeedKind.Literal, numberExpression.Value.ToString(CultureInfo.InvariantCulture));
                return true;
            case ForRestScriptBooleanExpression booleanExpression:
                seed = new(variable.Key, ForRestRuntimeSeedKind.Literal, booleanExpression.Value ? "true" : "false");
                return true;
            case ForRestScriptFunctionCallExpression functionCallExpression:
                switch (functionCallExpression.Name.Trim().ToLowerInvariant())
                {
                    case "guid" when functionCallExpression.Arguments.Count == 0:
                        seed = new(variable.Key, ForRestRuntimeSeedKind.Guid, string.Empty);
                        return true;
                    case "now" when functionCallExpression.Arguments.Count == 0:
                        seed = new(variable.Key, ForRestRuntimeSeedKind.Now, string.Empty);
                        return true;
                    case "utc_now" when functionCallExpression.Arguments.Count == 0:
                        seed = new(variable.Key, ForRestRuntimeSeedKind.UtcNow, string.Empty);
                        return true;
                    case "random" when functionCallExpression.Arguments.Count == 2
                        && functionCallExpression.Arguments[0] is ForRestScriptNumberExpression minimum
                        && functionCallExpression.Arguments[1] is ForRestScriptNumberExpression maximum:
                        seed = new(variable.Key, ForRestRuntimeSeedKind.RandomNumber, string.Empty, minimum.Value, maximum.Value);
                        return true;
                    default:
                        diagnosticMessage = $"Runtime variable '{variable.Key}' uses unsupported function '{functionCallExpression.Name}'. Supported functions: guid(), now(), utc_now(), random(min,max).";
                        return false;
                }
            default:
                diagnosticMessage = $"Runtime variable '{variable.Key}' uses an unsupported expression.";
                return false;
        }
    }

    private static RequestAuthDefinition BuildAuth(
        ForRestScriptDocument document,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        if (!document.Auth.TryGetValue("mode", out var modeExpression))
        {
            return new();
        }

        if (!TryRenderScalar(modeExpression, out var modeText)
            || !TryParseAuthMode(modeText ?? string.Empty, out var mode))
        {
            diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "The auth section declares an invalid auth mode.", 0, 0));
            return new();
        }

        return new()
        {
            Mode = mode,
            Username = TryReadOptionalString(document.Auth, "username") ?? string.Empty,
            Password = TryReadOptionalString(document.Auth, "password") ?? string.Empty,
            BearerToken = TryReadOptionalString(document.Auth, "token") ?? string.Empty,
            ApiKeyName = TryReadOptionalString(document.Auth, "name") ?? string.Empty,
            ApiKeyValue = TryReadOptionalString(document.Auth, "value") ?? string.Empty,
            HeaderName = TryReadOptionalString(document.Auth, "header_name") ?? string.Empty,
            HeaderValue = TryReadOptionalString(document.Auth, "header_value")
                ?? TryReadOptionalString(document.Auth, "value")
                ?? string.Empty,
            QueryParameterName = TryReadOptionalString(document.Auth, "query_name") ?? string.Empty,
            Scheme = TryReadOptionalString(document.Auth, "scheme") ?? string.Empty,
            UseDefaultCredentials = TryReadOptionalBoolean(document.Auth, "use_default_credentials") ?? false,
            Domain = TryReadOptionalString(document.Auth, "domain") ?? string.Empty,
            Authority = TryReadOptionalString(document.Auth, "authority") ?? string.Empty,
            TokenUrl = TryReadOptionalString(document.Auth, "token_url") ?? string.Empty,
            ClientId = TryReadOptionalString(document.Auth, "client_id") ?? string.Empty,
            ClientSecret = TryReadOptionalString(document.Auth, "client_secret") ?? string.Empty,
            Scopes = TryReadOptionalString(document.Auth, "scopes") ?? string.Empty,
            Resource = TryReadOptionalString(document.Auth, "resource") ?? string.Empty,
            Audience = TryReadOptionalString(document.Auth, "audience") ?? string.Empty,
            ApiKeyLocation = Enum.TryParse<ApiKeyLocation>(TryReadOptionalString(document.Auth, "location"), true, out var apiKeyLocation)
                ? apiKeyLocation
                : ApiKeyLocation.Header,
        };
    }

    private static RequestBodyDefinition BuildBody(ForRestScriptDocument document)
    {
        if (document.Body is not null)
        {
            return new()
            {
                Mode = document.Body.Mode,
                RawContent = document.Body.Content,
                ContentType = TryReadOptionalString(document.Request, "content_type")
                    ?? (document.Body.Mode == RequestBodyMode.Json ? "application/json" : "text/plain"),
            };
        }

        if (document.FormValues.Count > 0)
        {
            return new()
            {
                Mode = RequestBodyMode.FormUrlEncoded,
                ContentType = TryReadOptionalString(document.Request, "content_type") ?? "application/x-www-form-urlencoded",
            };
        }

        if (document.MultipartValues.Count > 0)
        {
            return new()
            {
                Mode = RequestBodyMode.MultipartFormData,
                ContentType = TryReadOptionalString(document.Request, "content_type") ?? "multipart/form-data",
            };
        }

        return new()
        {
            Mode = RequestBodyMode.None,
        };
    }

    private static List<KeyValueDefinition> BuildEntries(
        IEnumerable<ForRestScriptNamedValue> items,
        List<ForRestScriptDiagnostic> diagnostics,
        string sectionName)
    {
        var entries = new List<KeyValueDefinition>();
        foreach (var item in items)
        {
            if (!TryRenderScalar(item.Value, out var value))
            {
                diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, $"The '{sectionName}' entry '{item.Key}' must use a scalar literal value.", 0, 0));
                continue;
            }

            entries.Add(new()
            {
                Key = item.Key,
                Value = value!,
            });
        }

        return entries;
    }

    private static List<ExtractionDefinition> BuildExtractions(IEnumerable<ForRestScriptExtraction> extractions)
    {
        return
        [
            .. extractions.Select(
                extraction => new ExtractionDefinition
                {
                    Name = extraction.TargetVariableName,
                    Source = extraction.Source switch
                    {
                        ForRestScriptExtractionSource.Body => ExtractionSource.Body,
                        ForRestScriptExtractionSource.Header => ExtractionSource.Header,
                        _ => ExtractionSource.Json,
                    },
                    Selector = extraction.Selector,
                    Pattern = extraction.Pattern,
                    Group = extraction.Group,
                    TargetVariableName = extraction.TargetVariableName,
                    TargetScope = extraction.TargetScope,
                }),
        ];
    }

    private static ScheduleDefinition BuildSchedule(IReadOnlyDictionary<string, ForRestScriptValueExpression> repeat)
    {
        return new()
        {
            RepeatCount = Math.Max(1, TryReadOptionalNumber(repeat, "count") ?? 1),
            DelayMilliseconds = Math.Max(0, TryReadOptionalNumber(repeat, "delay") ?? 0),
            IntervalMilliseconds = Math.Max(0, TryReadOptionalNumber(repeat, "interval") ?? 0),
        };
    }

    private static RetryDefinition BuildRetry(IReadOnlyDictionary<string, ForRestScriptValueExpression> retry)
    {
        return new()
        {
            Count = Math.Max(0, TryReadOptionalNumber(retry, "count") ?? 0),
            IntervalMilliseconds = Math.Max(0, TryReadOptionalNumber(retry, "interval") ?? 0),
        };
    }

    private static string BuildTestsScript(IEnumerable<ForRestScriptAssertion> assertions, List<ForRestScriptDiagnostic> diagnostics)
    {
        var builder = new StringBuilder();
        var testIndex = 0;

        foreach (var assertion in assertions)
        {
            testIndex++;
            switch (assertion.Target)
            {
                case ForRestScriptAssertionTarget.Status:
                    if (assertion.Value is not ForRestScriptNumberExpression statusNumber)
                    {
                        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "Status assertions must compare against a numeric value.", 0, 0));
                        continue;
                    }

                    builder.AppendLine($"tests.Assert(response.Status {RenderOperator(assertion.Operator)} {statusNumber.Value}, {RenderString(assertion.Message)});");
                    break;
                case ForRestScriptAssertionTarget.Body:
                    if (assertion.Operator == ForRestScriptComparisonOperator.RegexMatch)
                    {
                        if (!TryRenderScalar(assertion.Value, out var bodyPattern))
                        {
                            diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "Body regex assertions require a scalar pattern.", 0, 0));
                            continue;
                        }

                        builder.AppendLine($"tests.Assert(regex.IsMatch(response.Body ?? string.Empty, {RenderString(bodyPattern!)}), {RenderString(assertion.Message)});");
                        break;
                    }

                    if (!TryRenderScalar(assertion.Value, out var bodyValue))
                    {
                        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "Body assertions require a scalar comparison value.", 0, 0));
                        continue;
                    }

                    AppendStringAssertion(builder, "response.Body ?? string.Empty", assertion.Operator, bodyValue!, assertion.Message);
                    break;
                case ForRestScriptAssertionTarget.Header:
                    if (assertion.Operator == ForRestScriptComparisonOperator.RegexMatch)
                    {
                        if (!TryRenderScalar(assertion.Value, out var headerPattern) || string.IsNullOrWhiteSpace(assertion.HeaderName))
                        {
                            diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "Header regex assertions require a header name and scalar pattern.", 0, 0));
                            continue;
                        }

                        var headerRegexVariableName = $"__headerValue{testIndex}";
                        builder.AppendLine($"var {headerRegexVariableName} = response.Headers.TryGetValue({RenderString(assertion.HeaderName!)}, out string __headerRaw{testIndex}) ? __headerRaw{testIndex} : string.Empty;");
                        builder.AppendLine($"tests.Assert(regex.IsMatch({headerRegexVariableName}, {RenderString(headerPattern!)}), {RenderString(assertion.Message)});");
                        break;
                    }

                    if (!TryRenderScalar(assertion.Value, out var headerValue) || string.IsNullOrWhiteSpace(assertion.HeaderName))
                    {
                        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "Header assertions require a header name and scalar value.", 0, 0));
                        continue;
                    }

                    var headerVariableName = $"__headerValue{testIndex}";
                    builder.AppendLine($"var {headerVariableName} = response.Headers.TryGetValue({RenderString(assertion.HeaderName!)}, out string __headerRaw{testIndex}) ? __headerRaw{testIndex} : string.Empty;");
                    AppendStringAssertion(builder, headerVariableName, assertion.Operator, headerValue!, assertion.Message);
                    break;
                case ForRestScriptAssertionTarget.Json:
                    if (assertion.Operator == ForRestScriptComparisonOperator.RegexMatch)
                    {
                        var jsonRegexVariableName = $"__jsonValue{testIndex}";
                        builder.AppendLine($"var {jsonRegexVariableName} = json.Select(response.Json(), {RenderString(assertion.Selector ?? "$")});");
                        if (!TryRenderScalar(assertion.Value, out var jsonPattern))
                        {
                            diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "JSON regex assertions require a scalar pattern.", 0, 0));
                            continue;
                        }

                        builder.AppendLine($"tests.Assert(regex.IsMatch({jsonRegexVariableName} ?? string.Empty, {RenderString(jsonPattern!)}), {RenderString(assertion.Message)});");
                        break;
                    }

                    var jsonVariableName = $"__jsonValue{testIndex}";
                    builder.AppendLine($"var {jsonVariableName} = json.Select(response.Json(), {RenderString(assertion.Selector ?? "$")});");
                    if (assertion.Operator == ForRestScriptComparisonOperator.Exists)
                    {
                        builder.AppendLine($"tests.Assert(!string.IsNullOrWhiteSpace({jsonVariableName}), {RenderString(assertion.Message)});");
                        break;
                    }

                    if (!TryRenderScalar(assertion.Value, out var jsonValue))
                    {
                        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "JSON assertions require a scalar comparison value.", 0, 0));
                        continue;
                    }

                    AppendStringAssertion(builder, $"{jsonVariableName} ?? string.Empty", assertion.Operator, jsonValue!, assertion.Message);
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static void AppendStringAssertion(
        StringBuilder builder,
        string actualExpression,
        ForRestScriptComparisonOperator comparisonOperator,
        string expectedValue,
        string message)
    {
        var expectedLiteral = RenderString(expectedValue);
        var messageLiteral = RenderString(message);

        switch (comparisonOperator)
        {
            case ForRestScriptComparisonOperator.Equal:
                builder.AppendLine($"tests.Equal({expectedLiteral}, {actualExpression}, {messageLiteral});");
                break;
            case ForRestScriptComparisonOperator.NotEqual:
                builder.AppendLine($"tests.Assert(!string.Equals({actualExpression}, {expectedLiteral}, StringComparison.Ordinal), {messageLiteral});");
                break;
            case ForRestScriptComparisonOperator.Contains:
                builder.AppendLine($"tests.Assert(({actualExpression}).Contains({expectedLiteral}, StringComparison.Ordinal), {messageLiteral});");
                break;
            case ForRestScriptComparisonOperator.RegexMatch:
                builder.AppendLine($"tests.Assert(regex.IsMatch({actualExpression}, {expectedLiteral}), {messageLiteral});");
                break;
            default:
                builder.AppendLine($"tests.Fail({RenderString($"Unsupported string operator '{comparisonOperator}'.")});");
                break;
        }
    }

    private static string RenderOperator(ForRestScriptComparisonOperator comparisonOperator)
    {
        return comparisonOperator switch
        {
            ForRestScriptComparisonOperator.Equal => "==",
            ForRestScriptComparisonOperator.NotEqual => "!=",
            ForRestScriptComparisonOperator.GreaterThan => ">",
            ForRestScriptComparisonOperator.GreaterThanOrEqual => ">=",
            ForRestScriptComparisonOperator.LessThan => "<",
            ForRestScriptComparisonOperator.LessThanOrEqual => "<=",
            _ => "==",
        };
    }

    private static string RenderString(string value)
    {
        return $"@\"{value.Replace("\"", "\"\"")}\"";
    }

    private static bool TryReadRequiredIdentifier(
        IReadOnlyDictionary<string, ForRestScriptValueExpression> source,
        string key,
        out string value,
        List<ForRestScriptDiagnostic> diagnostics,
        string sectionName)
    {
        if (source.TryGetValue(key, out var expression)
            && expression is ForRestScriptIdentifierExpression identifierExpression)
        {
            value = identifierExpression.Value;
            return true;
        }

        value = string.Empty;
        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, $"The '{sectionName}' section requires '{key} = IDENTIFIER'.", 0, 0));
        return false;
    }

    private static bool TryReadRequiredString(
        IReadOnlyDictionary<string, ForRestScriptValueExpression> source,
        string key,
        out string? value,
        List<ForRestScriptDiagnostic> diagnostics,
        string sectionName)
    {
        value = TryReadOptionalString(source, key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, $"The '{sectionName}' section requires '{key} = \"...\"'.", 0, 0));
        return false;
    }

    private static string? TryReadOptionalString(
        IReadOnlyDictionary<string, ForRestScriptValueExpression> source,
        string key)
    {
        return source.TryGetValue(key, out var expression) && TryRenderScalar(expression, out var value)
            ? value
            : null;
    }

    private static int? TryReadOptionalNumber(
        IReadOnlyDictionary<string, ForRestScriptValueExpression> source,
        string key)
    {
        return source.TryGetValue(key, out var expression) && expression is ForRestScriptNumberExpression numberExpression
            ? numberExpression.Value
            : null;
    }

    private static bool? TryReadOptionalBoolean(
        IReadOnlyDictionary<string, ForRestScriptValueExpression> source,
        string key)
    {
        return source.TryGetValue(key, out var expression) && expression is ForRestScriptBooleanExpression booleanExpression
            ? booleanExpression.Value
            : null;
    }

    private static bool TryRenderScalar(ForRestScriptValueExpression? expression, out string? value)
    {
        switch (expression)
        {
            case null:
                value = null;
                return false;
            case ForRestScriptStringExpression stringExpression:
                value = stringExpression.Value;
                return true;
            case ForRestScriptNumberExpression numberExpression:
                value = numberExpression.Value.ToString(CultureInfo.InvariantCulture);
                return true;
            case ForRestScriptBooleanExpression booleanExpression:
                value = booleanExpression.Value ? "true" : "false";
                return true;
            case ForRestScriptIdentifierExpression identifierExpression:
                value = identifierExpression.Value;
                return true;
            default:
                value = null;
                return false;
        }
    }

    private static bool TryParseAuthMode(string rawValue, out AuthMode mode)
    {
        switch ((rawValue ?? string.Empty).Trim().Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant())
        {
            case "none":
                mode = AuthMode.None;
                return true;
            case "bearer":
            case "bearer_token":
                mode = AuthMode.BearerToken;
                return true;
            case "basic":
                mode = AuthMode.Basic;
                return true;
            case "apikey":
            case "api_key":
                mode = AuthMode.ApiKey;
                return true;
            case "header":
            case "custom_header":
                mode = AuthMode.Header;
                return true;
            case "digest":
                mode = AuthMode.Digest;
                return true;
            case "ntlm":
                mode = AuthMode.Ntlm;
                return true;
            case "negotiate":
                mode = AuthMode.Negotiate;
                return true;
            case "oauth_client_credentials":
            case "client_credentials":
                mode = AuthMode.OAuthClientCredentials;
                return true;
            case "oauth_device_code":
            case "device_code":
                mode = AuthMode.OAuthDeviceCode;
                return true;
            case "oauth_integrated_windows":
            case "integrated_windows":
            case "windows_iwa":
                mode = AuthMode.OAuthIntegratedWindows;
                return true;
            default:
                mode = AuthMode.None;
                return false;
        }
    }

    #endregion
}
