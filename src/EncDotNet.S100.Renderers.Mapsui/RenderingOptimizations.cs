using System;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Central, mutable configuration for the Mapsui vector-rendering optimizations
/// surfaced in the viewer's <c>Settings → Map</c> section.
/// </summary>
/// <remarks>
/// <para>
/// Each flag is <i>seeded</i> from the legacy environment variable that used to
/// be the sole control (so the performance A/B harness and headless tooling keep
/// working unchanged), and may then be overridden at runtime by the viewer's
/// settings.
/// </para>
/// <para>
/// When an environment variable is <b>explicitly</b> set (present and non-empty)
/// it takes precedence: the matching <c>…EnvExplicit</c> flag is <see langword="true"/>
/// and subsequent programmatic writes are ignored. This keeps perf measurements
/// faithful — a run started with <c>S100_VECTOR_PICTURE_SNAPSHOT=0</c> stays off
/// regardless of the persisted user setting. Normal users (no env var) get fully
/// live-toggleable knobs.
/// </para>
/// <para>
/// The "best" default is <see langword="true"/> for every knob; line geometry
/// simplification runs at <see cref="DefaultSimplificationTolerancePx"/> px. See
/// <c>docs/design/mapsui-performance.md</c> for the measurements behind those
/// choices.
/// </para>
/// </remarks>
public static class RenderingOptimizations
{
    /// <summary>Default geometry-simplification tolerance, in screen pixels, used when the knob is on and no env override is present.</summary>
    public const double DefaultSimplificationTolerancePx = 0.6;

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

    /// <summary>Minimum / maximum accepted concurrent tile-worker cap.</summary>
    public const int MinTileWorkers = 1;

    /// <inheritdoc cref="MinTileWorkers"/>
    public const int MaxTileWorkers = 32;

    private static PerformanceProfile s_resolvedProfile;
    private static int s_tileWorkers;
    private static bool s_vectorSnapshotEnabled;
    private static bool s_vectorSnapshotPrebuildEnabled;
    private static bool s_vectorPathCacheEnabled;
    private static bool s_geometrySimplificationEnabled;
    private static RenderSubsystemKind s_renderSubsystem;
    private static VectorSceneMode s_sceneMode;
    private static double s_tileGutterDip;
    private static double s_tileBudgetMb;
    private static bool s_tilePredictionEnabled;
    private static bool s_tileDiskCacheEnabled;
    private static double s_tileDiskMb;
    private static bool s_tileGpuResidencyEnabled;
    private static double s_tileGpuBudgetMb;
    private static string? s_tileDiskDirectory;
    private static PerformanceProfile s_profile;

    static RenderingOptimizations()
    {
        (s_profile, ProfileEnvExplicit) = SeedProfile("S100_PERF_PROFILE", PerformanceProfile.Auto);
        (s_vectorSnapshotEnabled, VectorSnapshotEnvExplicit) =
            SeedBool("S100_VECTOR_PICTURE_SNAPSHOT", defaultValue: true);

        (s_vectorSnapshotPrebuildEnabled, VectorSnapshotPrebuildEnvExplicit) =
            SeedBool("S100_VECTOR_SNAPSHOT_PREBUILD", defaultValue: true);

        (s_vectorPathCacheEnabled, VectorPathCacheEnvExplicit) =
            SeedBool("S100_VECTOR_PATH_CACHE", defaultValue: true);

        (s_geometrySimplificationEnabled, SimplificationTolerancePx, GeometrySimplificationEnvExplicit) =
            SeedSimplification();

        (s_renderSubsystem, RenderSubsystemEnvExplicit) = SeedRenderSubsystem();
        (s_sceneMode, SceneModeEnvExplicit) = SeedSceneMode();

        // The performance profile sets the *defaults* for the per-layer tile
        // budgets and worker cap; Auto derives the tier from cores + RAM so a
        // constrained host gets smaller caches and fewer workers. Explicit env
        // vars or persisted slider values still override any of these.
        var tier = MachineProfile.Resolve(Profile);
        s_resolvedProfile = tier;

        (s_tileGutterDip, TileGutterDipEnvExplicit) =
            SeedDouble("S100_VECTOR_TILE_GUTTER", DefaultTileGutterDip, MinTileGutterDip, MaxTileGutterDip);
        (s_tileBudgetMb, TileBudgetMbEnvExplicit) =
            SeedDouble("S100_VECTOR_TILE_BUDGET_MB", MachineProfile.TileBudgetMb(tier), MinTileBudgetMb, MaxTileBudgetMb);
        (s_tilePredictionEnabled, TilePredictionEnvExplicit) =
            SeedBool("S100_VECTOR_TILE_PREDICT", defaultValue: true);
        (s_tileDiskCacheEnabled, TileDiskCacheEnvExplicit) =
            SeedBool("S100_VECTOR_TILE_DISK", defaultValue: true);
        (s_tileDiskMb, TileDiskMbEnvExplicit) =
            SeedDouble("S100_VECTOR_TILE_DISK_MB", MachineProfile.TileDiskMb(tier), MinTileDiskMb, MaxTileDiskMb);
        (s_tileGpuResidencyEnabled, TileGpuResidencyEnvExplicit) =
            SeedBool("S100_VECTOR_TILE_GPU", defaultValue: true);
        (s_tileGpuBudgetMb, TileGpuBudgetMbEnvExplicit) =
            SeedDouble("S100_VECTOR_TILE_GPU_MB", MachineProfile.TileGpuBudgetMb(tier), MinTileGpuBudgetMb, MaxTileGpuBudgetMb);
        (s_tileWorkers, TileWorkersEnvExplicit) =
            SeedInt("S100_VECTOR_TILE_WORKERS", MachineProfile.MaxWorkers(tier), MinTileWorkers, MaxTileWorkers);

        var diskDir = Environment.GetEnvironmentVariable("S100_VECTOR_TILE_DISK_DIR");
        TileDiskDirectoryEnvExplicit = !string.IsNullOrEmpty(diskDir);
        s_tileDiskDirectory = TileDiskDirectoryEnvExplicit ? diskDir : null;
    }

