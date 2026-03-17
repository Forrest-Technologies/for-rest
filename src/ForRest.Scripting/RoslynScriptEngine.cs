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

        var requestApi = new ScriptRequestApi(request.PreparedRequest);
        var responseApi = new ScriptResponseApi(request.Response);
        var variablesApi = new VariablesApi(
        [
            .. request.GlobalVariables,
            .. request.WorkspaceVariables,
            .. request.EnvironmentVariables,
            .. request.RequestVariables,
            .. request.RuntimeVariables,
        ]);
        var testsApi = new TestsApi();
        var consoleApi = new ConsoleApi();

        var globals = new ScriptGlobals
        {
            request = requestApi,
            response = responseApi,
            variables = variablesApi,
            tests = testsApi,
            console = consoleApi,
            time = new TimeApi(),
            json = new JsonApi(),
            random = new RandomApi(),
            workspace = new WorkspaceApi(request.Workspace),
        };

        try
        {
            await CSharpScript.RunAsync(request.Script, ScriptOptions, globals, cancellationToken: cancellationToken);
        }
        catch (CompilationErrorException exception)
        {
            var message = string.Join(Environment.NewLine, exception.Diagnostics.Select(static item => item.ToString()));
            logger.LogWarning("Script compilation failed: {Message}", message);
            consoleApi.Error(message);
            return BuildResult(request.PreparedRequest, requestApi, variablesApi, testsApi, consoleApi, message);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Script execution failed");
            consoleApi.Error(exception.Message);
            return BuildResult(request.PreparedRequest, requestApi, variablesApi, testsApi, consoleApi, exception.Message);
        }

        return BuildResult(request.PreparedRequest, requestApi, variablesApi, testsApi, consoleApi, string.Empty);
    }

    #endregion

    #region Private Methods

    private static ScriptExecutionResult BuildResult(
        PreparedRequest originalRequest,
        ScriptRequestApi requestApi,
        VariablesApi variablesApi,
        TestsApi testsApi,
        ConsoleApi consoleApi,
        string errorMessage)
    {
        var method = Enum.TryParse<HttpMethodKind>(requestApi.Method, true, out var parsedMethod)
            ? parsedMethod
            : originalRequest.Method;

        var bodyMode = string.IsNullOrWhiteSpace(requestApi.Body)
            ? RequestBodyMode.None
            : originalRequest.Body.Mode == RequestBodyMode.None
                ? RequestBodyMode.RawText
                : originalRequest.Body.Mode;

        return new()
        {
            PreparedRequest = originalRequest with
            {
                Method = method,
                Uri = Uri.TryCreate(requestApi.Url, UriKind.Absolute, out var uri) ? uri : originalRequest.Uri,
                Headers =
                [
                    .. requestApi.Headers.Select(
                        item => new KeyValueDefinition
                        {
                            Key = item.Key,
                            Value = item.Value,
                        }),
                ],
                Body = originalRequest.Body with
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
