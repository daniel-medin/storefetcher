using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StoreFetcher.Data;
using StoreFetcher.Models;
using StoreFetcher.Services;

namespace StoreFetcher.Pages.Admin.Stores;

public sealed class IndexModel(StoreFetcherDbContext db, IStoreScanQueue scanQueue) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public IReadOnlyList<Store> Stores { get; private set; } = [];
    public int TotalCount { get; private set; }

    public async Task OnGetAsync()
    {
        var query = db.Stores.AsNoTracking().OrderBy(store => store.Name).AsQueryable();

        if (!string.IsNullOrWhiteSpace(Q))
        {
            query = query.Where(store =>
                store.Name.Contains(Q) ||
                (store.Brand != null && store.Brand.Contains(Q)) ||
                (store.Street != null && store.Street.Contains(Q)) ||
                (store.City != null && store.City.Contains(Q)));
        }

        TotalCount = await query.CountAsync();
        Stores = await query.Take(100).ToListAsync();
    }

    public IActionResult OnPostScan(int limit)
    {
        limit = Math.Clamp(limit, 1, 50000);
        var result = scanQueue.EnqueueSwedenOsmScan(limit);
        StatusMessage = result.Enabled
            ? $"{result.Message} Limit {limit}."
            : result.Message;
        return RedirectToPage(new { Q });
    }

    public string FormatAddress(Store store)
    {
        var parts = new[]
        {
            JoinStreet(store.Street, store.HouseNumber),
            store.Postcode,
            store.City,
        }.Where(part => !string.IsNullOrWhiteSpace(part));

        var address = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(address) ? "Missing address" : address;
    }

    private static string? JoinStreet(string? street, string? houseNumber) =>
        string.IsNullOrWhiteSpace(street)
            ? null
            : string.Join(" ", new[] { street, houseNumber }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
