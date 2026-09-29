using BackgroundJobs.Storage.PostgreSQL.Provider;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs;

public static class PostgreSqlBackgroundJobsOptionsExtensions
{
    public static void UsePostgreSqlStorage(this BackgroundJobsOptions options, string connectionString)
    {
        options.HasStorageConfigured = true;
        options.Services.AddSingleton<Abstractions.Storage.IJobStorage>(new PostgreSqlJobStorage(connectionString));
    }
}
