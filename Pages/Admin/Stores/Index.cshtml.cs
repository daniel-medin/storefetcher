using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StoreFetcher.Data;
using StoreFetcher.Services;
using System.Text.Json;

namespace StoreFetcher.Pages.Admin.Stores;

public sealed class IndexModel(
    StoreFetcherDbContext db,
    IStoreScanQueue scanQueue,
    IStoreScanMonitor scanMonitor,
    IAddressEnrichmentRunner addressEnrichmentRunner,
    StoreImportService importer,
    IWebHostEnvironment environment) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty]
    public string? LantmaterietPath { get; set; }

    [BindProperty]
    public int? AddressMaxStores { get; set; }

    [BindProperty]
    public bool UseOsmAddressFallback { get; set; } = true;

    [BindProperty]
    public bool UseOsmPlaceFallback { get; set; } = true;

    [TempData]
    public string? StatusMessage { get; set; }

    public int TotalCount { get; private set; }
    public int MissingAddressCount { get; private set; }
    public StoreScanStatus ScanStatus { get; private set; } = new(false, 0, 0, 0, 0);
    public AddressEnrichmentStatus AddressEnrichmentStatus { get; private set; } = AddressEnrichmentStatus.Idle();
    public AddressFileSummary EnrichedImportSummary { get; private set; } = AddressFileSummary.Empty();
    public AddressFileSummary AddressReviewSummary { get; private set; } = AddressFileSummary.Empty();

    public async Task OnGetAsync()
    {
        var query = db.Stores
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Q))
        {
            query = query.Where(store =>
                store.Name.Contains(Q) ||
                (store.Correction != null && store.Correction.Name != null && store.Correction.Name.Contains(Q)) ||
                (store.Brand != null && store.Brand.Contains(Q)) ||
                (store.Correction != null && store.Correction.Brand != null && store.Correction.Brand.Contains(Q)) ||
                (store.Street != null && store.Street.Contains(Q)) ||
                (store.Correction != null && store.Correction.Street != null && store.Correction.Street.Contains(Q)) ||
                (store.City != null && store.City.Contains(Q)) ||
                (store.Correction != null && store.Correction.City != null && store.Correction.City.Contains(Q)));
        }

        TotalCount = await query.CountAsync();
        MissingAddressCount = await db.Stores.AsNoTracking().CountAsync(store =>
            store.Street == null ||
            store.Street == "" ||
            store.HouseNumber == null ||
            store.HouseNumber == "" ||
            store.Postcode == null ||
            store.Postcode == "" ||
            store.City == null ||
            store.City == "");
        ScanStatus = scanMonitor.GetStatus();
        AddressEnrichmentStatus = addressEnrichmentRunner.GetStatus();
        EnrichedImportSummary = ReadAddressFileSummary(DefaultAddressOutputPath, "stores");
        AddressReviewSummary = ReadAddressFileSummary(DefaultAddressReviewPath, "reviews");
    }

    public IActionResult OnPostScan(int limit)
    {
        limit = Math.Clamp(limit, 1, 50000);
        var result = scanQueue.EnqueueSwedenOsmScan(limit);
        StatusMessage = result.Enabled
            ? $"{result.Message} Sweden limit {limit}."
            : result.Message;
        return RedirectToPage(new { Q });
    }

    public IActionResult OnPostAddressEnrichment()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var maxStores = AddressMaxStores is > 0 ? AddressMaxStores : null;
        var result = addressEnrichmentRunner.Start(new AddressEnrichmentOptions(
            baseUrl,
            LantmaterietPath,
            maxStores,
            UseOsmAddressFallback,
            UseOsmPlaceFallback));

        StatusMessage = result.Message;
        return RedirectToPage(new { Q });
    }

    public async Task<IActionResult> OnPostImportEnrichedAddressesAsync(CancellationToken cancellationToken)
    {
        var path = DefaultAddressOutputPath;
        if (!System.IO.File.Exists(path))
        {
            StatusMessage = "No enriched address import file exists yet. Run address enrichment first.";
            return RedirectToPage(new { Q });
        }

        await using var stream = System.IO.File.OpenRead(path);
        var result = await importer.ImportPreparedJsonAsync(stream, cancellationToken);
        StatusMessage =
            $"Imported enriched addresses. Created {result.Created}, updated {result.Updated}, skipped {result.Skipped}.";
        return RedirectToPage(new { Q });
    }

    public IActionResult OnPostPlaceScan(string place, int limit)
    {
        place = place.Trim();
        if (string.IsNullOrWhiteSpace(place))
        {
            StatusMessage = "Enter a Swedish city, municipality, or administrative place to scan.";
            return RedirectToPage(new { Q });
        }

        if (place.Length > 160)
        {
            StatusMessage = "Place must be 160 characters or fewer.";
            return RedirectToPage(new { Q });
        }

        limit = Math.Clamp(limit, 1, 50000);
        var result = scanQueue.EnqueuePlaceOsmScan(place, limit);
        StatusMessage = result.Enabled
            ? $"{result.Message} Place limit {limit}."
            : result.Message;
        return RedirectToPage(new { Q });
    }

    public string FormatAddressFileSummary(AddressFileSummary summary)
    {
        if (!summary.Exists)
        {
            return "No file yet";
        }

        var count = summary.Count is null ? "unknown count" : $"{summary.Count} records";
        var timestamp = summary.GeneratedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ??
            summary.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
        return $"{count}, {timestamp}";
    }

    private string DefaultAddressOutputPath =>
        Path.Combine(environment.ContentRootPath, "data", "address-enriched-import.json");

    private string DefaultAddressReviewPath =>
        Path.Combine(environment.ContentRootPath, "data", "address-review.json");

    private AddressFileSummary ReadAddressFileSummary(string path, string arrayName)
    {
        if (!System.IO.File.Exists(path))
        {
            return AddressFileSummary.Empty(path);
        }

        try
        {
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var root = document.RootElement;
            var count = root.TryGetProperty("metadata", out var metadata) &&
                metadata.TryGetProperty("count", out var countElement) &&
                countElement.TryGetInt32(out var metadataCount)
                    ? metadataCount
                    : CountArray(root, arrayName);
            var generatedAt = root.TryGetProperty("metadata", out metadata) &&
                metadata.TryGetProperty("generated_at", out var generatedAtElement) &&
                generatedAtElement.TryGetDateTimeOffset(out var parsedGeneratedAt)
                    ? parsedGeneratedAt
                    : (DateTimeOffset?)null;

            return new AddressFileSummary(
                true,
                path,
                count,
                generatedAt,
                new FileInfo(path).LastWriteTime);
        }
        catch (JsonException)
        {
            return new AddressFileSummary(
                true,
                path,
                null,
                null,
                new FileInfo(path).LastWriteTime);
        }
    }

    private static int? CountArray(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.GetArrayLength()
            : null;
}

public sealed record AddressFileSummary(
    bool Exists,
    string Path,
    int? Count,
    DateTimeOffset? GeneratedAt,
    DateTime LastWriteTime)
{
    public static AddressFileSummary Empty(string path = "") => new(false, path, null, null, default);
}
