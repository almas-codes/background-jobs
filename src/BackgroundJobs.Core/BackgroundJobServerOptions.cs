namespace BackgroundJobs.Core;

public sealed class BackgroundJobServerOptions
{
    public string[] Queues { get; set; } = ["default"];
    public int WorkerCount { get; set; } = Environment.ProcessorCount * 2;
    public Dictionary<string, int> QueueConcurrencyLimits { get; set; } = new();
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
    public int BatchSize { get; set; } = 50;
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    // Unique identifier for this server instance — used for lease ownership
    public string ServerId { get; set; } =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"[..48];
}
