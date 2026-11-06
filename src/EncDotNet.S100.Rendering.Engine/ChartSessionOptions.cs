namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// The tuning options of a chart rendering session: tile gutter, the memory,
/// disk and GPU budgets, prediction and cross-band pre-warm, metatiles, the
/// warm disk cache, GPU residency, the scene mode and the performance profile.
/// They replace the <c>RenderingOptimizations</c> statics and the
/// <c>S100_VECTOR_TILE_*</c> environment knobs (map rendering slice&#160;7,
/// issue&#160;#607&#160;Q2).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FromEnvironment"/> seeds each option from the environment
/// variable that used to be its sole control, so headless tooling and perf
/// runs keep working unchanged. When a variable is <b>explicitly</b> set
/// (present and non-empty) it pins the option: the matching
/// <c>…EnvExplicit</c> flag is <see langword="true"/> and later writes are
/// ignored, so a run started with <c>S100_VECTOR_TILE_METATILE=0</c> stays off
/// whatever the persisted user setting says. Options from the parameterless
/// constructor read no environment and pin nothing.
/// </para>
/// <para>
/// The engine reads the process-wide <see cref="ChartEngine.Options"/>
/// instance. Each option says when a change takes effect. Writes are not
/// synchronised; a host changes options from one thread (its UI thread).
/// </para>
/// <para>
/// See <c>docs/design/mapsui-performance.md</c> for the measurements behind the
/// default choices.
/// </para>
/// </remarks>
public sealed class ChartSessionOptions
{

    /// <summary>Default tiled-base-plane gutter, in DIP (<c>S100_VECTOR_TILE_GUTTER</c>).</summary>
    public const double DefaultTileGutterDip = 64.0;

    /// <summary>Minimum / maximum accepted tiled-base-plane gutter, in DIP.</summary>
    public const double MinTileGutterDip = 0.0;

    /// <inheritdoc cref="MinTileGutterDip"/>
    public const double MaxTileGutterDip = 256.0;

    /// <summary>
    /// Default process-wide hot-cache native budget, in MB, shared by every
    /// layer's tiles (<c>S100_VECTOR_TILE_BUDGET_MB</c>; the HighEnd tier).
    /// </summary>
    public const double DefaultTileBudgetMb = 1536.0;

    /// <summary>Minimum / maximum accepted process-wide hot-cache native budget, in MB.</summary>
    public const double MinTileBudgetMb = 4.0;

    /// <inheritdoc cref="MinTileBudgetMb"/>
    public const double MaxTileBudgetMb = 4096.0;

    /// <summary>Default warm disk-cache budget, in MB (<c>S100_VECTOR_TILE_DISK_MB</c>).</summary>
    public const double DefaultTileDiskMb = 512.0;

    /// <summary>Minimum / maximum accepted warm disk-cache budget, in MB.</summary>
    public const double MinTileDiskMb = 16.0;

    /// <inheritdoc cref="MinTileDiskMb"/>
    public const double MaxTileDiskMb = 8192.0;

    /// <summary>
    /// Default process-wide GPU-residency budget, in MB, shared by every layer's
    /// textures (<c>S100_VECTOR_TILE_GPU_MB</c>; the HighEnd tier).
    /// </summary>
    public const double DefaultTileGpuBudgetMb = 768.0;

    /// <summary>Minimum / maximum accepted process-wide GPU-residency budget, in MB.</summary>
    public const double MinTileGpuBudgetMb = 4.0;

    /// <inheritdoc cref="MinTileGpuBudgetMb"/>
    public const double MaxTileGpuBudgetMb = 4096.0;

    /// <summary>Minimum / maximum accepted Skia GPU resource-cache budget, in MB (<c>S100_SKIA_GPU_RESOURCE_MB</c>).</summary>
    public const double MinSkiaGpuResourceMb = 32.0;

    /// <inheritdoc cref="MinSkiaGpuResourceMb"/>
    public const double MaxSkiaGpuResourceMb = 4096.0;

    /// <summary>Default single-surface scene margin, in screen pixels (<c>S100_VECTOR_SCENE_MARGIN</c>).</summary>
    public const double DefaultSceneMarginPx = 256.0;

    /// <summary>Minimum / maximum accepted single-surface scene margin, in screen pixels.</summary>
    public const double MinSceneMarginPx = 0.0;

