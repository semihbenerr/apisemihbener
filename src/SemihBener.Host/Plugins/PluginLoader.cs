using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Tokens;
using SemihBener.Abstractions;
using SemihBener.Host.Core;
using SemihBener.Host.Security;

namespace SemihBener.Host.Plugins;

/// <summary>
/// Açılışta plugin'leri yükler. Bir plugin'in hatası sadece o plugin'i kapatır; host her durumda açılır.
/// Yükleme sırasında süreç tamamen çökerse (yakalanamayan hata), bir sonraki açılışta o plugin
/// "loading marker" dosyası sayesinde otomatik kapatılır ve IIS çökme döngüsüne girmez.
/// </summary>
public static class PluginLoader
{
    private const string CrashMessage =
        "Önceki açılışta bu plugin yüklenirken süreç çöktü. Otomatik olarak kapatıldı; sorunu giderip panelden tekrar açın.";

    public static PluginCatalog Load(
        WebApplicationBuilder builder,
        AuthenticationBuilder authentication,
        HostStateStore store,
        SecretProtector secrets,
        HostPaths paths)
    {
        var messages = new List<string>();
        store.Update(state => state.RestartRequired = false);

        var safeMode = builder.Configuration.GetValue("Host:SafeMode", false) || File.Exists(paths.SafeModeFile);
        if (safeMode)
        {
            messages.Add("Güvenli mod açık: hiçbir plugin yüklenmedi. Sadece panel çalışıyor.");
            return new PluginCatalog([], safeMode: true, messages);
        }

        HandleCrashMarkers(store, paths, messages);

        if (builder.Environment.IsDevelopment())
        {
            RegisterDevelopmentFolders(builder, store, secrets, messages);
        }

        var result = new List<LoadedPlugin>();
        foreach (var record in store.Snapshot().Plugins.OrderBy(p => p.Key))
        {
            var loaded = new LoadedPlugin { Record = record };
            result.Add(loaded);

            if (!record.Enabled)
            {
                loaded.Status = PluginStatus.Disabled;
                PersistStatus(store, record.Key, PluginStatus.Disabled, error: null, loaded: false);
                continue;
            }

            var marker = paths.LoadingMarker(record.Key);
            File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
            try
            {
                LoadOne(loaded, builder, authentication, secrets, paths);
                loaded.Status = PluginStatus.Active;
                PersistStatus(store, record.Key, PluginStatus.Active, error: null, loaded: true);
            }
            catch (Exception ex)
            {
                loaded.Status = PluginStatus.Error;
                loaded.Error = Describe(ex);
                loaded.Assembly = null;
                loaded.Instance = null;
                PersistStatus(store, record.Key, PluginStatus.Error, loaded.Error, loaded: false);
                messages.Add($"{record.Key}: {loaded.Error}");
            }
            finally
            {
                File.Delete(marker);
            }
        }

        return new PluginCatalog(result, safeMode: false, messages);
    }

    /// <summary>plugin.json'daki bilgileri kayda işler.</summary>
    public static void ApplyManifest(PluginRecord record, PluginManifest manifest)
    {
        record.Name = manifest.Name;
        record.Version = manifest.Version;
        record.Provider = manifest.Provider;
        record.AuthKind = manifest.AuthKind;
        record.MigrationsEnabled = manifest.Migrations;
    }

    public static Type FindPluginType(Assembly assembly)
    {
        var candidates = assembly.GetExportedTypes()
            .Where(t => typeof(IApiPlugin).IsAssignableFrom(t)
                        && t is { IsAbstract: false, IsInterface: false }
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new PluginManifestException("IApiPlugin uygulayan, parametresiz constructor'ı olan public bir sınıf bulunamadı."),
            _ => throw new PluginManifestException("Birden fazla IApiPlugin sınıfı var; sadece bir tane olmalı.")
        };
    }

    public static string Describe(Exception ex) => ex switch
    {
        ReflectionTypeLoadException rtle => "Tipler yüklenemedi: " + string.Join("; ",
            rtle.LoaderExceptions.Where(e => e is not null).Select(e => e!.Message).Distinct().Take(5)),
        TargetInvocationException { InnerException: { } inner } => Describe(inner),
        PluginConfigurationException or PluginManifestException => ex.Message,
        _ => $"{ex.GetType().Name}: {ex.Message}"
    };

