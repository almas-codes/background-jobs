using System;
using System.Collections.Generic;

namespace BackgroundJobs.Abstractions.Models;

public enum JobStatus
{
    Enqueued,
    Processing,
    Completed,
    Failed,
    Cancelled
}

public class JobData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Queue { get; set; } = "default";
    public string TypeName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public string SerializedArguments { get; set; } = "[]";
    
    public JobStatus Status { get; set; } = JobStatus.Enqueued;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessAt { get; set; }
    
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    
    public string? LastError { get; set; }
}

public class JobExecutionResult
{
    public bool Success { get; set; }
    public Exception? Exception { get; set; }
    public bool ShouldRetry => !Success && Exception != null;
    
    public static JobExecutionResult Successful() => new() { Success = true };
    public static JobExecutionResult Failure(Exception ex) => new() { Success = false, Exception = ex };
}
