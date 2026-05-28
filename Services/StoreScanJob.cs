namespace StoreFetcher.Services;

public sealed class StoreScanJob(StoreImportService importer, ILogger<StoreScanJob> logger)
{
    public async Task ScanSwedenAsync(int limit)
    {
        logger.LogInformation("Starting OSM Sweden grocery scan with limit {Limit}.", limit);
        var imported = await importer.ImportSwedenFromOsmAsync(limit);
        logger.LogInformation("Finished OSM Sweden grocery scan. Imported {Imported} stores.", imported);
    }

    public async Task ScanPlaceAsync(string place, int limit)
    {
        logger.LogInformation(
            "Starting OSM grocery scan for {Place} with limit {Limit}.",
            place,
            limit);
        var imported = await importer.ImportPlaceFromOsmAsync(place, limit);
        logger.LogInformation(
            "Finished OSM grocery scan for {Place}. Imported {Imported} stores.",
            place,
            imported);
    }
}
