namespace BackgroundJobs.Core;

/// <summary>
/// Exponential backoff with jitter. Grows as 2^attempt seconds, capped at 10 minutes.
/// Jitter (±30%) prevents retry storms across multiple failed workers.
/// </summary>
public static class RetryBackoffCalculator
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(10);

    public static TimeSpan GetDelay(int attempt)
    {
        var seconds = Math.Min(Math.Pow(2, attempt), MaxDelay.TotalSeconds);
        var jitter   = Random.Shared.NextDouble() * 0.3 * seconds;
        return TimeSpan.FromSeconds(seconds + jitter);
    }
}
