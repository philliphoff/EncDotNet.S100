using System;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Selects how the tiled render subsystem's resource budgets (in-memory / GPU /
/// disk tile caches) default when no explicit environment variable or persisted
/// user value pins them.
/// </summary>
/// <remarks>
/// <see cref="Auto"/> derives the tier from the host's logical-core count and
/// total available RAM at start-up, so a constrained machine (small Parallels /
/// cloud VM, low-RAM laptop) gets smaller per-layer caches — bounding total
/// memory and fixing the tile-cache thrash a many-cell / multi-exchange-set
/// chart created at a fixed 256&#160;MB ceiling. The explicit tiers exist so a
/// user can pin a profile regardless of detected hardware; the individual
/// budget knobs remain independently overridable.
/// </remarks>
public enum PerformanceProfile
{
    /// <summary>Derive the tier from detected cores + RAM. The default.</summary>
    Auto = 0,

    /// <summary>Generous caches (workstation defaults).</summary>
    HighEnd = 1,

    /// <summary>Mid-range caches.</summary>
    Balanced = 2,

    /// <summary>Small caches and minimal concurrency for low-RAM / low-core hosts.</summary>
    LowEnd = 3,
}

/// <summary>
/// Computes the default tiled-renderer resource budgets for a
/// <see cref="PerformanceProfile"/>, including the hardware-derived
/// <see cref="PerformanceProfile.Auto"/> tier. All members are deterministic
/// given the (cores, RAM) inputs, so the tier mapping is unit-testable without
/// touching the real machine.
/// </summary>
public static class MachineProfile
{
    /// <summary>Per-layer hot (CPU) tile-cache budget, MB, for each resolved tier.</summary>
    public static double TileBudgetMb(PerformanceProfile tier) => tier switch
    {
        PerformanceProfile.LowEnd => 96.0,
        PerformanceProfile.Balanced => 192.0,
        _ => RenderingOptimizations.DefaultTileBudgetMb,
    };

    /// <summary>Per-layer GPU-residency budget, MB, for each resolved tier.</summary>
    public static double TileGpuBudgetMb(PerformanceProfile tier) => tier switch
    {
        PerformanceProfile.LowEnd => 96.0,
        PerformanceProfile.Balanced => 192.0,
        _ => RenderingOptimizations.DefaultTileGpuBudgetMb,
    };

    /// <summary>Shared warm disk-cache budget, MB, for each resolved tier.</summary>
    public static double TileDiskMb(PerformanceProfile tier) => tier switch
    {
        PerformanceProfile.LowEnd => 256.0,
        PerformanceProfile.Balanced => 384.0,
        _ => RenderingOptimizations.DefaultTileDiskMb,
    };

    /// <summary>
    /// Resolves <see cref="PerformanceProfile.Auto"/> to a concrete tier from the
    /// supplied core count and available RAM. The explicit tiers pass through.
    /// LowEnd: ≤4 cores or ≤8&#160;GB; Balanced: ≤8 cores or ≤16&#160;GB; else HighEnd.
    /// </summary>
    public static PerformanceProfile Resolve(PerformanceProfile profile, int cores, double ramGb)
    {
        if (profile != PerformanceProfile.Auto)
        {
            return profile;
        }

        if (cores <= 4 || ramGb <= 8.0)
        {
            return PerformanceProfile.LowEnd;
        }

        if (cores <= 8 || ramGb <= 16.0)
        {
            return PerformanceProfile.Balanced;
        }

        return PerformanceProfile.HighEnd;
    }

    /// <summary>Resolves <paramref name="profile"/> against the live host's cores + RAM.</summary>
    public static PerformanceProfile Resolve(PerformanceProfile profile) =>
        Resolve(profile, Environment.ProcessorCount, AvailableRamGb());

    /// <summary>Total available RAM in GB (cross-platform; reflects a container/VM cap).</summary>
    public static double AvailableRamGb()
    {
        var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return bytes > 0 ? bytes / (1024.0 * 1024.0 * 1024.0) : 8.0;
    }
}
