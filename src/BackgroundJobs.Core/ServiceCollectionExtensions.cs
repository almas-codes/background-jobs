using BackgroundJobs.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BackgroundJobs.Core;

public sealed class BackgroundJobsBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    public BackgroundJobsBuilder Configure(Action<BackgroundJobServerOptions> configure)
    {
        Services.Configure(configure);
        return this;
    }
}

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BackgroundJobs engine. Call a storage extension (UseInMemoryStorage,
    /// UsePostgreSql, etc.) inside the <paramref name="configure"/> callback.
    /// </summary>
    public static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services, Action<BackgroundJobsBuilder>? configure = null)
    {
        services.AddOptions<BackgroundJobServerOptions>();
        services.TryAddSingleton<JobActivator>();
        services.TryAddSingleton<JobCancellationRegistry>();
        services.TryAddSingleton<IJobClient, BackgroundJobClient>();
        services.TryAddSingleton<IRecurringJobManager, RecurringJobManager>();
        services.AddHostedService<BackgroundJobServerHostedService>();

        configure?.Invoke(new BackgroundJobsBuilder(services));

        // Guard: blow up early if no storage provider was registered.
        services.TryAddSingleton<IJobStorage>(_ =>
            throw new InvalidOperationException(
                "No job storage configured. Call .UseInMemoryStorage() or .UsePostgreSql() " +
                "inside AddBackgroundJobs(builder => ...)."));

        return services;
    }
}
