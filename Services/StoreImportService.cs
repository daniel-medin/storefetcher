using Microsoft.EntityFrameworkCore;
using StoreFetcher.Data;
using StoreFetcher.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StoreFetcher.Services;

public sealed class StoreImportService(StoreFetcherDbContext db, OverpassClient overpass)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> ImportSwedenFromOsmAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var elements = await overpass.FetchSwedenGroceryStoresAsync(limit, cancellationToken);
        return await ImportOverpassElementsAsync(elements, cancellationToken);
    }

    public async Task<int> ImportPlaceFromOsmAsync(
        string place,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var elements = await overpass.FetchPlaceGroceryStoresAsync(place, limit, cancellationToken);
        return await ImportOverpassElementsAsync(elements, cancellationToken);
    }

    private async Task<int> ImportOverpassElementsAsync(
        IReadOnlyList<OverpassElement> elements,
        CancellationToken cancellationToken)
    {
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

    public async Task<PreparedStoreImportResult> ImportPreparedJsonAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var dataset = await JsonSerializer.DeserializeAsync<PreparedStoreDataset>(
            stream,
            JsonOptions,
            cancellationToken);

        if (dataset?.Stores is null)
        {
            throw new InvalidDataException("Import file must contain a stores array.");
        }

        var created = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var prepared in dataset.Stores)
        {
            var store = ToStore(prepared);
            if (store is null)
            {
                skipped++;
                continue;
            }

            var existing = await db.Stores.FirstOrDefaultAsync(
                candidate => candidate.OsmType == store.OsmType && candidate.OsmId == store.OsmId,
                cancellationToken);

            if (existing is null)
            {
                db.Stores.Add(store);
                created++;
            }
            else
            {
                ApplyImport(existing, store);
                updated++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new PreparedStoreImportResult(
            dataset.Stores.Count,
            created,
            updated,
            skipped);
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

    private static Store? ToStore(PreparedStore store)
    {
        if (store.Osm is null ||
            string.IsNullOrWhiteSpace(store.Osm.Type) ||
            store.Osm.Id <= 0 ||
            string.IsNullOrWhiteSpace(store.Name) ||
            store.Latitude is null ||
            store.Longitude is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;

        return new Store
        {
            Name = store.Name.Trim(),
            Street = Clean(store.Address?.Street),
            HouseNumber = Clean(store.Address?.HouseNumber),
            Postcode = Clean(store.Address?.Postcode),
            City = Clean(store.Address?.City),
            Country = Clean(store.Address?.Country) ?? "SE",
            Latitude = store.Latitude.Value,
            Longitude = store.Longitude.Value,
            Shop = Clean(store.Shop),
            Brand = Clean(store.Brand),
            Website = Clean(store.Website),
            Phone = Clean(store.Phone),
            OpeningHours = Clean(store.OpeningHours),
            OsmType = store.Osm.Type.Trim(),
            OsmId = store.Osm.Id,
            OsmUrl = Clean(store.Osm.Url) ?? $"https://www.openstreetmap.org/{store.Osm.Type.Trim()}/{store.Osm.Id}",
            LastSeenAt = now,
            UpdatedAt = now,
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

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record PreparedStoreImportResult(
    int Total,
    int Created,
    int Updated,
    int Skipped);

public sealed record PreparedStoreDataset(
    [property: JsonPropertyName("stores")] IReadOnlyList<PreparedStore> Stores);

public sealed record PreparedStore(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("address")] PreparedStoreAddress? Address,
    [property: JsonPropertyName("lat")] double? Latitude,
    [property: JsonPropertyName("lon")] double? Longitude,
    [property: JsonPropertyName("shop")] string? Shop,
    [property: JsonPropertyName("brand")] string? Brand,
    [property: JsonPropertyName("website")] string? Website,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("opening_hours")] string? OpeningHours,
    [property: JsonPropertyName("osm")] PreparedStoreOsm? Osm);

public sealed record PreparedStoreAddress(
    [property: JsonPropertyName("street")] string? Street,
    [property: JsonPropertyName("house_number")] string? HouseNumber,
    [property: JsonPropertyName("postcode")] string? Postcode,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("country")] string? Country);

public sealed record PreparedStoreOsm(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("url")] string? Url);
