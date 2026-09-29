using System.Linq.Expressions;
using BackgroundJobs.Abstractions;
using Cronos;

namespace BackgroundJobs.Core;

public sealed class RecurringJobManager(IJobStorage storage) : IRecurringJobManager
{
    public void AddOrUpdate<T>(string recurringJobId, Expression<Func<T, Task>> methodCall,
        string cronExpression, string timeZoneId = "UTC", string queue = "default")
    {
        var inv  = CallExpressionSerializer.Serialize(methodCall);
        var cron = CronExpression.Parse(cronExpression, CronFormat.Standard);
        var tz   = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, tz) ?? DateTimeOffset.UtcNow;

        var definition = new RecurringJobDefinition
        {
            Id                 = recurringJobId,
            TypeName           = inv.TypeName,
            MethodName         = inv.MethodName,
            ParameterTypeNames = inv.ParameterTypeNames,
            ArgumentsJson      = inv.ArgumentsJson,
            CronExpression     = cronExpression,
            TimeZoneId         = timeZoneId,
            Queue              = queue,
            NextRunAt          = next
        };

        storage.UpsertRecurringJobAsync(definition).GetAwaiter().GetResult();
    }

    public void RemoveIfExists(string recurringJobId)
        => storage.RemoveRecurringJobAsync(recurringJobId).GetAwaiter().GetResult();
}
