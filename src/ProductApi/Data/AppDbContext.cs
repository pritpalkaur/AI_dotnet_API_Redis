using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProductApi.Models;

namespace ProductApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    // datetime2 has no time zone; values are stored as UTC, so mark them UTC when read back.
    private static readonly ValueConverter<DateTime, DateTime> ToUtc =
        new(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("Products");
            e.HasKey(p => p.Id);
            e.Property(p => p.Sku).HasMaxLength(50).IsRequired();
            e.HasIndex(p => p.Sku).IsUnique();
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Description).HasMaxLength(2000);
            e.Property(p => p.Category).HasMaxLength(100);
            e.HasIndex(p => p.Category);
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.Property(p => p.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()").HasConversion(ToUtc);
            e.Property(p => p.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()").HasConversion(ToUtc);
        });
    }
}
