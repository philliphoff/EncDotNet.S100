using System;
using System.IO;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Builds the <see cref="ViewerSettings"/> instance for a run, applying
/// any command-line overrides on top of the persisted profile. Kept
/// free of Avalonia/GUI dependencies so the override logic is unit
/// testable in isolation.
/// </summary>
internal static class StartupSettingsFactory
{
    /// <summary>
    /// Resolves the settings file for this run and layers the supplied
    /// <paramref name="options"/> over it.
    /// </summary>
    /// <remarks>
    /// Path selection:
    /// <list type="bullet">
    /// <item><c>--settings &lt;PATH&gt;</c> loads (and saves) that file.</item>
    /// <item><c>--ephemeral</c> loads the user profile read-only — nothing
    /// is persisted, so the real profile is never mutated.</item>
    /// <item>otherwise the per-user default profile is used as before.</item>
    /// </list>
    /// MCP, palette, and display-category options override the loaded
    /// values for the lifetime of the process only.
    /// </remarks>
    public static ViewerSettings Create(ViewerCommandSettings? options)
    {
        var settings = LoadBase(options);
        ApplyOverrides(settings, options);
        return settings;
    }

    private static ViewerSettings LoadBase(ViewerCommandSettings? options)
    {
        if (options?.SettingsPath is { } path && !string.IsNullOrWhiteSpace(path))
            return ViewerSettings.Load(path);

        var settings = ViewerSettings.Load();
        if (options?.Ephemeral == true)
            settings.IsReadOnly = true;
        return settings;
    }

    private static void ApplyOverrides(ViewerSettings settings, ViewerCommandSettings? options)
    {
        if (options is null)
            return;

        // ── MCP ──────────────────────────────────────────────────────
        if (options.McpRequested)
        {
            settings.McpEnabled = true;
            settings.McpConfiguredFromCommandLine = true;
            if (options.McpPort is { } port)
                settings.McpPort = port;
            if (options.McpBind is { } bind && !string.IsNullOrWhiteSpace(bind))
                settings.McpBindAddress = bind;
            if (options.McpPortFile is { } portFile && !string.IsNullOrWhiteSpace(portFile))
                settings.McpPortFilePath = portFile;
        }

        // ── Render state ─────────────────────────────────────────────
        if (options.Palette is { } palette && !string.IsNullOrWhiteSpace(palette))
            settings.ColorProfile = NormalizeEnum<EncDotNet.S100.Pipelines.PaletteType>(palette, settings.ColorProfile);

        if (options.DisplayCategory is { } category && !string.IsNullOrWhiteSpace(category))
            settings.EcdisDisplayCategory =
                NormalizeEnum<EncDotNet.S100.Datasets.Pipelines.EcdisDisplayCategory>(category, settings.EcdisDisplayCategory);
    }

    private static string NormalizeEnum<TEnum>(string value, string fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed)
            ? parsed.ToString()
            : fallback;
}
