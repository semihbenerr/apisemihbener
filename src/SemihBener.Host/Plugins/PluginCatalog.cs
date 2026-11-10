using System.Reflection;
using SemihBener.Abstractions;
using SemihBener.Host.Core;

namespace SemihBener.Host.Plugins;

/// <summary>Açılışta yüklenen bir plugin'in çalışma zamanı bilgisi.</summary>
public sealed class LoadedPlugin
{
    public required PluginRecord Record { get; init; }
    public PluginManifest? Manifest { get; set; }
    public string? SourceDirectory { get; set; }
    public Assembly? Assembly { get; set; }
    public IApiPlugin? Instance { get; set; }
    public IReadOnlyList<Type> DbContextTypes { get; set; } = [];
    public PluginStatus Status { get; set; } = PluginStatus.Pending;
    public string? Error { get; set; }

    public string Key => Record.Key;
    public bool IsActive => Status == PluginStatus.Active && Instance is not null && Assembly is not null;

    /// <summary>Loglarda bu plugin'e ait kayıtları ayırt etmek için kullanılan namespace öneki.</summary>
    public string? LogCategoryPrefix => Assembly?.GetName().Name;
}

public sealed class PluginCatalog(IReadOnlyList<LoadedPlugin> plugins, bool safeMode, IReadOnlyList<string> messages)
{
    private readonly Dictionary<Assembly, LoadedPlugin> _byAssembly = plugins
        .Where(p => p.IsActive)
        .ToDictionary(p => p.Assembly!, p => p);

    public IReadOnlyList<LoadedPlugin> All { get; } = plugins;
    public IEnumerable<LoadedPlugin> Active => All.Where(p => p.IsActive);
    public bool SafeMode { get; } = safeMode;
    public IReadOnlyList<string> StartupMessages { get; } = messages;

    public LoadedPlugin? Find(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    public LoadedPlugin? FindByAssembly(Assembly assembly) =>
        _byAssembly.TryGetValue(assembly, out var plugin) ? plugin : null;
}
