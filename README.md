# BackgroundJobs - Lightweight .NET Job System

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 9.0" />
  <img src="https://img.shields.io/badge/PostgreSQL-316192?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL" />
  <img src="https://img.shields.io/badge/License-MIT-green.svg?style=for-the-badge" alt="License" />
</p>

**BackgroundJobs** is a high-performance, lightweight .NET background job processing system. Designed as a simpler, faster alternative to Hangfire and Quartz.NET for modern ASP.NET Core applications that need powerful background processing without the bloat of a massive infrastructure stack.

Whether you need **delayed jobs**, **recurring tasks**, **automatic retries with exponential backoff**, or **persistent queues**, BackgroundJobs delivers a robust, strongly-typed API tailored for scale.

## ?? Key Features (SEO Optimized for .NET Background Tasks)
* **Fire-and-Forget Jobs**: Execute tasks asynchronously in the background.
* **Delayed & Scheduled Jobs**: Schedule tasks to execute at a precise time in the future.
* **Strongly-Typed Expression API**: Enqueue jobs using elegant lambda expressions (e.g., client.EnqueueAsync<IEmailService>(x => x.Send(userId))). No magic strings!
* **Automatic Retries & Exponential Backoff**: Built-in resilience for transient failures.
* **High Concurrency & Thread-Safe**: Utilizes PostgreSQL's advanced FOR UPDATE SKIP LOCKED mechanism to prevent deadlocks and guarantee safe execution across horizontally scaled microservices.
* **Multiple Storage Providers**: Ships with **InMemory** (for testing/dev) and **PostgreSQL** (production-ready). Extensible for SQL Server, Redis, and MySQL.
* **Dead-Letter Queues**: Automatically segregates permanently failed jobs.
* **Zero Configuration Initialization**: Automatically creates and migrates necessary database tables on startup.

## ?? Installation

This repository is structured cleanly with Central Package Management. To get started, clone the repository and add the core packages to your ASP.NET Core project:

`ash
dotnet add reference src/BackgroundJobs.Core
dotnet add reference src/BackgroundJobs.Storage.PostgreSQL
dotnet add reference src/BackgroundJobs.DependencyInjection
`

## ??? Quick Start Guide

### 1. Configure Services in Program.cs
Register the BackgroundJobs engine and choose your storage provider.

`csharp
using BackgroundJobs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBackgroundJobs(options =>
{
    // Use high-performance PostgreSQL storage
    options.UsePostgreSqlStorage("Host=localhost;Port=5432;Username=postgres;Password=admin;Database=backgroundjobs_db");
    
    // OR use InMemory storage for local testing
    // options.UseInMemoryStorage();
});

// Register your business logic services
builder.Services.AddTransient<IEmailService, EmailService>();

var app = builder.Build();
app.Run();
`

### 2. Enqueue Background Tasks seamlessly
Inject the IBackgroundJobClient anywhere in your application (Controllers, Minimal APIs, MediatR Handlers) to dispatch work.

`csharp
app.MapPost("/users/register", async (IBackgroundJobClient jobClient, string userId) => 
{
    // 1. Fire-and-Forget Job (Executes instantly in the background)
    await jobClient.EnqueueAsync<IEmailService>(service => service.SendWelcomeEmailAsync(userId));

    // 2. Delayed Job (Executes after 15 minutes)
    await jobClient.ScheduleAsync<IEmailService>(
        service => service.SendFollowUpEmailAsync(userId), 
        TimeSpan.FromMinutes(15));
        
    return Results.Ok("User registered and background tasks queued!");
});
`

## ??? Architecture & How to Extend

This project is built using Senior Architect-level SOLID principles. The architecture is strictly decoupled into distinct layers:

* **BackgroundJobs.Abstractions**: Core models, enums (JobStatus), and interfaces (IJobClient, IJobStorage).
* **BackgroundJobs.Core**: The background worker daemon (BackgroundJobWorker : BackgroundService), the expression tree decompiler (MethodCallInspector), and retry policies.
* **BackgroundJobs.Storage.***: Pluggable storage providers.

### How to Add a New Storage Provider (e.g., Redis or SQL Server)
1. Create a new Class Library (e.g., BackgroundJobs.Storage.Redis).
2. Add a reference to BackgroundJobs.Abstractions.
3. Implement the IJobStorage interface. You must handle EnqueueAsync, DequeueAsync (ensure thread-safety!), CompleteAsync, and FailAsync.
4. Create an extension method in BackgroundJobs.DependencyInjection to register your provider:
   `csharp
   public static void UseRedisStorage(this BackgroundJobsOptions options, string connectionString)
   {
       options.HasStorageConfigured = true;
       options.Services.AddSingleton<IJobStorage>(new RedisJobStorage(connectionString));
   }
   `

### How to Add New Core Features
* **Recurring Jobs (Cron)**: Extend IBackgroundJobClient with AddOrUpdateRecurringJob. Modify BackgroundJobWorker to poll a RecurringJobs table and enqueue instances based on cron schedules (using libraries like Cronos).
* **Dashboard / UI**: Build a Razor Class Library (BackgroundJobs.Dashboard) that injects IJobStorage and calls .GetJobsAsync() to visualize the queue in real-time.

## ?? Testing
The project includes comprehensive xUnit test coverage. The engine is easily mockable.

`csharp
// Mocking the job client in your unit tests
var mockClient = new Mock<IBackgroundJobClient>();
await myController.RegisterUser(mockClient.Object, "user-123");

// Verify job was queued
mockClient.Verify(x => x.EnqueueAsync<IEmailService>(s => s.SendWelcomeEmailAsync("user-123")), Times.Once);
`

---
*Developed with focus on zero-allocations, high throughput, and developer ergonomics. The ultimate open-source .NET task scheduling library.*
