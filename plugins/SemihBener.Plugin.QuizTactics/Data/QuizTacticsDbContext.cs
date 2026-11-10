using Microsoft.EntityFrameworkCore;
using SemihBener.Abstractions;

namespace SemihBener.Plugin.QuizTactics.Data;

public sealed class QuizUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int TotalScore { get; set; }
    public int GamesPlayed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<Game> Games { get; set; } = [];
}

public sealed class Game
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public QuizUser User { get; set; } = null!;
    public string Category { get; set; } = "";
    public int Score { get; set; }
    public int CorrectAnswers { get; set; }
    public int TotalQuestions { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime PlayedAtUtc { get; set; }
}

public sealed class QuizTacticsDbContext(DbContextOptions<QuizTacticsDbContext> options) : DbContext(options)
{
    public DbSet<QuizUser> Users => Set<QuizUser>();
    public DbSet<Game> Games => Set<Game>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCharSet("utf8mb4");

        modelBuilder.Entity<QuizUser>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.Username).HasMaxLength(32).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.TotalScore);
        });

        modelBuilder.Entity<Game>(e =>
        {
            e.ToTable("Games");
            e.Property(x => x.Category).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.Games).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.PlayedAtUtc });
        });
    }
}

/// <summary>
/// "dotnet ef migrations add" için. Varsayılan bağlantı docker-compose'daki lokal MySQL'dir;
/// farklı bir veritabanı için SB_DESIGN_CONNECTION ortam değişkeni kullanılır.
/// </summary>
public sealed class QuizTacticsDesignTimeFactory : PluginDesignTimeFactory<QuizTacticsDbContext>
{
    protected override DatabaseProvider Provider => DatabaseProvider.MySql;

    protected override string DefaultConnectionString =>
        "Server=localhost;Port=3306;Database=DB_QuizTactics;User=quiztactics;Password=dev_quiztactics;";
}
