using System.Text;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using SemihBener.Abstractions;
using SemihBener.Host.Areas.Admin.Models;
using SemihBener.Host.Core;
using SemihBener.Host.Plugins;

namespace SemihBener.Host.Areas.Admin.Controllers;

public sealed class PluginsController(
    HostStateStore store,
    PluginCatalog catalog,
    SecretProtector secrets,
    PluginInstaller installer,
    PluginDatabaseService databases,
    ILogger<PluginsController> logger) : AdminControllerBase
{
    // ---------------- Yükleme ----------------

    [HttpGet]
    public IActionResult Upload() => View();

    [HttpPost]
    [RequestSizeLimit(PluginInstaller.MaxZipBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = PluginInstaller.MaxZipBytes)]
    [DisableRequestTimeout]
    public IActionResult Upload(IFormFile? package)
    {
        if (package is null || package.Length == 0)
        {
            FlashError("Bir zip dosyası seçin.");
            return RedirectToAction(nameof(Upload));
        }

        if (!package.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            FlashError("Sadece .zip dosyası yüklenebilir.");
            return RedirectToAction(nameof(Upload));
        }

        try
        {
            using var stream = package.OpenReadStream();
            var result = installer.Install(stream);
            Flash(result.IsNew
                ? $"{result.Name} {result.Version} yüklendi. Veritabanı bağlantısını girip yeniden başlatın."
                : $"{result.Name} {result.Version} yüklendi. Yeniden başlatınca yeni sürüm devreye girer.");
            return RedirectToAction(nameof(Details), new { id = result.Key });
        }
        catch (PluginInstallException ex)
        {
            FlashError(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin yüklenemedi.");
            FlashError($"Yükleme başarısız: {ex.Message}");
        }

        return RedirectToAction(nameof(Upload));
    }

    // ---------------- Detay ----------------

    [HttpGet]
    public async Task<IActionResult> Details(string id, CancellationToken cancellationToken)
    {
        var record = store.FindPlugin(id);
        if (record is null)
        {
            return NotFound();
        }

        var loaded = catalog.Find(id);
        var connectionString = secrets.Unprotect(record.ConnectionStringProtected);

        IReadOnlyList<ContextStatus> dbStatus = [];
        string? dbError = null;
        if (loaded is { IsActive: true } && loaded.DbContextTypes.Count > 0)
        {
            try
            {
                dbStatus = await databases.GetStatusAsync(loaded, cancellationToken);
            }
            catch (Exception ex)
            {
                dbError = ex.Message;
            }
        }

        return View(new PluginDetailsViewModel
        {
            Record = record,
            Loaded = loaded,
            HasConnectionString = connectionString is not null,
            ConnectionStringMasked = MaskConnectionString(connectionString),
            ApiKey = record.AuthKind == PluginAuthKind.ApiKey ? secrets.Unprotect(record.ApiKeyProtected) : null,
            DatabaseStatus = dbStatus,
            DatabaseStatusError = dbError
        });
    }

    // ---------------- Veritabanı ----------------

    [HttpPost]
    public IActionResult SaveDatabase(string id, string? connectionString)
    {
        var updated = store.UpdatePlugin(id, record =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                record.ConnectionStringProtected = secrets.Protect(connectionString.Trim());
            }
        });

        if (!updated)
        {
            return NotFound();
        }

        store.Update(state => state.RestartRequired = true);
        Flash("Veritabanı ayarları kaydedildi. Geçerli olması için yeniden başlatın.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> TestDatabase(string id, string? connectionString, CancellationToken cancellationToken)
    {
        var record = store.FindPlugin(id);
        if (record is null)
        {
            return NotFound();
        }

        var toTest = string.IsNullOrWhiteSpace(connectionString)
            ? secrets.Unprotect(record.ConnectionStringProtected)
            : connectionString.Trim();

        if (toTest is null)
        {
            FlashError("Test edilecek bağlantı yok. Önce bağlantı bilgisini girin.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var (success, message) = await PluginDatabaseService.TestConnectionAsync(record.Provider, toTest, cancellationToken);
        if (success)
        {
            Flash(message);
        }
        else
        {
            FlashError($"Bağlantı başarısız: {message}");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Migrate(string id, CancellationToken cancellationToken)
    {
        var loaded = catalog.Find(id);
        if (loaded is not { IsActive: true })
        {
            FlashError("Migration için plugin'in aktif olması gerekiyor (bağlantıyı girip yeniden başlatın).");
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            Flash(await databases.MigrateAsync(loaded, cancellationToken));
            logger.LogWarning("{Plugin} migration'ları panelden uygulandı.", id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Plugin} migration başarısız.", id);
            FlashError($"Migration başarısız: {ex.Message}");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public IActionResult Script(string id)
    {
        var loaded = catalog.Find(id);
        if (loaded is not { IsActive: true })
        {
            FlashError("SQL script için plugin'in aktif olması gerekiyor.");
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            var script = databases.GenerateScript(loaded);
            return File(Encoding.UTF8.GetBytes(script), "application/sql", $"{id}-{loaded.Record.Version}.sql");
        }
        catch (Exception ex)
        {
            FlashError($"Script üretilemedi: {ex.Message}");
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    // ---------------- Ayarlar ----------------

    [HttpPost]
    public IActionResult SaveSettings(string id, int requestsPerMinute, int jwtExpiryMinutes, string? corsOrigins, string? customSettings)
    {
        var origins = (corsOrigins ?? "")
            .Split(['\n', '\r', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(o => o.TrimEnd('/'))
            .ToList();

        var invalid = origins.FirstOrDefault(o =>
            !Uri.TryCreate(o, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.AbsolutePath != "/");
        if (invalid is not null)
        {
            FlashError($"Geçersiz CORS adresi: {invalid}. Örnek: https://site.vercel.app");
            return RedirectToAction(nameof(Details), new { id });
        }

        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (customSettings ?? "").Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                FlashError($"Ayar satırı 'anahtar=değer' biçiminde olmalı: {line}");
                return RedirectToAction(nameof(Details), new { id });
            }

            settings[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        var updated = store.UpdatePlugin(id, record =>
        {
            record.RequestsPerMinute = Math.Clamp(requestsPerMinute, 1, 100_000);
            record.JwtExpiryMinutes = Math.Clamp(jwtExpiryMinutes, 5, 60 * 24 * 90);
            record.CorsOrigins = origins.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            record.Settings = settings;
        });

        if (!updated)
        {
            return NotFound();
        }

        store.Update(state => state.RestartRequired = true);
        Flash("Ayarlar kaydedildi. CORS hemen geçerli; rate limit, token süresi ve özel ayarlar yeniden başlatınca geçerli olur.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public IActionResult Toggle(string id)
    {
        var enabled = false;
        var updated = store.UpdatePlugin(id, record =>
        {
            record.Enabled = !record.Enabled;
            enabled = record.Enabled;
            if (enabled && record.Status == PluginStatus.Error)
            {
                record.Status = PluginStatus.Pending;
            }
        });

        if (!updated)
        {
            return NotFound();
        }

        store.Update(state => state.RestartRequired = true);
        Flash(enabled ? "Plugin açıldı. Yeniden başlatınca yüklenecek." : "Plugin kapatıldı. Yeniden başlatınca devre dışı kalacak.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public IActionResult RegenerateSecret(string id)
    {
        var record = store.FindPlugin(id);
        if (record is null)
        {
            return NotFound();
        }

        store.UpdatePlugin(id, r =>
        {
            r.JwtSigningKeyProtected = null;
            r.ApiKeyProtected = null;
            secrets.EnsurePluginSecrets(r);
        });

        store.Update(state => state.RestartRequired = true);
        Flash(record.AuthKind == PluginAuthKind.Jwt
            ? "Yeni JWT imza anahtarı üretildi. Yeniden başlatınca tüm kullanıcıların tekrar giriş yapması gerekecek."
            : "Yeni API anahtarı üretildi. Yeniden başlatınca eski anahtar geçersiz olur; uygulamaları güncelleyin.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public IActionResult ActivateVersion(string id, string version)
    {
        var record = store.FindPlugin(id);
        if (record is null || !record.InstalledVersions.Contains(version))
        {
            FlashError("Bu sürüm yüklü değil.");
            return RedirectToAction(nameof(Details), new { id });
        }

        store.UpdatePlugin(id, r =>
        {
            r.Version = version;
            r.DevelopmentPath = null;
            r.Status = PluginStatus.Pending;
            r.LastError = null;
        });

        store.Update(state => state.RestartRequired = true);
        Flash($"{version} sürümü seçildi. Yeniden başlatınca devreye girer.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public IActionResult Delete(string id, string? confirmKey)
    {
        if (!string.Equals(id, confirmKey?.Trim(), StringComparison.Ordinal))
        {
            FlashError("Silmek için plugin key'ini aynen yazın.");
            return RedirectToAction(nameof(Details), new { id });
        }

        installer.Remove(id);
        logger.LogWarning("Plugin silindi: {Plugin}", id);
        Flash($"{id} silindi. Veritabanına dokunulmadı. Yeniden başlatınca tamamen kalkar.");
        return RedirectToAction("Index", "Dashboard");
    }

    private static string? MaskConnectionString(string? connectionString)
    {
        if (connectionString is null)
        {
            return null;
        }

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var separator = part.IndexOf('=');
                if (separator < 0)
                {
                    return part;
                }

                var name = part[..separator].Trim();
                return name.Equals("password", StringComparison.OrdinalIgnoreCase)
                       || name.Equals("pwd", StringComparison.OrdinalIgnoreCase)
                    ? $"{name}=••••••"
                    : part;
            });

        return string.Join(';', parts);
    }
}
