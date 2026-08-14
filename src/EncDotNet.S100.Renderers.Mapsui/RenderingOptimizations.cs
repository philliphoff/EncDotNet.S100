namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Central, mutable configuration for the Mapsui vector-rendering optimizations
/// surfaced in the viewer's <c>Settings → Map</c> section.
/// </summary>
/// <remarks>
/// <para>
/// Each knob is <i>seeded</i> from the environment variable that used to be its
/// sole control (so headless tooling and perf runs keep working unchanged), and
/// may then be overridden at runtime by the viewer's settings.
/// </para>
/// <para>
/// When an environment variable is <b>explicitly</b> set (present and non-empty)
/// it takes precedence: the matching <c>…EnvExplicit</c> flag is <see langword="true"/>
/// and subsequent programmatic writes are ignored. This keeps perf measurements
/// faithful — a run started with <c>S100_VECTOR_TILE_METATILE=0</c> stays off
/// regardless of the persisted user setting. Normal users (no env var) get fully
/// live-toggleable knobs.
/// </para>
/// <para>
/// See <c>docs/design/mapsui-performance.md</c> for the measurements behind the
/// default choices.
/// </para>
/// </remarks>
public static class RenderingOptimizations
{

    /// <summary>Default tiled-base-plane gutter, in DIP (<c>S100_VECTOR_TILE_GUTTER</c>).</summary>
    public const double DefaultTileGutterDip = 64.0;

    /// <summary>Minimum / maximum accepted tiled-base-plane gutter, in DIP.</summary>
    public const double MinTileGutterDip = 0.0;

    /// <inheritdoc cref="MinTileGutterDip"/>
    public const double MaxTileGutterDip = 256.0;

    /// <summary>Default per-layer hot-cache native budget, in MB (<c>S100_VECTOR_TILE_BUDGET_MB</c>).</summary>
    public const double DefaultTileBudgetMb = 256.0;

    /// <summary>Minimum / maximum accepted per-layer hot-cache native budget, in MB.</summary>
    public const double MinTileBudgetMb = 4.0;

    /// <inheritdoc cref="MinTileBudgetMb"/>
    public const double MaxTileBudgetMb = 4096.0;

    /// <summary>Default warm disk-cache budget, in MB (<c>S100_VECTOR_TILE_DISK_MB</c>).</summary>
    public const double DefaultTileDiskMb = 512.0;

    /// <summary>Minimum / maximum accepted warm disk-cache budget, in MB.</summary>
    public const double MinTileDiskMb = 16.0;

    /// <inheritdoc cref="MinTileDiskMb"/>
    public const double MaxTileDiskMb = 8192.0;

    /// <summary>Default per-layer GPU-residency budget, in MB (<c>S100_VECTOR_TILE_GPU_MB</c>).</summary>
    public const double DefaultTileGpuBudgetMb = 256.0;

    /// <summary>Minimum / maximum accepted per-layer GPU-residency budget, in MB.</summary>
    public const double MinTileGpuBudgetMb = 4.0;

    /// <inheritdoc cref="MinTileGpuBudgetMb"/>
    public const double MaxTileGpuBudgetMb = 4096.0;

    /// <summary>Minimum / maximum accepted concurrent tile-rasterisation workers per layer.</summary>
    public const int MinTileWorkers = 1;

    /// <inheritdoc cref="MinTileWorkers"/>
    public const int MaxTileWorkers = 8;

    private static PerformanceProfile _resolvedProfile;
    private static bool _precomputedLineLodEnabled;
    private static VectorSceneMode _sceneMode;
    private static double _tileGutterDip;
    private static double _tileBudgetMb;
    private static bool _tilePredictionEnabled;
    private static bool _tileCrossBandPrewarmEnabled;
    private static bool _tileMetatileEnabled;
    private static bool _tileDiskCacheEnabled;
    private static double _tileDiskMb;
    private static bool _tileGpuResidencyEnabled;
    private static double _tileGpuBudgetMb;
    private static int _tileWorkerCount;
    private static string? _tileDiskDirectory;
    private static PerformanceProfile _profile;

