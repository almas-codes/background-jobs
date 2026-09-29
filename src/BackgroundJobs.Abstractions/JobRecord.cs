namespace BackgroundJobs.Abstractions;

public sealed class JobRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string TypeName { get; init; }
    public required string MethodName { get; init; }
    public required string[] ParameterTypeNames { get; init; }
    public required string ArgumentsJson { get; init; }

    public string Queue { get; init; } = "default";
    public JobPriority Priority { get; init; } = JobPriority.Normal;
    public JobStatus Status { get; set; } = JobStatus.Scheduled;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ScheduledAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public int RetryCount { get; set; }
    public int MaxRetries { get; init; } = 3;
    public string? LastError { get; set; }

    public string? RecurringJobId { get; init; }
    public string? IdempotencyKey { get; init; }
    public string? ClaimedBy { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
}

public sealed record JobInvocationData(
    string TypeName,
    string MethodName,
    string[] ParameterTypeNames,
    string ArgumentsJson);

public sealed class RecurringJobDefinition
{
    public required string Id { get; init; }
    public required string TypeName { get; init; }
    public required string MethodName { get; init; }
    public required string[] ParameterTypeNames { get; init; }
    public required string ArgumentsJson { get; init; }
    public required string CronExpression { get; init; }
    public string TimeZoneId { get; init; } = "UTC";
    public string Queue { get; init; } = "default";
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
}

public sealed record JobStorageStats(
    int Scheduled, int Processing, int Succeeded,
    int Failed, int AwaitingRetry, int DeadLetter);
