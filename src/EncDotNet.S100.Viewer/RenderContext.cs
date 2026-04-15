using System;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Base class for spec-specific render contexts passed to dataset processors.
/// </summary>
internal abstract record RenderContext
{
    /// <summary>The color palette (Day/Dusk/Night) to use for rendering.</summary>
    public PaletteType Palette { get; init; } = PaletteType.Day;

    /// <summary>Global symbol scale factor (1.0 = default).</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>Global text scale factor (1.0 = default).</summary>
    public double TextScale { get; init; } = 1.0;
}

internal sealed record S101RenderContext : RenderContext;

internal sealed record S102RenderContext : RenderContext;

internal sealed record S111RenderContext(DateTime? TimeStep = null) : RenderContext;

internal sealed record S104RenderContext(DateTime? TimeStep = null) : RenderContext;

internal sealed record S124RenderContext : RenderContext;

internal sealed record S129RenderContext : RenderContext;
