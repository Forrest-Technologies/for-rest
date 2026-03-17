namespace ForRest.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddForRestCore(this IServiceCollection services)
    {
        services.AddSingleton<VariableResolver>();
        services.AddSingleton<JsonEditorService>();
        services.AddSingleton<RequestCompiler>();
        services.AddSingleton<ResponseExtractionService>();
        services.AddSingleton<IScriptEngine, RoslynScriptEngine>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IRepeatRunnerService, RepeatRunnerService>();
        services.AddSingleton<IRequestExecutionService, RequestExecutionService>();

        return services;
    }
}
