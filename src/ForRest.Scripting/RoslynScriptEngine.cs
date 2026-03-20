namespace ForRest.Scripting;

public sealed class RoslynScriptEngine(ILogger<RoslynScriptEngine> logger) : IScriptEngine
{
    #region Private Fields

    private static readonly ScriptOptions ScriptOptions = Microsoft.CodeAnalysis.Scripting.ScriptOptions.Default
        .AddReferences(
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(JsonNode).Assembly,
            typeof(PreparedRequest).Assembly,
            typeof(VariableDefinition).Assembly)
        .AddImports(
            "System",
            "System.Linq",
            "System.Collections.Generic",
            "System.Text.Json.Nodes",
            "ForRest.Models");

    #endregion

    #region Public Methods

    public async Task<ScriptExecutionResult> Run(ScriptExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Script))
        {
            return new()
            {
                PreparedRequest = request.PreparedRequest,
                RuntimeVariables = request.RuntimeVariables,
            };
        }

        var testsApi = new TestsApi();
        var consoleApi = new ConsoleApi();
        ScriptRequestApi? requestApi = null;
        VariablesApi? variablesApi = null;
        ScriptGlobals? globals = null;

        try
        {
            requestApi = new ScriptRequestApi(request.PreparedRequest);
            variablesApi = new VariablesApi(
            [
                .. request.GlobalVariables,
                .. request.WorkspaceVariables,
                .. request.EnvironmentVariables,
                .. request.RequestVariables,
                .. request.RuntimeVariables,
            ]);
            globals = new ScriptGlobals
            {
                request = requestApi,
                response = new ScriptResponseApi(request.Response),
                variables = variablesApi,
                tests = testsApi,
                console = consoleApi,
                time = new TimeApi(),
                json = new JsonApi(),
                random = new RandomApi(),
                workspace = new WorkspaceApi(request.Workspace),
            };

            await CSharpScript.RunAsync(request.Script, ScriptOptions, globals, cancellationToken: cancellationToken);
        }
        catch (CompilationErrorException exception)
        {
            var message = string.Join(Environment.NewLine, exception.Diagnostics.Select(static item => item.ToString()));
            logger.LogWarning("Script compilation failed: {Message}", message);
            consoleApi.Error(message);
            return BuildResult(request, requestApi, variablesApi, testsApi, consoleApi, message);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Script execution failed");
            consoleApi.Error(exception.Message);
            return BuildResult(request, requestApi, variablesApi, testsApi, consoleApi, exception.Message);
        }

        return BuildResult(request, requestApi, variablesApi, testsApi, consoleApi, string.Empty);
    }

    #endregion

    #region Private Methods

    private static ScriptExecutionResult BuildResult(
        ScriptExecutionRequest originalRequest,
        ScriptRequestApi? requestApi,
        VariablesApi? variablesApi,
        TestsApi testsApi,
        ConsoleApi consoleApi,
        string errorMessage)
    {
        if (requestApi is null || variablesApi is null)
        {
            return new()
            {
                PreparedRequest = originalRequest.PreparedRequest,
                RuntimeVariables =
                [
                    .. originalRequest.RuntimeVariables.Where(static item => item.Scope == VariableScope.Runtime),
                ],
                Tests =
                [
                    .. testsApi.All(),
                ],
                ConsoleEntries =
                [
                    .. consoleApi.All(),
                ],
                ErrorMessage = errorMessage,
            };
        }

        var method = Enum.TryParse<HttpMethodKind>(requestApi.Method, true, out var parsedMethod)
            ? parsedMethod
            : originalRequest.PreparedRequest.Method;

        var bodyMode = string.IsNullOrWhiteSpace(requestApi.Body)
            ? RequestBodyMode.None
            : originalRequest.PreparedRequest.Body.Mode == RequestBodyMode.None
                ? RequestBodyMode.RawText
                : originalRequest.PreparedRequest.Body.Mode;

        return new()
        {
            PreparedRequest = originalRequest.PreparedRequest with
            {
                Method = method,
                Uri = Uri.TryCreate(requestApi.Url, UriKind.Absolute, out var uri) ? uri : originalRequest.PreparedRequest.Uri,
                Headers =
                [
                    .. requestApi.Headers.Select(
                        item => new KeyValueDefinition
                        {
                            Key = item.Key,
                            Value = item.Value,
                        }),
                ],
                Body = originalRequest.PreparedRequest.Body with
                {
                    Mode = bodyMode,
                    RawContent = requestApi.Body,
                    ContentType = requestApi.ContentType,
                },
            },
            RuntimeVariables =
            [
                .. variablesApi.All().Where(static item => item.Scope == VariableScope.Runtime),
            ],
            Tests =
            [
                .. testsApi.All(),
            ],
            ConsoleEntries =
            [
                .. consoleApi.All(),
            ],
            ErrorMessage = errorMessage,
        };
    }

    #endregion
}
