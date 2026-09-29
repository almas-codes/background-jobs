using System;
using BackgroundJobs.Abstractions.Client;
using BackgroundJobs.Abstractions.Storage;
using BackgroundJobs.Core.Client;
using BackgroundJobs.Core.Worker;
using BackgroundJobs.Storage.InMemory.Provider;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs;

public static class BackgroundJobsServiceCollectionExtensions
{
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, Action<BackgroundJobsOptions>? configure = null)
    {
        var options = new BackgroundJobsOptions { Services = services };
        configure?.Invoke(options);
        
        services.AddSingleton<IBackgroundJobClient, BackgroundJobClient>();
        
        if (!options.HasStorageConfigured)
        {
            services.AddSingleton<IJobStorage, InMemoryJobStorage>();
        }
        
        services.AddHostedService<BackgroundJobWorker>();
        
        return services;
    }
}

public class BackgroundJobsOptions
{
    public bool HasStorageConfigured { get; set; }
    public IServiceCollection Services { get; set; } = null!;
}

public static class InMemoryBackgroundJobsOptionsExtensions
{
    public static void UseInMemoryStorage(this BackgroundJobsOptions options)
    {
        options.HasStorageConfigured = true;
        options.Services.AddSingleton<IJobStorage, InMemoryJobStorage>();
    }
}