    /// <inheritdoc cref="MinSceneMarginPx"/>
    public const double MaxSceneMarginPx = 4096.0;

    private PerformanceProfile _resolvedProfile;
    private VectorSceneMode _sceneMode;
    private double _tileGutterDip;
    private double _tileBudgetMb;
    private bool _tilePredictionEnabled;
    private bool _tileCrossBandPrewarmEnabled;
    private bool _tileMetatileEnabled;
    private bool _tileDiskCacheEnabled;
    private double _tileDiskMb;
    private bool _tileGpuResidencyEnabled;
    private double _tileGpuBudgetMb;
    private string? _tileDiskDirectory;
    private PerformanceProfile _profile;
    private bool _liveBaseEnabled;
    private double _sceneMarginPx;

    /// <summary>
    /// Creates options with the defaults for this machine's tier, reading no
    /// environment variables, so nothing is pinned.
    /// </summary>
    public ChartSessionOptions()
        : this(static _ => null)
    {
    }

    private ChartSessionOptions(Func<string, string?> environment)
    {
        (_profile, ProfileEnvExplicit) = SeedProfile(environment, "S100_PERF_PROFILE", PerformanceProfile.Auto);

        (_sceneMode, SceneModeEnvExplicit) = SeedSceneMode(environment);

        // The performance profile sets the *defaults* for the per-layer tile
        // budgets; Auto derives the tier from cores + RAM so a constrained host
        // gets smaller caches. Explicit env vars or persisted slider values
        // still override any of these.
        var tier = MachineProfile.Resolve(Profile);
        _resolvedProfile = tier;

        (_tileGutterDip, TileGutterDipEnvExplicit) =
            SeedDouble(environment, "S100_VECTOR_TILE_GUTTER", DefaultTileGutterDip, MinTileGutterDip, MaxTileGutterDip);
        (_tileBudgetMb, TileBudgetMbEnvExplicit) =
            SeedDouble(environment, "S100_VECTOR_TILE_BUDGET_MB", MachineProfile.TileBudgetMb(tier), MinTileBudgetMb, MaxTileBudgetMb);
        (_tilePredictionEnabled, TilePredictionEnvExplicit) =
            SeedBool(environment, "S100_VECTOR_TILE_PREDICT", defaultValue: true);
        // Idle cross-band (±1) pre-warm (issue #428): seed default on except on
        // the LowEnd tier, where the extra speculative raster/cache pressure is not
        // worth it on a constrained host. This governs only the default seed (and
        // the profile-switch default in ApplyProfile); an explicit opt-in via
        // env var or the setter is still honoured on any tier.
        (_tileCrossBandPrewarmEnabled, TileCrossBandPrewarmEnvExplicit) =
            SeedBool(environment, "S100_VECTOR_TILE_XBAND", defaultValue: tier != PerformanceProfile.LowEnd);
        (_tileMetatileEnabled, TileMetatileEnvExplicit) =
            SeedBool(environment, "S100_VECTOR_TILE_METATILE", defaultValue: false);
        (_tileDiskCacheEnabled, TileDiskCacheEnvExplicit) =
            SeedBool(environment, "S100_VECTOR_TILE_DISK", defaultValue: true);
        (_tileDiskMb, TileDiskMbEnvExplicit) =
            SeedDouble(environment, "S100_VECTOR_TILE_DISK_MB", MachineProfile.TileDiskMb(tier), MinTileDiskMb, MaxTileDiskMb);
        (_tileGpuResidencyEnabled, TileGpuResidencyEnvExplicit) =
            SeedBool(environment, "S100_VECTOR_TILE_GPU", defaultValue: false);
        (_tileGpuBudgetMb, TileGpuBudgetMbEnvExplicit) =
            SeedDouble(environment, "S100_VECTOR_TILE_GPU_MB", MachineProfile.TileGpuBudgetMb(tier), MinTileGpuBudgetMb, MaxTileGpuBudgetMb);
        (SkiaGpuResourceMb, _) =
            SeedDouble(environment, "S100_SKIA_GPU_RESOURCE_MB", MachineProfile.SkiaGpuResourceMb(tier), MinSkiaGpuResourceMb, MaxSkiaGpuResourceMb);

        var diskDir = environment("S100_VECTOR_TILE_DISK_DIR");
        TileDiskDirectoryEnvExplicit = !string.IsNullOrEmpty(diskDir);
        _tileDiskDirectory = TileDiskDirectoryEnvExplicit ? diskDir : null;

        (_liveBaseEnabled, LiveBaseEnvExplicit) =
            SeedOffSwitch(environment, "S100_VECTOR_TILE_LIVE_BASE", defaultValue: true);
        (_sceneMarginPx, SceneMarginPxEnvExplicit) =
            SeedDouble(environment, "S100_VECTOR_SCENE_MARGIN", DefaultSceneMarginPx, MinSceneMarginPx, MaxSceneMarginPx);
    }

