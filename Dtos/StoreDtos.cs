using StoreFetcher.Models;

namespace StoreFetcher.Dtos;

public sealed record PagedStoreResponse(
    IReadOnlyList<StoreResponse> Stores,
    int Page,
    int PageSize,
    int Total);

public sealed record StoreResponse(
    int Id,
    string Name,
    string? Street,
    string? HouseNumber,
    string? Postcode,
    string? City,
    string Country,
    double Latitude,
    double Longitude,
    string? Shop,
    string? Brand,
    string? Website,
    string? Phone,
    string? OpeningHours,
    string OsmType,
    long OsmId,
    string OsmUrl,
    string? Notes,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastSeenAt)
{
    public static StoreResponse FromStore(Store store) =>
        new(
            store.Id,
            store.Name,
            store.Street,
            store.HouseNumber,
            store.Postcode,
            store.City,
            store.Country,
            store.Latitude,
            store.Longitude,
            store.Shop,
            store.Brand,
            store.Website,
            store.Phone,
            store.OpeningHours,
            store.OsmType,
            store.OsmId,
            store.OsmUrl,
            store.Notes,
            store.IsActive,
            store.UpdatedAt,
            store.LastSeenAt);
}

public sealed record UpdateStoreRequest(
    string Name,
    string? Street,
    string? HouseNumber,
    string? Postcode,
    string? City,
    string Country,
    double Latitude,
    double Longitude,
    string? Shop,
    string? Brand,
    string? Website,
    string? Phone,
    string? OpeningHours,
    string? Notes,
    bool IsActive)
{
    public void ApplyTo(Store store)
    {
        store.Name = Name.Trim();
        store.Street = Clean(Street);
        store.HouseNumber = Clean(HouseNumber);
        store.Postcode = Clean(Postcode);
        store.City = Clean(City);
        store.Country = string.IsNullOrWhiteSpace(Country) ? "SE" : Country.Trim().ToUpperInvariant();
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
