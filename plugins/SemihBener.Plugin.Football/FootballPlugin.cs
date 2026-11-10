using SemihBener.Abstractions;
using SemihBener.Plugin.Football.Data;

namespace SemihBener.Plugin.Football;

public sealed class FootballPlugin : IApiPlugin
{
    public void ConfigureServices(PluginServiceContext context)
    {
        context.AddDbContext<FootballDbContext>();
    }
}
