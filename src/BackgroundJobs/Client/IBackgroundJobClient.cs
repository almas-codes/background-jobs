using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace BackgroundJobs.Abstractions.Client;

public interface IBackgroundJobClient
{
    Task<string> EnqueueAsync<T>(Expression<Func<T, Task>> methodCall, CancellationToken cancellationToken = default);
    Task<string> ScheduleAsync<T>(Expression<Func<T, Task>> methodCall, TimeSpan delay, CancellationToken cancellationToken = default);
    Task<string> ScheduleAsync<T>(Expression<Func<T, Task>> methodCall, DateTimeOffset processAt, CancellationToken cancellationToken = default);
}
