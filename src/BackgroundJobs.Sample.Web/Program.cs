using System;
using System.Threading.Tasks;
using BackgroundJobs;
using BackgroundJobs.Abstractions.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Configure Background Jobs (Zero Setup Required for InMemory!)
builder.Services.AddBackgroundJobs(options =>
{
    // Defaulting to InMemory so anyone cloning this repo can hit F5 and it works instantly!
    options.UseInMemoryStorage();
    
    // To scale horizontally, swap to PostgreSQL:
    // options.UsePostgreSqlStorage("Host=localhost;Port=5432;Username=postgres;Password=admin;Database=backgroundjobs_db");
});

builder.Services.AddTransient<EmailService>();

var app = builder.Build();

app.MapGet("/", () => "Background Jobs API is running flawlessly! Try POST /enqueue-email?userId=123");

app.MapPost("/enqueue-email", async (IBackgroundJobClient client, string userId) =>
{
    var jobId = await client.EnqueueAsync<EmailService>(x => x.SendEmailAsync(userId));
    return Results.Ok(new { JobId = jobId, Message = "Email job enqueued immediately" });
});

app.MapPost("/schedule-email", async (IBackgroundJobClient client, string userId, int delayMinutes) =>
{
    var jobId = await client.ScheduleAsync<EmailService>(x => x.SendEmailAsync(userId), TimeSpan.FromMinutes(delayMinutes));
    return Results.Ok(new { JobId = jobId, Message = $"Email job scheduled in {delayMinutes} minutes" });
});

app.MapPost("/failing-job", async (IBackgroundJobClient client) =>
{
    var jobId = await client.EnqueueAsync<EmailService>(x => x.SimulateFailureAsync());
    return Results.Ok(new { JobId = jobId, Message = "Failing job enqueued to test automatic retries" });
});

app.Run();

public class EmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger) => _logger = logger;

    public async Task SendEmailAsync(string userId)
    {
        _logger.LogInformation("Sending email to user {UserId}...", userId);
        await Task.Delay(2000); // Simulate work without blocking the main thread
        _logger.LogInformation("Email sent to user {UserId} successfully!", userId);
    }

    public async Task SimulateFailureAsync()
    {
        _logger.LogWarning("This job is designed to fail...");
        await Task.Delay(500);
        throw new InvalidOperationException("Simulated job failure to test exponential backoff retries!");
    }
}
