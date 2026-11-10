using SemihBener.Host.Core;
using SemihBener.Host.Plugins;
using SemihBener.Host.Web;

namespace SemihBener.Host.Areas.Admin.Models;

public sealed class LoginViewModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? TotpCode { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
}

public sealed class SetupViewModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
    public string? SetupToken { get; set; }
    public bool RequiresToken { get; set; }
    public string? Error { get; set; }
}

public sealed class SecurityViewModel
{
    public string Username { get; set; } = "";
    public bool TotpEnabled { get; set; }
    public string? PendingSecret { get; set; }
    public string? PendingUri { get; set; }
}

public sealed class DashboardViewModel
{
    public required IReadOnlyList<PluginRecord> Records { get; init; }
    public required PluginCatalog Catalog { get; init; }
    public bool RestartRequired { get; init; }
    public bool SafeModeFile { get; init; }
    public string Environment { get; init; } = "";
    public string DataPath { get; init; } = "";
    public long WorkingSetMb { get; init; }
    public long ManagedMemoryMb { get; init; }
    public TimeSpan Uptime { get; init; }
}

public sealed class PluginDetailsViewModel
{
    public required PluginRecord Record { get; init; }
    public LoadedPlugin? Loaded { get; init; }
    public string? ConnectionStringMasked { get; init; }
    public bool HasConnectionString { get; init; }
    public string? ApiKey { get; init; }
    public IReadOnlyList<ContextStatus> DatabaseStatus { get; init; } = [];
    public string? DatabaseStatusError { get; init; }
    public string CorsText => string.Join(Environment.NewLine, Record.CorsOrigins);
    public string SettingsText => string.Join(Environment.NewLine, Record.Settings.Select(kv => $"{kv.Key}={kv.Value}"));
}

public sealed class LogsViewModel
{
    public required IReadOnlyList<LogEntry> Entries { get; init; }
    public required IReadOnlyList<string> PluginKeys { get; init; }
    public string? SelectedPlugin { get; init; }
}
