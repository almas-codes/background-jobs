using System;
using System.Threading;
using System.Threading.Tasks;
using BackgroundJobs.Abstractions.Models;
using BackgroundJobs.Abstractions.Storage;
using BackgroundJobs.Core.Client;
using BackgroundJobs.Storage.InMemory;
using FluentAssertions;
using Xunit;

namespace BackgroundJobs.Tests;

public class QualityAssuranceTests
{
    [Fact]
    public async Task Enqueue_Should_PersistJob_And_QueueForProcessing()
    {
        // Arrange
        using var storage = new InMemoryJobStorage();
        var client = new BackgroundJobClient(storage);

        // Act
        var jobId = await client.EnqueueAsync<TestService>(x => x.DoWorkAsync());

        // Assert
        var job = await storage.DequeueAsync("default");
        job.Should().NotBeNull();
        job!.Id.Should().Be(jobId);
        job.TypeName.Should().Contain("TestService");
        job.MethodName.Should().Be(nameof(TestService.DoWorkAsync));
    }

    [Fact]
    public async Task ScheduledJob_ShouldNotBeDequeued_Before_ProcessAt()
    {
        // Arrange
        using var storage = new InMemoryJobStorage();
        var client = new BackgroundJobClient(storage);

        // Act
        var jobId = await client.ScheduleAsync<TestService>(x => x.DoWorkAsync(), TimeSpan.FromMinutes(10));

        // Assert
        var job = await storage.DequeueAsync("default");
        job.Should().BeNull("Job is scheduled for the future and should not be dequeued yet.");
    }

    [Fact]
    public async Task InMemoryStorage_Should_Not_Leak_Memory_After_Cleanup()
    {
        // Arrange
        using var storage = new InMemoryJobStorage();
        
        // Act - Simulate a completed job from 2 hours ago
        var oldJob = new JobData 
        { 
            Id = Guid.NewGuid().ToString(), 
            Status = JobStatus.Completed, 
            CreatedAt = DateTime.UtcNow.AddHours(-2) 
        };
        await storage.EnqueueAsync(oldJob);
        
        // Trigger private cleanup method via reflection for testing
        var method = typeof(InMemoryJobStorage).GetMethod("CleanupCompletedJobs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(storage, new object[] { null! });

        // Assert
        var count = await storage.GetCountAsync();
        count.Should().Be(0, "Old completed jobs should be garbage collected to prevent memory leaks.");
    }
}

public class TestService
{
    public Task DoWorkAsync() => Task.CompletedTask;
}
