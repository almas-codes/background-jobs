using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BackgroundJobs.Abstractions.Models;

namespace BackgroundJobs.Abstractions.Storage;

public interface IJobStorage
{
    Task<string> EnqueueAsync(JobData job, CancellationToken cancellationToken = default);
    Task<JobData?> DequeueAsync(string queue, CancellationToken cancellationToken = default);
    Task CompleteAsync(string id, CancellationToken cancellationToken = default);
    Task FailAsync(string id, string error, bool willRetry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JobData>> GetJobsAsync(int skip, int take, CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
}
