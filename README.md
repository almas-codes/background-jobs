# BackgroundJobs 🚀

A ridiculously lightweight, extremely fast .NET 10 background job processing engine. 

I built **BackgroundJobs** because I was tired of massive, complex dependencies just to fire off a simple async task in the background. If you've ever thought *"I just want to run this method in the background without setting up a dedicated worker service or pulling in a giant scheduling framework"*, this is exactly what you've been looking for.

No massive tables. No bloated dependencies. Just pure, strongly-typed execution.

## Why This Exists (And Why You Should Use It)

- **Zero Friction Setup**: Add the package, register AddBackgroundJobs(), and you're done. Out of the box, it uses an ultra-fast InMemory storage with built-in garbage collection that absolutely will not leak RAM.
- **Strongly-Typed Magic**: Forget magic strings and reflection nightmares. Enqueue jobs natively with lambda expressions: client.EnqueueAsync<IEmailService>(x => x.Send("almas")).
- **PostgreSQL Scale**: When you're ready to scale horizontally, swap to the PostgreSQL provider. It natively leverages PostgreSQL's advanced SKIP LOCKED queries, guaranteeing your background workers will never deadlock or execute the same job twice, no matter how many microservices you run.
- **Automatic Resilience**: Every job is protected by a built-in exponential backoff engine. If your API call fails, it automatically retries safely.

## Quick Start ⚡

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

var app = builder.Build();
app.Run();
`

### 3. Define Your Job

To add a new background job, all you need to do is create a standard C# class and register it in your dependency injection container. There are no special interfaces you are forced to inherit from!

`csharp
// 1. Create your job logic
public class ImageProcessingService 
{
    public async Task ResizeImageAsync(string imageId, int width, int height) 
    {
        // Your heavy background logic goes here
        await Task.Delay(5000); 
    }
}

// 2. Register it in Program.cs
builder.Services.AddTransient<ImageProcessingService>();
`

### 4. Dispatch Jobs from Anywhere

Inject IBackgroundJobClient into your Controllers, Minimal APIs, or MediatR handlers. The engine will safely serialize your method arguments and execute them in the background!

`csharp
app.MapPost("/images/upload", async (IBackgroundJobClient jobs, string imageId) => 
{
    // 🔥 Fire-and-Forget (Runs immediately in the background)
    await jobs.EnqueueAsync<ImageProcessingService>(
        x => x.ResizeImageAsync(imageId, 800, 600));

    // 🕒 Scheduled (Runs precisely 15 minutes from now)
    await jobs.ScheduleAsync<ImageProcessingService>(
        x => x.ResizeImageAsync(imageId, 800, 600), 
        TimeSpan.FromMinutes(15));
        
    return Results.Ok("Upload complete! Image is being resized in the background.");
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
*Built with ❤️ for the .NET community by Almas Khan.*