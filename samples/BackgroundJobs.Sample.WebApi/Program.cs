using BackgroundJobs.Abstractions;
using BackgroundJobs.Core;
using BackgroundJobs.Dashboard;
using BackgroundJobs.Storage.InMemory;
using BackgroundJobs.Sample.WebApi.Jobs;

var builder = WebApplication.CreateBuilder(args);

// ─── BackgroundJobs engine ────────────────────────────────────────────────────
builder.Services.AddBackgroundJobs(jobs =>
{
    // Zero-config InMemory default — clone the repo, hit F5, it just works.
    jobs.UseInMemoryStorage();

    // For production, run schema.sql and swap to:
    // jobs.UsePostgreSql(builder.Configuration.GetConnectionString("Postgres")!);

    jobs.Configure(options =>
    {
        options.WorkerCount   = 8;
        options.Queues        = ["default", "emails", "critical"];
        options.QueueConcurrencyLimits["critical"] = 2;
        options.PollingInterval = TimeSpan.FromSeconds(1);
    });
});

// ─── Job implementations ──────────────────────────────────────────────────────
builder.Services.AddScoped<EmailJob>();
builder.Services.AddScoped<DailyReportJob>();
builder.Services.AddScoped<FailingJob>();

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();

// ─── Dashboard  ───────────────────────────────────────────────────────────────
app.MapBackgroundJobsDashboard("/jobs");

// ─── API endpoints ────────────────────────────────────────────────────────────
app.MapGet("/", () => "BackgroundJobs sample — Dashboard: /jobs | OpenAPI: /openapi/v1.json")
   .ExcludeFromDescription();

// Delayed welcome email (idempotent — duplicate requests return the same jobId)
app.MapPost("/users/{userId}/welcome-email", (string userId, IJobClient jobs) =>
{
    var jobId = jobs.Enqueue<EmailJob>(
        x => x.ExecuteAsync(userId, CancellationToken.None),
        delay: TimeSpan.FromSeconds(5),
        queue: "emails",
        idempotencyKey: $"welcome-{userId}");

    return Results.Accepted($"/jobs/api/jobs/{jobId}", new { jobId });
})
.WithName("EnqueueWelcomeEmail")
.WithSummary("Queue a welcome email (5s delay, idempotent per userId)");

// High-priority immediate email
app.MapPost("/users/{userId}/instant-email", (string userId, IJobClient jobs) =>
{
    var jobId = jobs.Enqueue<EmailJob>(
        x => x.ExecuteAsync(userId, CancellationToken.None),
        queue: "emails",
        priority: JobPriority.High);

    return Results.Accepted($"/jobs/api/jobs/{jobId}", new { jobId });
})
.WithName("EnqueueInstantEmail")
.WithSummary("Queue a high-priority instant email");

// Register daily recurring report
app.MapPost("/reports/daily", (IRecurringJobManager recurring) =>
{
    recurring.AddOrUpdate<DailyReportJob>(
        "daily-report",
        x => x.ExecuteAsync(CancellationToken.None),
        cronExpression: "0 6 * * *",
        timeZoneId: "UTC");

    return Results.Ok(new { message = "Daily report scheduled at 06:00 UTC" });
})
.WithName("ScheduleDailyReport")
.WithSummary("Register a daily recurring job (cron: 0 6 * * *)");

// Demo: watch retry + dead-letter flow in the dashboard
app.MapPost("/demo/failing-job", (IJobClient jobs) =>
{
    var jobId = jobs.Enqueue<FailingJob>(
        x => x.ExecuteAsync(CancellationToken.None),
        maxRetries: 3);

    return Results.Accepted($"/jobs/api/jobs/{jobId}",
        new { jobId, tip = "Open /jobs and watch this job retry 3x then move to DeadLetter." });
})
.WithName("DemoFailingJob")
.WithSummary("Demonstrates automatic retries and the dead-letter queue");

app.Run();
