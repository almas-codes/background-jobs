namespace BackgroundJobs.Sample.WebApi.Jobs;

public sealed class DailyReportJob(ILogger<DailyReportJob> logger)
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Generating daily report at {Time}", DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}
