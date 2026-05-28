using Hangfire;
using Hangfire.Storage;

namespace StoreFetcher.Services;

public interface IStoreScanQueue
{
    StoreScanQueueResult EnqueueSwedenOsmScan(int limit);
    StoreScanQueueResult EnqueuePlaceOsmScan(string place, int limit);
}

public sealed record StoreScanQueueResult(
    bool Enabled,
    string? JobId,
    int Limit,
    string Message,
    string? Place = null);

public sealed class HangfireStoreScanQueue(IBackgroundJobClient jobs) : IStoreScanQueue
{
    public StoreScanQueueResult EnqueueSwedenOsmScan(int limit)
    {
        var jobId = jobs.Enqueue<StoreScanJob>(job => job.ScanSwedenAsync(limit));
        return new StoreScanQueueResult(true, jobId, limit, $"Queued OSM scan job {jobId}.");
    }

    public StoreScanQueueResult EnqueuePlaceOsmScan(string place, int limit)
    {
        var jobId = jobs.Enqueue<StoreScanJob>(job => job.ScanPlaceAsync(place, limit));
        return new StoreScanQueueResult(true, jobId, limit, $"Queued OSM scan job {jobId} for {place}.", place);
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

    public StoreScanQueueResult EnqueuePlaceOsmScan(string place, int limit) =>
        new(
            false,
            null,
            limit,
            "Hangfire is disabled. Set Hangfire:Enabled=true after MariaDB credentials are configured.",
            place);
}

public interface IStoreScanMonitor
{
    StoreScanStatus GetStatus();
}

public sealed record StoreScanStatus(
    bool Enabled,
    long Enqueued,
    long Processing,
    long Scheduled,
    long Failed,
    string? Message = null)
{
    public bool HasWork => Enqueued > 0 || Processing > 0 || Scheduled > 0;
}

public sealed class HangfireStoreScanMonitor(JobStorage storage, ILogger<HangfireStoreScanMonitor> logger)
    : IStoreScanMonitor
{
    public StoreScanStatus GetStatus()
    {
        try
        {
            var monitor = storage.GetMonitoringApi();
            return new StoreScanStatus(
                true,
                monitor.EnqueuedCount("default"),
                monitor.ProcessingCount(),
                monitor.ScheduledCount(),
                monitor.FailedCount());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read Hangfire scan status.");
            return new StoreScanStatus(
                true,
                0,
                0,
                0,
                0,
                "Could not read Hangfire status. Check the Hangfire dashboard.");
        }
    }
}

public sealed class DisabledStoreScanMonitor : IStoreScanMonitor
{
    public StoreScanStatus GetStatus() =>
        new(
            false,
            0,
            0,
            0,
            0,
            "Hangfire is disabled.");
}
