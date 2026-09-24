using System.Text.Json;
using Ax206Display.Config.Models;

namespace Ax206Display.Config.Services;

/// <summary>
/// Loads and saves the application's <see cref="AppConfig"/> as JSON at a fixed
/// file path. The path is supplied by the caller (composition root) rather than
/// computed here, which keeps this class usable from unit tests without
/// touching real per-user/per-machine directories.
/// </summary>
public sealed class ConfigService : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public ConfigService(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return new AppConfig();
        }

        await using var stream = File.OpenRead(_filePath);
        var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, SerializerOptions, cancellationToken);
        return config ?? new AppConfig();
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            SecureDirectory.EnsureExists(directory);
        }

        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, config, SerializerOptions, cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>
    /// Load, transform, save as one step, serialized against every other
    /// UpdateAsync on this instance - so two editors (say, a layout save and
    /// an integration save from the web UI) can't each load the same old
    /// config and have the second save silently revert the first. Returns
    /// the config as saved. Plain LoadAsync/SaveAsync callers aren't covered.
    /// </summary>
    public async Task<AppConfig> UpdateAsync(Func<AppConfig, AppConfig> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _updateLock.WaitAsync(cancellationToken);
        try
        {
            var updated = update(await LoadAsync(cancellationToken));
            await SaveAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>Default per-machine config location used by the composition root (App project).</summary>
    public static string GetDefaultConfigPath()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(baseDirectory, "Ax206Display", "config.json");
    }

    /// <summary>Default per-machine secret store location, alongside the config file.</summary>
    public static string GetDefaultSecretStorePath()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(baseDirectory, "Ax206Display", "secrets.dat");
    }

    public void Dispose() => _updateLock.Dispose();
}
