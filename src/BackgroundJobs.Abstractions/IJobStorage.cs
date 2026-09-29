namespace BackgroundJobs.Abstractions;

/// <summary>
/// The only contract a persistence provider needs to implement.
/// All claim operations MUST be atomic and guarantee no two workers receive the same job.
/// </summary>
public interface IJobStorage
{
    Task<string> CreateAsync(JobRecord job, CancellationToken ct = default);
    Task<JobRecord?> GetAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> due jobs.
    /// PostgreSQL: use FOR UPDATE SKIP LOCKED. SQL Server: use READPAST+ROWLOCK. Redis: Lua script.
    /// </summary>
    Task<IReadOnlyList<JobRecord>> ClaimDueJobsAsync(
        string[] queues, int batchSize, string serverId,
        TimeSpan leaseDuration, CancellationToken ct = default);

    Task UpdateAsync(JobRecord job, CancellationToken ct = default);
    Task<bool> DeleteAsync(string jobId, CancellationToken ct = default);
    Task MoveToDeadLetterAsync(JobRecord job, CancellationToken ct = default);

    Task UpsertRecurringJobAsync(RecurringJobDefinition definition, CancellationToken ct = default);
    Task RemoveRecurringJobAsync(string recurringJobId, CancellationToken ct = default);
    Task<IReadOnlyList<RecurringJobDefinition>> GetDueRecurringJobsAsync(DateTimeOffset now, CancellationToken ct = default);
    Task UpdateRecurringJobRunAsync(string recurringJobId, DateTimeOffset lastRun, DateTimeOffset nextRun, CancellationToken ct = default);

    Task<JobStorageStats> GetStatsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JobRecord>> GetJobsAsync(JobStatus? status, int skip, int take, CancellationToken ct = default);
}
