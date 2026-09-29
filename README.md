# BackgroundJobs ??

A ridiculously lightweight, extremely fast .NET 10 background job processing engine. 

I built **BackgroundJobs** because I was tired of massive, complex dependencies just to fire off a simple async task in the background. If you've ever thought *"I just want to run this method in the background without setting up a dedicated worker service or pulling in a giant scheduling framework"*, this is exactly what you've been looking for.

No massive tables. No bloated dependencies. Just pure, strongly-typed execution.

## Why This Exists (And Why You Should Use It)

- **Zero Friction Setup**: Add the package, register AddBackgroundJobs(), and you're done. Out of the box, it uses an ultra-fast InMemory storage with built-in garbage collection that absolutely will not leak RAM.
- **Strongly-Typed Magic**: Forget magic strings and reflection nightmares. Enqueue jobs natively with lambda expressions: client.EnqueueAsync<IEmailService>(x => x.Send("almas")).
- **PostgreSQL Scale**: When you're ready to scale horizontally, swap to the PostgreSQL provider. It natively leverages PostgreSQL's advanced SKIP LOCKED queries, guaranteeing your background workers will never deadlock or execute the same job twice, no matter how many microservices you run.
- **Automatic Resilience**: Every job is protected by a built-in exponential backoff engine. If your API call fails, it automatically retries safely.

## Quick Start ?

### 1. Add to your ASP.NET Core Project

`ash
# Add the core engine
dotnet add package BackgroundJobs

# (Optional) Add PostgreSQL storage for production
dotnet add package BackgroundJobs.Storage.PostgreSQL
`

### 2. Configure Program.cs

`csharp
using BackgroundJobs;

var builder = WebApplication.CreateBuilder(args);

// Register the engine
builder.Services.AddBackgroundJobs(options =>
{
    // The perfect default for local development. Flat RAM profile guaranteed.
    options.UseInMemoryStorage();
    
    // Ready for production? Swap it to Postgres instantly:
    // options.UsePostgreSqlStorage("Host=localhost;Port=5432;Username=postgres;Password=admin;Database=jobs");
});

// Don't forget to register the services your jobs will use!
builder.Services.AddTransient<IEmailService, EmailService>();

var app = builder.Build();
app.Run();
`

### 3. Dispatch Jobs from Anywhere

Inject IBackgroundJobClient into your Controllers, Minimal APIs, or MediatR handlers.

`csharp
app.MapPost("/users/register", async (IBackgroundJobClient jobs, string userId) => 
{
    // ?? Fire-and-Forget (Runs immediately in the background)
    await jobs.EnqueueAsync<IEmailService>(x => x.SendWelcomeEmailAsync(userId));

    // ?? Scheduled (Runs precisely 15 minutes from now)
    await jobs.ScheduleAsync<IEmailService>(
        x => x.SendFollowUpEmailAsync(userId), 
        TimeSpan.FromMinutes(15));
        
    return Results.Ok("User registered! Background tasks are queued.");
});
`

## Contributing & Extending

I intentionally kept the architecture strictly decoupled and clean. If you want to dive in and add features, here's how the repo is structured:

- **BackgroundJobs**: This single, consolidated project contains the IBackgroundJobClient, the strongly-typed expression parsers, the background IHostedService worker daemon, and the InMemory implementation. 
- **BackgroundJobs.Storage.PostgreSQL**: The robust Dapper-powered Postgres implementation. It's fully plug-and-play.

### Writing a Custom Storage Provider
Want to back this with Redis, SQL Server, or MongoDB? Just implement IJobStorage! You need to handle EnqueueAsync, thread-safe DequeueAsync, CompleteAsync, and FailAsync. That's it. 

### Tests
We love testable code. The entire engine is covered by xUnit. You can easily mock IBackgroundJobClient in your own unit tests to verify your code is queueing the correct methods without actually executing them.

---
*Built with ?? for the .NET community by Almas Khan.*