    static RenderingOptimizations()
    {
        (_profile, ProfileEnvExplicit) = SeedProfile("S100_PERF_PROFILE", PerformanceProfile.Auto);

        // Precomputed line LOD pyramid (issue #489). Default OFF; the seven-gate
        // measurement pass in docs/design/mapsui-performance.md must clear
        // "≥50% cold-rebuild spike reduction, zero warm regression, no visual
        // regression" on both the dense S-101 cell and the multi-cell AU IC-ENC
        // set before we consider flipping this default.
        (_precomputedLineLodEnabled, PrecomputedLineLodEnvExplicit) =
            SeedBool("S100_VECTOR_LINE_LOD", defaultValue: false);

        (_sceneMode, SceneModeEnvExplicit) = SeedSceneMode();

        // The performance profile sets the *defaults* for the per-layer tile
        // budgets; Auto derives the tier from cores + RAM so a constrained host
        // gets smaller caches. Explicit env vars or persisted slider values
        // still override any of these.
        var tier = MachineProfile.Resolve(Profile);
        _resolvedProfile = tier;

        (_tileGutterDip, TileGutterDipEnvExplicit) =
            SeedDouble("S100_VECTOR_TILE_GUTTER", DefaultTileGutterDip, MinTileGutterDip, MaxTileGutterDip);
        (_tileBudgetMb, TileBudgetMbEnvExplicit) =
            SeedDouble("S100_VECTOR_TILE_BUDGET_MB", MachineProfile.TileBudgetMb(tier), MinTileBudgetMb, MaxTileBudgetMb);
        (_tilePredictionEnabled, TilePredictionEnvExplicit) =
            SeedBool("S100_VECTOR_TILE_PREDICT", defaultValue: true);
        // Idle cross-band (±1) pre-warm (issue #428): seed default on except on
        // the LowEnd tier, where the extra speculative raster/cache pressure is not
        // worth it on a constrained host. This governs only the default seed (and
        // the profile-switch default in ApplyProfile); an explicit opt-in via
        // env var or the setter is still honoured on any tier.
        (_tileCrossBandPrewarmEnabled, TileCrossBandPrewarmEnvExplicit) =
            SeedBool("S100_VECTOR_TILE_XBAND", defaultValue: tier != PerformanceProfile.LowEnd);
        (_tileMetatileEnabled, TileMetatileEnvExplicit) =
            SeedBool("S100_VECTOR_TILE_METATILE", defaultValue: false);
        (_tileDiskCacheEnabled, TileDiskCacheEnvExplicit) =
            SeedBool("S100_VECTOR_TILE_DISK", defaultValue: true);
        (_tileDiskMb, TileDiskMbEnvExplicit) =
            SeedDouble("S100_VECTOR_TILE_DISK_MB", MachineProfile.TileDiskMb(tier), MinTileDiskMb, MaxTileDiskMb);
        (_tileGpuResidencyEnabled, TileGpuResidencyEnvExplicit) =
            SeedBool("S100_VECTOR_TILE_GPU", defaultValue: false);
        (_tileGpuBudgetMb, TileGpuBudgetMbEnvExplicit) =
            SeedDouble("S100_VECTOR_TILE_GPU_MB", MachineProfile.TileGpuBudgetMb(tier), MinTileGpuBudgetMb, MaxTileGpuBudgetMb);
        (_tileWorkerCount, TileWorkerCountEnvExplicit) =
            SeedInt("S100_VECTOR_TILE_WORKERS", MachineProfile.TileWorkers(tier), MinTileWorkers, MaxTileWorkers);

        var diskDir = Environment.GetEnvironmentVariable("S100_VECTOR_TILE_DISK_DIR");
        TileDiskDirectoryEnvExplicit = !string.IsNullOrEmpty(diskDir);
        _tileDiskDirectory = TileDiskDirectoryEnvExplicit ? diskDir : null;
    }

