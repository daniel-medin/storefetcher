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
    bool HasCorrection,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastSeenAt)
{
    public static StoreResponse FromStore(Store store)
    {
        var correction = store.Correction;

        return
        new(
            store.Id,
            correction?.Name ?? store.Name,
            correction?.Street ?? store.Street,
            correction?.HouseNumber ?? store.HouseNumber,
            correction?.Postcode ?? store.Postcode,
            correction?.City ?? store.City,
            correction?.Country ?? store.Country,
            correction?.Latitude ?? store.Latitude,
            correction?.Longitude ?? store.Longitude,
            correction?.Shop ?? store.Shop,
            correction?.Brand ?? store.Brand,
            correction?.Website ?? store.Website,
            correction?.Phone ?? store.Phone,
            correction?.OpeningHours ?? store.OpeningHours,
            store.OsmType,
            store.OsmId,
            store.OsmUrl,
            correction?.Notes ?? store.Notes,
            correction?.IsActive ?? store.IsActive,
            correction is not null,
            correction?.UpdatedAt ?? store.UpdatedAt,
            store.LastSeenAt);
    }
}

public sealed record DatasetMetadataResponse(
    string Name,
    string Source,
    string Attribution,
    string License,
    string LicenseUrl,
    string AttributionStatement,
    DateTimeOffset? GeneratedAt,
    int StoreCount);

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
    public void ApplyTo(StoreCorrection correction)
    {
        correction.Name = Name.Trim();
        correction.Street = Clean(Street);
        correction.HouseNumber = Clean(HouseNumber);
        correction.Postcode = Clean(Postcode);
        correction.City = Clean(City);
        correction.Country = string.IsNullOrWhiteSpace(Country) ? "SE" : Country.Trim().ToUpperInvariant();
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
