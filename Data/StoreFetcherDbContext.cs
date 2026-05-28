using Microsoft.EntityFrameworkCore;
using StoreFetcher.Models;

namespace StoreFetcher.Data;

public sealed class StoreFetcherDbContext(DbContextOptions<StoreFetcherDbContext> options)
    : DbContext(options)
{
    public DbSet<Store> Stores => Set<Store>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var store = modelBuilder.Entity<Store>();

        store.HasIndex(entity => new { entity.OsmType, entity.OsmId }).IsUnique();
        store.Property(entity => entity.Name).HasMaxLength(240);
        store.Property(entity => entity.Street).HasMaxLength(240);
        store.Property(entity => entity.HouseNumber).HasMaxLength(40);
        store.Property(entity => entity.Postcode).HasMaxLength(32);
        store.Property(entity => entity.City).HasMaxLength(160);
        store.Property(entity => entity.Country).HasMaxLength(2);
        store.Property(entity => entity.Shop).HasMaxLength(80);
        store.Property(entity => entity.Brand).HasMaxLength(160);
        store.Property(entity => entity.Website).HasMaxLength(600);
        store.Property(entity => entity.Phone).HasMaxLength(80);
        store.Property(entity => entity.OpeningHours).HasMaxLength(500);
        store.Property(entity => entity.OsmType).HasMaxLength(16);
        store.Property(entity => entity.OsmUrl).HasMaxLength(160);
        store.Property(entity => entity.Source).HasMaxLength(80);
        store.Property(entity => entity.Notes).HasMaxLength(1000);
    }
}
