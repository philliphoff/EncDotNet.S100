using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Persists user settings to a JSON file in the app's local data directory.
/// </summary>
internal sealed class ViewerSettings
{
    private static readonly string DefaultSettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EncDotNet.S100.Viewer");

    private static readonly string DefaultSettingsPath = Path.Combine(DefaultSettingsDir, "settings.json");

    /// <summary>
    /// Filesystem path used by <see cref="Save"/> and <see cref="Load"/>.
    /// Defaults to the per-user application-data location; tests override
    /// this with a temp path so they don't pollute the real settings file.
    /// </summary>
    [JsonIgnore]
    public string SettingsFilePath { get; set; } = DefaultSettingsPath;

    /// <summary>
    /// When <see langword="true"/>, <see cref="Save"/> is a no-op. Set
    /// for <c>--ephemeral</c> agent runs so nothing is persisted and
    /// the user's real profile is left untouched.
    /// </summary>
    [JsonIgnore]
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the MCP server was configured from
    /// the command line for this run; the host must not persist the
    /// bound (ephemeral) port back to the settings file. Prevents an
    /// automation run from mutating the user's persisted MCP port.
    /// </summary>
    [JsonIgnore]
    public bool McpConfiguredFromCommandLine { get; set; }

    /// <summary>
    /// Optional path the MCP host writes the bound endpoint URI to once
    /// it is listening (set from <c>--mcp-port-file</c>). Lets an agent
    /// discover an ephemeral port without scraping the status bar.
    /// </summary>
    [JsonIgnore]
    public string? McpPortFilePath { get; set; }

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

    /// <summary>
    /// Persisted chrome theme name — one of "Light", "Dark",
    /// "S100Night", "S100Dusk". Independent of <see cref="ColorProfile"/>
    /// (which drives map portrayal colours). Default "Light" so first-launch
    /// users get a familiar Avalonia chrome.
    /// </summary>
    public string ChromeTheme { get; set; } = "Light";

    /// <summary>
    /// Whether the viewer checks GitHub for a newer release. Default
    /// <see langword="true"/>; the user can turn it off from the About
    /// dialog. See issue #379.
    /// </summary>
    public bool UpdateCheckEnabled { get; set; } = true;

    /// <summary>
    /// The release version the user chose to "skip"; the update prompt stays
    /// silent for it but still fires for any later release. <c>null</c> when
    /// nothing has been skipped. Stored without the leading <c>v</c>.
    /// </summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>
    /// UTC timestamp of the last completed update check, used to throttle
    /// network checks to roughly once per day. <c>null</c> until the first
    /// check runs.
    /// </summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    /// <summary>
    /// The latest release version seen by the most recent check (tag without
    /// the leading <c>v</c>), cached for display. <c>null</c> until a check
    /// has succeeded.
    /// </summary>
    public string? LastKnownLatestVersion { get; set; }

    /// <summary>
    /// Id of the last-selected left-dock activity tab, or <c>null</c> if
    /// none was open. Name kept for back-compat with pre-PR-M3 settings
    /// files; the corresponding right- and bottom-dock fields are
    /// <see cref="LastSelectedRightTab"/> and <see cref="LastSelectedBottomTab"/>.
    /// </summary>
    public string? LastSelectedActivity { get; set; }

    /// <summary>Id of the last-selected right-dock activity tab (PR-M3).</summary>
    public string? LastSelectedRightTab { get; set; }

    /// <summary>Id of the last-selected bottom-dock activity tab (PR-M3).</summary>
    public string? LastSelectedBottomTab { get; set; }

    /// <summary>Whether the left dock (activity pane) was open at last shutdown. Default <c>true</c>.</summary>
    public bool IsLeftDockOpen { get; set; } = true;

    /// <summary>Whether the right dock was open at last shutdown. Default <c>false</c>.</summary>
    public bool IsRightDockOpen { get; set; } = false;

    /// <summary>Whether the bottom dock was open at last shutdown. Default <c>false</c>.</summary>
    public bool IsBottomDockOpen { get; set; } = false;

    /// <summary>
    /// Persisted panel sizes — outer dock widths/height plus inner
    /// splitter fractions for the Datasets and Catalog tabs.
    /// Writes are debounced (500 ms) via <see cref="Services.DebouncedSettingsSaver"/>
    /// so dragging splitters doesn't hammer the disk.
    /// </summary>
    public PanelSizes Panels { get; set; } = new();

