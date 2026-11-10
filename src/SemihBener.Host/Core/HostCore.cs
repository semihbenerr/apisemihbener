using System.Reflection;

namespace SemihBener.Host.Core;

public static class HostInfo
{
    public const string AppName = "SemihBener.Host";
    public const string SecretsPurpose = "SemihBener.Secrets.v1";

    public static Version Version { get; } =
        typeof(HostInfo).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);

    public static string InformationalVersion { get; } =
        typeof(HostInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Version.ToString();

    public static DateTime StartedAtUtc { get; } = DateTime.UtcNow;
}

public static class AuthSchemes
{
    public const string Admin = "Admin";

    public static string ForPlugin(string key) => $"plugin:{key}";
}

public static class RateLimitPolicies
{
    public static string Standard(string key) => $"plugin:{key}";
    public static string Strict(string key) => $"plugin:{key}:strict";
}

/// <summary>Host'un çalışma verisinin klasörleri. Host güncellenirken bu klasöre dokunulmaz.</summary>
public sealed class HostPaths
{
    public HostPaths(string root)
    {
        Root = Path.GetFullPath(root);
        Plugins = Path.Combine(Root, "plugins");
        Keys = Path.Combine(Root, "keys");
        Logs = Path.Combine(Root, "logs");
        Temp = Path.Combine(Root, "tmp");

        foreach (var dir in new[] { Root, Plugins, Keys, Logs, Temp })
        {
            Directory.CreateDirectory(dir);
        }
    }

    public string Root { get; }
    public string Plugins { get; }
    public string Keys { get; }
    public string Logs { get; }
    public string Temp { get; }

    public string StateFile => Path.Combine(Root, "host-state.json");
    public string SafeModeFile => Path.Combine(Root, "SAFE_MODE");

    public string PluginVersionDirectory(string key, string version) => Path.Combine(Plugins, key, version);

    public string LoadingMarker(string key) => Path.Combine(Root, $"loading-{key}.marker");

    public IEnumerable<string> ExistingLoadingMarkers() =>
        Directory.EnumerateFiles(Root, "loading-*.marker");
}

/// <summary>Panelden "yeniden başlat" denildiğinde süreci durdurur; IIS ilk istekte tekrar açar.</summary>
public sealed class HostRestarter(IHostApplicationLifetime lifetime, ILogger<HostRestarter> logger)
{
    public void RequestRestart(string reason)
    {
        logger.LogWarning("Yeniden başlatma istendi: {Reason}", reason);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            lifetime.StopApplication();
        });
    }
}
