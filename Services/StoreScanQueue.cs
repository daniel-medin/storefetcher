using Hangfire;

namespace StoreFetcher.Services;

public interface IStoreScanQueue
{
    StoreScanQueueResult EnqueueSwedenOsmScan(int limit);
}

public sealed record StoreScanQueueResult(
    bool Enabled,
    string? JobId,
    int Limit,
    string Message);

public sealed class HangfireStoreScanQueue(IBackgroundJobClient jobs) : IStoreScanQueue
{
    public StoreScanQueueResult EnqueueSwedenOsmScan(int limit)
    {
        var jobId = jobs.Enqueue<StoreScanJob>(job => job.ScanSwedenAsync(limit));
        return new StoreScanQueueResult(true, jobId, limit, $"Queued OSM scan job {jobId}.");
    }
}

public sealed class DisabledStoreScanQueue : IStoreScanQueue
{
    public StoreScanQueueResult EnqueueSwedenOsmScan(int limit) =>
        new(
            false,
            null,
            limit,
            "Hangfire is disabled. Set Hangfire:Enabled=true after MariaDB credentials are configured.");
}