    /// <summary>Global symbol scale factor (1.0 = default). Scales all point symbols.</summary>
    public double SymbolScale { get; set; } = 1.0;

    /// <summary>Global text scale factor (1.0 = default). Scales all text labels.</summary>
    public double TextScale { get; set; } = 1.0;

    /// <summary>Distance unit used by the map scale bar.</summary>
    public string DistanceUnit { get; set; } = "NauticalMiles";

    /// <summary>
    /// Display format for date/time values across the viewer
    /// ("Local" or "Utc"). Defaults to <c>"Local"</c>. Stored as a
    /// string for forward-compat with other enum-shaped settings.
    /// </summary>
    public string? TimeFormat { get; set; }

    /// <summary>
    /// Active ECDIS display category — one of "DisplayBase",
    /// "Standard", "OtherInformation", "All". Defaults to Standard
    /// (S-100 Part 9 §11.7).
    /// </summary>
    public string EcdisDisplayCategory { get; set; } = "Standard";

    // Mariner settings (S-100 Part 9 §4.2). Depth values are stored in
    // metres regardless of the user's chosen DepthUnit. All fields are
    // nullable so older settings.json files keep working — defaults are
    // applied by SettingsViewModel / MarinerSettingsProvider when null.

    /// <summary>Safety contour depth in metres.</summary>
    public double? SafetyContour { get; set; }

    /// <summary>Safety depth in metres for sounding selection.</summary>
    public double? SafetyDepth { get; set; }

    /// <summary>Shallow contour depth in metres.</summary>
    public double? ShallowContour { get; set; }

    /// <summary>Deep contour depth in metres.</summary>
    public double? DeepContour { get; set; }

    /// <summary>Display unit name ("Metres", "Feet", "FathomsFeet", "Fathoms").</summary>
    public string? DepthUnit { get; set; }

    public bool? FourShades { get; set; }
    public bool? ShallowWaterDangers { get; set; }
    public bool? PlainBoundaries { get; set; }
    public bool? SimplifiedSymbols { get; set; }
    public bool? FullLightLines { get; set; }
    public bool? RadarOverlay { get; set; }
    public bool? IgnoreScaleMinimum { get; set; }

    /// <summary>
    /// Whether the raster vector-layer snapshot fast path is enabled
    /// (<c>RenderingOptimizations.VectorSnapshotEnabled</c>). Records a settled
    /// S-100 vector layer once per (resolution, feature-set) and blits it under
    /// translation during pans, so a pan costs one textured blit instead of
    /// re-drawing every feature. <see langword="null"/> in legacy settings files;
    /// treated as the best default (on) by <see cref="SettingsViewModel"/>.
    /// </summary>
    public bool? VectorSnapshotEnabled { get; set; }

    /// <summary>
    /// Whether the off-thread snapshot prebuild is enabled
    /// (<c>RenderingOptimizations.VectorSnapshotPrebuildEnabled</c>). Hides the
    /// one-time record stall on zoom and the sustained-pan record stall by
    /// rasterising on a background thread. Only meaningful when
    /// <see cref="VectorSnapshotEnabled"/> is on. <see langword="null"/> → best
    /// default (on).
    /// </summary>
    public bool? VectorSnapshotPrebuildEnabled { get; set; }

    /// <summary>
    /// Whether the translation-invariant vector path cache is enabled
    /// (<c>RenderingOptimizations.VectorPathCacheEnabled</c>). Builds each
    /// geometry's projected path once per (feature, resolution) and re-uses it
    /// across pans. <see langword="null"/> → best default (on).
    /// </summary>
    public bool? VectorPathCacheEnabled { get; set; }

    /// <summary>
    /// Whether resolution-aware geometry simplification is enabled
    /// (<c>RenderingOptimizations.GeometrySimplificationEnabled</c>). Drops
    /// on-screen sub-pixel detail from dense S-100 line <b>and polygon</b>
    /// geometries at path-build time (polygons via topology-preserving
    /// simplification). Requires <see cref="VectorPathCacheEnabled"/>.
    /// <see langword="null"/> → best default (on).
    /// </summary>
    public bool? GeometrySimplificationEnabled { get; set; }

    /// <summary>
    /// Deprecated: prior name for <see cref="GeometrySimplificationEnabled"/>
    /// (line-only simplification). Still deserialized so existing settings
    /// migrate forward; read only as a fallback when
    /// <see cref="GeometrySimplificationEnabled"/> is unset. Do not write.
    /// </summary>
    public bool? LineSimplificationEnabled { get; set; }

