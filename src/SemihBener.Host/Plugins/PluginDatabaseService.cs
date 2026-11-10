using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SemihBener.Abstractions;

namespace SemihBener.Host.Plugins;

public sealed record ContextStatus(
    string ContextName,
    bool CanConnect,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Pending,
    string? Error);

/// <summary>Panelin veritabanı işlemleri: bağlantı testi, migration durumu, migration uygulama, SQL script.</summary>
public sealed class PluginDatabaseService(IServiceScopeFactory scopeFactory)
{
    public static async Task<(bool Success, string Message)> TestConnectionAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        try
        {
            DbConnection connection = provider switch
            {
                DatabaseProvider.MySql => new MySqlConnector.MySqlConnection(connectionString),
                DatabaseProvider.SqlServer => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
                _ => throw new InvalidOperationException("Bu plugin veritabanı kullanmıyor.")
            };

            await using (connection)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await connection.OpenAsync(timeout.Token);
                return (true, $"Bağlantı başarılı. Sunucu sürümü: {connection.ServerVersion}");
            }
        }
        catch (OperationCanceledException)
        {
            return (false, "Bağlantı 10 saniye içinde kurulamadı.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<ContextStatus>> GetStatusAsync(LoadedPlugin plugin, CancellationToken cancellationToken)
    {
        var result = new List<ContextStatus>();
        foreach (var contextType in plugin.DbContextTypes)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            try
            {
                var db = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
                var canConnect = await db.Database.CanConnectAsync(cancellationToken);
                if (!canConnect)
                {
                    result.Add(new ContextStatus(contextType.Name, false, [], db.Database.GetMigrations().ToList(), "Veritabanına bağlanılamadı."));
                    continue;
                }

                var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                result.Add(new ContextStatus(contextType.Name, true, applied, pending, null));
            }
            catch (Exception ex)
            {
                result.Add(new ContextStatus(contextType.Name, false, [], [], ex.Message));
            }
        }

        return result;
    }

    public async Task<string> MigrateAsync(LoadedPlugin plugin, CancellationToken cancellationToken)
    {
        if (!plugin.Record.MigrationsEnabled)
        {
            return "Bu plugin için migration kapalı (plugin.json: migrations=false).";
        }

        var report = new StringBuilder();
        foreach (var contextType in plugin.DbContextTypes)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            await db.Database.MigrateAsync(cancellationToken);
            report.Append($"{contextType.Name}: {pending.Count} migration uygulandı. ");
        }

        return report.Length == 0 ? "Bu plugin'de DbContext yok." : report.ToString().Trim();
    }

    public string GenerateScript(LoadedPlugin plugin)
    {
        var script = new StringBuilder();
        script.AppendLine($"-- {plugin.Record.Name} {plugin.Record.Version} — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

        foreach (var contextType in plugin.DbContextTypes)
        {
            using var scope = scopeFactory.CreateScope();
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            var migrator = db.GetService<IMigrator>();

            // MySQL (Pomelo) idempotent script desteklemez; SQL Server'da idempotent üretilir.
            var options = plugin.Record.Provider == DatabaseProvider.SqlServer
                ? MigrationsSqlGenerationOptions.Idempotent
                : MigrationsSqlGenerationOptions.Default;

            script.AppendLine($"-- Context: {contextType.Name}");
            script.AppendLine(migrator.GenerateScript(fromMigration: null, toMigration: null, options));
        }

        return script.ToString();
    }

    public async Task<IReadOnlyList<(string Context, bool Ok)>> HealthAsync(LoadedPlugin plugin, IServiceProvider requestServices, CancellationToken cancellationToken)
    {
        var result = new List<(string, bool)>();
        foreach (var contextType in plugin.DbContextTypes)
        {
            var db = (DbContext)requestServices.GetRequiredService(contextType);
            bool ok;
            try
            {
                ok = await db.Database.CanConnectAsync(cancellationToken);
            }
            catch
            {
                ok = false;
            }

            result.Add((contextType.Name, ok));
        }

        return result;
    }
}
