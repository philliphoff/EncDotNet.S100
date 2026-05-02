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

    /// <summary>Selected color profile name: "Day", "Dusk", or "Night".</summary>
    public string ColorProfile { get; set; } = "Day";

    /// <summary>Last selected activity pane, or null if none was open.</summary>
    public string? LastSelectedActivity { get; set; }

    /// <summary>Global symbol scale factor (1.0 = default). Scales all point symbols.</summary>
    public double SymbolScale { get; set; } = 1.0;

    /// <summary>Global text scale factor (1.0 = default). Scales all text labels.</summary>
    public double TextScale { get; set; } = 1.0;

    /// <summary>Distance unit used by the map scale bar.</summary>
    public string DistanceUnit { get; set; } = "NauticalMiles";

    public bool IsStatusBarVisible { get; set; } = true;

    /// <summary>
    /// Whether the Pick Report (Object Information) side panel auto-opens
    /// when a feature is picked. The user can also explicitly hide it via
    /// the View menu.
    /// </summary>
    public bool IsPickPanelVisible { get; set; } = true;

    /// <summary>Maximum number of dataset paths kept in <see cref="RecentDatasetPaths"/>.</summary>
    public const int MaxRecentDatasets = 10;

    /// <summary>
    /// Most-recently-opened dataset file paths, ordered most-recent first.
    /// Capped at <see cref="MaxRecentDatasets"/>.
    /// </summary>
    public List<string> RecentDatasetPaths { get; set; } = new();

    /// <summary>
    /// Records <paramref name="path"/> as the most-recently-opened dataset, removing any
    /// prior occurrence and trimming the list to <see cref="MaxRecentDatasets"/>.
    /// </summary>
    public void AddRecentDataset(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        RecentDatasetPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentDatasetPaths.Insert(0, path);

        if (RecentDatasetPaths.Count > MaxRecentDatasets)
        {
            RecentDatasetPaths.RemoveRange(MaxRecentDatasets, RecentDatasetPaths.Count - MaxRecentDatasets);
        }
    }

    /// <summary>Clears the recently-opened dataset list.</summary>
    public void ClearRecentDatasets() => RecentDatasetPaths.Clear();

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
