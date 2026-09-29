using BackgroundJobs.Abstractions;
using BackgroundJobs.Core;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BackgroundJobs.Storage.PostgreSql;

/// <summary>
/// Production PostgreSQL storage using raw Npgsql — no ORM overhead.
/// Uses FOR UPDATE SKIP LOCKED in ClaimDueJobsAsync, enabling safe concurrent claiming
/// across any number of horizontally scaled server instances.
/// </summary>
public sealed class PostgreSqlJobStorage(NpgsqlDataSource dataSource) : IJobStorage
{
    public async Task<string> CreateAsync(JobRecord job, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            INSERT INTO background_jobs
                (id, type_name, method_name, parameter_type_names, arguments_json,
                 queue, priority, status, scheduled_at, max_retries, recurring_job_id, idempotency_key)
            VALUES (@id,@type,@method,@paramTypes,@args,@queue,@priority,@status,@scheduledAt,@maxRetries,@recurringId,@idemKey)
            ON CONFLICT (idempotency_key) DO NOTHING
            RETURNING id;
            """);
        cmd.Parameters.AddWithValue("id",          job.Id);
        cmd.Parameters.AddWithValue("type",        job.TypeName);
        cmd.Parameters.AddWithValue("method",      job.MethodName);
        cmd.Parameters.AddWithValue("paramTypes",  string.Join('|', job.ParameterTypeNames));
        cmd.Parameters.AddWithValue("args",        job.ArgumentsJson);
        cmd.Parameters.AddWithValue("queue",       job.Queue);
        cmd.Parameters.AddWithValue("priority",    (int)job.Priority);
        cmd.Parameters.AddWithValue("status",      (int)job.Status);
        cmd.Parameters.AddWithValue("scheduledAt", job.ScheduledAt);
        cmd.Parameters.AddWithValue("maxRetries",  job.MaxRetries);
        cmd.Parameters.AddWithValue("recurringId", (object?)job.RecurringJobId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("idemKey",     (object?)job.IdempotencyKey ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string ?? job.Id;
    }

    public async Task<JobRecord?> GetAsync(string jobId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("SELECT * FROM background_jobs WHERE id = @id");
        cmd.Parameters.AddWithValue("id", jobId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<JobRecord>> ClaimDueJobsAsync(
        string[] queues, int batchSize, string serverId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        // FOR UPDATE SKIP LOCKED: multiple servers poll the same table with zero contention.
        // Rows locked by another connection are simply skipped — no blocking, no duplicates.
        await using var cmd = dataSource.CreateCommand("""
            WITH cte AS (
                SELECT id FROM background_jobs
                WHERE queue = ANY(@queues)
                  AND status IN (0, 4)
                  AND scheduled_at <= now()
                ORDER BY priority DESC, scheduled_at
                LIMIT @batchSize
                FOR UPDATE SKIP LOCKED
            )
            UPDATE background_jobs j
            SET status = 1, claimed_by = @serverId,
                lease_expires_at = now() + @lease,
                started_at = now()
            FROM cte WHERE j.id = cte.id
            RETURNING j.*;
            """);
        cmd.Parameters.AddWithValue("queues",    queues);
        cmd.Parameters.AddWithValue("batchSize", batchSize);
        cmd.Parameters.AddWithValue("serverId",  serverId);
        cmd.Parameters.AddWithValue("lease",     leaseDuration);
        var results = new List<JobRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) results.Add(Map(reader));
        return results;
    }

    public async Task UpdateAsync(JobRecord job, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            UPDATE background_jobs SET
                status       = @status,
                started_at   = @started,
                completed_at = @completed,
                retry_count  = @retries,
                last_error   = @error,
                scheduled_at = @scheduledAt
            WHERE id = @id;
            """);
        cmd.Parameters.AddWithValue("id",          job.Id);
        cmd.Parameters.AddWithValue("status",      (int)job.Status);
        cmd.Parameters.AddWithValue("started",     (object?)job.StartedAt   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("completed",   (object?)job.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("retries",     job.RetryCount);
        cmd.Parameters.AddWithValue("error",       (object?)job.LastError   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("scheduledAt", job.ScheduledAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> DeleteAsync(string jobId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("DELETE FROM background_jobs WHERE id = @id");
        cmd.Parameters.AddWithValue("id", jobId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task MoveToDeadLetterAsync(JobRecord job, CancellationToken ct = default)
    {
        job.Status = JobStatus.DeadLetter;
        await UpdateAsync(job, ct);
    }

    public async Task UpsertRecurringJobAsync(RecurringJobDefinition def, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            INSERT INTO background_jobs_recurring
                (id,type_name,method_name,parameter_type_names,arguments_json,cron_expression,timezone_id,queue,next_run_at)
            VALUES (@id,@type,@method,@paramTypes,@args,@cron,@tz,@queue,@next)
            ON CONFLICT (id) DO UPDATE SET
                type_name=EXCLUDED.type_name, method_name=EXCLUDED.method_name,
                parameter_type_names=EXCLUDED.parameter_type_names,
                arguments_json=EXCLUDED.arguments_json,
                cron_expression=EXCLUDED.cron_expression,
                timezone_id=EXCLUDED.timezone_id,
                queue=EXCLUDED.queue, next_run_at=EXCLUDED.next_run_at;
            """);
        cmd.Parameters.AddWithValue("id",         def.Id);
        cmd.Parameters.AddWithValue("type",       def.TypeName);
        cmd.Parameters.AddWithValue("method",     def.MethodName);
        cmd.Parameters.AddWithValue("paramTypes", string.Join('|', def.ParameterTypeNames));
        cmd.Parameters.AddWithValue("args",       def.ArgumentsJson);
        cmd.Parameters.AddWithValue("cron",       def.CronExpression);
        cmd.Parameters.AddWithValue("tz",         def.TimeZoneId);
        cmd.Parameters.AddWithValue("queue",      def.Queue);
        cmd.Parameters.AddWithValue("next",       (object?)def.NextRunAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveRecurringJobAsync(string recurringJobId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("DELETE FROM background_jobs_recurring WHERE id = @id");
        cmd.Parameters.AddWithValue("id", recurringJobId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<RecurringJobDefinition>> GetDueRecurringJobsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT * FROM background_jobs_recurring WHERE next_run_at IS NULL OR next_run_at <= @now");
        cmd.Parameters.AddWithValue("now", now);
        var results = new List<RecurringJobDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new RecurringJobDefinition
            {
                Id                 = reader.GetString(reader.GetOrdinal("id")),
                TypeName           = reader.GetString(reader.GetOrdinal("type_name")),
                MethodName         = reader.GetString(reader.GetOrdinal("method_name")),
                ParameterTypeNames = reader.GetString(reader.GetOrdinal("parameter_type_names")).Split('|'),
                ArgumentsJson      = reader.GetString(reader.GetOrdinal("arguments_json")),
                CronExpression     = reader.GetString(reader.GetOrdinal("cron_expression")),
                TimeZoneId         = reader.GetString(reader.GetOrdinal("timezone_id")),
                Queue              = reader.GetString(reader.GetOrdinal("queue")),
                NextRunAt          = reader.IsDBNull(reader.GetOrdinal("next_run_at")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("next_run_at")),
                LastRunAt          = reader.IsDBNull(reader.GetOrdinal("last_run_at"))  ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_run_at"))
            });
        }
        return results;
    }

    public async Task UpdateRecurringJobRunAsync(string recurringJobId, DateTimeOffset lastRun, DateTimeOffset nextRun, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "UPDATE background_jobs_recurring SET last_run_at=@last, next_run_at=@next WHERE id=@id");
        cmd.Parameters.AddWithValue("id",   recurringJobId);
        cmd.Parameters.AddWithValue("last", lastRun);
        cmd.Parameters.AddWithValue("next", nextRun);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<JobStorageStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("SELECT status, COUNT(*) FROM background_jobs GROUP BY status");
        var counts = new Dictionary<int, int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            counts[reader.GetInt32(0)] = (int)reader.GetInt64(1);
        return new JobStorageStats(
            counts.GetValueOrDefault(0), counts.GetValueOrDefault(1),
            counts.GetValueOrDefault(2), counts.GetValueOrDefault(3),
            counts.GetValueOrDefault(4), counts.GetValueOrDefault(6));
    }

    public async Task<IReadOnlyList<JobRecord>> GetJobsAsync(JobStatus? status, int skip, int take, CancellationToken ct = default)
    {
        var sql = status.HasValue
            ? "SELECT * FROM background_jobs WHERE status=@status ORDER BY created_at DESC OFFSET @skip LIMIT @take"
            : "SELECT * FROM background_jobs ORDER BY created_at DESC OFFSET @skip LIMIT @take";
        await using var cmd = dataSource.CreateCommand(sql);
        if (status.HasValue) cmd.Parameters.AddWithValue("status", (int)status.Value);
        cmd.Parameters.AddWithValue("skip", skip);
        cmd.Parameters.AddWithValue("take", take == 0 ? 50 : take);
        var results = new List<JobRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) results.Add(Map(reader));
        return results;
    }

    private static JobRecord Map(NpgsqlDataReader r) => new()
    {
        Id                 = r.GetString(r.GetOrdinal("id")),
        TypeName           = r.GetString(r.GetOrdinal("type_name")),
        MethodName         = r.GetString(r.GetOrdinal("method_name")),
        ParameterTypeNames = r.GetString(r.GetOrdinal("parameter_type_names")).Split('|'),
        ArgumentsJson      = r.GetString(r.GetOrdinal("arguments_json")),
        Queue              = r.GetString(r.GetOrdinal("queue")),
        Priority           = (JobPriority)r.GetInt32(r.GetOrdinal("priority")),
        Status             = (JobStatus)r.GetInt32(r.GetOrdinal("status")),
        ScheduledAt        = r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("scheduled_at")),
        RetryCount         = r.GetInt32(r.GetOrdinal("retry_count")),
        MaxRetries         = r.GetInt32(r.GetOrdinal("max_retries")),
        LastError          = r.IsDBNull(r.GetOrdinal("last_error"))  ? null : r.GetString(r.GetOrdinal("last_error")),
        ClaimedBy          = r.IsDBNull(r.GetOrdinal("claimed_by"))  ? null : r.GetString(r.GetOrdinal("claimed_by")),
    };
}

public static class PostgreSqlStorageExtensions
{
    public static BackgroundJobsBuilder UsePostgreSql(
        this BackgroundJobsBuilder builder, string connectionString)
    {
        var dataSource = NpgsqlDataSource.Create(connectionString);
        builder.Services.AddSingleton(dataSource);
        builder.Services.AddSingleton<IJobStorage, PostgreSqlJobStorage>();
        return builder;
    }
}
