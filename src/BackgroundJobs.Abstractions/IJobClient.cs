using System.Linq.Expressions;

namespace BackgroundJobs.Abstractions;

public interface IJobClient
{
    /// <summary>Enqueue a fire-and-forget or delayed job synchronously.</summary>
    string Enqueue<T>(
        Expression<Func<T, Task>> methodCall,
        TimeSpan? delay = null,
        DateTimeOffset? enqueueAt = null,
        JobPriority priority = JobPriority.Normal,
        string queue = "default",
        int? maxRetries = null,
        string? idempotencyKey = null);

    /// <summary>Enqueue a job asynchronously.</summary>
    Task<string> EnqueueAsync<T>(
        Expression<Func<T, Task>> methodCall,
        TimeSpan? delay = null,
        DateTimeOffset? enqueueAt = null,
        JobPriority priority = JobPriority.Normal,
        string queue = "default",
        int? maxRetries = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);

    bool Delete(string jobId);
    bool Requeue(string jobId);

    /// <summary>Cooperatively cancels a running job — job must observe CancellationToken.</summary>
    bool Cancel(string jobId);
}

public interface IRecurringJobManager
{
    void AddOrUpdate<T>(
        string recurringJobId,
        Expression<Func<T, Task>> methodCall,
        string cronExpression,
        string timeZoneId = "UTC",
        string queue = "default");

    void RemoveIfExists(string recurringJobId);
}
