namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// The geometry of an NIS feature, as simple features in WGS 84.
/// </summary>
/// <remarks>
/// A <see cref="NisGeometryKind.Point"/> has one part with one position, and a
/// <see cref="NisGeometryKind.MultiPoint"/> one part with every position. A
/// <see cref="NisGeometryKind.Line"/> has one part per path. An
/// <see cref="NisGeometryKind.Area"/> has one part per ring, each closed (its
/// last position repeats its first), with outer rings running clockwise and
/// holes anticlockwise, as Esri stores them.
/// </remarks>
public sealed class NisGeometry
{
    /// <summary>Creates a geometry.</summary>
    /// <param name="kind">The kind of geometry.</param>
    /// <param name="parts">The parts, as described in the remarks.</param>
    public NisGeometry(NisGeometryKind kind, IReadOnlyList<IReadOnlyList<NisPosition>> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        Kind = kind;
        Parts = parts;
    }

    /// <summary>The kind of geometry.</summary>
    public NisGeometryKind Kind { get; }

    /// <summary>The parts: one per path or ring, or a single part of points.</summary>
    public IReadOnlyList<IReadOnlyList<NisPosition>> Parts { get; }

    /// <summary>Whether the geometry has no positions.</summary>
    public bool IsEmpty => Parts.All(p => p.Count == 0);

    /// <summary>Creates a point geometry.</summary>
    /// <param name="position">The position.</param>
    /// <returns>The geometry.</returns>
    public static NisGeometry FromPoint(NisPosition position) => new(NisGeometryKind.Point, [[position]]);
}
