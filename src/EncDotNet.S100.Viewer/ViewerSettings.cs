using System;
using System.IO;
using System.Text.Json;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Persists user settings to a JSON file in the app's local data directory.
/// </summary>
internal sealed class ViewerSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EncDotNet.S100.Viewer");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public string? PortrayalCataloguePath { get; set; }

    public static ViewerSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<ViewerSettings>(json) ?? new ViewerSettings();
            }
        }
        catch
        {
            // If settings are corrupt, start fresh.
        }

        return new ViewerSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