    /// <summary>
    /// Whether the precomputed line LOD pyramid (issue #489) is built at dataset
    /// open. When on, each line's coordinates are simplified once per feature
    /// into a small tolerance-tagged pyramid (see
    /// <c>EncDotNet.S100.Pipelines.Vector.Caching.LineLodPyramid</c>). Seeded from
    /// <c>S100_VECTOR_LINE_LOD</c>; default <b>off</b> until the seven-gate perf
    /// pass in the design doc clears (see <c>docs/design/mapsui-performance.md</c>).
    /// </summary>
    /// <remarks>
    /// The render-time consumer of the pyramid (the legacy Mapsui "A" fast-line
    /// path) was retired with the A render arm under #600; only the pyramid
    /// <em>producer</em> remains wired (the disk-cache injection in the viewer's
    /// composition root). Retiring that orphaned producer — and this flag — is
    /// tracked by #601.
    /// </remarks>
    public static bool PrecomputedLineLodEnabled
    {
        get => _precomputedLineLodEnabled;
        set { if (!PrecomputedLineLodEnvExplicit) _precomputedLineLodEnabled = value; }
    }

    /// <summary>True when <see cref="PrecomputedLineLodEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool PrecomputedLineLodEnvExplicit { get; }

    /// <summary>
    /// Selects the base-plane scene mode: the <see cref="VectorSceneMode.Tiled"/>
    /// Phase-2 tiled base plane (default) or the <see cref="VectorSceneMode.Single"/>
    /// Phase-1 single-surface arm.
    /// Seeded from <c>S100_VECTOR_SCENE_MODE</c> (<c>single</c> selects Phase&#160;1).
    /// Read at layer build time, so a change re-applies on the next re-render.
    /// </summary>
    public static VectorSceneMode SceneMode
    {
        get => _sceneMode;
        set { if (!SceneModeEnvExplicit) _sceneMode = value; }
    }

    /// <summary>True when <see cref="SceneMode"/> is pinned by an explicit environment variable.</summary>
    public static bool SceneModeEnvExplicit { get; }

    /// <summary>
    /// Gutter, in DIP, rasterised beyond each tiled-base-plane tile's bounds so
    /// strokes crossing a tile seam keep their joins/caps. Seeded from
    /// <c>S100_VECTOR_TILE_GUTTER</c> (default <see cref="DefaultTileGutterDip"/>).
    /// Read when a tile is rasterised; only newly-rasterised tiles pick up a
    /// change (existing cached tiles keep their gutter until evicted), so a live
    /// change is best paired with a dataset reload.
    /// </summary>
    public static double TileGutterDip
    {
        get => _tileGutterDip;
        set { if (!TileGutterDipEnvExplicit) _tileGutterDip = Clamp(value, MinTileGutterDip, MaxTileGutterDip); }
    }

    /// <summary>True when <see cref="TileGutterDip"/> is pinned by an explicit environment variable.</summary>
    public static bool TileGutterDipEnvExplicit { get; }

    /// <summary>
    /// Per-layer hot-cache native budget, in MB, for the tiled base plane. Seeded
    /// from <c>S100_VECTOR_TILE_BUDGET_MB</c> (default
    /// <see cref="DefaultTileBudgetMb"/>). The cache capacity is captured when a
    /// layer's tile state is first created, so a change applies on the next
    /// dataset reload (restart-only in practice).
    /// </summary>
    public static double TileBudgetMb
    {
        get => _tileBudgetMb;
        set { if (!TileBudgetMbEnvExplicit) _tileBudgetMb = Clamp(value, MinTileBudgetMb, MaxTileBudgetMb); }
    }

    /// <summary>True when <see cref="TileBudgetMb"/> is pinned by an explicit environment variable.</summary>
    public static bool TileBudgetMbEnvExplicit { get; }

    /// <summary>
    /// Whether speculative prediction / pre-warm (Phase&#160;3) is enabled for the
    /// tiled base plane. Seeded from <c>S100_VECTOR_TILE_PREDICT</c> (default on).
    /// Read every frame, so a change takes effect live.
    /// </summary>
    public static bool TilePredictionEnabled
    {
        get => _tilePredictionEnabled;
        set { if (!TilePredictionEnvExplicit) _tilePredictionEnabled = value; }
    }

    /// <summary>True when <see cref="TilePredictionEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool TilePredictionEnvExplicit { get; }

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
    public static bool TileCrossBandPrewarmEnabled
    {
        get => _tileCrossBandPrewarmEnabled;
        set { if (!TileCrossBandPrewarmEnvExplicit) _tileCrossBandPrewarmEnabled = value; }
    }

    /// <summary>True when <see cref="TileCrossBandPrewarmEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool TileCrossBandPrewarmEnvExplicit { get; }

