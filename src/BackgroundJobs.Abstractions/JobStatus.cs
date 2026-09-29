namespace BackgroundJobs.Abstractions;

public enum JobStatus
{
    Scheduled    = 0,
    Processing   = 1,
    Succeeded    = 2,
    Failed       = 3,
    AwaitingRetry = 4,
    Deleted      = 5,
    DeadLetter   = 6
}

public enum JobPriority
{
    Low      = 0,
    Normal   = 1,
    High     = 2,
    Critical = 3
}
