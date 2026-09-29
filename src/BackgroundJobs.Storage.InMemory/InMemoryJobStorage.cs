using System.Collections.Concurrent;
using BackgroundJobs.Abstractions;
using BackgroundJobs.Core;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs.Storage.InMemory;

/// <summary>
/// Thread-safe in-memory storage. Perfect for development and testing.
/// For production use PostgreSQL or SQL Server.
/// </summary>
public sealed class InMemoryJobStorage : IJobStorage
{
    private readonly ConcurrentDictionary<string, JobRecord> _jobs      = new();
    private readonly ConcurrentDictionary<string, RecurringJobDefinition> _recurring = new();
    private readonly object _claimLock = new();

    public Task<string> CreateAsync(JobRecord job, CancellationToken ct = default)
    {
        if (job.IdempotencyKey is not null)
        {
            var existing = _jobs.Values.FirstOrDefault(
                j => j.IdempotencyKey == job.IdempotencyKey && j.Status != JobStatus.DeadLetter);
            if (existing is not null)
                return Task.FromResult(existing.Id);
        }
        _jobs[job.Id] = job;
        return Task.FromResult(job.Id);
    }

    public Task<JobRecord?> GetAsync(string jobId, CancellationToken ct = default)
        => Task.FromResult(_jobs.GetValueOrDefault(jobId));

    public Task<IReadOnlyList<JobRecord>> ClaimDueJobsAsync(
        string[] queues, int batchSize, string serverId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        // Lock ensures no two concurrent pollers claim the same job (mirrors SKIP LOCKED semantics)
        var now = DateTimeOffset.UtcNow;
        lock (_claimLock)
        {
            var due = _jobs.Values
                .Where(j => queues.Contains(j.Queue)
                         && j.Status is JobStatus.Scheduled or JobStatus.AwaitingRetry
                         && j.ScheduledAt <= now)
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.ScheduledAt)
                .Take(batchSize)
                .ToList();

            foreach (var job in due)
            {
                job.Status         = JobStatus.Processing;
                job.ClaimedBy      = serverId;
                job.LeaseExpiresAt = now + leaseDuration;
            }

            return Task.FromResult<IReadOnlyList<JobRecord>>(due);
        }
    }

    public Task UpdateAsync(JobRecord job, CancellationToken ct = default)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string jobId, CancellationToken ct = default)
        => Task.FromResult(_jobs.TryRemove(jobId, out _));

    public Task MoveToDeadLetterAsync(JobRecord job, CancellationToken ct = default)
    {
        job.Status  = JobStatus.DeadLetter;
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task UpsertRecurringJobAsync(RecurringJobDefinition definition, CancellationToken ct = default)
    {
        _recurring[definition.Id] = definition;
        return Task.CompletedTask;
    }

    public Task RemoveRecurringJobAsync(string recurringJobId, CancellationToken ct = default)
    {
        _recurring.TryRemove(recurringJobId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecurringJobDefinition>> GetDueRecurringJobsAsync(DateTimeOffset now, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RecurringJobDefinition>>(
            _recurring.Values.Where(r => r.NextRunAt is null || r.NextRunAt <= now).ToList());

    public Task UpdateRecurringJobRunAsync(string recurringJobId, DateTimeOffset lastRun, DateTimeOffset nextRun, CancellationToken ct = default)
    {
        if (_recurring.TryGetValue(recurringJobId, out var def))
        {
            def.LastRunAt = lastRun;
            def.NextRunAt = nextRun;
        }
        return Task.CompletedTask;
    }

    public Task<JobStorageStats> GetStatsAsync(CancellationToken ct = default)
    {
        var g = _jobs.Values.GroupBy(j => j.Status).ToDictionary(x => x.Key, x => x.Count());
        return Task.FromResult(new JobStorageStats(
            g.GetValueOrDefault(JobStatus.Scheduled),
            g.GetValueOrDefault(JobStatus.Processing),
            g.GetValueOrDefault(JobStatus.Succeeded),
            g.GetValueOrDefault(JobStatus.Failed),
            g.GetValueOrDefault(JobStatus.AwaitingRetry),
            g.GetValueOrDefault(JobStatus.DeadLetter)));
    }

    public Task<IReadOnlyList<JobRecord>> GetJobsAsync(JobStatus? status, int skip, int take, CancellationToken ct = default)
    {
        var query = _jobs.Values.AsEnumerable();
        if (status.HasValue) query = query.Where(j => j.Status == status);
        return Task.FromResult<IReadOnlyList<JobRecord>>(
            query.OrderByDescending(j => j.CreatedAt).Skip(skip).Take(take == 0 ? 50 : take).ToList());
    }
}

public static class InMemoryStorageExtensions
{
    public static BackgroundJobsBuilder UseInMemoryStorage(this BackgroundJobsBuilder builder)
    {
        builder.Services.AddSingleton<IJobStorage, InMemoryJobStorage>();
        return builder;
    }
}
