using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;

namespace SemihBener.Plugin.BidClash.Data;

public enum AuctionStatus
{
    Active = 1,
    Finished = 2,
    Cancelled = 3
}

public sealed class Player
{
    public const long StartingCoins = 1_000;

    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public long Coins { get; set; } = StartingCoins;
    public int AuctionsWon { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Eşzamanlılık kontrolü: her güncellemede elle artırılır (MySQL'de rowversion yok).</summary>
    public int Version { get; set; }
}

public sealed class Auction
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public long StartingPrice { get; set; }
    public long MinIncrement { get; set; } = 1;
    public long CurrentPrice { get; set; }
    public int? HighestBidderId { get; set; }
    public Player? HighestBidder { get; set; }
    public int CreatedById { get; set; }
    public Player CreatedBy { get; set; } = null!;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public AuctionStatus Status { get; set; } = AuctionStatus.Active;
    public List<Bid> Bids { get; set; } = [];

    /// <summary>Aynı anda gelen iki teklifin birbirini ezmesini engeller.</summary>
    public int Version { get; set; }
}

public sealed class Bid
{
    public long Id { get; set; }
    public int AuctionId { get; set; }
    public Auction Auction { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public long Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class BidClashDbContext(DbContextOptions<BidClashDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<Bid> Bids => Set<Bid>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCharSet("utf8mb4");

        modelBuilder.Entity<Player>(e =>
        {
            e.ToTable("Players");
            e.Property(x => x.Username).HasMaxLength(32).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Coins);
        });

        modelBuilder.Entity<Auction>(e =>
        {
            e.ToTable("Auctions");
            e.Property(x => x.Title).HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.HighestBidder).WithMany().HasForeignKey(x => x.HighestBidderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Status, x.EndsAtUtc });
        });

        modelBuilder.Entity<Bid>(e =>
        {
            e.ToTable("Bids");
            e.HasOne(x => x.Auction).WithMany(a => a.Bids).HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AuctionId, x.Amount });
        });
    }
}

public sealed class BidClashDesignTimeFactory : PluginDesignTimeFactory<BidClashDbContext>
{
    protected override DatabaseProvider Provider => DatabaseProvider.MySql;

    protected override string DefaultConnectionString =>
        "Server=localhost;Port=3306;Database=DB_BidClash;User=bidclash;Password=dev_bidclash;";
}
