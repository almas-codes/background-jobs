using System.Text.Json;
using BackgroundJobs.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs.Core;

/// <summary>
/// Resolves the job class from DI and invokes its method with deserialized arguments.
/// CancellationToken parameters are injected at runtime, not from the serialized payload.
/// </summary>
public sealed class JobActivator(IServiceScopeFactory scopeFactory)
{
    public async Task ExecuteAsync(JobRecord job, CancellationToken cancellationToken)
    {
        var type     = Type.GetType(job.TypeName, throwOnError: true)!;
        using var scope = scopeFactory.CreateScope();
        var instance = ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, type);

        var paramTypes = job.ParameterTypeNames
            .Select(n => Type.GetType(n, throwOnError: true)!)
            .ToArray();

        var method = type.GetMethod(job.MethodName, paramTypes)
            ?? throw new InvalidOperationException($"Method '{job.MethodName}' not found on '{type.Name}'.");

        using var doc    = JsonDocument.Parse(job.ArgumentsJson);
        var rawArgs      = doc.RootElement.EnumerateArray().ToArray();
        var invokedArgs  = new object?[paramTypes.Length];

        for (var i = 0; i < paramTypes.Length; i++)
        {
            invokedArgs[i] = paramTypes[i] == typeof(CancellationToken)
                ? cancellationToken
                : rawArgs[i].ValueKind == JsonValueKind.Null
                    ? null
                    : JsonSerializer.Deserialize(rawArgs[i].GetRawText(), paramTypes[i]);
        }

        var result = method.Invoke(instance, invokedArgs);
        if (result is Task task) await task.ConfigureAwait(false);
    }
}