    /// <summary>
    /// Active base-plane render subsystem (issue #331). One of "Mapsui" (the "A"
    /// arm) or "TiledScene" (the "B" arm), matching
    /// <see cref="Renderers.Mapsui.RenderSubsystemKind"/>. <see langword="null"/>
    /// → best default ("TiledScene"). Mirrors
    /// <c>RenderingOptimizations.RenderSubsystem</c> /
    /// <c>S100_RENDER_SUBSYSTEM</c>.
    /// </summary>
    public string? RenderSubsystem { get; set; }

    /// <summary>
    /// Within the TiledScene ("B") arm, the base-plane scene mode (issue #331).
    /// One of "Tiled" (Phase-2 default) or "Single" (Phase-1 single surface),
    /// matching <see cref="Renderers.Mapsui.VectorSceneMode"/>.
    /// <see langword="null"/> → best default ("Tiled"). Mirrors
    /// <c>RenderingOptimizations.SceneMode</c> / <c>S100_VECTOR_SCENE_MODE</c>.
    /// </summary>
    public string? VectorSceneMode { get; set; }

    /// <summary>
    /// Tiled-base-plane gutter, in DIP (issue #331). <see langword="null"/> →
    /// best default. Mirrors <c>RenderingOptimizations.TileGutterDip</c> /
    /// <c>S100_VECTOR_TILE_GUTTER</c>.
    /// </summary>
    public double? TileGutterDip { get; set; }

    /// <summary>
    /// Per-layer hot-cache native budget, in MB (issue #331).
    /// <see langword="null"/> → best default. Mirrors
    /// <c>RenderingOptimizations.TileBudgetMb</c> /
    /// <c>S100_VECTOR_TILE_BUDGET_MB</c>.
    /// </summary>
    public double? TileBudgetMb { get; set; }

    /// <summary>
    /// Whether tiled-base-plane speculative prediction / pre-warm is enabled
    /// (issue #331). <see langword="null"/> → best default (on). Mirrors
    /// <c>RenderingOptimizations.TilePredictionEnabled</c> /
    /// <c>S100_VECTOR_TILE_PREDICT</c>.
    /// </summary>
    public bool? TilePredictionEnabled { get; set; }

    /// <summary>
    /// Whether idle cross-band (±1) pre-warm is enabled (issue&#160;#428).
    /// <see langword="null"/> → best default (on, except a no-op on the LowEnd
    /// tier). Mirrors <c>RenderingOptimizations.TileCrossBandPrewarmEnabled</c> /
    /// <c>S100_VECTOR_TILE_XBAND</c>.
    /// </summary>
    public bool? TileCrossBandPrewarmEnabled { get; set; }

    /// <summary>
    /// Whether the warm disk tile cache is enabled (issue #331).
    /// <see langword="null"/> → best default (on). Mirrors
    /// <c>RenderingOptimizations.TileDiskCacheEnabled</c> /
    /// <c>S100_VECTOR_TILE_DISK</c>.
    /// </summary>
    public bool? TileDiskCacheEnabled { get; set; }

    /// <summary>
    /// Warm disk tile-cache budget, in MB (issue #331). <see langword="null"/> →
    /// best default. Mirrors <c>RenderingOptimizations.TileDiskMb</c> /
    /// <c>S100_VECTOR_TILE_DISK_MB</c>.
    /// </summary>
    public double? TileDiskMb { get; set; }

    /// <summary>
    /// Whether tiled-base-plane GPU texture residency is enabled (issue #331).
    /// <see langword="null"/> → best default (on). Mirrors
    /// <c>RenderingOptimizations.TileGpuResidencyEnabled</c> /
    /// <c>S100_VECTOR_TILE_GPU</c>.
    /// </summary>
    public bool? TileGpuResidencyEnabled { get; set; }

    /// <summary>
    /// Per-layer GPU-residency budget, in MB (issue #331).
    /// <see langword="null"/> → best default. Mirrors
    /// <c>RenderingOptimizations.TileGpuBudgetMb</c> /
    /// <c>S100_VECTOR_TILE_GPU_MB</c>.
    /// </summary>
    public double? TileGpuBudgetMb { get; set; }

    /// <summary>
    /// Number of concurrent tile-rasterisation workers per layer.
    /// <see langword="null"/> → best default for the resolved profile (one on
    /// low-end hosts, scaling with cores on high-end). Mirrors
    /// <c>RenderingOptimizations.TileWorkerCount</c> / <c>S100_VECTOR_TILE_WORKERS</c>.
    /// </summary>
    public int? TileWorkerCount { get; set; }