    /// <summary>
    /// Whether the raster vector-layer snapshot fast path
    /// (<see cref="S100VectorSnapshotRenderer"/>) is enabled. Records a settled
    /// layer once per (resolution, feature-set) and blits it under translation on
    /// subsequent pans. Default on.
    /// </summary>
    public static bool VectorSnapshotEnabled
    {
        get => s_vectorSnapshotEnabled;
        set { if (!VectorSnapshotEnvExplicit) s_vectorSnapshotEnabled = value; }
    }

    /// <summary>True when <see cref="VectorSnapshotEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool VectorSnapshotEnvExplicit { get; }

    /// <summary>
    /// Whether the off-thread snapshot prebuild is enabled. Hides the one-time
    /// record stall on zoom and the sustained-pan record stall by rasterizing on
    /// a background thread. Only meaningful when <see cref="VectorSnapshotEnabled"/>
    /// is on. Default on.
    /// </summary>
    public static bool VectorSnapshotPrebuildEnabled
    {
        get => s_vectorSnapshotPrebuildEnabled;
        set { if (!VectorSnapshotPrebuildEnvExplicit) s_vectorSnapshotPrebuildEnabled = value; }
    }

    /// <summary>True when <see cref="VectorSnapshotPrebuildEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool VectorSnapshotPrebuildEnvExplicit { get; }

    /// <summary>
    /// Whether the translation-invariant vector path cache
    /// (<see cref="CachedVectorStyleRenderer"/>) is enabled. Builds each
    /// geometry's projected <c>SKPath</c> once per (feature, resolution) and
    /// re-uses it across pans instead of rebuilding every frame. Default on.
    /// </summary>
    public static bool VectorPathCacheEnabled
    {
        get => s_vectorPathCacheEnabled;
        set { if (!VectorPathCacheEnvExplicit) s_vectorPathCacheEnabled = value; }
    }

    /// <summary>True when <see cref="VectorPathCacheEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool VectorPathCacheEnvExplicit { get; }

    /// <summary>
    /// Whether resolution-aware <b>line</b> simplification (dropping on-screen
    /// sub-pixel detail from dense S-101 line geometries at path-build time) is
    /// enabled. Applied inside <see cref="CachedVectorStyleRenderer"/> as inline
    /// sub-pixel vertex dropping and therefore requires
    /// <see cref="VectorPathCacheEnabled"/>. Polygons are always rendered
    /// vertex-exact. Default on.
    /// </summary>
    public static bool GeometrySimplificationEnabled
    {
        get => s_geometrySimplificationEnabled;
        set { if (!GeometrySimplificationEnvExplicit) s_geometrySimplificationEnabled = value; }
    }

    /// <summary>True when geometry simplification is pinned by an explicit <c>S100_VECTOR_SIMPLIFY_PX</c>.</summary>
    public static bool GeometrySimplificationEnvExplicit { get; }

