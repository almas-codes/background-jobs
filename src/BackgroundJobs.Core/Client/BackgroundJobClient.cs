using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using BackgroundJobs.Abstractions.Client;
using BackgroundJobs.Abstractions.Models;
using BackgroundJobs.Abstractions.Storage;
using BackgroundJobs.Core.ExpressionHelpers;

namespace BackgroundJobs.Core.Client;

public class BackgroundJobClient : IBackgroundJobClient
{
    private readonly IJobStorage _storage;

    public BackgroundJobClient(IJobStorage storage)
    {
        _storage = storage;
    }

    public Task<string> EnqueueAsync<T>(Expression<Func<T, Task>> methodCall, CancellationToken cancellationToken = default)
    {
        return ScheduleAsync(methodCall, TimeSpan.Zero, cancellationToken);
    }

    public Task<string> ScheduleAsync<T>(Expression<Func<T, Task>> methodCall, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return ScheduleAsync(methodCall, DateTimeOffset.UtcNow.Add(delay), cancellationToken);
    }

    public async Task<string> ScheduleAsync<T>(Expression<Func<T, Task>> methodCall, DateTimeOffset processAt, CancellationToken cancellationToken = default)
    {
        var (typeName, methodName, serializedArguments) = MethodCallInspector.Inspect(methodCall);

        var job = new JobData
        {
            TypeName = typeName,
            MethodName = methodName,
            SerializedArguments = serializedArguments,
            ProcessAt = processAt.UtcDateTime,
            Status = processAt <= DateTimeOffset.UtcNow ? JobStatus.Enqueued : JobStatus.Processing // Or a 'Scheduled' status if extended
        };

        return await _storage.EnqueueAsync(job, cancellationToken);
    }
}
