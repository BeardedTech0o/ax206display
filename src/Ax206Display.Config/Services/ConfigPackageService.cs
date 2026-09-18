using System.IO.Compression;
using System.Text.Json;
using Ax206Display.Config.Models;

namespace Ax206Display.Config.Services;

/// <summary>
/// Exports/imports an <see cref="AppConfig"/> as a single portable .zip,
/// bundling each device's background image alongside it - the config alone
/// isn't portable between machines (or Windows to Linux) because
/// <see cref="DeviceProfileConfig.BackgroundImagePath"/> is an absolute,
/// machine-local path.
///
/// Deliberately excludes secrets: <see cref="IntegrationConfig.SecretKey"/>/
/// <see cref="IntegrationConfig.TotpSecretKey"/> only ever point at entries
/// in <see cref="SecretStore"/>, which are encrypted with a machine-local
/// key (DPAPI on Windows, an AES key file on Linux) that doesn't exist - and
/// can't be recreated - on the destination machine. Importing a package
/// carries over integration settings (URLs, usernames, site names) but not
/// passwords/API tokens; re-enter those by hand after importing.
/// </summary>
public static class ConfigPackageService
{
    private const string ConfigEntryName = "config.json";
    private const string BackgroundsEntryPrefix = "backgrounds/";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Writes <paramref name="config"/> plus every device's background image
    /// (skipping devices with none, or whose file is missing/unreadable) to
    /// a new .zip at <paramref name="outputZipPath"/>.
    /// </summary>
    public static async Task ExportAsync(AppConfig config, string outputZipPath, CancellationToken cancellationToken = default)
    {
        var tempPath = outputZipPath + ".tmp";
        File.Delete(tempPath);

        var usedEntryNames = new HashSet<string>(StringComparer.Ordinal);
        var portableDevices = new List<DeviceProfileConfig>(config.Devices.Count);

        using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
        {
            foreach (var device in config.Devices)
            {
                portableDevices.Add(await AddDeviceBackgroundAsync(archive, device, usedEntryNames, cancellationToken));
            }

            var portableConfig = config with { Devices = portableDevices };
            var configEntry = archive.CreateEntry(ConfigEntryName, CompressionLevel.Optimal);
            await using var configStream = configEntry.Open();
            await JsonSerializer.SerializeAsync(configStream, portableConfig, SerializerOptions, cancellationToken);
        }

        File.Move(tempPath, outputZipPath, overwrite: true);
    }

    /// <summary>
    /// Reads a package written by <see cref="ExportAsync"/>: extracts each
    /// device's background image (if any) into
    /// <paramref name="backgroundsDestinationDirectory"/> and returns the
    /// config with <see cref="DeviceProfileConfig.BackgroundImagePath"/>
    /// rewritten to those new local paths. Doesn't touch the caller's
    /// existing config/secrets - merging the returned devices/integrations
    /// into it (e.g. by <c>Id</c>) is left to the caller, same as it's left
    /// to the caller to prompt for integration secrets afterward.
    /// </summary>
    public static async Task<AppConfig> ImportAsync(string zipPath, string backgroundsDestinationDirectory, CancellationToken cancellationToken = default)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var configEntry = archive.GetEntry(ConfigEntryName)
            ?? throw new InvalidDataException($"Config package is missing '{ConfigEntryName}'.");

        AppConfig portableConfig;
        await using (var configStream = configEntry.Open())
        {
            portableConfig = await JsonSerializer.DeserializeAsync<AppConfig>(configStream, SerializerOptions, cancellationToken)
                ?? throw new InvalidDataException($"Config package's '{ConfigEntryName}' could not be parsed.");
        }

        var rewrittenDevices = new List<DeviceProfileConfig>(portableConfig.Devices.Count);
        foreach (var device in portableConfig.Devices)
        {
            rewrittenDevices.Add(ExtractDeviceBackground(archive, device, backgroundsDestinationDirectory));
        }