    /// <summary>
    /// Selects the active base-plane chart render subsystem (the A/B switch for
    /// the tiled/async render-subsystem redesign). <see cref="RenderSubsystemKind.Mapsui"/>
    /// is the established Mapsui feature/style/layer path (the "A" arm);
    /// <see cref="RenderSubsystemKind.TiledScene"/> selects the tiled/async
    /// subsystem (the "B" arm). Seeded from <c>S100_RENDER_SUBSYSTEM</c>
    /// (<c>mapsui</c> | <c>tiledscene</c>); default <see cref="RenderSubsystemKind.TiledScene"/>.
    /// </summary>
    public static RenderSubsystemKind RenderSubsystem
    {
        get => s_renderSubsystem;
        set { if (!RenderSubsystemEnvExplicit) s_renderSubsystem = value; }
    }

    /// <summary>True when <see cref="RenderSubsystem"/> is pinned by an explicit environment variable.</summary>
    public static bool RenderSubsystemEnvExplicit { get; }

    /// <summary>
    /// Within the <see cref="RenderSubsystemKind.TiledScene"/> ("B") arm, selects
    /// the <see cref="VectorSceneMode.Tiled"/> Phase-2 tiled base plane (default)
    /// or the <see cref="VectorSceneMode.Single"/> Phase-1 single-surface arm.
    /// Seeded from <c>S100_VECTOR_SCENE_MODE</c> (<c>single</c> selects Phase&#160;1).
    /// Read at layer build time, so a change re-applies on the next re-render.
    /// </summary>
    public static VectorSceneMode SceneMode
    {
        get => s_sceneMode;
        set { if (!SceneModeEnvExplicit) s_sceneMode = value; }
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
        get => s_tileGutterDip;
        set { if (!TileGutterDipEnvExplicit) s_tileGutterDip = Clamp(value, MinTileGutterDip, MaxTileGutterDip); }
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
        get => s_tileBudgetMb;
        set { if (!TileBudgetMbEnvExplicit) s_tileBudgetMb = Clamp(value, MinTileBudgetMb, MaxTileBudgetMb); }
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
        get => s_tilePredictionEnabled;
        set { if (!TilePredictionEnvExplicit) s_tilePredictionEnabled = value; }
    }

    /// <summary>True when <see cref="TilePredictionEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool TilePredictionEnvExplicit { get; }

    /// <summary>
    /// Whether the persistent warm disk tile cache (Phase&#160;4) is enabled.
    /// Seeded from <c>S100_VECTOR_TILE_DISK</c> (default on). The shared disk
    /// cache is created once per process, so a change applies on restart.
    /// </summary>
    public static bool TileDiskCacheEnabled
    {
        get => s_tileDiskCacheEnabled;
        set { if (!TileDiskCacheEnvExplicit) s_tileDiskCacheEnabled = value; }
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
        get => s_tileDiskMb;
        set { if (!TileDiskMbEnvExplicit) s_tileDiskMb = Clamp(value, MinTileDiskMb, MaxTileDiskMb); }
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
        get => s_tileDiskDirectory;
        set { if (!TileDiskDirectoryEnvExplicit) s_tileDiskDirectory = value; }
    }

    /// <summary>True when <see cref="TileDiskDirectory"/> is pinned by an explicit environment variable.</summary>
    public static bool TileDiskDirectoryEnvExplicit { get; }

    /// <summary>
    /// Whether GPU texture residency (Phase&#160;5) is enabled for the tiled base
    /// plane. Seeded from <c>S100_VECTOR_TILE_GPU</c> (default on). Read every
    /// frame, so a change takes effect live (inert on a software surface).
    /// </summary>
    public static bool TileGpuResidencyEnabled
    {
        get => s_tileGpuResidencyEnabled;
        set { if (!TileGpuResidencyEnvExplicit) s_tileGpuResidencyEnabled = value; }
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
        get => s_tileGpuBudgetMb;
        set { if (!TileGpuBudgetMbEnvExplicit) s_tileGpuBudgetMb = Clamp(value, MinTileGpuBudgetMb, MaxTileGpuBudgetMb); }
    }

    /// <summary>True when <see cref="TileGpuBudgetMb"/> is pinned by an explicit environment variable.</summary>
    public static bool TileGpuBudgetMbEnvExplicit { get; }

    /// <summary>
    /// The selected performance profile. <see cref="PerformanceProfile.Auto"/>
    /// (the default) derives a tier from detected cores + RAM. Setting a profile
    /// recomputes any tile budget / worker default that is not pinned by an env
    /// var or a prior explicit slider value, so a constrained host can be tuned
    /// up or a workstation tuned down. Applies to the next dataset reload.
    /// </summary>
    public static PerformanceProfile Profile
    {
        get => s_profile;
        set { if (!ProfileEnvExplicit) { s_profile = value; ApplyProfile(value); } }
    }

