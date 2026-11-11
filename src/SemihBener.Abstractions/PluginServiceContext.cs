using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SemihBener.Abstractions;

public enum DatabaseProvider
{
    None = 0,
    SqlServer = 1
}

public enum PluginAuthKind
{
    None = 0,
    Jwt = 1,
    ApiKey = 2
}

public sealed record PluginInfo(string Key, string Name, string Version);

public sealed record JwtSettings(string Issuer, string Audience, string SigningKey, int ExpiryMinutes);

public sealed class PluginDatabaseSettings
{
    public DatabaseProvider Provider { get; init; }
    public string? ConnectionString { get; init; }

    public bool IsConfigured => Provider != DatabaseProvider.None && !string.IsNullOrWhiteSpace(ConnectionString);
}

/// <summary>Plugin ayarı eksik veya hatalı olduğunda fırlatılır; host plugin'i "Hata" durumuna alır.</summary>
public sealed class PluginConfigurationException(string message) : Exception(message)
{
}

/// <summary>
/// Host'un plugin'e verdiği kayıt bağlamı. Bağlantı bilgileri ve anahtarlar panelden gelir,
/// plugin bunları hiçbir zaman kendi dosyalarında tutmaz.
/// </summary>
public sealed class PluginServiceContext(
    IServiceCollection services,
    PluginInfo info,
    PluginDatabaseSettings database,
    JwtSettings? jwt,
    IReadOnlyDictionary<string, string> settings)
{
    private readonly List<Type> _dbContextTypes = [];

    public IServiceCollection Services { get; } = services;
    public PluginInfo Info { get; } = info;
    public PluginDatabaseSettings Database { get; } = database;
    public JwtSettings? Jwt { get; } = jwt;

    /// <summary>Panelden girilen serbest anahtar/değer ayarları.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; } = settings;

    public IReadOnlyList<Type> DbContextTypes => _dbContextTypes;

    /// <summary>
    /// Plugin'in DbContext'ini, panelde tanımlanan bağlantı ve provider ile kaydeder.
    /// </summary>
    public void AddDbContext<TContext>() where TContext : DbContext
    {
        if (!Database.IsConfigured)
        {
            throw new PluginConfigurationException(
                $"'{Info.Name}' için veritabanı bağlantısı tanımlanmamış. Panelden bağlantıyı girip yeniden başlatın.");
        }

        var provider = Database.Provider;
        var connectionString = Database.ConnectionString!;

        Services.AddDbContext<TContext>(options =>
            PluginDbOptions.Configure(options, provider, connectionString));

        _dbContextTypes.Add(typeof(TContext));
    }

    /// <summary>
    /// Bu plugin'e özel JWT üreticisini kaydeder. Controller'larda
    /// <c>JwtTokenIssuer&lt;TMarker&gt;</c> olarak inject edilir.
    /// </summary>
    public void AddJwtIssuer<TMarker>()
    {
        if (Jwt is null)
        {
            throw new PluginConfigurationException(
                $"'{Info.Name}' için JWT ayarı yok. plugin.json içinde \"auth\": \"jwt\" olmalı.");
        }

        Services.AddSingleton(new JwtTokenIssuer<TMarker>(Jwt));
    }
}
