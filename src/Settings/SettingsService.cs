using System;
using System.IO;
using System.Text.Json;

namespace NodeKit.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in the user's application-data directory.
/// Path: {AppData}/NodeKit/settings.json
/// Defaults are returned on any load error; use the <see cref="Load(out bool)"/> overload
/// to detect whether the file existed but was corrupted versus a first run.
/// </summary>
internal static class SettingsService
{
    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    /// <summary>Full path to the settings JSON file.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NodeKit",
        "settings.json");

    /// <summary>Loads settings from disk, returning defaults on first run or any error.</summary>
    public static AppSettings Load() => Load(out _);

    /// <summary>
    /// Loads settings from disk, returning defaults on first run or any error.
    /// <paramref name="wasCorrupted"/> is true only when a settings file exists but
    /// could not be read/parsed, so the caller can distinguish that from a first run.
    /// </summary>
    public static AppSettings Load(out bool wasCorrupted)
    {
        wasCorrupted = false;

        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
#pragma warning disable CA1031 // swallow all errors — return safe defaults
        catch
        {
            wasCorrupted = true;
            return new AppSettings();
        }
#pragma warning restore CA1031
    }

    /// <summary>Persists <paramref name="settings"/> to disk, creating the directory if needed.</summary>
    public static void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(FilePath)}.tmp");
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, _writeOptions));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
