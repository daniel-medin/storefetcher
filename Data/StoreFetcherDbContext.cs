using Microsoft.EntityFrameworkCore;
using StoreFetcher.Models;

namespace StoreFetcher.Data;

public sealed class StoreFetcherDbContext(DbContextOptions<StoreFetcherDbContext> options)
    : DbContext(options)
{
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreCorrection> StoreCorrections => Set<StoreCorrection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var store = modelBuilder.Entity<Store>();
        var correction = modelBuilder.Entity<StoreCorrection>();

        store.HasIndex(entity => new { entity.OsmType, entity.OsmId }).IsUnique();
        store.HasOne(entity => entity.Correction)
            .WithOne(entity => entity.Store)
            .HasForeignKey<StoreCorrection>(entity => entity.StoreId)
            .OnDelete(DeleteBehavior.Cascade);
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

        correction.HasIndex(entity => entity.StoreId).IsUnique();
        correction.Property(entity => entity.Name).HasMaxLength(240);
        correction.Property(entity => entity.Street).HasMaxLength(240);
        correction.Property(entity => entity.HouseNumber).HasMaxLength(40);
        correction.Property(entity => entity.Postcode).HasMaxLength(32);
        correction.Property(entity => entity.City).HasMaxLength(160);
        correction.Property(entity => entity.Country).HasMaxLength(2);
        correction.Property(entity => entity.Shop).HasMaxLength(80);
        correction.Property(entity => entity.Brand).HasMaxLength(160);
        correction.Property(entity => entity.Website).HasMaxLength(600);
        correction.Property(entity => entity.Phone).HasMaxLength(80);
        correction.Property(entity => entity.OpeningHours).HasMaxLength(500);
        correction.Property(entity => entity.Notes).HasMaxLength(1000);
    }
}
