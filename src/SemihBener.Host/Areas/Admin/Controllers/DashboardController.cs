using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SemihBener.Host.Areas.Admin.Models;
using SemihBener.Host.Core;
using SemihBener.Host.Plugins;

namespace SemihBener.Host.Areas.Admin.Controllers;

public sealed class DashboardController(
    HostStateStore store,
    PluginCatalog catalog,
    HostPaths paths,
    HostRestarter restarter,
    IWebHostEnvironment environment) : AdminControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        var state = store.Snapshot();
        using var process = Process.GetCurrentProcess();

        return View(new DashboardViewModel
        {
            Records = state.Plugins.OrderBy(p => p.Key).ToList(),
            Catalog = catalog,
            RestartRequired = state.RestartRequired,
            SafeModeFile = System.IO.File.Exists(paths.SafeModeFile),
            Environment = environment.EnvironmentName,
            DataPath = paths.Root,
            WorkingSetMb = process.WorkingSet64 / 1024 / 1024,
            ManagedMemoryMb = GC.GetTotalMemory(forceFullCollection: false) / 1024 / 1024,
            Uptime = DateTime.UtcNow - HostInfo.StartedAtUtc
        });
    }

    [HttpPost]
    public IActionResult Restart()
    {
        restarter.RequestRestart("Panelden yeniden başlatma");
        return View("Restarting", environment.IsDevelopment());
    }

    /// <summary>Güvenli mod: bir sonraki açılışta hiçbir plugin yüklenmez, sadece panel çalışır.</summary>
    [HttpPost]
    public IActionResult SafeMode(bool enable)
    {
        if (enable)
        {
            System.IO.File.WriteAllText(paths.SafeModeFile, DateTime.UtcNow.ToString("O"));
            Flash("Güvenli mod açıldı. Yeniden başlatınca plugin'ler yüklenmeyecek.");
        }
        else
        {
            System.IO.File.Delete(paths.SafeModeFile);
            Flash("Güvenli mod kapatıldı. Yeniden başlatınca plugin'ler yüklenecek.");
        }

        store.Update(state => state.RestartRequired = true);
        return RedirectToAction(nameof(Index));
    }
}
