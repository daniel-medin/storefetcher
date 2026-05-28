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
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(store => store.Id == id);
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

        var store = await db.Stores.FirstOrDefaultAsync(store => store.Id == id);
        if (store is null)
        {
            return NotFound();
        }

        Input.ApplyTo(store);
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
                Name = store.Name,
                Street = store.Street,
                HouseNumber = store.HouseNumber,
                Postcode = store.Postcode,
                City = store.City,
                Latitude = store.Latitude,
                Longitude = store.Longitude,
                Shop = store.Shop,
                Brand = store.Brand,
                Website = store.Website,
                Phone = store.Phone,
                OpeningHours = store.OpeningHours,
                Notes = store.Notes,
                IsActive = store.IsActive,
                OsmUrl = store.OsmUrl,
            };

        public void ApplyTo(Store store)
        {
            store.Name = Name.Trim();
            store.Street = Clean(Street);
            store.HouseNumber = Clean(HouseNumber);
            store.Postcode = Clean(Postcode);
            store.City = Clean(City);
            store.Latitude = Latitude;
            store.Longitude = Longitude;
            store.Shop = Clean(Shop);
            store.Brand = Clean(Brand);
            store.Website = Clean(Website);
            store.Phone = Clean(Phone);
            store.OpeningHours = Clean(OpeningHours);
            store.Notes = Clean(Notes);
            store.IsActive = IsActive;
            store.UpdatedAt = DateTimeOffset.UtcNow;
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
