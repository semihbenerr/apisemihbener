using System.Reflection;
using System.Runtime.Loader;

namespace SemihBener.Host.Plugins;

/// <summary>
/// Her plugin kendi load context'inde yüklenir.
/// Kural 1: Host'ta bulunan her assembly (ASP.NET Core, EF Core, Pomelo, Abstractions...) her zaman
///          host'tan gelir. Böylece tip uyumu bozulmaz.
/// Kural 2: Host'ta olmayan, plugin'e özel bağımlılıklar plugin klasöründen yüklenir.
/// Kural 3: Dosyalar bellekten (stream) yüklenir; IIS DLL'i kilitlemez, güncellemede üzerine yazılabilir.
/// </summary>
public sealed class PluginLoadContext(string name, string directory, bool isCollectible = false)
    : AssemblyLoadContext(name, isCollectible)
{
    private static readonly Lazy<Dictionary<string, string>> HostAssemblies = new(() =>
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";
        foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            result.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }
        return result;
    });

    public string Directory { get; } = directory;

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var simpleName = assemblyName.Name;
        if (simpleName is null)
        {
            return null;
        }

        if (HostAssemblies.Value.TryGetValue(simpleName, out var hostPath))
        {
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
            {
                // Plugin farklı bir sürümle derlenmiş olabilir; yine host'unkini kullan.
                return Default.LoadFromAssemblyPath(hostPath);
            }
        }

        var localPath = Path.Combine(Directory, simpleName + ".dll");
        return File.Exists(localPath) ? LoadFromBytes(localPath) : null;
    }

    public Assembly LoadFromBytes(string assemblyPath)
    {
        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");

        using var assemblyStream = new MemoryStream(assemblyBytes);
        if (File.Exists(pdbPath))
        {
            using var symbolStream = new MemoryStream(File.ReadAllBytes(pdbPath));
            return LoadFromStream(assemblyStream, symbolStream);
        }

        return LoadFromStream(assemblyStream);
    }
}
