using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Viewer;

internal sealed class ViewerCommandSettings : CommandSettings
{
    // ── Capture ──────────────────────────────────────────────────────

    [CommandOption("--screenshot <PATH>")]
    [Description("Capture a screenshot of the map to the specified file path after loading.")]
    public string? ScreenshotPath { get; set; }

    [CommandOption("--exit-after-screenshot")]
    [Description("Quit the viewer immediately after the screenshot has been captured (one-shot capture).")]
    public bool ExitAfterScreenshot { get; set; }

    [CommandOption("--full-window")]
    [Description("Capture the entire viewer window (panels, toolbars, status bar) instead of just the map control.")]
    public bool FullWindowScreenshot { get; set; }

    [CommandOption("--window-size <WIDTHxHEIGHT>")]
    [Description("Force the viewer window to a fixed size (e.g. 1280x800) so screenshots are reproducible across machines.")]
    public string? WindowSize { get; set; }

    // ── Catalogues / datasets ────────────────────────────────────────

    [CommandOption("-f|--fc <PATH>")]
    [Description("Feature catalogue XML file path. The product spec is detected automatically. May be specified multiple times.")]
    public string[]? FeatureCatalogues { get; set; }

    [CommandOption("-p|--pc <PATH>")]
    [Description("Portrayal catalogue folder path. The product spec is detected automatically. May be specified multiple times.")]
    public string[]? PortrayalCatalogues { get; set; }

    // ── MCP server (agent automation) ────────────────────────────────

    [CommandOption("--mcp")]
    [Description("Start the embedded MCP server on launch, overriding the persisted setting for this run.")]
    public bool Mcp { get; set; }

    [CommandOption("--mcp-port <PORT>")]
    [Description("TCP port for the MCP server. 0 (the default) picks an ephemeral port. Implies --mcp.")]
    public int? McpPort { get; set; }

    [CommandOption("--mcp-bind <ADDRESS>")]
    [Description("MCP server bind address (loopback only is recommended). Implies --mcp.")]
    public string? McpBind { get; set; }

    [CommandOption("--mcp-port-file <PATH>")]
    [Description("Write the bound MCP endpoint URI to this file once the server is listening, so an agent can discover an ephemeral port. Implies --mcp.")]
    public string? McpPortFile { get; set; }

    // ── Settings isolation ───────────────────────────────────────────

    [CommandOption("--settings <PATH>")]
    [Description("Use the settings file at this path instead of the per-user default. Lets agent runs avoid the real profile.")]
    public string? SettingsPath { get; set; }

    [CommandOption("--ephemeral")]
    [Description("Run against a throwaway settings file that is never persisted, leaving the user's profile untouched.")]
    public bool Ephemeral { get; set; }

    // ── Viewport ─────────────────────────────────────────────────────

    [CommandOption("--center <LAT,LON>")]
    [Description("Center the map on this WGS-84 latitude,longitude after loading (e.g. 47.6,-122.3). Requires --zoom; suppresses auto-zoom-to-extent.")]
    public string? Center { get; set; }

    [CommandOption("--zoom <LEVEL>")]
    [Description("Web-mercator zoom level (0-24) to apply with --center. Suppresses auto-zoom-to-extent.")]
    public double? Zoom { get; set; }

    [CommandOption("--bbox <SOUTH,WEST,NORTH,EAST>")]
    [Description("Zoom to this WGS-84 bounding box after loading (e.g. 47.5,-122.5,47.7,-122.1). Suppresses auto-zoom-to-extent.")]
    public string? BoundingBox { get; set; }

    // ── Render state ─────────────────────────────────────────────────

    [CommandOption("--palette <PALETTE>")]
    [Description("ECDIS palette to use: Day, Dusk, or Night. Overrides the persisted palette for this run.")]
    public string? Palette { get; set; }

    [CommandOption("--display-category <CATEGORY>")]
    [Description("ECDIS display category: DisplayBase, Standard, OtherInformation, or All. Overrides the persisted category for this run.")]
    public string? DisplayCategory { get; set; }

    [CommandOption("--time-step <INDEX|TIMESTAMP>")]
    [Description("For time-varying data, jump to this time step after loading — a zero-based index or an ISO-8601 UTC timestamp.")]
    public string? TimeStep { get; set; }

    // ── Logging / diagnostics ────────────────────────────────────────

    [CommandOption("--log-file <PATH>")]
    [Description("Append structured viewer logs to this file in addition to the standard diagnostics output.")]
    public string? LogFile { get; set; }

    [CommandOption("--crash-log <PATH>")]
    [Description("Path for the crash log (default: a 'viewer-crash.log' file in the system temp directory).")]
    public string? CrashLog { get; set; }

    [CommandOption("-v|--verbose")]
    [Description("Enable verbose (Debug-level) logging.")]
    public bool Verbose { get; set; }

    // ── Positional ───────────────────────────────────────────────────

    [CommandArgument(0, "[datasets]")]
    [Description("One or more dataset file paths to open.")]
    public string[]? Datasets { get; set; }

    /// <summary>
    /// True when any MCP-related option was supplied on the command
    /// line. Such a run enables the server and must not persist the
    /// bound port back to the user's settings file.
    /// </summary>
    public bool McpRequested =>
        Mcp || McpPort.HasValue || McpBind is not null || McpPortFile is not null;

