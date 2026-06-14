using System;
using System.Threading;
using System.Threading.Tasks;

namespace MulletaFlix.Api.Jobs;

/// <summary>
/// Internal background job queue for long running MulletaFlix work.
/// </summary>
public interface IJobQueue
{
    JobQueueItemDto Enqueue(
        string kind,
        string title,
        Func<CancellationToken, IProgress<JobQueueProgress>, Task> handler,
        string? correlationId = null);

    JobQueueStatusDto GetStatus();

    JobQueueItemDto? GetJob(string id);

    bool Cancel(string id);

    bool CancelByCorrelationId(string correlationId);

    int CancelAll();

    Task SetCacheAsync(string cacheKey, string value, TimeSpan ttl, CancellationToken cancellationToken);

    Task<string?> GetCacheAsync(string cacheKey, CancellationToken cancellationToken);
}
