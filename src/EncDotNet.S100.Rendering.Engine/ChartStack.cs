namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// One layer of a <see cref="ChartStack"/>: a scene already bound to
/// <see cref="Source"/> with
/// <see cref="S100VectorTileRenderer.BindScene(object, Rendering.Scene.VectorScene, string?, string?)"/>,
/// and the stack settings it is drawn with.
/// </summary>
/// <remarks>
/// Per-dataset visibility, opacity, scale window and S-98 feature suppression
/// are settings of the layer in the stack rather than properties of a host map
/// layer: one chart layer draws every stack layer into one raster per slot.
/// </remarks>
public sealed class ChartStackEntry
{
    /// <summary>Creates an entry for the scene bound to <paramref name="source"/>.</summary>
    /// <param name="source">The owner token the scene was bound to.</param>
    public ChartStackEntry(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    /// <summary>The owner token whose bound scene this entry draws.</summary>
    public object Source { get; }

    /// <summary>The opacity, 0 to 1, the layer is drawn with. Defaults to 1.</summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>
    /// The finest live resolution, in metres per pixel, at which the layer
    /// draws (as a map layer's minimum visible resolution). Defaults to 0.
    /// </summary>
    public double MinVisibleResolution { get; init; }

    /// <summary>
    /// The coarsest live resolution, in metres per pixel, at which the layer
    /// draws: a cell's scale window. Defaults to no limit.
    /// </summary>
    public double MaxVisibleResolution { get; init; } = double.MaxValue;

    /// <summary>
    /// Feature references whose ops the layer leaves out (S-98 suppression,
    /// for example S-101 depth areas under a loaded S-102), or
    /// <see langword="null"/> for none.
    /// </summary>
    public IReadOnlySet<string>? SuppressedFeatures { get; init; }

    /// <summary>
    /// The finer, overlapping cells' coverages that clip this layer (cross-cell
    /// overlap suppression), or <see langword="null"/> for none.
    /// </summary>
    internal FinerCoverage[]? Finer { get; init; }
}

/// <summary>
/// The ordered layers one chart layer draws, bottom first: an immutable
/// snapshot with a version. Publishing a new stack re-rasters only the slots
/// whose content changed (see <see cref="ChartTileKey.StackSlice"/>); the
/// others keep their tiles, and a changed slot shows its old tile until its
/// new one lands.
/// </summary>
public sealed class ChartStack
{
    /// <summary>An empty stack.</summary>
    public static ChartStack Empty { get; } = new(0, []);

    /// <summary>Creates a stack.</summary>
    /// <param name="version">The stack version; a newer stack has a larger one.</param>
    /// <param name="entries">The layers, bottom first.</param>
    public ChartStack(long version, IReadOnlyList<ChartStackEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Version = version;
        Entries = entries;
    }

    /// <summary>The stack version.</summary>
    public long Version { get; }

    /// <summary>The layers, bottom first.</summary>
    public IReadOnlyList<ChartStackEntry> Entries { get; }
}