    /// <summary>True when <see cref="Profile"/> is pinned by an explicit environment variable.</summary>
    public static bool ProfileEnvExplicit { get; }

    /// <summary>The concrete tier <see cref="Profile"/> resolves to on this host.</summary>
    public static PerformanceProfile ResolvedProfile => s_resolvedProfile;

    /// <summary>
    /// Maximum number of tile-rasterising workers allowed to run concurrently
    /// across all layers. Bounds the per-cell worker storm a many-cell chart
    /// creates (one worker per layer otherwise). Seeded from
    /// <c>S100_VECTOR_TILE_WORKERS</c>, otherwise the profile default.
    /// </summary>
    public static int MaxConcurrentTileWorkers
    {
        get => s_tileWorkers;
        set { if (!TileWorkersEnvExplicit) s_tileWorkers = (int)Clamp(value, MinTileWorkers, MaxTileWorkers); }
    }

    /// <summary>True when <see cref="MaxConcurrentTileWorkers"/> is pinned by an explicit environment variable.</summary>
    public static bool TileWorkersEnvExplicit { get; }

    /// <summary>
    /// Recomputes profile-derived budgets/worker cap for non-env-pinned knobs.
    /// </summary>
    private static void ApplyProfile(PerformanceProfile profile)
    {
        var tier = MachineProfile.Resolve(profile);
        s_resolvedProfile = tier;
        if (!TileBudgetMbEnvExplicit) s_tileBudgetMb = MachineProfile.TileBudgetMb(tier);
        if (!TileGpuBudgetMbEnvExplicit) s_tileGpuBudgetMb = MachineProfile.TileGpuBudgetMb(tier);
        if (!TileDiskMbEnvExplicit) s_tileDiskMb = MachineProfile.TileDiskMb(tier);
        if (!TileWorkersEnvExplicit) s_tileWorkers = MachineProfile.MaxWorkers(tier);
    }


    /// <summary>
    /// Pixel tolerance applied when <see cref="GeometrySimplificationEnabled"/> is
    /// on. Seeded from <c>S100_VECTOR_SIMPLIFY_PX</c>, otherwise
    /// <see cref="DefaultSimplificationTolerancePx"/>. Used by line simplification.
    /// </summary>
    public static double SimplificationTolerancePx { get; }

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

    private static (bool enabled, double tolerancePx, bool envExplicit) SeedSimplification()
    {
        var raw = Environment.GetEnvironmentVariable("S100_VECTOR_SIMPLIFY_PX");
        if (!string.IsNullOrEmpty(raw)
            && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            // A tolerance of 0 (or negative) means "vertex-exact" — i.e. the
            // optimization is explicitly disabled.
            return v > 0 ? (true, v, true) : (false, DefaultSimplificationTolerancePx, true);
        }

        return (true, DefaultSimplificationTolerancePx, false);
    }

    private static (RenderSubsystemKind kind, bool envExplicit) SeedRenderSubsystem()
    {
        var raw = Environment.GetEnvironmentVariable("S100_RENDER_SUBSYSTEM");
        if (string.IsNullOrEmpty(raw))
        {
            return (RenderSubsystemKind.TiledScene, false);
        }

        var kind = raw.Trim().ToLowerInvariant() switch
        {
            "tiledscene" or "tiled" or "tile" or "b" => RenderSubsystemKind.TiledScene,
            _ => RenderSubsystemKind.Mapsui,
        };
        return (kind, true);
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

    private static (int value, bool envExplicit) SeedInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var v))
        {
            return ((int)Clamp(v, min, max), true);
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
/// Within the <see cref="RenderSubsystemKind.TiledScene"/> arm, selects the
/// tiled base plane (Phase&#160;2) or the single-surface arm (Phase&#160;1). See
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

/// <summary>
/// Selects the active base-plane chart render subsystem — the A/B switch for the
/// tiled/async render-subsystem redesign (see
/// <c>docs/design/S100-Render-Subsystem-Design.md</c>).
/// </summary>
public enum RenderSubsystemKind
{
    /// <summary>
    /// The established Mapsui feature/style/layer rendering path (the "A" arm).
    /// Retained as a selectable fallback; <see cref="TiledScene"/> is now the
    /// default and the baseline against which this path is compared.
    /// </summary>
    Mapsui = 0,

    /// <summary>
    /// The tiled/async predictive render subsystem that rasterises the base
    /// plane directly from the <c>VectorScene</c> IR (the "B" arm). This is the
    /// default subsystem.
    /// </summary>
    TiledScene = 1,
}
