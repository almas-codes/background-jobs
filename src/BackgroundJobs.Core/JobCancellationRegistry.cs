using System.Collections.Concurrent;

namespace BackgroundJobs.Core;

/// <summary>
/// Bridges IJobClient.Cancel() with a currently-running job's CancellationTokenSource.
/// Shared singleton so the client and the engine can communicate without coupling.
/// </summary>
public sealed class JobCancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();

    public void Register(string jobId, CancellationTokenSource cts)   => _running[jobId] = cts;
    public void Unregister(string jobId)                               => _running.TryRemove(jobId, out _);

    public bool TryCancel(string jobId)
    {
        if (_running.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }
}