    private static void LoadOne(
        LoadedPlugin plugin,
        WebApplicationBuilder builder,
        AuthenticationBuilder authentication,
        SecretProtector secrets,
        HostPaths paths)
    {
        var record = plugin.Record;

        var directory = builder.Environment.IsDevelopment()
                        && record.DevelopmentPath is not null
                        && Directory.Exists(record.DevelopmentPath)
            ? record.DevelopmentPath
            : paths.PluginVersionDirectory(record.Key, record.Version);

        if (!Directory.Exists(directory))
        {
            throw new PluginManifestException($"Plugin dosyaları bulunamadı: {directory}");
        }

        plugin.SourceDirectory = directory;

        var manifest = PluginManifest.Read(Path.Combine(directory, "plugin.json"));
        manifest.Validate(HostInfo.Version);
        if (!string.Equals(manifest.Key, record.Key, StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginManifestException($"plugin.json key'i ({manifest.Key}) kayıttaki key ile ({record.Key}) uyuşmuyor.");
        }

        plugin.Manifest = manifest;

        var entryPath = Path.Combine(directory, manifest.EntryAssembly);
        if (!File.Exists(entryPath))
        {
            throw new PluginManifestException($"{manifest.EntryAssembly} bulunamadı.");
        }

        var loadContext = new PluginLoadContext($"plugin:{record.Key}", directory);
        var assembly = loadContext.LoadFromBytes(entryPath);
        var instance = (IApiPlugin)Activator.CreateInstance(FindPluginType(assembly))!;

        var database = new PluginDatabaseSettings
        {
            Provider = record.Provider,
            ConnectionString = secrets.Unprotect(record.ConnectionStringProtected)
        };

        JwtSettings? jwt = null;
        string? apiKey = null;

        switch (record.AuthKind)
        {
            case PluginAuthKind.Jwt:
                var signingKey = secrets.Unprotect(record.JwtSigningKeyProtected)
                                 ?? throw new PluginConfigurationException("JWT imza anahtarı okunamadı. Panelden yeni anahtar üretin.");
                jwt = new JwtSettings(
                    record.JwtIssuer ?? "api.semihbener.com",
                    record.JwtAudience ?? $"{record.Key}-clients",
                    signingKey,
                    record.JwtExpiryMinutes);
                break;

            case PluginAuthKind.ApiKey:
                apiKey = secrets.Unprotect(record.ApiKeyProtected)
                         ?? throw new PluginConfigurationException("API anahtarı okunamadı. Panelden yeni anahtar üretin.");
                break;
        }

        // Plugin önce ayrı bir koleksiyona kayıt yapar; hata olursa host'a yarım kayıt kalmaz.
        var staging = new ServiceCollection();
        var serviceContext = new PluginServiceContext(
            staging,
            new PluginInfo(record.Key, record.Name, record.Version),
            database,
            jwt,
            record.Settings);

        instance.ConfigureServices(serviceContext);

        foreach (var descriptor in staging)
        {
            builder.Services.Add(descriptor);
        }

        var scheme = AuthSchemes.ForPlugin(record.Key);
        if (jwt is not null)
        {
            authentication.AddJwtBearer(scheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "name"
                };
            });
        }
        else if (apiKey is not null)
        {
            authentication.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                scheme, options => options.ApiKey = apiKey);
        }

        plugin.Assembly = assembly;
        plugin.Instance = instance;
        plugin.DbContextTypes = serviceContext.DbContextTypes.ToList();
    }

    private static void HandleCrashMarkers(HostStateStore store, HostPaths paths, List<string> messages)
    {
        foreach (var marker in paths.ExistingLoadingMarkers().ToList())
        {
            var key = Path.GetFileNameWithoutExtension(marker)["loading-".Length..];
            store.UpdatePlugin(key, record =>
            {
                record.Enabled = false;
                record.Status = PluginStatus.Error;
                record.LastError = CrashMessage;
            });
            messages.Add($"{key}: {CrashMessage}");
            File.Delete(marker);
        }
    }

    private static void RegisterDevelopmentFolders(
        WebApplicationBuilder builder,
        HostStateStore store,
        SecretProtector secrets,
        List<string> messages)
    {
        var folders = builder.Configuration.GetSection("Plugins:DevelopmentFolders").Get<string[]>() ?? [];
        foreach (var folder in folders)
        {
            var fullPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, folder));
            var manifestPath = Path.Combine(fullPath, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                messages.Add($"Geliştirme klasöründe plugin.json yok (önce tüm solution'ı derleyin): {fullPath}");
                continue;
            }

            try
            {
                var manifest = PluginManifest.Read(manifestPath);
                manifest.Validate(HostInfo.Version);

                store.Update(state =>
                {
                    var record = state.Plugins.FirstOrDefault(p => p.Key == manifest.Key);
                    if (record is null)
                    {
                        record = new PluginRecord { Key = manifest.Key, InstalledAtUtc = DateTime.UtcNow };
                        state.Plugins.Add(record);
                    }

                    ApplyManifest(record, manifest);
                    record.DevelopmentPath = fullPath;
                    secrets.EnsurePluginSecrets(record);
                });
            }
            catch (Exception ex)
            {
                messages.Add($"Geliştirme plugin'i okunamadı ({fullPath}): {Describe(ex)}");
            }
        }
    }

    private static void PersistStatus(HostStateStore store, string key, PluginStatus status, string? error, bool loaded)
    {
        store.UpdatePlugin(key, record =>
        {
            record.Status = status;
            record.LastError = error;
            if (loaded)
            {
                record.LastLoadedAtUtc = DateTime.UtcNow;
            }
        });
    }
}
