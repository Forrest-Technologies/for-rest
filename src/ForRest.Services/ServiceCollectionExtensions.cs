namespace ForRest.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddForRestCore(this IServiceCollection services)
    {
        services.AddSingleton<VariableResolver>();
        services.AddSingleton<JsonEditorService>();
        services.AddSingleton<JsonNodeSelector>();
        services.AddSingleton<RequestCompiler>();
        services.AddSingleton<ResponseExtractionService>();
        services.AddSingleton<ForRestScriptParser>();
        services.AddSingleton<IForRestScriptCompiler, ForRestScriptCompiler>();
        services.AddSingleton<ForRestRuntimeVariableSeedEvaluator>();
        services.AddSingleton<IScriptEngine, RoslynScriptEngine>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IRepeatRunnerService, RepeatRunnerService>();
        services.AddSingleton<IRequestAuthenticationService, RequestAuthenticationService>();
        services.AddSingleton<IRequestExecutionService, RequestExecutionService>();
        services.AddSingleton<IForRestScriptExecutionService, ForRestScriptExecutionService>();

        return services;
    }
}
