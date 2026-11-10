using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;

namespace SemihBener.Plugin.Football.Data;

public enum MatchStatus
{
    Scheduled = 1,
    Live = 2,
    Finished = 3,
    Postponed = 4
}

public sealed class FootballMatch
{
    public int Id { get; set; }
    public string League { get; set; } = "";
    public string Season { get; set; } = "";
    public string HomeTeam { get; set; } = "";
    public string AwayTeam { get; set; } = "";
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public DateTime KickoffUtc { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.Scheduled;
    public string? Venue { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class FootballDbContext(DbContextOptions<FootballDbContext> options) : DbContext(options)
{
    public DbSet<FootballMatch> Matches => Set<FootballMatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCharSet("utf8mb4");

        modelBuilder.Entity<FootballMatch>(e =>
        {
            e.ToTable("Matches");
            e.Property(x => x.League).HasMaxLength(64).IsRequired();
            e.Property(x => x.Season).HasMaxLength(16).IsRequired();
            e.Property(x => x.HomeTeam).HasMaxLength(64).IsRequired();
            e.Property(x => x.AwayTeam).HasMaxLength(64).IsRequired();
            e.Property(x => x.Venue).HasMaxLength(128);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.League, x.Season, x.KickoffUtc });
            e.HasIndex(x => x.HomeTeam);
            e.HasIndex(x => x.AwayTeam);
        });
    }
}

public sealed class FootballDesignTimeFactory : PluginDesignTimeFactory<FootballDbContext>
{
    protected override DatabaseProvider Provider => DatabaseProvider.MySql;

    protected override string DefaultConnectionString =>
        "Server=localhost;Port=3306;Database=DB_Football;User=football;Password=dev_football;";
}
