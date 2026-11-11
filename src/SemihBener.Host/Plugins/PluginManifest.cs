using System.Text.Json;
using System.Text.RegularExpressions;
using SemihBener.Abstractions;

namespace SemihBener.Host.Plugins;

/// <summary>Plugin zip'inin kökündeki plugin.json dosyası.</summary>
public sealed partial class PluginManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> ReservedKeys = ["admin", "swagger", "openapi", "health", "host", "api"];

    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string EntryAssembly { get; set; } = "";
    public string Database { get; set; } = "none";
    public string Auth { get; set; } = "none";
    public bool Migrations { get; set; }
    public string MinHostVersion { get; set; } = "1.0.0";

    public DatabaseProvider Provider => Database.Trim().ToLowerInvariant() switch
    {
        "mssql" or "sqlserver" => DatabaseProvider.SqlServer,
        _ => DatabaseProvider.None
    };

    public PluginAuthKind AuthKind => Auth.Trim().ToLowerInvariant() switch
    {
        "jwt" => PluginAuthKind.Jwt,
        "apikey" => PluginAuthKind.ApiKey,
        _ => PluginAuthKind.None
    };

    public static PluginManifest Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new PluginManifestException("plugin.json bulunamadı.");
        }

        try
        {
            return JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), JsonOptions)
                   ?? throw new PluginManifestException("plugin.json boş.");
        }
        catch (JsonException ex)
        {
            throw new PluginManifestException($"plugin.json okunamadı: {ex.Message}");
        }
    }

    public void Validate(Version hostVersion)
    {
        Key = Key.Trim().ToLowerInvariant();

        if (!KeyRegex().IsMatch(Key))
            throw new PluginManifestException("key 2-32 karakter olmalı; küçük harfle başlamalı, sadece küçük harf ve rakam içermeli.");
        if (ReservedKeys.Contains(Key))
            throw new PluginManifestException($"'{Key}' ayrılmış bir isim, kullanılamaz.");
        if (string.IsNullOrWhiteSpace(Name))
            throw new PluginManifestException("name boş olamaz.");
        if (!System.Version.TryParse(Version, out _))
            throw new PluginManifestException("version '1.0.0' biçiminde olmalı.");
        if (string.IsNullOrWhiteSpace(EntryAssembly)
            || !EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || EntryAssembly.IndexOfAny(['/', '\\']) >= 0)
            throw new PluginManifestException("entryAssembly sadece dll dosya adı olmalı (örn. SemihBener.Plugin.X.dll).");
        if (Database.Trim().ToLowerInvariant() is not ("mssql" or "sqlserver" or "none"))
            throw new PluginManifestException("database: mssql veya none olmalı.");
        if (Auth.Trim().ToLowerInvariant() is not ("jwt" or "apikey" or "none"))
            throw new PluginManifestException("auth: jwt, apikey veya none olmalı.");
        if (Migrations && Provider == DatabaseProvider.None)
            throw new PluginManifestException("Veritabanı olmayan plugin'de migrations: true olamaz.");
        if (!System.Version.TryParse(MinHostVersion, out var minHost))
            throw new PluginManifestException("minHostVersion '1.0.0' biçiminde olmalı.");
        if (minHost > hostVersion)
            throw new PluginManifestException($"Bu plugin en az host {minHost} istiyor; çalışan host {hostVersion}.");
    }

    [GeneratedRegex("^[a-z][a-z0-9]{1,31}$")]
    private static partial Regex KeyRegex();
}

public sealed class PluginManifestException(string message) : Exception(message)
{
}
