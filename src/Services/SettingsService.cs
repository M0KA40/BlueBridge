using System.Text.Json;
using BlueBridge.Models;

namespace BlueBridge.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BlueBridge");

    private readonly string _legacySettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JackPhoneAudio",
        "settings.json");

    private string SettingsPath => Path.Combine(_settingsFolder, "settings.json");

    public AppSettings Load()
    {
        try
        {
            string sourcePath = File.Exists(SettingsPath) ? SettingsPath : _legacySettingsPath;
            if (!File.Exists(sourcePath))
            {
                return new AppSettings();
            }

            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(sourcePath), JsonOptions)
                                   ?? new AppSettings();
            if (!File.Exists(SettingsPath) && File.Exists(_legacySettingsPath))
            {
                Save(settings);
                AppLogger.Info("Migrated settings from an earlier BlueBridge release.");
            }
            return settings;
        }
        catch (Exception exception)
        {
            AppLogger.Error("Could not load settings; using defaults.", exception);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_settingsFolder);
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch (Exception exception)
        {
            AppLogger.Error("Could not save settings.", exception);
        }
    }
}