    /// <summary>
    /// Creates options seeded from this process's environment variables; each
    /// variable that is set pins its option (see the remarks).
    /// </summary>
    /// <returns>The options.</returns>
    public static ChartSessionOptions FromEnvironment() => new(Environment.GetEnvironmentVariable);

    /// <summary>
    /// Selects the base-plane scene mode: the <see cref="VectorSceneMode.Tiled"/>
    /// Phase-2 tiled base plane (default) or the <see cref="VectorSceneMode.Single"/>
    /// Phase-1 single-surface arm.
    /// Seeded from <c>S100_VECTOR_SCENE_MODE</c> (<c>single</c> selects Phase&#160;1).
    /// Read at layer build time, so a change re-applies on the next re-render.
    /// </summary>
    public VectorSceneMode SceneMode
    {
        get => _sceneMode;
        set { if (!SceneModeEnvExplicit) _sceneMode = value; }
    }

    /// <summary>True when <see cref="SceneMode"/> is pinned by an explicit environment variable.</summary>
    public bool SceneModeEnvExplicit { get; }

    /// <summary>
    /// Gutter, in DIP, rasterised beyond each tiled-base-plane tile's bounds so
    /// strokes crossing a tile seam keep their joins/caps. Seeded from
    /// <c>S100_VECTOR_TILE_GUTTER</c> (default <see cref="DefaultTileGutterDip"/>).
    /// Read when a tile is rasterised; only newly-rasterised tiles pick up a
    /// change (existing cached tiles keep their gutter until evicted), so a live
    /// change is best paired with a dataset reload.
    /// </summary>
    public double TileGutterDip
    {
        get => _tileGutterDip;
        set { if (!TileGutterDipEnvExplicit) _tileGutterDip = Clamp(value, MinTileGutterDip, MaxTileGutterDip); }
    }

    /// <summary>True when <see cref="TileGutterDip"/> is pinned by an explicit environment variable.</summary>
    public bool TileGutterDipEnvExplicit { get; }

    /// <summary>
    /// Process-wide hot-cache native budget, in MB, for the tiled base plane:
    /// every layer's tiles share it, and the least recently drawn go first when
    /// it is full (map rendering slice&#160;4). Seeded from
    /// <c>S100_VECTOR_TILE_BUDGET_MB</c> (default by machine tier,
    /// <see cref="DefaultTileBudgetMb"/> on HighEnd). Read on every tile insert,
    /// so a change applies at once.
    /// </summary>
    public double TileBudgetMb
    {
        get => _tileBudgetMb;
        set { if (!TileBudgetMbEnvExplicit) _tileBudgetMb = Clamp(value, MinTileBudgetMb, MaxTileBudgetMb); }
    }

    /// <summary>True when <see cref="TileBudgetMb"/> is pinned by an explicit environment variable.</summary>
    public bool TileBudgetMbEnvExplicit { get; }

    /// <summary>
    /// Whether speculative prediction / pre-warm (Phase&#160;3) is enabled for the
    /// tiled base plane. Seeded from <c>S100_VECTOR_TILE_PREDICT</c> (default on).
    /// Read every frame, so a change takes effect live.
    /// </summary>
    public bool TilePredictionEnabled
    {
        get => _tilePredictionEnabled;
        set { if (!TilePredictionEnvExplicit) _tilePredictionEnabled = value; }
    }

    /// <summary>True when <see cref="TilePredictionEnabled"/> is pinned by an explicit environment variable.</summary>
    public bool TilePredictionEnvExplicit { get; }

