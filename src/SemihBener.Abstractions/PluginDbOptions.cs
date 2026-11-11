using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SemihBener.Abstractions;

public static class PluginDbOptions
{
    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options,
        DatabaseProvider provider,
        string connectionString)
    {
        switch (provider)
        {
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
/// MSSQL'de migration üretmek için veritabanına bağlanmak gerekmez.
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
        PluginDbOptions.Configure(builder, Provider, connectionString);

        return (TContext)Activator.CreateInstance(typeof(TContext), builder.Options)!;
    }
}
