using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StoreFetcher.Data;
using StoreFetcher.Models;
using StoreFetcher.Services;

namespace StoreFetcher.Pages.Admin.Stores;

public sealed class IndexModel(
    StoreFetcherDbContext db,
    IStoreScanQueue scanQueue,
    IStoreScanMonitor scanMonitor) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public IReadOnlyList<Store> Stores { get; private set; } = [];
    public int TotalCount { get; private set; }
    public StoreScanStatus ScanStatus { get; private set; } = new(false, 0, 0, 0, 0);

    public async Task OnGetAsync()
    {
        var query = db.Stores
            .AsNoTracking()
            .Include(store => store.Correction)
            .OrderBy(store => store.Correction != null && store.Correction.Name != null
                ? store.Correction.Name
                : store.Name)
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
        Stores = await query.Take(100).ToListAsync();
        ScanStatus = scanMonitor.GetStatus();
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

    public string FormatAddress(Store store)
    {
        var parts = new[]
        {
            JoinStreet(store.Correction?.Street ?? store.Street, store.Correction?.HouseNumber ?? store.HouseNumber),
            store.Correction?.Postcode ?? store.Postcode,
            store.Correction?.City ?? store.City,
        }.Where(part => !string.IsNullOrWhiteSpace(part));

        var address = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(address) ? "Missing address" : address;
    }

    private static string? JoinStreet(string? street, string? houseNumber) =>
        string.IsNullOrWhiteSpace(street)
            ? null
            : string.Join(" ", new[] { street, houseNumber }.Where(part => !string.IsNullOrWhiteSpace(part)));

    public string Name(Store store) => store.Correction?.Name ?? store.Name;
    public string? Brand(Store store) => store.Correction?.Brand ?? store.Brand;
    public double Latitude(Store store) => store.Correction?.Latitude ?? store.Latitude;
    public double Longitude(Store store) => store.Correction?.Longitude ?? store.Longitude;
    public DateTimeOffset UpdatedAt(Store store) => store.Correction?.UpdatedAt ?? store.UpdatedAt;
}