        return portableConfig with { Devices = rewrittenDevices };
    }

    /// <summary>
    /// Folds an imported config (from <see cref="ImportAsync"/>) into an
    /// existing one: devices/integrations sharing an <c>Id</c> with the
    /// import are replaced by the imported version, everything else in
    /// <paramref name="existing"/> is kept untouched, and anything new in
    /// the import is appended. Same semantics regardless of caller (the
    /// Windows app's tray menu, or the web UI's import endpoint).
    /// </summary>
    public static AppConfig MergeImported(AppConfig existing, AppConfig imported)
    {
        return existing with
        {
            Devices = MergeById(existing.Devices, imported.Devices, d => d.Id),
            Integrations = MergeById(existing.Integrations, imported.Integrations, i => i.Id),
        };
    }

    private static List<T> MergeById<T>(List<T> existing, List<T> imported, Func<T, string> idSelector)
    {
        var merged = new List<T>(existing);
        foreach (var importedItem in imported)
        {
            var existingIndex = merged.FindIndex(e => idSelector(e) == idSelector(importedItem));
            if (existingIndex >= 0)
            {
                merged[existingIndex] = importedItem;
            }
            else
            {
                merged.Add(importedItem);
            }
        }

        return merged;
    }

    private static async Task<DeviceProfileConfig> AddDeviceBackgroundAsync(
        ZipArchive archive, DeviceProfileConfig device, HashSet<string> usedEntryNames, CancellationToken cancellationToken)
    {
        var sourcePath = device.BackgroundImagePath;
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            return device with { BackgroundImagePath = null };
        }

        var entryName = MakeUniqueEntryName(device.Id, Path.GetFileName(sourcePath), usedEntryNames);
        var entry = archive.CreateEntry(BackgroundsEntryPrefix + entryName, CompressionLevel.Optimal);

        await using var entryStream = entry.Open();
        await using var sourceStream = File.OpenRead(sourcePath);
        await sourceStream.CopyToAsync(entryStream, cancellationToken);

        return device with { BackgroundImagePath = BackgroundsEntryPrefix + entryName };
    }

    private static DeviceProfileConfig ExtractDeviceBackground(ZipArchive archive, DeviceProfileConfig device, string destinationDirectory)
    {
        var entryName = device.BackgroundImagePath;
        if (string.IsNullOrEmpty(entryName) || !entryName.StartsWith(BackgroundsEntryPrefix, StringComparison.Ordinal))
        {
            return device with { BackgroundImagePath = null };
        }

        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return device with { BackgroundImagePath = null };
        }

        // One fixed slot per device (background<original-extension>, in a
        // per-device subfolder) rather than a name derived from the zip
        // entry: a name built from the source file's name would carry
        // forward whatever this same import logic previously named it,
        // growing a little on every export/import round trip (export
        // prefixes the device Id onto its source file's current name, which
        // - after the first import - already *is* a previous export's
        // name). A fixed slot can't accumulate, and since
        // DeviceProfileConfig only ever has one background at a time,
        // there's nothing to collide with.
        var deviceDirectory = Path.Combine(destinationDirectory, SanitizePathComponent(device.Id));
        Directory.CreateDirectory(deviceDirectory);

        var extension = SanitizePathComponent(Path.GetExtension(entry.Name));
        var destinationPath = Path.Combine(deviceDirectory, "background" + extension);
        entry.ExtractToFile(destinationPath, overwrite: true);

        return device with { BackgroundImagePath = destinationPath };
    }

    private static string MakeUniqueEntryName(string deviceId, string fileName, HashSet<string> usedEntryNames)
    {
        var baseName = $"{SanitizePathComponent(deviceId)}-{SanitizePathComponent(fileName)}";
        var candidate = baseName;
        var suffix = 1;
        while (!usedEntryNames.Add(candidate))
        {
            candidate = $"{baseName}.{suffix++}";
        }

        return candidate;
    }

    /// <summary>
    /// Replaces characters invalid in a file name (plus ':'/'@', common in
    /// device Ids like "usb:1908:0102@1-1") with '_'. Public so other code
    /// deriving a safe directory/file name from a device Id - e.g. the web
    /// UI's background-upload endpoint - uses the exact same rule as config
    /// packages do, rather than a second slightly-different sanitizer.
    /// </summary>
    public static string SanitizePathComponent(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            sanitized[i] = Array.IndexOf(invalidChars, c) >= 0 || c is ':' or '@' ? '_' : c;
        }

        return new string(sanitized);
    }
}
