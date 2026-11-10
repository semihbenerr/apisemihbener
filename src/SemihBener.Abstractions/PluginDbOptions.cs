using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SemihBener.Abstractions;

public static class PluginDbOptions
{
    /// <summary>
    /// MySQL sürümü sabit verilir. AutoDetect açılışta veritabanına bağlanır;
    /// o an DB kapalıysa plugin hiç açılmaz.
    /// </summary>
    public const string DefaultMySqlServerVersion = "8.0.36-mysql";

    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options,
        DatabaseProvider provider,
        string connectionString,
        string? mySqlServerVersion = null)
    {
        switch (provider)
        {
            case DatabaseProvider.MySql:
                var version = string.IsNullOrWhiteSpace(mySqlServerVersion) ? DefaultMySqlServerVersion : mySqlServerVersion;
                options.UseMySql(connectionString, ServerVersion.Parse(version), mysql =>
                {
                    mysql.EnableRetryOnFailure(3);
                    mysql.CommandTimeout(30);
                });
                break;

            case DatabaseProvider.SqlServer:
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure(3);
                    sql.CommandTimeout(30);
                });
                break;

            default:
                throw new PluginConfigurationException($"Desteklenmeyen veritabanı türü: {provider}");
        }

        return options;
    }
}

/// <summary>
/// "dotnet ef migrations add" için tasarım zamanı fabrikası. Plugin projesinde bir alt sınıf açılır.
/// Bağlantı: SB_DESIGN_CONNECTION ortam değişkeni, yoksa <see cref="DefaultConnectionString"/>.
/// MySQL'de migration üretmek için veritabanına bağlanmak gerekmez (sürüm sabit).
/// </summary>
public abstract class PluginDesignTimeFactory<TContext> : IDesignTimeDbContextFactory<TContext>
    where TContext : DbContext
{
    protected abstract DatabaseProvider Provider { get; }
    protected abstract string DefaultConnectionString { get; }

    public TContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SB_DESIGN_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DefaultConnectionString;
        }

        var builder = new DbContextOptionsBuilder<TContext>();
        PluginDbOptions.Configure(builder, Provider, connectionString, Environment.GetEnvironmentVariable("SB_MYSQL_VERSION"));

        return (TContext)Activator.CreateInstance(typeof(TContext), builder.Options)!;
    }
}
