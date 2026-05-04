using System;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Base class for spec-specific render contexts passed to dataset processors.
/// </summary>
public abstract record RenderContext
{
    /// <summary>The color palette (Day/Dusk/Night) to use for rendering.</summary>
    public PaletteType Palette { get; init; } = PaletteType.Day;

    /// <summary>Global symbol scale factor (1.0 = default).</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>Global text scale factor (1.0 = default).</summary>
    public double TextScale { get; init; } = 1.0;
}

public sealed record S101RenderContext : RenderContext;

public sealed record S102RenderContext : RenderContext;

public sealed record S111RenderContext(DateTime? TimeStep = null) : RenderContext;

public sealed record S104RenderContext(DateTime? TimeStep = null) : RenderContext;

public sealed record S122RenderContext : RenderContext;

public sealed record S124RenderContext : RenderContext;

public sealed record S125RenderContext : RenderContext;
public sealed record S127RenderContext : RenderContext;

public sealed record S129RenderContext : RenderContext;

public sealed record S411RenderContext(DateTime? TimeStep = null) : RenderContext;
