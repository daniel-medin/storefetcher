namespace StoreFetcher.Models;

public sealed class StoreCorrection
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public string? Name { get; set; }
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? Postcode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Shop { get; set; }
    public string? Brand { get; set; }
    public string? Website { get; set; }
    public string? Phone { get; set; }
    public string? OpeningHours { get; set; }
    public string? Notes { get; set; }
    public bool? IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
