using ForRest.Repositories;

namespace ForRest.Infrastructure.Sqlite;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddForRestSqlite(this IServiceCollection services)
    {
        services.AddSingleton<SqliteAppDatabase>();
        services.AddSingleton<IWorkspaceRepository, SqliteWorkspaceRepository>();
        services.AddSingleton<IExecutionHistoryRepository, SqliteExecutionHistoryRepository>();

        return services;
    }
}
