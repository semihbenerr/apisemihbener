using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;

namespace SemihBener.Plugin.EhliKurban.Data;

// ÖRNEK MODEL: Mevcut EhliKurban veritabanının gerçek tabloları README'deki
// "dotnet ef dbcontext scaffold" komutuyla bu klasöre üretilir ve controller'lar ona göre uyarlanır.

public enum AnimalType
{
    Cattle = 1, // Büyükbaş (7 hisse)
    Sheep = 2,  // Koyun (1 hisse)
    Goat = 3    // Keçi (1 hisse)
}

public enum AnimalStatus
{
    Waiting = 1,     // Kesim bekliyor
    Slaughtered = 2, // Kesildi
    Delivered = 3    // Teslim edildi
}

public sealed class Animal
{
    public int Id { get; set; }
    public string TagNumber { get; set; } = "";
    public AnimalType Type { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal Price { get; set; }
    public int TotalShares { get; set; }
    public AnimalStatus Status { get; set; } = AnimalStatus.Waiting;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<Customer> Customers { get; set; } = [];

    public decimal SharePrice => TotalShares == 0 ? 0 : Math.Round(Price / TotalShares, 2);
}

public sealed class Customer
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public int? AnimalId { get; set; }
    public Animal? Animal { get; set; }
    public int ShareCount { get; set; } = 1;
    public decimal AmountPaid { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class EhliKurbanDbContext(DbContextOptions<EhliKurbanDbContext> options) : DbContext(options)
{
    public DbSet<Animal> Animals => Set<Animal>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Animal>(e =>
        {
            e.ToTable("Animals");
            e.Property(x => x.TagNumber).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.TagNumber).IsUnique();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.WeightKg).HasPrecision(10, 2);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Ignore(x => x.SharePrice);
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.Property(x => x.FullName).HasMaxLength(128).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            e.Property(x => x.AmountPaid).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasOne(x => x.Animal).WithMany(a => a.Customers).HasForeignKey(x => x.AnimalId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Phone);
        });
    }
}

/// <summary>Lokal MSSQL (Windows kimlik doğrulaması). Farklıysa SB_DESIGN_CONNECTION ortam değişkenini kullanın.</summary>
public sealed class EhliKurbanDesignTimeFactory : PluginDesignTimeFactory<EhliKurbanDbContext>
{
    protected override DatabaseProvider Provider => DatabaseProvider.SqlServer;

    protected override string DefaultConnectionString =>
        "Server=localhost;Database=EhliKurban;Trusted_Connection=True;TrustServerCertificate=True;";
}