    /// <summary>
    /// Whether idle cross-band pre-warm (issue&#160;#428) is enabled: when the
    /// tiled base plane is otherwise idle (no cold visible misses and cache
    /// headroom to spare) the renderer speculatively rasterises the
    /// band&#160;±&#160;1 tiles covering the current viewport, so a subsequent
    /// zoom starts warm. Seeded from <c>S100_VECTOR_TILE_XBAND</c>; the seed
    /// <b>default</b> is on, except off on the <see cref="PerformanceProfile.LowEnd"/>
    /// tier (and re-forced off whenever the profile switches to LowEnd — see
    /// <see cref="ApplyProfile"/>), where the extra speculative raster/cache
    /// pressure is not worth it on a constrained host. That LowEnd rule governs the
    /// <i>default</i> only: an explicit opt-in — env var or this setter (e.g. the
    /// viewer toggle) — is still honoured on any tier, exactly like
    /// <see cref="TilePredictionEnabled"/>. Read every frame, so a change takes
    /// effect live. Independent of <see cref="TilePredictionEnabled"/> (same-band
    /// halo/fan warm set), but drained at a strictly lower worker priority than it.
    /// </summary>
    public bool TileCrossBandPrewarmEnabled
    {
        get => _tileCrossBandPrewarmEnabled;
        set { if (!TileCrossBandPrewarmEnvExplicit) _tileCrossBandPrewarmEnabled = value; }
    }

    /// <summary>True when <see cref="TileCrossBandPrewarmEnabled"/> is pinned by an explicit environment variable.</summary>
    public bool TileCrossBandPrewarmEnvExplicit { get; }

    /// <summary>
    /// Whether adjacent tile misses may be rasterised as one 2&#215;2 metatile
    /// job and sliced back into tile-granular cache entries (issue&#160;#427).
    /// Seeded from <c>S100_VECTOR_TILE_METATILE</c>; default off until the
    /// real-corpus performance gate demonstrates a measurable gain. Read for
    /// each worker job, so a change takes effect without rebuilding a dataset.
    /// </summary>
    public bool TileMetatileEnabled
    {
        get => _tileMetatileEnabled;
        set { if (!TileMetatileEnvExplicit) _tileMetatileEnabled = value; }
    }

    /// <summary>True when <see cref="TileMetatileEnabled"/> is pinned by an explicit environment variable.</summary>
    public bool TileMetatileEnvExplicit { get; }

    /// <summary>
    /// Whether the persistent warm disk tile cache (Phase&#160;4) is enabled.
    /// Seeded from <c>S100_VECTOR_TILE_DISK</c> (default on). The shared disk
    /// cache is created once per process, so a change applies on restart.
    /// </summary>
    public bool TileDiskCacheEnabled
    {
        get => _tileDiskCacheEnabled;
        set { if (!TileDiskCacheEnvExplicit) _tileDiskCacheEnabled = value; }
    }

    /// <summary>True when <see cref="TileDiskCacheEnabled"/> is pinned by an explicit environment variable.</summary>
    public bool TileDiskCacheEnvExplicit { get; }

    /// <summary>
    /// Warm disk tile-cache budget, in MB. Seeded from
    /// <c>S100_VECTOR_TILE_DISK_MB</c> (default <see cref="DefaultTileDiskMb"/>).
    /// Read when the shared disk cache is created (once per process), so a change
    /// applies on restart.
    /// </summary>
    public double TileDiskMb
    {
        get => _tileDiskMb;
        set { if (!TileDiskMbEnvExplicit) _tileDiskMb = Clamp(value, MinTileDiskMb, MaxTileDiskMb); }
    }

    /// <summary>True when <see cref="TileDiskMb"/> is pinned by an explicit environment variable.</summary>
    public bool TileDiskMbEnvExplicit { get; }

    /// <summary>
    /// Optional override directory for the warm disk tile cache, seeded from
    /// <c>S100_VECTOR_TILE_DISK_DIR</c>. <see langword="null"/> uses an OS-temp
    /// subdirectory. Read once when the disk cache is created (restart-only);
    /// not surfaced as an interactive control. The host may assign it at
    /// startup (e.g. to re-root the cache under a <c>--data-dir</c> folder);
    /// an explicit environment variable always wins.
    /// </summary>
    public string? TileDiskDirectory
    {
        get => _tileDiskDirectory;
        set { if (!TileDiskDirectoryEnvExplicit) _tileDiskDirectory = value; }
    }

    /// <summary>True when <see cref="TileDiskDirectory"/> is pinned by an explicit environment variable.</summary>
    public bool TileDiskDirectoryEnvExplicit { get; }

