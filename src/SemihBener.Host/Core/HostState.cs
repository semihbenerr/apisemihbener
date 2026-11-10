using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using SemihBener.Abstractions;

namespace SemihBener.Host.Core;

public enum PluginStatus
{
    Pending = 0,
    Active = 1,
    Error = 2,
    Disabled = 3
}

public sealed class AdminAccount
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool TotpEnabled { get; set; }
    public string? TotpSecretProtected { get; set; }
    public string? TotpPendingSecretProtected { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}

public sealed class PluginRecord
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public List<string> InstalledVersions { get; set; } = [];

    /// <summary>Sadece Development ortamında: plugin'in derleme çıktısı klasörü.</summary>
    public string? DevelopmentPath { get; set; }

    public bool Enabled { get; set; } = true;
    public PluginStatus Status { get; set; } = PluginStatus.Pending;
    public string? LastError { get; set; }
    public DateTime? LastLoadedAtUtc { get; set; }
    public DateTime InstalledAtUtc { get; set; }

    public DatabaseProvider Provider { get; set; }
    public string? ConnectionStringProtected { get; set; }
    public string? MySqlServerVersion { get; set; }
    public bool MigrationsEnabled { get; set; }

    public PluginAuthKind AuthKind { get; set; }
    public string? JwtIssuer { get; set; }
    public string? JwtAudience { get; set; }
    public string? JwtSigningKeyProtected { get; set; }
    public int JwtExpiryMinutes { get; set; } = 60 * 24 * 7;
    public string? ApiKeyProtected { get; set; }

    public int RequestsPerMinute { get; set; } = 120;
    public List<string> CorsOrigins { get; set; } = [];
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class HostState
{
    public AdminAccount? Admin { get; set; }
    public List<PluginRecord> Plugins { get; set; } = [];
    public bool RestartRequired { get; set; }
}

/// <summary>
/// Host ayarlarını App_Data/host-state.json dosyasında tutar.
/// Host'un açılışı hiçbir veritabanına bağlı değildir: MySQL kapalı olsa bile panel açılır.
/// Yazma işlemi atomiktir (önce .tmp, sonra yer değiştirme).
/// </summary>
public sealed class HostStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _lock = new();
    private readonly string _file;
    private HostState _state;

    public HostStateStore(HostPaths paths)
    {
        _file = paths.StateFile;
        _state = Load(_file, out var warning);
        LoadWarning = warning;
    }

    public string? LoadWarning { get; }

    public HostState Snapshot()
    {
        lock (_lock)
        {
            return Clone(_state);
        }
    }

    public PluginRecord? FindPlugin(string key)
    {
        lock (_lock)
        {
            var record = _state.Plugins.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            return record is null ? null : Clone(record);
        }
    }

    public string[] GetCorsOrigins(string key)
    {
        lock (_lock)
        {
            var record = _state.Plugins.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            return record is null || !record.Enabled ? [] : record.CorsOrigins.ToArray();
        }
    }

    public void Update(Action<HostState> mutate)
    {
        lock (_lock)
        {
            mutate(_state);
            Save();
        }
    }

    public bool UpdatePlugin(string key, Action<PluginRecord> mutate)
    {
        var found = false;
        Update(state =>
        {
            var record = state.Plugins.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            if (record is not null)
            {
                mutate(record);
                found = true;
            }
        });
        return found;
    }

    private void Save()
    {
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_state, JsonOptions));
        File.Move(tmp, _file, overwrite: true);
    }

    private static HostState Load(string file, out string? warning)
    {
        warning = null;
        if (!File.Exists(file))
        {
            return new HostState();
        }

        try
        {
            return JsonSerializer.Deserialize<HostState>(File.ReadAllText(file), JsonOptions) ?? new HostState();
        }
        catch (JsonException ex)
        {
            var backup = $"{file}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(file, backup, overwrite: true);
            warning = $"host-state.json okunamadı ({ex.Message}). Yedeği alındı: {backup}. Boş ayarlarla açıldı.";
            return new HostState();
        }
    }

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;
}

/// <summary>Connection string, JWT anahtarı, API key gibi sırları Data Protection ile şifreler.</summary>
public sealed class SecretProtector(IDataProtector protector)
{
    public string Protect(string plainText) => protector.Protect(plainText);

    public string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(protectedText);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static string NewRandomSecret(int bytes = 48) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Kayıtta auth türüne göre eksik anahtarları üretir.</summary>
    public void EnsurePluginSecrets(PluginRecord record)
    {
        switch (record.AuthKind)
        {
            case PluginAuthKind.Jwt:
                record.JwtIssuer ??= "api.semihbener.com";
                record.JwtAudience ??= $"{record.Key}-clients";
                if (Unprotect(record.JwtSigningKeyProtected) is null)
                {
                    record.JwtSigningKeyProtected = Protect(NewRandomSecret(64));
                }
                break;

            case PluginAuthKind.ApiKey:
                if (Unprotect(record.ApiKeyProtected) is null)
                {
                    record.ApiKeyProtected = Protect(NewRandomSecret(36));
                }
                break;
        }
    }
}
