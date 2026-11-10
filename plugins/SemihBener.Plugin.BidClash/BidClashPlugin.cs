using SemihBener.Abstractions;
using SemihBener.Plugin.BidClash.Data;

namespace SemihBener.Plugin.BidClash;

public sealed class BidClashPlugin : IApiPlugin
{
    public void ConfigureServices(PluginServiceContext context)
    {
        context.AddDbContext<BidClashDbContext>();
        context.AddJwtIssuer<BidClashPlugin>();
    }
}
