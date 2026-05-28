namespace StoreFetcher.Models;

public sealed class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? Postcode { get; set; }
    public string? City { get; set; }
    public string Country { get; set; } = "SE";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Shop { get; set; }
    public string? Brand { get; set; }
    public string? Website { get; set; }
    public string? Phone { get; set; }
    public string? OpeningHours { get; set; }
    public string OsmType { get; set; } = "";
    public long OsmId { get; set; }
    public string OsmUrl { get; set; } = "";
    public string Source { get; set; } = "OpenStreetMap";
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
