using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StoreFetcher.Data;
using StoreFetcher.Models;

namespace StoreFetcher.Pages.Admin.Stores;

public sealed class CreateModel(StoreFetcherDbContext db) : PageModel
{
    [BindProperty]
    public StoreInput Input { get; set; } = new();

    public void OnGet()
    {
        Input = new StoreInput();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var now = DateTimeOffset.UtcNow;
        var osmId = await NextManualOsmIdAsync();

        var store = new Store
        {
            Name = Input.Name.Trim(),
            Street = Clean(Input.Street),
            HouseNumber = Clean(Input.HouseNumber),
            Postcode = Clean(Input.Postcode),
            City = Clean(Input.City),
            Country = "SE",
            Latitude = Input.Latitude,
            Longitude = Input.Longitude,
            Shop = Clean(Input.Shop) ?? "supermarket",
            Brand = Clean(Input.Brand),
            Website = Clean(Input.Website),
            Phone = Clean(Input.Phone),
            OpeningHours = Clean(Input.OpeningHours),
            OsmType = "manual",
            OsmId = osmId,
            OsmUrl = $"manual://store/{Math.Abs(osmId)}",
            Source = "Manual",
            Notes = Clean(Input.Notes),
            IsActive = Input.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
            LastSeenAt = now,
        };

        db.Stores.Add(store);
        await db.SaveChangesAsync();

        return RedirectToPage("/Admin/Stores/Edit", new { id = store.Id });
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<long> NextManualOsmIdAsync()
    {
        var lowestManualId = await db.Stores
            .Where(store => store.OsmType == "manual")
            .Select(store => (long?)store.OsmId)
            .MinAsync();

        return lowestManualId is null || lowestManualId >= 0
            ? -1
            : lowestManualId.Value - 1;
    }

    public sealed class StoreInput
    {
        [Required]
        [StringLength(240)]
        public string Name { get; set; } = "";

        public string? Street { get; set; }
        public string? HouseNumber { get; set; }
        public string? Postcode { get; set; }
        public string? City { get; set; }

        [Range(-90, 90)]
        public double Latitude { get; set; }

        [Range(-180, 180)]
        public double Longitude { get; set; }

        public string? Shop { get; set; } = "supermarket";
        public string? Brand { get; set; }
        public string? Website { get; set; }
        public string? Phone { get; set; }
        public string? OpeningHours { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
