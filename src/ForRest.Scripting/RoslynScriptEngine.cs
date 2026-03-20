namespace ForRest.Scripting;

public sealed class RoslynScriptEngine(ILogger<RoslynScriptEngine> logger) : IScriptEngine
{
    #region Private Fields

    private static readonly ScriptOptions ScriptOptions = Microsoft.CodeAnalysis.Scripting.ScriptOptions.Default
        .AddReferences(
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(JsonNode).Assembly,
            typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly,
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
        ScriptResponseApi? responseApi = null;
        VariablesApi? variablesApi = null;
        ScriptGlobals? globals = null;

        try
        {
            responseApi = new ScriptResponseApi(request.Response);
            requestApi = new ScriptRequestApi(
                request.PreparedRequest,
                responseApi,
                request.SendAsync,
                request.MaxSendIterations);
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
                response = responseApi,
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
            return BuildResult(request, requestApi, responseApi, variablesApi, testsApi, consoleApi, message);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Script execution failed");
            consoleApi.Error(exception.Message);
            return BuildResult(request, requestApi, responseApi, variablesApi, testsApi, consoleApi, exception.Message);
        }

        return BuildResult(request, requestApi, responseApi, variablesApi, testsApi, consoleApi, string.Empty);
    }

    #endregion

    #region Private Methods

    private static ScriptExecutionResult BuildResult(
        ScriptExecutionRequest originalRequest,
        ScriptRequestApi? requestApi,
        ScriptResponseApi? responseApi,
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
                Response = originalRequest.Response,
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

        return new()
        {
            PreparedRequest = requestApi.ToPreparedRequest(),
            Response = responseApi?.Snapshot ?? originalRequest.Response,
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
