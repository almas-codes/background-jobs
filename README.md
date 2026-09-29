# BackgroundJobs.NET

A Hangfire-alternative built for modern .NET 10. No bloat, no lock-in, just a clean job engine that gets out of your way.

I got tired of Hangfire's SQL Server lock-in and the XML config nightmare in Quartz.NET. So I built this — a strongly-typed, provider-agnostic background job system that you can drop into any ASP.NET Core app and be running jobs in under five minutes.

## What you get

| Feature | Details |
|---|---|
| Fire-and-forget jobs | `jobs.Enqueue<T>(x => x.DoWork())` |
| Delayed jobs | `delay: TimeSpan.FromMinutes(5)` |
| Cron recurring jobs | Full cron syntax via Cronos |
| Retry with exponential backoff + jitter | Auto-configured, never storms |
| Cooperative cancellation | Jobs observe `CancellationToken`, cancel cleanly |
| Priority queues | Low / Normal / High / Critical |
| Per-queue concurrency limits | `options.QueueConcurrencyLimits["critical"] = 2` |
| Idempotency keys | Safe for webhook retries |
| Dead-letter queue | Permanently failed jobs saved for inspection |
| Built-in real-time dashboard | No extra package — open `/jobs` |
| OpenTelemetry traces + metrics | Wire into Prometheus/Grafana with two lines |
| Pluggable storage | InMemory → PostgreSQL → SQL Server / MySQL / Redis |

## Solution layout

```
BackgroundJobs.sln
Directory.Build.props              (.NET 10, nullable, analyzers)
src/
  BackgroundJobs.Abstractions/     interfaces + models — zero dependencies
  BackgroundJobs.Core/             engine, worker, client, DI extensions
  BackgroundJobs.Storage.InMemory/ thread-safe in-memory provider
  BackgroundJobs.Storage.PostgreSql/ FOR UPDATE SKIP LOCKED, raw Npgsql
  BackgroundJobs.Dashboard/        Minimal API endpoints + inline HTML
samples/
  BackgroundJobs.Sample.WebApi/    runnable demo — clone and F5
tests/
  BackgroundJobs.Tests/            xUnit + FluentAssertions (7 tests, all green)
```

## Quick start — clone and run

```bash
git clone https://github.com/almas-codes/background-jobs
cd background-jobs/samples/BackgroundJobs.Sample.WebApi
dotnet run
```

Open **http://localhost:5000/jobs** — live dashboard, auto-refreshing every 2 seconds.

Then try:

```bash
# Queue a welcome email with a 5-second delay (idempotent)
curl -X POST http://localhost:5000/users/alice/welcome-email

# High-priority instant email
curl -X POST http://localhost:5000/users/alice/instant-email

# Schedule a daily report at 06:00 UTC
curl -X POST http://localhost:5000/reports/daily

# Watch retry → dead-letter in action
curl -X POST http://localhost:5000/demo/failing-job
```

## Setup in your own app

```csharp
// Program.cs
builder.Services.AddBackgroundJobs(jobs =>
{
    jobs.UseInMemoryStorage(); // or .UsePostgreSql("...")

    jobs.Configure(options =>
    {
        options.WorkerCount   = 8;
        options.Queues        = ["default", "emails", "critical"];
        options.QueueConcurrencyLimits["critical"] = 2;
    });
});

// Dashboard (optional)
app.MapBackgroundJobsDashboard("/jobs");
```

## How to add a new job

A job is a plain C# class with an `async Task` method. No base class. No interface. No attributes.

**Step 1 — create the class:**

```csharp
public sealed class ResizeImageJob(ILogger<ResizeImageJob> logger, IImageService images)
{
    public async Task ExecuteAsync(string imageId, int width, CancellationToken cancellationToken)
    {
        logger.LogInformation("Resizing {ImageId}...", imageId);
        await images.ResizeAsync(imageId, width, cancellationToken);
    }
}
```

**Step 2 — register it:**

```csharp
builder.Services.AddScoped<ResizeImageJob>();
builder.Services.AddScoped<IImageService, ImageService>();
```

**Step 3 — enqueue from anywhere:**

```csharp
// Fire-and-forget
jobs.Enqueue<ResizeImageJob>(x => x.ExecuteAsync(imageId, 800, CancellationToken.None));

// Delayed
jobs.Enqueue<ResizeImageJob>(
    x => x.ExecuteAsync(imageId, 800, CancellationToken.None),
    delay: TimeSpan.FromSeconds(10));

// Scheduled to a precise timestamp
await jobs.EnqueueAsync<ResizeImageJob>(
    x => x.ExecuteAsync(imageId, 800, CancellationToken.None),
    enqueueAt: DateTimeOffset.UtcNow.AddHours(2));

// With priority + idempotency key
jobs.Enqueue<ResizeImageJob>(
    x => x.ExecuteAsync(imageId, 800, CancellationToken.None),
    priority: JobPriority.High,
    idempotencyKey: $"resize-{imageId}");
```

**Step 4 — recurring jobs (optional):**

```csharp
// Register once at startup — updates automatically if cron changes
recurring.AddOrUpdate<ResizeImageJob>(
    "nightly-cleanup",
    x => x.ExecuteAsync("all", 150, CancellationToken.None),
    cronExpression: "0 2 * * *",  // every night at 02:00 UTC
    timeZoneId: "UTC");
```

## Switching to PostgreSQL

```bash
# 1. Run the schema (once)
psql "$CONNECTION_STRING" -f src/BackgroundJobs.Storage.PostgreSql/schema.sql
```

```csharp
// 2. One-line swap in Program.cs
jobs.UsePostgreSql(builder.Configuration.GetConnectionString("Postgres")!);
```

The PostgreSQL provider uses `FOR UPDATE SKIP LOCKED` — completely safe for any number of horizontally scaled instances with zero duplicate execution and no deadlocks.

## Writing a custom storage provider

Implement `IJobStorage` from `BackgroundJobs.Abstractions`. There are 12 methods. The only constraint that matters is `ClaimDueJobsAsync` — it **must** be atomic. Everything else — retry logic, worker concurrency, dashboard, DI — stays untouched.

```csharp
public static BackgroundJobsBuilder UseRedis(this BackgroundJobsBuilder builder, string connectionString)
{
    builder.Services.AddSingleton<IJobStorage>(new RedisJobStorage(connectionString));
    return builder;
}
```

## OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(JobDiagnostics.SourceName))
    .WithMetrics(m => m.AddMeter(JobDiagnostics.SourceName));
```

Metrics: `background_jobs.enqueued`, `.succeeded`, `.failed`, `.dead_lettered`, `.duration_ms`.

## Production checklist

- **Graceful shutdown** — the `BackgroundService` stops cleanly; in-flight jobs are re-claimed on restart via lease expiry.
- **Dashboard security** — add `.RequireAuthorization()` to the dashboard group in production.
- **Dead-letter alerting** — alert on the `background_jobs.dead_lettered` metric in Grafana.
- **Health check** — call `IJobStorage.GetStatsAsync()` inside a standard `IHealthCheck`.

---

*Built by Almas Khan.*