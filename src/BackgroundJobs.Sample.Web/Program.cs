using System;
using System.Threading.Tasks;
using BackgroundJobs;
using BackgroundJobs.Abstractions.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

// Ensure DB exists before starting services
try {
    using var conn = new NpgsqlConnection("Host=localhost;Port=5432;Username=postgres;Password=admin;Database=postgres");
    conn.Open();
    using var cmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = 'backgroundjobs_db'", conn);
    var exists = cmd.ExecuteScalar() != null;
    if (!exists) {
        using var createCmd = new NpgsqlCommand("CREATE DATABASE backgroundjobs_db", conn);
        createCmd.ExecuteNonQuery();
        Console.WriteLine("Database created.");
    }
} catch (Exception ex) {
    Console.WriteLine("Could not verify/create db: " + ex.Message);
}

var builder = WebApplication.CreateBuilder(args);

// Configure Background Jobs
builder.Services.AddBackgroundJobs(options =>
{
    // You can use InMemory for quick dev
    // options.UseInMemoryStorage();
    
    // Using PostgreSQL since it's a requested feature
    options.UsePostgreSqlStorage("Host=localhost;Port=5432;Username=postgres;Password=admin;Database=backgroundjobs_db");
});

builder.Services.AddTransient<EmailService>();

var app = builder.Build();

app.MapGet("/", () => "Background Jobs API is running! Try POST /enqueue-email?userId=123");

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
    return Results.Ok(new { JobId = jobId, Message = "Failing job enqueued" });
});

app.Run();

public class EmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    public async Task SendEmailAsync(string userId)
    {
        _logger.LogInformation("Sending email to user {UserId}...", userId);
        await Task.Delay(2000); // Simulate work
        _logger.LogInformation("Email sent to user {UserId} successfully!", userId);
    }

    public async Task SimulateFailureAsync()
    {
        _logger.LogWarning("This job is designed to fail...");
        await Task.Delay(500);
        throw new InvalidOperationException("Simulated job failure to test retries!");
    }
}
