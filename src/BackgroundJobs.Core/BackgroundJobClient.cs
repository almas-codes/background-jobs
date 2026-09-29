using System.Linq.Expressions;
using BackgroundJobs.Abstractions;

namespace BackgroundJobs.Core;

public sealed class BackgroundJobClient(IJobStorage storage, JobCancellationRegistry cancellations) : IJobClient
{
    public string Enqueue<T>(Expression<Func<T, Task>> methodCall, TimeSpan? delay = null,
        DateTimeOffset? enqueueAt = null, JobPriority priority = JobPriority.Normal,
        string queue = "default", int? maxRetries = null, string? idempotencyKey = null)
        => EnqueueAsync(methodCall, delay, enqueueAt, priority, queue, maxRetries, idempotencyKey)
            .GetAwaiter().GetResult();

    public async Task<string> EnqueueAsync<T>(Expression<Func<T, Task>> methodCall, TimeSpan? delay = null,
        DateTimeOffset? enqueueAt = null, JobPriority priority = JobPriority.Normal,
        string queue = "default", int? maxRetries = null, string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        var inv         = CallExpressionSerializer.Serialize(methodCall);
        var scheduledAt = enqueueAt ?? (delay.HasValue ? DateTimeOffset.UtcNow + delay.Value : DateTimeOffset.UtcNow);

        var job = new JobRecord
        {
            TypeName           = inv.TypeName,
            MethodName         = inv.MethodName,
            ParameterTypeNames = inv.ParameterTypeNames,
            ArgumentsJson      = inv.ArgumentsJson,
            Queue              = queue,
            Priority           = priority,
            ScheduledAt        = scheduledAt,
            MaxRetries         = maxRetries ?? 3,
            IdempotencyKey     = idempotencyKey
        };

        var id = await storage.CreateAsync(job, ct).ConfigureAwait(false);
        JobDiagnostics.JobsEnqueued.Add(1, new KeyValuePair<string, object?>("queue", queue));
        return id;
    }

    public bool Delete(string jobId) => storage.DeleteAsync(jobId).GetAwaiter().GetResult();

    public bool Requeue(string jobId)
    {
        var job = storage.GetAsync(jobId).GetAwaiter().GetResult();
        if (job is null) return false;
        job.Status      = JobStatus.Scheduled;
        job.ScheduledAt = DateTimeOffset.UtcNow;
        job.RetryCount  = 0;
        storage.UpdateAsync(job).GetAwaiter().GetResult();
        return true;
    }

    public bool Cancel(string jobId) => cancellations.TryCancel(jobId);
}