    /// <summary>
    /// Whether GPU texture residency (Phase&#160;5) is enabled for the tiled base
    /// plane. Seeded from <c>S100_VECTOR_TILE_GPU</c> (default off). Read every
    /// frame, so a change takes effect live (inert on a software surface).
    /// </summary>
    public bool TileGpuResidencyEnabled
    {
        get => _tileGpuResidencyEnabled;
        set { if (!TileGpuResidencyEnvExplicit) _tileGpuResidencyEnabled = value; }
    }

    /// <summary>True when <see cref="TileGpuResidencyEnabled"/> is pinned by an explicit environment variable.</summary>
    public bool TileGpuResidencyEnvExplicit { get; }

    /// <summary>
    /// Process-wide GPU-residency budget, in MB, shared by every layer's tile
    /// textures (map rendering slice&#160;4). Seeded from
    /// <c>S100_VECTOR_TILE_GPU_MB</c> (default by machine tier,
    /// <see cref="DefaultTileGpuBudgetMb"/> on HighEnd). Read on every upload, so
    /// a change applies at once.
    /// </summary>
    public double TileGpuBudgetMb
    {
        get => _tileGpuBudgetMb;
        set { if (!TileGpuBudgetMbEnvExplicit) _tileGpuBudgetMb = Clamp(value, MinTileGpuBudgetMb, MaxTileGpuBudgetMb); }
    }

    /// <summary>True when <see cref="TileGpuBudgetMb"/> is pinned by an explicit environment variable.</summary>
    public bool TileGpuBudgetMbEnvExplicit { get; }

    /// <summary>
    /// Budget, in MB, for the host GPU context's resource cache — the cache Skia
    /// keeps uploaded textures in, including the textures for the raster tiles
    /// the compositor blits every frame. Seeded from
    /// <c>S100_SKIA_GPU_RESOURCE_MB</c> (default
    /// <see cref="MachineProfile.SkiaGpuResourceMb(PerformanceProfile)"/>) and read
    /// by a host when it creates its GPU context, so it is start-up only. When the
    /// visible tiles of every drawing layer do not fit, Skia evicts and
    /// re-uploads them on every frame; Avalonia's own default (about 28&#160;MB)
    /// holds only a couple of dozen retina tiles.
    /// </summary>
    public double SkiaGpuResourceMb { get; }

    /// <summary>
    /// Whether a scene whose product hints live drawing
    /// (<see cref="Rendering.Scene.VectorScene.PreferLiveDrawing"/>, an S-421
    /// route) is drawn directly each frame instead of tiled. Seeded from
    /// <c>S100_VECTOR_TILE_LIVE_BASE</c> (default on); off tiles every scene.
    /// Read when a scene is bound, so a change applies to scenes bound after it.
    /// </summary>
    public bool LiveBaseEnabled
    {
        get => _liveBaseEnabled;
        set { if (!LiveBaseEnvExplicit) _liveBaseEnabled = value; }
    }

    /// <summary>True when <see cref="LiveBaseEnabled"/> is pinned by an explicit environment variable.</summary>
    public bool LiveBaseEnvExplicit { get; }

    /// <summary>
    /// Margin, in screen pixels, the single-surface scene mode
    /// (<see cref="VectorSceneMode.Single"/>) rasterises around the view on
    /// every edge, so a pan reveals already-rendered content out to the margin
    /// before a re-raster is needed. Seeded from <c>S100_VECTOR_SCENE_MARGIN</c>
    /// (default <see cref="DefaultSceneMarginPx"/>). Read every frame.
    /// </summary>
    public double SceneMarginPx
    {
        get => _sceneMarginPx;
        set { if (!SceneMarginPxEnvExplicit) _sceneMarginPx = Clamp(value, MinSceneMarginPx, MaxSceneMarginPx); }
    }

    /// <summary>True when <see cref="SceneMarginPx"/> is pinned by an explicit environment variable.</summary>
    public bool SceneMarginPxEnvExplicit { get; }

    /// <summary>
    /// The selected performance profile. <see cref="PerformanceProfile.Auto"/>
    /// (the default) derives a tier from detected cores + RAM. Setting a profile
    /// recomputes any tile budget / worker default that is not pinned by an env
    /// var or a prior explicit slider value, so a constrained host can be tuned
    /// up or a workstation tuned down. Applies to the next dataset reload.
    /// </summary>
    public PerformanceProfile Profile
    {
        get => _profile;
        set { if (!ProfileEnvExplicit) { _profile = value; ApplyProfile(value); } }
    }

