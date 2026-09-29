using System.Collections.Concurrent;
using System.Diagnostics;
using BackgroundJobs.Abstractions;
using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackgroundJobs.Core;

/// <summary>
/// The engine. Runs as a hosted service, polls storage for due jobs, dispatches them
/// with concurrency-aware semaphores, handles retry/backoff/dead-letter, and fires
/// recurring jobs on their cron schedules.
/// </summary>
public sealed class BackgroundJobServerHostedService(
    IJobStorage storage,
    JobActivator activator,
    JobCancellationRegistry cancellations,
    IOptions<BackgroundJobServerOptions> options,
    ILogger<BackgroundJobServerHostedService> logger) : BackgroundService
{
    private readonly BackgroundJobServerOptions _opts = options.Value;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _queueLimiters = new();
    private SemaphoreSlim? _globalLimiter;
    private SemaphoreSlim GlobalLimiter => _globalLimiter ??= new SemaphoreSlim(_opts.WorkerCount);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "BackgroundJobs server {ServerId} started — {Workers} workers, queues: [{Queues}]",
            _opts.ServerId, _opts.WorkerCount, string.Join(", ", _opts.Queues));

        using var timer = new PeriodicTimer(_opts.PollingInterval);
        do
        {
            try
            {
                await DispatchDueJobsAsync(stoppingToken);
                await ProcessRecurringJobsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled error in BackgroundJobs main loop");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchDueJobsAsync(CancellationToken stoppingToken)
    {
        if (GlobalLimiter.CurrentCount == 0) return;

        var claimed = await storage.ClaimDueJobsAsync(
            _opts.Queues, _opts.BatchSize, _opts.ServerId, _opts.LeaseDuration, stoppingToken);

        foreach (var job in claimed.OrderByDescending(j => j.Priority).ThenBy(j => j.ScheduledAt))
        {
            var limiter = GetLimiterFor(job.Queue);
            if (!await GlobalLimiter.WaitAsync(0, stoppingToken)) break;
            if (!await limiter.WaitAsync(0, stoppingToken))
            {
                GlobalLimiter.Release();
                continue;
            }
            _ = ExecuteJobAsync(job, limiter, stoppingToken);
        }
    }

    private SemaphoreSlim GetLimiterFor(string queue) => _queueLimiters.GetOrAdd(queue, q =>
        new SemaphoreSlim(_opts.QueueConcurrencyLimits.GetValueOrDefault(q, _opts.WorkerCount)));

    private async Task ExecuteJobAsync(JobRecord job, SemaphoreSlim queueLimiter, CancellationToken serverStopping)
    {
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(serverStopping);
        cancellations.Register(job.Id, jobCts);

        job.Status    = JobStatus.Processing;
        job.StartedAt = DateTimeOffset.UtcNow;
        await storage.UpdateAsync(job, CancellationToken.None);

        using var activity = JobDiagnostics.ActivitySource.StartActivity("job.execute");
        activity?.SetTag("job.id",    job.Id);
        activity?.SetTag("job.type",  job.TypeName);
        activity?.SetTag("job.queue", job.Queue);

        var sw = Stopwatch.StartNew();
        try
        {
            await activator.ExecuteAsync(job, jobCts.Token);

            job.Status      = JobStatus.Succeeded;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await storage.UpdateAsync(job, CancellationToken.None);
            JobDiagnostics.JobsSucceeded.Add(1);
            logger.LogInformation("Job {JobId} succeeded in {Ms}ms", job.Id, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested && !serverStopping.IsCancellationRequested)
        {
            job.Status    = JobStatus.Deleted;
            job.LastError = "Cancelled by request";
            await storage.UpdateAsync(job, CancellationToken.None);
            logger.LogInformation("Job {JobId} cancelled", job.Id);
        }
        catch (Exception ex)
        {
            job.RetryCount++;
            job.LastError = ex.ToString();
            JobDiagnostics.JobsFailed.Add(1);

            if (job.RetryCount > job.MaxRetries)
            {
                job.Status = JobStatus.DeadLetter;
                await storage.MoveToDeadLetterAsync(job, CancellationToken.None);
                JobDiagnostics.JobsDeadLettered.Add(1);
                logger.LogError(ex, "Job {JobId} moved to DEAD-LETTER after {Retries} attempts", job.Id, job.RetryCount);
            }
            else
            {
                job.Status      = JobStatus.AwaitingRetry;
                job.ScheduledAt = DateTimeOffset.UtcNow + RetryBackoffCalculator.GetDelay(job.RetryCount);
                await storage.UpdateAsync(job, CancellationToken.None);
                logger.LogWarning(ex, "Job {JobId} failed (attempt {Retry}/{Max}), retrying at {At}",
                    job.Id, job.RetryCount, job.MaxRetries, job.ScheduledAt);
            }
        }
        finally
        {
            sw.Stop();
            JobDiagnostics.JobDuration.Record(sw.Elapsed.TotalMilliseconds);
            cancellations.Unregister(job.Id);
            queueLimiter.Release();
            GlobalLimiter.Release();
        }
    }

    private async Task ProcessRecurringJobsAsync(CancellationToken stoppingToken)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await storage.GetDueRecurringJobsAsync(now, stoppingToken);

        foreach (var def in due)
        {
            var cron = CronExpression.Parse(def.CronExpression, CronFormat.Standard);
            var tz   = TimeZoneInfo.FindSystemTimeZoneById(def.TimeZoneId);
            var next = cron.GetNextOccurrence(now, tz) ?? now.AddMinutes(1);

            await storage.CreateAsync(new JobRecord
            {
                TypeName           = def.TypeName,
                MethodName         = def.MethodName,
                ParameterTypeNames = def.ParameterTypeNames,
                ArgumentsJson      = def.ArgumentsJson,
                Queue              = def.Queue,
                RecurringJobId     = def.Id,
                ScheduledAt        = now
            }, stoppingToken);

            await storage.UpdateRecurringJobRunAsync(def.Id, now, next, stoppingToken);
            logger.LogInformation("Recurring job {JobId} triggered, next at {Next}", def.Id, next);
        }
    }
}
