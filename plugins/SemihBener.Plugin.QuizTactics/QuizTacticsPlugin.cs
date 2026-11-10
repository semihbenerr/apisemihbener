using SemihBener.Abstractions;
using SemihBener.Plugin.QuizTactics.Data;

namespace SemihBener.Plugin.QuizTactics;

public sealed class QuizTacticsPlugin : IApiPlugin
{
    public void ConfigureServices(PluginServiceContext context)
    {
        context.AddDbContext<QuizTacticsDbContext>();
        context.AddJwtIssuer<QuizTacticsPlugin>();
    }
}
