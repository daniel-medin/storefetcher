using Microsoft.EntityFrameworkCore;
using StoreFetcher.Data;
using StoreFetcher.Models;

namespace StoreFetcher.Services;

public sealed class StoreImportService(StoreFetcherDbContext db, OverpassClient overpass)
{
    public async Task<int> ImportSwedenFromOsmAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var elements = await overpass.FetchSwedenGroceryStoresAsync(limit, cancellationToken);
        var imported = 0;

        foreach (var element in elements)
        {
            var store = ToStore(element);
            if (store is null)
            {
                continue;
            }

            var existing = await db.Stores.FirstOrDefaultAsync(
                candidate => candidate.OsmType == store.OsmType && candidate.OsmId == store.OsmId,
                cancellationToken);

            if (existing is null)
            {
                db.Stores.Add(store);
            }
            else
            {
                ApplyImport(existing, store);
            }

            imported++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return imported;
    }

    private static Store? ToStore(OverpassElement element)
    {
        var tags = element.Tags ?? [];
        var lat = element.Latitude ?? element.Center?.Latitude;
        var lon = element.Longitude ?? element.Center?.Longitude;

        if (lat is null || lon is null || !tags.TryGetValue("name", out var name))
        {
            return null;
        }

        return new Store
        {
            Name = name,
            Street = Get(tags, "addr:street"),
            HouseNumber = Get(tags, "addr:housenumber"),
            Postcode = Get(tags, "addr:postcode"),
            City = Get(tags, "addr:city"),
            Country = Get(tags, "addr:country") ?? "SE",
            Latitude = lat.Value,
            Longitude = lon.Value,
            Shop = Get(tags, "shop"),
            Brand = Get(tags, "brand"),
            Website = Get(tags, "website") ?? Get(tags, "contact:website"),
            Phone = Get(tags, "phone") ?? Get(tags, "contact:phone"),
            OpeningHours = Get(tags, "opening_hours"),
            OsmType = element.Type,
            OsmId = element.Id,
            OsmUrl = $"https://www.openstreetmap.org/{element.Type}/{element.Id}",
            LastSeenAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static void ApplyImport(Store existing, Store imported)
    {
        existing.Name = imported.Name;
        existing.Street = imported.Street;
        existing.HouseNumber = imported.HouseNumber;
        existing.Postcode = imported.Postcode;
        existing.City = imported.City;
        existing.Country = imported.Country;
        existing.Latitude = imported.Latitude;
        existing.Longitude = imported.Longitude;
        existing.Shop = imported.Shop;
        existing.Brand = imported.Brand;
        existing.Website = imported.Website;
        existing.Phone = imported.Phone;
        existing.OpeningHours = imported.OpeningHours;
        existing.OsmUrl = imported.OsmUrl;
        existing.LastSeenAt = DateTimeOffset.UtcNow;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string? Get(Dictionary<string, string> tags, string key) =>
        tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
}