    /// <summary>
    /// Overall performance profile that sets the default tile budgets + worker
    /// cap. One of <c>Auto</c> / <c>HighEnd</c> / <c>Balanced</c> / <c>LowEnd</c>;
    /// <see langword="null"/> → Auto (derive from cores + RAM). Mirrors
    /// <c>RenderingOptimizations.Profile</c> / <c>S100_PERF_PROFILE</c>.
    /// </summary>
    public string? PerformanceProfile { get; set; }

    /// <summary>3-letter ISO 639-2/B language code; empty = catalogue default.</summary>
    public string? NationalLanguage { get; set; }

    /// <summary>
    /// Per-spec viewing-group ids the user has explicitly hidden via
    /// the ECDIS panel. Keys are spec codes (e.g. "S-101"); values
    /// are comma-separated viewing-group ids. Empty by default.
    /// </summary>
    public Dictionary<string, string> EcdisHiddenViewingGroups { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Display planes the user has hidden in the ECDIS panel.
    /// Stored as a comma-separated list of enum names so the JSON
    /// stays human-editable (S-100 Part 9 §11.6).
    /// </summary>
    public string EcdisHiddenDisplayPlanes { get; set; } = "";

    /// <summary>
    /// Per-spec explicit S-100 Part 9 §11.7 display-mode selections. Keys
    /// are spec codes (e.g. "S-411"); values are the spec-native mode id
    /// (e.g. "IceScientificIcesodDisplayMode"). Populated by the per-dataset
    /// display-mode selector for products declaring more than one mode.
    /// Empty by default (each catalogue renders its default mode).
    /// </summary>
    public Dictionary<string, string> EcdisActiveDisplayModes { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the viewer's default ECDIS viewing-group visibility has
    /// been seeded into <see cref="EcdisHiddenViewingGroups"/>. Applied
    /// once per profile (see
    /// <see cref="Services.EcdisDisplayDefaults.Apply"/>) so the noisy
    /// S-101 "Independent Mariner Selector" patterns (shallow water
    /// pattern, survey accuracy/quality, low-accuracy marker) start off
    /// — including in the "All" category — while the mariner's later
    /// choices are never overridden on subsequent launches.
    /// </summary>
    public bool EcdisDefaultsApplied { get; set; }

    public bool IsStatusBarVisible { get; set; } = true;

    /// <summary>
    /// Whether the online OpenStreetMap basemap tile layer is shown
    /// beneath the chart data. Enabled by default. Disabling it removes
    /// the remote tile fetch entirely — useful for offline operation
    /// and for agent-driven performance runs that want to measure only
    /// dataset-related rendering without basemap tile activity (issue
    /// #295). Can be overridden per-run with the <c>--basemap</c>
    /// command-line flag.
    /// </summary>
    public bool BasemapEnabled { get; set; } = true;

    /// <summary>
    /// Which basemap is drawn beneath the chart data: <see
    /// cref="BasemapMode.None"/>, <see cref="BasemapMode.Offline"/>
    /// (bundled Natural Earth land — the zero-network default), or
    /// <see cref="BasemapMode.Online"/> (OpenStreetMap tiles). Replaces
    /// the legacy boolean <see cref="BasemapEnabled"/>; defaults to
    /// <see cref="BasemapMode.Offline"/> so a fresh install needs no
    /// network. Can be overridden per-run with <c>--basemap</c> (issue
    /// #295).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BasemapMode BasemapMode { get; set; } = BasemapMode.Offline;

    /// <summary>
    /// Whether the own-ship overlay (PR-D2) is visible. The synthetic
    /// driver is always running; this flag controls whether the
    /// source publishes the glyph to the dynamic-source overlay tier.
    /// </summary>
    /// <remarks>
    /// PR-D2.1 supersedes this field with the per-source
    /// <see cref="DynamicSourceVisibility"/> dictionary. The field is
    /// kept on the POCO so a downgrade still reads the user's choice;
    /// <see cref="Load"/> migrates its value into the dictionary on
    /// first load, and <see cref="Save"/> mirrors the dictionary
    /// entry back so the legacy field stays in sync for one release.
    /// </remarks>
    public bool OwnShipVisible { get; set; } = true;

    /// <summary>
    /// Whether the simulated ("mocked") own-ship position overlay is
    /// active. When <see langword="false"/> (the default) the
    /// <c>OwnShipSource</c> publishes no features so the synthetic
    /// own-location glyph never appears. Distinct from the per-source
    /// layer visibility tracked in <see cref="DynamicSourceVisibility"/>:
    /// this gate is authoritative for the synthetic source — when it is
    /// off, nothing is published regardless of layer visibility.
    /// </summary>
    public bool OwnShipOverlayEnabled { get; set; } = false;

    /// <summary>
    /// Per-source visibility for dynamic feature sources (PR-D2.1),
    /// keyed by <c>IDynamicFeatureSource.Id</c>. Drives the Layer
    /// Stack panel's visibility toggle for the
    /// <c>DynamicArrows</c> plane.
    /// </summary>
    public Dictionary<string, bool> DynamicSourceVisibility { get; set; }
        = new(StringComparer.Ordinal);

    /// <summary>
    /// User-configured own-ship vessel dimensions (length, beam,
    /// CCRP / GPS antenna offsets). <see langword="null"/> in legacy
    /// settings files; the consuming
    /// <c>IOwnShipVesselGeometryProvider</c> materialises a default
    /// instance when absent. Pure additive — no migration needed.
    /// </summary>
    public OwnShipSettings? OwnShip { get; set; }

    /// <summary>
    /// Selects which driver supplies the own-ship position — the
    /// steerable simulator (<c>"Simulated"</c>, the default) or pirate
    /// mode following a live AIS target (<c>"FollowAisTarget"</c>).
    /// Stored as the <see cref="OwnShipPositionSource"/> enum's string
    /// name. Absent / legacy settings default to the simulator, so the
    /// original dead-reckoned behaviour is preserved with no explicit
    /// migration. Unrecognised values fall back to the simulator.
    /// </summary>
    public string OwnShipPositionSource { get; set; } = "Simulated";

    /// <summary>
    /// MMSI of the AIS target impersonated in pirate mode, or
    /// <see langword="null"/> when no target is selected. Only
    /// meaningful while <see cref="OwnShipPositionSource"/> is
    /// <c>"FollowAisTarget"</c>.
    /// </summary>
    public uint? OwnShipFollowMmsi { get; set; }

    /// <summary>
    /// Optional AIS overlay configuration (PR-D3). When present and
    /// <see cref="AisOverlaySettings.Enabled"/> is <see langword="true"/>,
    /// the viewer registers an <c>AisDynamicFeatureSource</c> backed
    /// by the aisstream.io WebSocket driver. <see langword="null"/>
    /// — and the matching environment variable being unset — means
    /// the overlay is silently disabled. Pure additive; no migration.
    /// </summary>
    public AisOverlaySettings? AisOverlay { get; set; }

    /// <summary>
    /// User preference for whether the bottom timeline panel is shown.
    /// When true the panel surfaces (in either an empty state or with
    /// a global slider, depending on whether any time-varying dataset
    /// is loaded). When false the panel is hidden regardless of
    /// dataset state.
    /// </summary>
    public bool IsTimelineVisible { get; set; } = true;

    /// <summary>
    /// Whether the Pick Report (Object Information) side panel auto-opens
    /// when a feature is picked. The user can also explicitly hide it via
    /// the View menu.
    /// </summary>
    public bool IsPickPanelVisible { get; set; } = true;

    /// <summary>Whether the embedded MCP server should start when the viewer launches.</summary>
    public bool McpEnabled { get; set; } = false;

    /// <summary>
    /// TCP port for the MCP server. 0 = pick an ephemeral port at
    /// bind time (recommended).
    /// </summary>
    public int McpPort { get; set; } = 0;

    /// <summary>
    /// MCP server bind address. Loopback-only by default; not surfaced
    /// in the settings UI to enforce the loopback-only stance for v1.
    /// Power users can edit settings.json directly if they need to
    /// pin to a specific loopback variant.
    /// </summary>
    public string McpBindAddress { get; set; } = "127.0.0.1";

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

    /// <summary>
    /// Prepares a "reset all" clean slate: suppresses any further
    /// persistence for the remainder of this session (so the in-flight
    /// shutdown save cannot resurrect the old values) and deletes the
    /// settings file so the next launch starts from defaults. Best-effort:
    /// a delete failure is swallowed so the restart still proceeds.
    /// </summary>
    public void ResetForRestart()
    {
        IsReadOnly = true;
        try
        {
            if (File.Exists(SettingsFilePath))
                File.Delete(SettingsFilePath);
        }
        catch
        {
            // Best-effort: the next launch still falls back to defaults if
            // the (now unreadable/locked) file cannot be removed.
        }
    }

    public static ViewerSettings Load() => Load(DefaultSettingsPath);

    /// <summary>
    /// Loads settings from <paramref name="path"/>. The returned instance
    /// remembers the path so subsequent <see cref="Save"/> calls write back
    /// to the same file.
    /// </summary>
    public static ViewerSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<ViewerSettings>(json) ?? new ViewerSettings();
                settings.SettingsFilePath = path;

                // Issue #295: migrate legacy boolean BasemapEnabled to
                // the tri-state BasemapMode. Existing installs had an
                // online OSM basemap when enabled, so map true → Online
                // and false → None. Fresh installs (no key) keep the
                // Offline default.
                if (!json.Contains("\"BasemapMode\"", StringComparison.Ordinal))
                {
                    settings.BasemapMode = settings.BasemapEnabled ? BasemapMode.Online : BasemapMode.None;
                }
                settings.BasemapEnabled = settings.BasemapMode != BasemapMode.None;

                // Migrate legacy single-path setting to S-102 entry
                if (settings.PortrayalCataloguePath is { } legacy && !settings.CataloguePaths.ContainsKey("S-102"))
                {
                    settings.CataloguePaths["S-102"] = legacy;
                    settings.PortrayalCataloguePath = null;
                }

                // PR-D2.1: migrate legacy OwnShipVisible bool into the
                // per-source DynamicSourceVisibility dictionary so the
                // own-ship row in the Layer Stack picks up the user's
                // pre-PR-D2.1 choice on first load.
                if (!settings.DynamicSourceVisibility.ContainsKey(OwnShipVisibilityKey))
                {
                    settings.DynamicSourceVisibility[OwnShipVisibilityKey] = settings.OwnShipVisible;
                }

                return settings;
            }
        }
        catch
        {
            // If settings are corrupt, start fresh.
        }