    /// <summary>
    /// Whether adjacent tile misses may be rasterised as one 2&#215;2 metatile
    /// job and sliced back into tile-granular cache entries (issue&#160;#427).
    /// Seeded from <c>S100_VECTOR_TILE_METATILE</c>; default off until the
    /// real-corpus performance gate demonstrates a measurable gain. Read for
    /// each worker job, so a change takes effect without rebuilding a dataset.
    /// </summary>
    public static bool TileMetatileEnabled
    {
        get => _tileMetatileEnabled;
        set { if (!TileMetatileEnvExplicit) _tileMetatileEnabled = value; }
    }

    /// <summary>True when <see cref="TileMetatileEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool TileMetatileEnvExplicit { get; }

    /// <summary>
    /// Whether the persistent warm disk tile cache (Phase&#160;4) is enabled.
    /// Seeded from <c>S100_VECTOR_TILE_DISK</c> (default on). The shared disk
    /// cache is created once per process, so a change applies on restart.
    /// </summary>
    public static bool TileDiskCacheEnabled
    {
        get => _tileDiskCacheEnabled;
        set { if (!TileDiskCacheEnvExplicit) _tileDiskCacheEnabled = value; }
    }

    /// <summary>True when <see cref="TileDiskCacheEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool TileDiskCacheEnvExplicit { get; }

    /// <summary>
    /// Warm disk tile-cache budget, in MB. Seeded from
    /// <c>S100_VECTOR_TILE_DISK_MB</c> (default <see cref="DefaultTileDiskMb"/>).
    /// Read when the shared disk cache is created (once per process), so a change
    /// applies on restart.
    /// </summary>
    public static double TileDiskMb
    {
        get => _tileDiskMb;
        set { if (!TileDiskMbEnvExplicit) _tileDiskMb = Clamp(value, MinTileDiskMb, MaxTileDiskMb); }
    }

    /// <summary>True when <see cref="TileDiskMb"/> is pinned by an explicit environment variable.</summary>
    public static bool TileDiskMbEnvExplicit { get; }

    /// <summary>
    /// Optional override directory for the warm disk tile cache, seeded from
    /// <c>S100_VECTOR_TILE_DISK_DIR</c>. <see langword="null"/> uses an OS-temp
    /// subdirectory. Read once when the disk cache is created (restart-only);
    /// not surfaced as an interactive control. The host may assign it at
    /// startup (e.g. to re-root the cache under a <c>--data-dir</c> folder);
    /// an explicit environment variable always wins.
    /// </summary>
    public static string? TileDiskDirectory
    {
        get => _tileDiskDirectory;
        set { if (!TileDiskDirectoryEnvExplicit) _tileDiskDirectory = value; }
    }

    /// <summary>True when <see cref="TileDiskDirectory"/> is pinned by an explicit environment variable.</summary>
    public static bool TileDiskDirectoryEnvExplicit { get; }

    /// <summary>
    /// Whether GPU texture residency (Phase&#160;5) is enabled for the tiled base
    /// plane. Seeded from <c>S100_VECTOR_TILE_GPU</c> (default off). Read every
    /// frame, so a change takes effect live (inert on a software surface).
    /// </summary>
    public static bool TileGpuResidencyEnabled
    {
        get => _tileGpuResidencyEnabled;
        set { if (!TileGpuResidencyEnvExplicit) _tileGpuResidencyEnabled = value; }
    }

    /// <summary>True when <see cref="TileGpuResidencyEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool TileGpuResidencyEnvExplicit { get; }

    /// <summary>
    /// Per-layer GPU-residency budget, in MB. Seeded from
    /// <c>S100_VECTOR_TILE_GPU_MB</c> (default <see cref="DefaultTileGpuBudgetMb"/>).
    /// The resident-texture cache is sized when first created, so a change applies
    /// on the next dataset reload (restart-only in practice).
    /// </summary>
    public static double TileGpuBudgetMb
    {
        get => _tileGpuBudgetMb;
        set { if (!TileGpuBudgetMbEnvExplicit) _tileGpuBudgetMb = Clamp(value, MinTileGpuBudgetMb, MaxTileGpuBudgetMb); }
    }

    /// <summary>True when <see cref="TileGpuBudgetMb"/> is pinned by an explicit environment variable.</summary>
    public static bool TileGpuBudgetMbEnvExplicit { get; }

