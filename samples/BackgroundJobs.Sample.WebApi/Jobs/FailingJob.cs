namespace BackgroundJobs.Sample.WebApi.Jobs;

/// <summary>
/// Always fails — demonstrates the retry engine and dead-letter flow.
/// Watch it in the /jobs dashboard.
/// </summary>
public sealed class FailingJob(ILogger<FailingJob> logger)
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning("This job always fails — watch it retry 3x then dead-letter in the dashboard!");
        throw new InvalidOperationException("Simulated failure to demonstrate exponential backoff retries.");
    }
}
