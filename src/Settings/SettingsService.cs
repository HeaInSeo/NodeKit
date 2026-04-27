using System;
using System.IO;
using System.Text.Json;

namespace NodeKit.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in the user's application-data directory.
/// Path: {AppData}/NodeKit/settings.json
/// Failures are silently swallowed; defaults are returned on any load error.
/// </summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions s_writeOpts = new() { WriteIndented = true };

    /// <summary>Full path to the settings JSON file.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NodeKit",
        "settings.json");

    /// <summary>Loads settings from disk, returning defaults on first run or any error.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
#pragma warning disable CA1031 // swallow all errors — return safe defaults
        catch
        {
            return new AppSettings();
        }
#pragma warning restore CA1031
    }

    /// <summary>Persists <paramref name="settings"/> to disk, creating the directory if needed.</summary>
    public static void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, s_writeOpts));
    }
}
