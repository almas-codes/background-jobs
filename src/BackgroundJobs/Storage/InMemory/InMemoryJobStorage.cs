using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackgroundJobs.Abstractions.Models;
using BackgroundJobs.Abstractions.Storage;

namespace BackgroundJobs.Storage.InMemory;

public class InMemoryJobStorage : IJobStorage, IDisposable
{
    private readonly ConcurrentDictionary<string, JobData> _jobs = new();
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly Timer _cleanupTimer;

    public InMemoryJobStorage()
    {
        // Prevent memory leaks: Prune completed jobs every 15 minutes
        _cleanupTimer = new Timer(CleanupCompletedJobs, null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
    }

    private void CleanupCompletedJobs(object? state)
    {
        var threshold = DateTime.UtcNow.AddHours(-1);
        var toRemove = _jobs.Values
            .Where(j => (j.Status == JobStatus.Completed || j.Status == JobStatus.Failed) && j.CreatedAt < threshold)
            .Select(j => j.Id)
            .ToList();

        foreach (var id in toRemove)
        {
            _jobs.TryRemove(id, out _);
        }
    }

    public Task<string> EnqueueAsync(JobData job, CancellationToken cancellationToken = default)
    {
        _jobs[job.Id] = job;
        if (job.ProcessAt == null || job.ProcessAt <= DateTime.UtcNow)
        {
            _queue.Enqueue(job.Id);
        }
        else
        {
            // Lightweight scheduled jobs implementation
            Task.Run(async () =>
            {
                var delay = job.ProcessAt.Value - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
                _queue.Enqueue(job.Id);
            }, cancellationToken);
        }

        return Task.FromResult(job.Id);
    }

    public Task<JobData?> DequeueAsync(string queue, CancellationToken cancellationToken = default)
    {
        if (_queue.TryDequeue(out var id) && _jobs.TryGetValue(id, out var job))
        {
            job.Status = JobStatus.Processing;
            return Task.FromResult<JobData?>(job);
        }
        return Task.FromResult<JobData?>(null);
    }

    public Task CompleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            job.Status = JobStatus.Completed;
        }
        return Task.CompletedTask;
    }

    public Task FailAsync(string id, string error, bool willRetry, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            job.LastError = error;
            if (willRetry)
            {
                job.RetryCount++;
                job.Status = JobStatus.Enqueued;
                // Simple exponential backoff
                Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, job.RetryCount)), cancellationToken);
                    _queue.Enqueue(id);
                }, cancellationToken);
            }
            else
            {
                job.Status = JobStatus.Failed;
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JobData>> GetJobsAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        var result = _jobs.Values
            .OrderByDescending(x => x.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToList();
            
        return Task.FromResult<IReadOnlyList<JobData>>(result);
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_jobs.Count);
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
