using System.IO.Compression;
using SemihBener.Host.Core;

namespace SemihBener.Host.Plugins;

public sealed record InstallResult(string Key, string Name, string Version, bool IsNew);

public sealed class PluginInstallException(string message) : Exception(message)
{
}

/// <summary>
/// Panelden yüklenen zip'i doğrular ve App_Data/plugins/{key}/{version} altına açar.
/// Yeni sürüm bir sonraki yeniden başlatmada devreye girer; önceki sürümler geri alma için saklanır.
/// </summary>
public sealed class PluginInstaller(
    HostPaths paths,
    HostStateStore store,
    SecretProtector secrets,
    ILogger<PluginInstaller> logger)
{
    public const long MaxZipBytes = 50L * 1024 * 1024;
    private const long MaxExtractedBytes = 200L * 1024 * 1024;
    private const int MaxEntries = 1000;

    public InstallResult Install(Stream zipStream)
    {
        var workDir = Path.Combine(paths.Temp, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            ExtractSafely(zipStream, workDir);

            var root = FindManifestRoot(workDir);
            var manifest = PluginManifest.Read(Path.Combine(root, "plugin.json"));
            manifest.Validate(HostInfo.Version);

            ValidateEntryAssembly(root, manifest);

            var existing = store.FindPlugin(manifest.Key);
            if (existing is not null && existing.InstalledVersions.Contains(manifest.Version))
            {
                throw new PluginInstallException(
                    $"{manifest.Key} {manifest.Version} zaten yüklü. plugin.json'daki version'ı artırıp tekrar paketleyin.");
            }

            var target = paths.PluginVersionDirectory(manifest.Key, manifest.Version);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(root, target);

            var isNew = existing is null;
            store.Update(state =>
            {
                var record = state.Plugins.FirstOrDefault(p => p.Key == manifest.Key);
                if (record is null)
                {
                    record = new PluginRecord { Key = manifest.Key, InstalledAtUtc = DateTime.UtcNow };
                    state.Plugins.Add(record);
                }

                PluginLoader.ApplyManifest(record, manifest);
                if (!record.InstalledVersions.Contains(manifest.Version))
                {
                    record.InstalledVersions.Add(manifest.Version);
                }

                record.DevelopmentPath = null;
                record.Enabled = true;
                record.Status = PluginStatus.Pending;
                record.LastError = null;
                secrets.EnsurePluginSecrets(record);
                state.RestartRequired = true;
            });

            logger.LogWarning("Plugin yüklendi: {Key} {Version}", manifest.Key, manifest.Version);
            return new InstallResult(manifest.Key, manifest.Name, manifest.Version, isNew);
        }
        catch (PluginManifestException ex)
        {
            throw new PluginInstallException(ex.Message);
        }
        catch (InvalidDataException)
        {
            throw new PluginInstallException("Dosya geçerli bir zip değil.");
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

    /// <summary>Plugin dosyalarını ve kaydını siler. Yüklü assembly bellekte, yeniden başlatmaya kadar kalır.</summary>
    public void Remove(string key)
    {
        store.Update(state =>
        {
            state.Plugins.RemoveAll(p => p.Key == key);
            state.RestartRequired = true;
        });

        var directory = Path.Combine(paths.Plugins, key);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ExtractSafely(Stream zipStream, string destination)
    {
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        if (archive.Entries.Count > MaxEntries)
        {
            throw new PluginInstallException($"Zip'te çok fazla dosya var (en fazla {MaxEntries}).");
        }

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            total += entry.Length;
            if (total > MaxExtractedBytes)
            {
                throw new PluginInstallException("Zip açıldığında 200 MB'ı aşıyor.");
            }

            var targetPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginInstallException($"Zip içinde geçersiz yol: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    /// <summary>plugin.json zip'in kökünde veya tek bir üst klasörün içinde olabilir.</summary>
    private static string FindManifestRoot(string workDir)
    {
        if (File.Exists(Path.Combine(workDir, "plugin.json")))
        {
            return workDir;
        }

        var directories = Directory.GetDirectories(workDir);
        if (directories.Length == 1 && File.Exists(Path.Combine(directories[0], "plugin.json")))
        {
            return directories[0];
        }

        throw new PluginInstallException("Zip'in kökünde plugin.json bulunamadı.");
    }

    /// <summary>
    /// Ana DLL'i geçici (boşaltılabilir) bir context'e yükleyip IApiPlugin sınıfı var mı kontrol eder.
    /// </summary>
    private static void ValidateEntryAssembly(string root, PluginManifest manifest)
    {
        var entryPath = Path.Combine(root, manifest.EntryAssembly);
        if (!File.Exists(entryPath))
        {
            throw new PluginInstallException($"{manifest.EntryAssembly} zip içinde bulunamadı.");
        }

        var context = new PluginLoadContext($"validate:{manifest.Key}", root, isCollectible: true);
        try
        {
            var assembly = context.LoadFromBytes(entryPath);
            PluginLoader.FindPluginType(assembly);
        }
        catch (PluginManifestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PluginInstallException($"Plugin DLL'i doğrulanamadı: {PluginLoader.Describe(ex)}");
        }
        finally
        {
            context.Unload();
        }
    }
}
