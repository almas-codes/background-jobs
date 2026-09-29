using BackgroundJobs.Abstractions;
using BackgroundJobs.Core;
using BackgroundJobs.Storage.InMemory;
using FluentAssertions;
using Xunit;

namespace BackgroundJobs.Tests;

public class InMemoryStorageTests
{
    [Fact]
    public async Task ClaimDueJobs_returns_only_due_jobs()
    {
        var storage = new InMemoryJobStorage();
        await storage.CreateAsync(MakeJob(DateTimeOffset.UtcNow.AddMinutes(10))); // future — must NOT be claimed
        var dueId = await storage.CreateAsync(MakeJob(DateTimeOffset.UtcNow.AddSeconds(-1)));

        var claimed = await storage.ClaimDueJobsAsync(["default"], 10, "server-1", TimeSpan.FromMinutes(5));

        claimed.Should().HaveCount(1);
        claimed[0].Id.Should().Be(dueId);
        claimed[0].Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task ClaimDueJobs_never_returns_same_job_twice()
    {
        var storage = new InMemoryJobStorage();
        await storage.CreateAsync(MakeJob(DateTimeOffset.UtcNow.AddSeconds(-1)));

        var first  = await storage.ClaimDueJobsAsync(["default"], 10, "server-1", TimeSpan.FromMinutes(5));
        var second = await storage.ClaimDueJobsAsync(["default"], 10, "server-2", TimeSpan.FromMinutes(5));

        first.Should().HaveCount(1);
        second.Should().BeEmpty("same job must never be claimed by two workers");
    }

    [Fact]
    public async Task Idempotency_key_deduplicates_enqueue()
    {
        var storage = new InMemoryJobStorage();
        var id1 = await storage.CreateAsync(MakeJob(DateTimeOffset.UtcNow, "key-abc"));
        var id2 = await storage.CreateAsync(MakeJob(DateTimeOffset.UtcNow, "key-abc"));

        id1.Should().Be(id2, "same idempotency key must return the existing job id");
        (await storage.GetStatsAsync()).Scheduled.Should().Be(1);
    }

    [Fact]
    public async Task MoveToDeadLetter_sets_correct_status()
    {
        var storage = new InMemoryJobStorage();
        var id  = await storage.CreateAsync(MakeJob(DateTimeOffset.UtcNow));
        var job = (await storage.GetAsync(id))!;

        await storage.MoveToDeadLetterAsync(job);

        (await storage.GetAsync(id))!.Status.Should().Be(JobStatus.DeadLetter);
    }

    [Fact]
    public async Task Stats_reflect_accurate_counts_after_transitions()
    {
        var storage = new InMemoryJobStorage();
        await storage.CreateAsync(MakeJob(DateTimeOffset.UtcNow));
        await storage.CreateAsync(MakeJob(DateTimeOffset.UtcNow));

        var claimed = await storage.ClaimDueJobsAsync(["default"], 1, "server", TimeSpan.FromMinutes(1));
        claimed[0].Status = JobStatus.Succeeded;
        await storage.UpdateAsync(claimed[0]);

        var stats = await storage.GetStatsAsync();
        stats.Scheduled.Should().Be(1);
        stats.Succeeded.Should().Be(1);
    }

    [Fact]
    public void RetryBackoff_grows_exponentially_and_stays_within_cap()
    {
        var d1  = RetryBackoffCalculator.GetDelay(1);
        var d3  = RetryBackoffCalculator.GetDelay(3);
        var d20 = RetryBackoffCalculator.GetDelay(20);

        d3.Should().BeGreaterThan(d1);
        d20.TotalMinutes.Should().BeLessThanOrEqualTo(14,
            "delay must never exceed 10min cap + 30% jitter");
    }

    [Fact]
    public async Task Priority_high_jobs_are_claimed_before_low_priority()
    {
        var storage = new InMemoryJobStorage();
        var lowId = await storage.CreateAsync(MakeJob(DateTimeOffset.UtcNow.AddSeconds(-2), priority: JobPriority.Low));
        var highId = await storage.CreateAsync(MakeJob(DateTimeOffset.UtcNow.AddSeconds(-1), priority: JobPriority.Critical));

        var claimed = await storage.ClaimDueJobsAsync(["default"], 1, "server", TimeSpan.FromMinutes(1));

        claimed.Should().HaveCount(1);
        claimed[0].Id.Should().Be(highId, "Critical priority job must be dispatched first");
    }

    private static JobRecord MakeJob(DateTimeOffset scheduledAt, string? idempotencyKey = null,
        JobPriority priority = JobPriority.Normal) => new()
    {
        TypeName           = typeof(object).AssemblyQualifiedName!,
        MethodName         = "ToString",
        ParameterTypeNames = [],
        ArgumentsJson      = "[]",
        ScheduledAt        = scheduledAt,
        IdempotencyKey     = idempotencyKey,
        Priority           = priority
    };
}
