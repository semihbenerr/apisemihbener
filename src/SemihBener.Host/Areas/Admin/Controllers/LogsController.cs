using Microsoft.AspNetCore.Mvc;
using SemihBener.Host.Areas.Admin.Models;
using SemihBener.Host.Plugins;
using SemihBener.Host.Web;

namespace SemihBener.Host.Areas.Admin.Controllers;

public sealed class LogsController(InMemoryLogStore logStore, PluginCatalog catalog) : AdminControllerBase
{
    [HttpGet]
    public IActionResult Index(string? plugin)
    {
        IEnumerable<LogEntry> entries = logStore.Snapshot();

        if (!string.IsNullOrWhiteSpace(plugin))
        {
            var prefix = catalog.Find(plugin)?.LogCategoryPrefix;
            entries = prefix is null
                ? Enumerable.Empty<LogEntry>()
                : entries.Where(e => e.Category.StartsWith(prefix, StringComparison.Ordinal));
        }

        return View(new LogsViewModel
        {
            Entries = entries.Take(300).ToList(),
            PluginKeys = catalog.All.Select(p => p.Key).ToList(),
            SelectedPlugin = plugin
        });
    }
}