        return new ViewerSettings { SettingsFilePath = path };
    }

    public void Save()
    {
        if (IsReadOnly)
            return;

        var dir = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // PR-D2.1: keep legacy OwnShipVisible in sync with the
        // per-source visibility dictionary so a downgrade still
        // picks up the user's current choice.
        if (DynamicSourceVisibility.TryGetValue(OwnShipVisibilityKey, out var ownShipVisible))
        {
            OwnShipVisible = ownShipVisible;
        }

        // Keep legacy BasemapEnabled consistent with BasemapMode for
        // forward/backward compatibility (issue #295).
        BasemapEnabled = BasemapMode != BasemapMode.None;

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFilePath, json);
    }

    /// <summary>
    /// Source id of the own-ship dynamic source (matches
    /// <c>OwnShipSource.FeatureId</c>). Used by the PR-D2.1
    /// migration / mirror logic.
    /// </summary>
    internal const string OwnShipVisibilityKey = "ownship";
}

/// <summary>
/// Persisted panel-size container (PR-M3). All fields are nullable;
/// <c>null</c> means "use the XAML-defined default". Sizes are written
/// through <see cref="Services.DebouncedSettingsSaver"/> so rapid
/// splitter drags coalesce into a single disk write 500 ms after the
/// last move.
/// </summary>
public sealed class PanelSizes
{
    /// <summary>Absolute pixel width of the left activity dock.</summary>
    public double? LeftDockWidth { get; set; }

    /// <summary>Absolute pixel width of the right dock.</summary>
    public double? RightDockWidth { get; set; }

    /// <summary>Absolute pixel height of the bottom dock.</summary>
    public double? BottomDockHeight { get; set; }

    /// <summary>
    /// Fraction <c>[0, 1]</c> of the Datasets-tab inner splitter — the
    /// share of vertical space occupied by the master list (top row).
    /// </summary>
    public double? DatasetsInnerSplit { get; set; }

    /// <summary>
    /// Fraction <c>[0, 1]</c> of the Catalog-tab inner splitter — the
    /// share of vertical space occupied by the entry list (top row).
    /// </summary>
    public double? CatalogInnerSplit { get; set; }

    /// <summary>
    /// Fraction <c>[0, 1]</c> of the Vessels-tab inner splitter — the
    /// share of vertical space occupied by the vessel list (top row).
    /// </summary>
    public double? VesselsInnerSplit { get; set; }
}