    /// <summary>True when <see cref="Profile"/> is pinned by an explicit environment variable.</summary>
    public bool ProfileEnvExplicit { get; }

    /// <summary>The concrete tier <see cref="Profile"/> resolves to on this host.</summary>
    public PerformanceProfile ResolvedProfile => _resolvedProfile;

    /// <summary>
    /// Recomputes profile-derived budgets for non-env-pinned knobs.
    /// </summary>
    private void ApplyProfile(PerformanceProfile profile)
    {
        var tier = MachineProfile.Resolve(profile);
        _resolvedProfile = tier;
        if (!TileBudgetMbEnvExplicit) _tileBudgetMb = MachineProfile.TileBudgetMb(tier);
        if (!TileGpuBudgetMbEnvExplicit) _tileGpuBudgetMb = MachineProfile.TileGpuBudgetMb(tier);
        if (!TileDiskMbEnvExplicit) _tileDiskMb = MachineProfile.TileDiskMb(tier);
        if (!TileCrossBandPrewarmEnvExplicit) _tileCrossBandPrewarmEnabled = tier != PerformanceProfile.LowEnd;
    }

    private static (bool value, bool envExplicit) SeedBool(Func<string, string?> environment, string envName, bool defaultValue)
    {
        var raw = environment(envName);
        if (string.IsNullOrEmpty(raw))
        {
            return (defaultValue, false);
        }

        var enabled = raw is not ("0" or "false" or "FALSE" or "False" or "off" or "OFF");
        return (enabled, true);
    }

    // The parse S100_VECTOR_TILE_LIVE_BASE always had: case-insensitive
    // 0/false/off/no turn it off.
    private static (bool value, bool envExplicit) SeedOffSwitch(Func<string, string?> environment, string envName, bool defaultValue)
    {
        var raw = environment(envName);
        if (string.IsNullOrEmpty(raw))
        {
            return (defaultValue, false);
        }

        var enabled = !(raw.Equals("0", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("false", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("off", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("no", StringComparison.OrdinalIgnoreCase));
        return (enabled, true);
    }

    private static (VectorSceneMode mode, bool envExplicit) SeedSceneMode(Func<string, string?> environment)
    {
        var raw = environment("S100_VECTOR_SCENE_MODE");
        if (string.IsNullOrEmpty(raw))
        {
            return (VectorSceneMode.Tiled, false);
        }

        var mode = string.Equals(raw.Trim(), "single", StringComparison.OrdinalIgnoreCase)
            ? VectorSceneMode.Single
            : VectorSceneMode.Tiled;
        return (mode, true);
    }

    private static (double value, bool envExplicit) SeedDouble(Func<string, string?> environment, string envName, double fallback, double min, double max)
    {
        var raw = environment(envName);
        if (!string.IsNullOrEmpty(raw)
            && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            return (Clamp(v, min, max), true);
        }

        return (fallback, false);
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    private static (PerformanceProfile value, bool envExplicit) SeedProfile(Func<string, string?> environment, string envName, PerformanceProfile fallback)
    {
        var raw = environment(envName);
        if (!string.IsNullOrEmpty(raw) && Enum.TryParse<PerformanceProfile>(raw.Trim(), ignoreCase: true, out var v))
        {
            return (v, true);
        }

        return (fallback, false);
    }
}

/// <summary>
/// Selects the base-plane scene mode: the tiled base plane (Phase&#160;2) or the
/// single-surface arm (Phase&#160;1). See
/// <c>docs/design/S100-Render-Subsystem-Design.md</c>.
/// </summary>
public enum VectorSceneMode
{
    /// <summary>
    /// The Phase-2 tiled base plane: a pyramid of cached, gutter-rasterised
    /// tiles composited under an affine. The default within the TiledScene arm.
    /// </summary>
    Tiled = 0,

    /// <summary>
    /// The Phase-1 single-surface arm: the whole viewport (plus an over-render
    /// margin) is rasterised to one image on a worker and composited under
    /// translation. Selected by <c>S100_VECTOR_SCENE_MODE=single</c>.
    /// </summary>
    Single = 1,
}
