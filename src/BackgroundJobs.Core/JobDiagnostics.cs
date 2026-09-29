using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BackgroundJobs.Core;

/// <summary>
/// OpenTelemetry integration — plug ActivitySource + Meter into your OTEL pipeline
/// and get traces and metrics for free.
/// </summary>
public static class JobDiagnostics
{
    public const string SourceName = "BackgroundJobs";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long>    JobsEnqueued     = Meter.CreateCounter<long>("background_jobs.enqueued");
    public static readonly Counter<long>    JobsSucceeded    = Meter.CreateCounter<long>("background_jobs.succeeded");
    public static readonly Counter<long>    JobsFailed       = Meter.CreateCounter<long>("background_jobs.failed");
    public static readonly Counter<long>    JobsDeadLettered = Meter.CreateCounter<long>("background_jobs.dead_lettered");
    public static readonly Histogram<double> JobDuration     = Meter.CreateHistogram<double>("background_jobs.duration_ms");
}