    /// <summary>
    /// Number of concurrent tile-rasterisation workers per layer
    /// (<c>S100_VECTOR_TILE_WORKERS</c>, default <see cref="MachineProfile.TileWorkers(PerformanceProfile)"/>).
    /// Multiple workers drain the visible-miss queue in parallel so a cold pan's
    /// tiles no longer rasterise strictly one at a time. The count is captured
    /// when a layer first starts producing tiles, so a change applies on the next
    /// dataset reload. Clamped to <see cref="MinTileWorkers"/>..<see cref="MaxTileWorkers"/>.
    /// </summary>
    public static int TileWorkerCount
    {
        get => _tileWorkerCount;
        set { if (!TileWorkerCountEnvExplicit) _tileWorkerCount = ClampInt(value, MinTileWorkers, MaxTileWorkers); }
    }

    /// <summary>True when <see cref="TileWorkerCount"/> is pinned by an explicit environment variable.</summary>
    public static bool TileWorkerCountEnvExplicit { get; }

    /// <summary>
    /// The selected performance profile. <see cref="PerformanceProfile.Auto"/>
    /// (the default) derives a tier from detected cores + RAM. Setting a profile
    /// recomputes any tile budget / worker default that is not pinned by an env
    /// var or a prior explicit slider value, so a constrained host can be tuned
    /// up or a workstation tuned down. Applies to the next dataset reload.
    /// </summary>
    public static PerformanceProfile Profile
    {
        get => _profile;
        set { if (!ProfileEnvExplicit) { _profile = value; ApplyProfile(value); } }
    }

    /// <summary>True when <see cref="Profile"/> is pinned by an explicit environment variable.</summary>
    public static bool ProfileEnvExplicit { get; }

    /// <summary>The concrete tier <see cref="Profile"/> resolves to on this host.</summary>
    public static PerformanceProfile ResolvedProfile => _resolvedProfile;

    /// <summary>
    /// Recomputes profile-derived budgets for non-env-pinned knobs.
    /// </summary>
    private static void ApplyProfile(PerformanceProfile profile)
    {
        var tier = MachineProfile.Resolve(profile);
        _resolvedProfile = tier;
        if (!TileBudgetMbEnvExplicit) _tileBudgetMb = MachineProfile.TileBudgetMb(tier);
        if (!TileGpuBudgetMbEnvExplicit) _tileGpuBudgetMb = MachineProfile.TileGpuBudgetMb(tier);
        if (!TileDiskMbEnvExplicit) _tileDiskMb = MachineProfile.TileDiskMb(tier);
        if (!TileWorkerCountEnvExplicit) _tileWorkerCount = MachineProfile.TileWorkers(tier);
        if (!TileCrossBandPrewarmEnvExplicit) _tileCrossBandPrewarmEnabled = tier != PerformanceProfile.LowEnd;
    }

    private static (bool value, bool envExplicit) SeedBool(string envName, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrEmpty(raw))
        {
            return (defaultValue, false);
        }

        var enabled = raw is not ("0" or "false" or "FALSE" or "False" or "off" or "OFF");
        return (enabled, true);
    }

    private static (VectorSceneMode mode, bool envExplicit) SeedSceneMode()
    {
        var raw = Environment.GetEnvironmentVariable("S100_VECTOR_SCENE_MODE");
        if (string.IsNullOrEmpty(raw))
        {
            return (VectorSceneMode.Tiled, false);
        }

        var mode = string.Equals(raw.Trim(), "single", StringComparison.OrdinalIgnoreCase)
            ? VectorSceneMode.Single
            : VectorSceneMode.Tiled;
        return (mode, true);
    }

    private static (double value, bool envExplicit) SeedDouble(string envName, double fallback, double min, double max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrEmpty(raw)
            && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            return (Clamp(v, min, max), true);
        }

        return (fallback, false);
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    private static int ClampInt(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;

    private static (int value, bool envExplicit) SeedInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrEmpty(raw)
            && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            return (ClampInt(v, min, max), true);
        }

        return (fallback, false);
    }

    private static (PerformanceProfile value, bool envExplicit) SeedProfile(string envName, PerformanceProfile fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
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
