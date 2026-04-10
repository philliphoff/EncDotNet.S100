using System;
using System.Collections.Generic;
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

    /// <summary>Portrayal catalogue folder paths keyed by product spec (e.g. "S-101", "S-102").</summary>
    public Dictionary<string, string> CataloguePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Feature catalogue file paths keyed by product spec (e.g. "S-101").</summary>
    public Dictionary<string, string> FeatureCataloguePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Legacy single path — migrated to <see cref="CataloguePaths"/> on load.</summary>
    public string? PortrayalCataloguePath { get; set; }

    /// <summary>Custom accent color hex string (e.g. "#007ACC"). Defaults to VS Code blue.</summary>
    public string AccentColor { get; set; } = "#007ACC";

    /// <summary>Last selected activity pane, or null if none was open.</summary>
    public string? LastSelectedActivity { get; set; }

    public static ViewerSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<ViewerSettings>(json) ?? new ViewerSettings();

                // Migrate legacy single-path setting to S-102 entry
                if (settings.PortrayalCataloguePath is { } legacy && !settings.CataloguePaths.ContainsKey("S-102"))
                {
                    settings.CataloguePaths["S-102"] = legacy;
                    settings.PortrayalCataloguePath = null;
                }

                return settings;
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
