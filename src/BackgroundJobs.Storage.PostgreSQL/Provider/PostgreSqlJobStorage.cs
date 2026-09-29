using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackgroundJobs.Abstractions.Models;
using BackgroundJobs.Abstractions.Storage;
using Dapper;
using Npgsql;
using System.Text.Json;

namespace BackgroundJobs.Storage.PostgreSQL.Provider;

public class PostgreSqlJobStorage : IJobStorage
{
    private readonly string _connectionString;

    public PostgreSqlJobStorage(string connectionString)
    {
        _connectionString = connectionString;
        EnsureTableCreated();
    }

    private void EnsureTableCreated()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Execute(@"
            CREATE TABLE IF NOT EXISTS BackgroundJobs (
                Id VARCHAR(50) PRIMARY KEY,
                Queue VARCHAR(50) NOT NULL,
                TypeName TEXT NOT NULL,
                MethodName TEXT NOT NULL,
                SerializedArguments TEXT NOT NULL,
                Status INT NOT NULL,
                CreatedAt TIMESTAMP NOT NULL,
                ProcessAt TIMESTAMP,
                RetryCount INT NOT NULL,
                MaxRetries INT NOT NULL,
                LastError TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_BackgroundJobs_Queue_Status_ProcessAt ON BackgroundJobs (Queue, Status, ProcessAt);
        ");
    }

    public async Task<string> EnqueueAsync(JobData job, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(@"
            INSERT INTO BackgroundJobs (Id, Queue, TypeName, MethodName, SerializedArguments, Status, CreatedAt, ProcessAt, RetryCount, MaxRetries, LastError)
            VALUES (@Id, @Queue, @TypeName, @MethodName, @SerializedArguments, @Status, @CreatedAt, @ProcessAt, @RetryCount, @MaxRetries, @LastError)
        ", job);
        
        return job.Id;
    }

    public async Task<JobData?> DequeueAsync(string queue, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        // Postgres SKIP LOCKED is perfect for queueing
        var job = await connection.QueryFirstOrDefaultAsync<JobData>(@"
            UPDATE BackgroundJobs 
            SET Status = @ProcessingStatus 
            WHERE Id = (
                SELECT Id 
                FROM BackgroundJobs 
                WHERE Queue = @Queue 
                  AND Status = @EnqueuedStatus 
                  AND (ProcessAt IS NULL OR ProcessAt <= @Now)
                ORDER BY CreatedAt ASC
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            RETURNING *;
        ", new 
        { 
            Queue = queue, 
            EnqueuedStatus = (int)JobStatus.Enqueued, 
            ProcessingStatus = (int)JobStatus.Processing,
            Now = DateTime.UtcNow
        });

        return job;
    }

    public async Task CompleteAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(@"
            UPDATE BackgroundJobs SET Status = @Status WHERE Id = @Id
        ", new { Id = id, Status = (int)JobStatus.Completed });
    }

    public async Task FailAsync(string id, string error, bool willRetry, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var status = willRetry ? (int)JobStatus.Enqueued : (int)JobStatus.Failed;
        
        // Calculate backoff if retrying (simple exponential backoff)
        // In a real system, you'd fetch RetryCount, increment it, and set ProcessAt
        await connection.ExecuteAsync(@"
            UPDATE BackgroundJobs 
            SET 
                LastError = @Error, 
                Status = @Status,
                RetryCount = RetryCount + 1,
                ProcessAt = CASE WHEN @WillRetry = true THEN @Now + (INTERVAL '1 second' * POWER(2, RetryCount)) ELSE ProcessAt END
            WHERE Id = @Id
        ", new { Id = id, Error = error, Status = status, WillRetry = willRetry, Now = DateTime.UtcNow });
    }

    public async Task<IReadOnlyList<JobData>> GetJobsAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var jobs = await connection.QueryAsync<JobData>(@"
            SELECT * FROM BackgroundJobs
            ORDER BY CreatedAt DESC
            OFFSET @Skip LIMIT @Take
        ", new { Skip = skip, Take = take });
        
        return jobs.ToList();
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BackgroundJobs");
    }
}