    /// <summary>
    /// True when the user supplied an explicit viewport (center+zoom or
    /// a bounding box). When set, the window suppresses its automatic
    /// zoom-to-extent so the requested framing is deterministic.
    /// </summary>
    public bool HasExplicitViewport => Center is not null || BoundingBox is not null;

    /// <summary>Parsed center as (latitude, longitude), or <c>null</c>.</summary>
    public (double Latitude, double Longitude)? ParsedCenter =>
        TryParseLatLon(Center, out var lat, out var lon) ? (lat, lon) : null;

    /// <summary>Parsed bounding box as (south, west, north, east), or <c>null</c>.</summary>
    public (double South, double West, double North, double East)? ParsedBoundingBox =>
        TryParseBoundingBox(BoundingBox, out var s, out var w, out var n, out var e) ? (s, w, n, e) : null;

    /// <summary>Parsed window size as (width, height) in pixels, or <c>null</c>.</summary>
    public (int Width, int Height)? ParsedWindowSize =>
        TryParseWindowSize(WindowSize, out var w, out var h) ? (w, h) : null;

    public override Spectre.Console.ValidationResult Validate()
    {
        if (Center is not null && !TryParseLatLon(Center, out _, out _))
            return Spectre.Console.ValidationResult.Error(
                $"--center must be 'LAT,LON' in decimal degrees (got '{Center}').");

        if (Center is not null && !Zoom.HasValue)
            return Spectre.Console.ValidationResult.Error("--center requires --zoom.");

        if (Zoom is { } z && (z < 0 || z > 24))
            return Spectre.Console.ValidationResult.Error("--zoom must be between 0 and 24.");

        if (BoundingBox is not null && !TryParseBoundingBox(BoundingBox, out _, out _, out _, out _))
            return Spectre.Console.ValidationResult.Error(
                $"--bbox must be 'SOUTH,WEST,NORTH,EAST' in decimal degrees with south<north and west<east (got '{BoundingBox}').");

        if (Center is not null && BoundingBox is not null)
            return Spectre.Console.ValidationResult.Error("--center/--zoom and --bbox are mutually exclusive.");

        if (WindowSize is not null && !TryParseWindowSize(WindowSize, out _, out _))
            return Spectre.Console.ValidationResult.Error(
                $"--window-size must be 'WIDTHxHEIGHT' in pixels (got '{WindowSize}').");

        if (McpPort is { } port && (port < 0 || port > 65535))
            return Spectre.Console.ValidationResult.Error("--mcp-port must be between 0 and 65535.");

        if (McpBind is not null && !System.Net.IPAddress.TryParse(McpBind, out _))
            return Spectre.Console.ValidationResult.Error(
                $"--mcp-bind must be a valid IP address (got '{McpBind}').");

        if (Palette is not null && !Enum.TryParse<EncDotNet.S100.Pipelines.PaletteType>(Palette, ignoreCase: true, out _))
            return Spectre.Console.ValidationResult.Error(
                $"--palette must be Day, Dusk, or Night (got '{Palette}').");

        if (DisplayCategory is not null &&
            !Enum.TryParse<EncDotNet.S100.Datasets.Pipelines.EcdisDisplayCategory>(DisplayCategory.Trim(), ignoreCase: true, out _))
            return Spectre.Console.ValidationResult.Error(
                $"--display-category must be DisplayBase, Standard, OtherInformation, or All (got '{DisplayCategory}').");

        if (ExitAfterScreenshot && ScreenshotPath is null)
            return Spectre.Console.ValidationResult.Error("--exit-after-screenshot requires --screenshot.");

        if (Ephemeral && SettingsPath is not null)
            return Spectre.Console.ValidationResult.Error("--ephemeral and --settings are mutually exclusive.");

        return Spectre.Console.ValidationResult.Success();
    }

    internal static bool TryParseLatLon(string? raw, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)) return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude)) return false;
        return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }

    internal static bool TryParseBoundingBox(
        string? raw, out double south, out double west, out double north, out double east)
    {
        south = west = north = east = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return false;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out south)) return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out west)) return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out north)) return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out east)) return false;
        return south is >= -90 and <= 90 && north is >= -90 and <= 90
            && west is >= -180 and <= 180 && east is >= -180 and <= 180
            && south < north && west < east;
    }

    internal static bool TryParseWindowSize(string? raw, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var parts = raw.Split(new[] { 'x', 'X', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height)) return false;
        return width is > 0 and <= 10000 && height is > 0 and <= 10000;
    }
}

internal sealed class ViewerCommand : Command<ViewerCommandSettings>
{
    public override int Execute(CommandContext context, ViewerCommandSettings settings)
    {
        App.StartupOptions = settings;

        // Route crash logging to the user-chosen path (or temp default)
        // before any startup code can fault.
        CrashLog.ConfigurePath(settings.CrashLog);

        try
        {
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime([]);
        }
        catch (System.Exception ex)
        {
            System.Console.Error.WriteLine($"[FATAL] {ex}");
            CrashLog.Append("FATAL", ex.ToString());
            throw;
        }

        return 0;
    }
}
