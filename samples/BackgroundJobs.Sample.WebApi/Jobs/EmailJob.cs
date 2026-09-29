namespace BackgroundJobs.Sample.WebApi.Jobs;

public sealed class EmailJob(ILogger<EmailJob> logger)
{
    public async Task ExecuteAsync(string userId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Sending welcome email to user {UserId}...", userId);
        await Task.Delay(1500, cancellationToken);
        logger.LogInformation("Email sent to user {UserId}", userId);
    }
}
