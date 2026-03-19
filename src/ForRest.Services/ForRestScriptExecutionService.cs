namespace ForRest.Services;

public sealed class ForRestScriptExecutionService(
    IForRestScriptCompiler scriptCompiler,
    ForRestRuntimeVariableSeedEvaluator runtimeVariableSeedEvaluator,
    IRequestExecutionService requestExecutionService) : IForRestScriptExecutionService
{
    #region Public Methods

    public ForRestScriptCompilationResult Compile(string source, Guid workspaceId, string? defaultRequestName = null)
    {
        return scriptCompiler.Compile(
            source,
            new()
            {
                WorkspaceId = workspaceId,
                DefaultRequestName = string.IsNullOrWhiteSpace(defaultRequestName) ? "Untitled Request" : defaultRequestName,
            });
    }

    public async Task<ForRestScriptExecutionOutcome> Execute(
        AppProfile profile,
        WorkspaceSnapshot workspace,
        string source,
        EnvironmentDefinition? environment,
        string? defaultRequestName = null,
        string? preRequestScriptOverride = null,
        CancellationToken cancellationToken = default)
    {
        var compilation = Compile(source, workspace.Workspace.Id, defaultRequestName);
        if (!compilation.Succeeded || compilation.Payload is null)
        {
            return new()
            {
                Compilation = compilation,
            };
        }

        var initialRuntimeVariables = runtimeVariableSeedEvaluator.Evaluate(compilation.Payload.RuntimeSeeds);
        var request = compilation.Payload.Request with
        {
            PreRequestScript = string.IsNullOrWhiteSpace(preRequestScriptOverride)
                ? compilation.Payload.Request.PreRequestScript
                : preRequestScriptOverride,
        };
        var execution = await requestExecutionService.Execute(
            profile,
            workspace,
            request,
            environment,
            initialRuntimeVariables,
            cancellationToken);

        return new()
        {
            Compilation = compilation,
            Execution = execution,
        };
    }

    #endregion
}
