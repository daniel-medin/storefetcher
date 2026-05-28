namespace StoreFetcher.Services;

public sealed class StoreScanJob(StoreImportService importer, ILogger<StoreScanJob> logger)
{
    public async Task ScanSwedenAsync(int limit)
    {
        logger.LogInformation("Starting OSM Sweden grocery scan with limit {Limit}.", limit);
        var imported = await importer.ImportSwedenFromOsmAsync(limit);
        logger.LogInformation("Finished OSM Sweden grocery scan. Imported {Imported} stores.", imported);
    }
}
