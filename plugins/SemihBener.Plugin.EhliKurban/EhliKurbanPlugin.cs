using SemihBener.Abstractions;
using SemihBener.Plugin.EhliKurban.Data;

namespace SemihBener.Plugin.EhliKurban;

/// <summary>
/// Mevcut MSSQL veritabanını kullanır; API tarafında migration çalıştırılmaz (plugin.json: migrations=false).
/// Data klasöründeki model örnektir: gerçek tablolar için README'deki scaffold komutuyla yeniden üretilir.
/// </summary>
public sealed class EhliKurbanPlugin : IApiPlugin
{
    public void ConfigureServices(PluginServiceContext context)
    {
        context.AddDbContext<EhliKurbanDbContext>();
    }
}
