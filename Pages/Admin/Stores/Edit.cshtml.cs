using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StoreFetcher.Data;
using StoreFetcher.Models;

namespace StoreFetcher.Pages.Admin.Stores;

public sealed class EditModel(StoreFetcherDbContext db) : PageModel
{
    [BindProperty]
    public StoreInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var store = await db.Stores
            .AsNoTracking()
            .Include(store => store.Correction)
            .FirstOrDefaultAsync(store => store.Id == id);
        if (store is null)
        {
            return NotFound();
        }

        Input = StoreInput.FromStore(store);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var store = await db.Stores
            .Include(store => store.Correction)
            .FirstOrDefaultAsync(store => store.Id == id);
        if (store is null)
        {
            return NotFound();
        }

        store.Correction ??= new StoreCorrection { StoreId = store.Id };
        Input.ApplyTo(store.Correction);
        await db.SaveChangesAsync();

        return RedirectToPage("/Admin/Stores/Index");
    }

    public sealed class StoreInput
    {
        public int Id { get; set; }

        [Required]
        [StringLength(240)]
        public string Name { get; set; } = "";

        public string? Street { get; set; }
        public string? HouseNumber { get; set; }
        public string? Postcode { get; set; }
        public string? City { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? Shop { get; set; }
        public string? Brand { get; set; }
        public string? Website { get; set; }
        public string? Phone { get; set; }
        public string? OpeningHours { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; }
        public string OsmUrl { get; set; } = "";

        public static StoreInput FromStore(Store store) =>
            new()
            {
                Id = store.Id,
                Name = store.Correction?.Name ?? store.Name,
                Street = store.Correction?.Street ?? store.Street,
                HouseNumber = store.Correction?.HouseNumber ?? store.HouseNumber,
                Postcode = store.Correction?.Postcode ?? store.Postcode,
                City = store.Correction?.City ?? store.City,
                Latitude = store.Correction?.Latitude ?? store.Latitude,
                Longitude = store.Correction?.Longitude ?? store.Longitude,
                Shop = store.Correction?.Shop ?? store.Shop,
                Brand = store.Correction?.Brand ?? store.Brand,
                Website = store.Correction?.Website ?? store.Website,
                Phone = store.Correction?.Phone ?? store.Phone,
                OpeningHours = store.Correction?.OpeningHours ?? store.OpeningHours,
                Notes = store.Correction?.Notes ?? store.Notes,
                IsActive = store.Correction?.IsActive ?? store.IsActive,
                OsmUrl = store.OsmUrl,
            };

        public void ApplyTo(StoreCorrection correction)
        {
            correction.Name = Name.Trim();
            correction.Street = Clean(Street);
            correction.HouseNumber = Clean(HouseNumber);
            correction.Postcode = Clean(Postcode);
            correction.City = Clean(City);
            correction.Latitude = Latitude;
            correction.Longitude = Longitude;
            correction.Shop = Clean(Shop);
            correction.Brand = Clean(Brand);
            correction.Website = Clean(Website);
            correction.Phone = Clean(Phone);
            correction.OpeningHours = Clean(OpeningHours);
            correction.Notes = Clean(Notes);
            correction.IsActive = IsActive;
            correction.UpdatedAt = DateTimeOffset.UtcNow;
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
