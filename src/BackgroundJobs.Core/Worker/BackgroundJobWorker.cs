using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BackgroundJobs.Abstractions.Models;
using BackgroundJobs.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackgroundJobs.Core.Worker;

public class BackgroundJobWorker : BackgroundService
{
    private readonly IJobStorage _storage;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackgroundJobWorker> _logger;

    public BackgroundJobWorker(IJobStorage storage, IServiceProvider serviceProvider, ILogger<BackgroundJobWorker> logger)
    {
        _storage = storage;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background Job Worker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _storage.DequeueAsync("default", stoppingToken);
            if (job != null)
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            else
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(JobData job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing Job {Id} ({TypeName}.{MethodName})", job.Id, job.TypeName, job.MethodName);
        
        using var scope = _serviceProvider.CreateScope();
        
        try
        {
            var type = Type.GetType(job.TypeName) ?? throw new InvalidOperationException($"Type {job.TypeName} not found.");
            var instance = ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, type);
            var method = type.GetMethod(job.MethodName) ?? throw new InvalidOperationException($"Method {job.MethodName} not found on type {type.Name}.");
            
            var parameters = method.GetParameters();
            var arguments = JsonSerializer.Deserialize<JsonElement[]>(job.SerializedArguments);
            
            var invokedArgs = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (arguments != null && i < arguments.Length)
                {
                    invokedArgs[i] = arguments[i].Deserialize(parameters[i].ParameterType);
                }
            }

            var result = method.Invoke(instance, invokedArgs);
            if (result is Task taskResult)
            {
                await taskResult;
            }

            await _storage.CompleteAsync(job.Id, cancellationToken);
            _logger.LogInformation("Job {Id} completed successfully.", job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {Id} failed.", job.Id);
            bool willRetry = job.RetryCount < job.MaxRetries;
            await _storage.FailAsync(job.Id, ex.Message, willRetry, cancellationToken);
        }
    }
}
