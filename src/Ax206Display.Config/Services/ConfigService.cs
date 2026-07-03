using System.Text.Json;
using Ax206Display.Config.Models;

namespace Ax206Display.Config.Services;

/// <summary>
/// Loads and saves the application's <see cref="AppConfig"/> as JSON at a fixed
/// file path. The path is supplied by the caller (composition root) rather than
/// computed here, which keeps this class usable from unit tests without
/// touching real per-user/per-machine directories.
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

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
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, config, SerializerOptions, cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
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
}
