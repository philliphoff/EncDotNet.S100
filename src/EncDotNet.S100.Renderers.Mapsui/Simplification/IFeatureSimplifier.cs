using Mapsui;

namespace EncDotNet.S100.Renderers.Mapsui.Simplification;

/// <summary>
/// Strategy that produces a simplified copy of an
/// <see cref="IFeature"/> for a given metric tolerance.
/// Implementations must be thread-safe.
/// </summary>
/// <remarks>
/// V1 ships only <see cref="DouglasPeuckerLineSimplifier"/>, which
/// simplifies <c>LineString</c> / <c>MultiLineString</c> geometry
/// only; polygon, point, and other types fall through unchanged
/// (returning the original feature). Polygon simplification is
/// deferred until topology preservation is wired in.
/// </remarks>
public interface IFeatureSimplifier
{
    /// <summary>
    /// Returns a simplified copy of <paramref name="feature"/> at the
    /// supplied <paramref name="toleranceMetres"/> tolerance, or the
    /// original feature when simplification does not apply (geometry
    /// type unsupported in v1, vertex count below
    /// <see cref="SimplificationOptions.MinVertexCount"/>, or the
    /// simplifier produced a degenerate result).
    /// </summary>
    /// <param name="feature">The original feature.</param>
    /// <param name="toleranceMetres">
    /// Maximum allowed displacement of any retained coordinate from
    /// the original geometry, in metres (EPSG:3857 units).
    /// </param>
    /// <param name="options">Configuration; see
    /// <see cref="SimplificationOptions"/>.</param>
    SimplificationResult Simplify(
        IFeature feature,
        double toleranceMetres,
        SimplificationOptions options);
}

/// <summary>
/// Outcome of a single <see cref="IFeatureSimplifier.Simplify"/>
/// call. <see cref="WasSimplified"/> distinguishes "we did real work
/// and produced a smaller copy" (<see langword="true"/>) from
/// "the geometry passes through unchanged" (<see langword="false"/>).
/// </summary>
public readonly record struct SimplificationResult(
    IFeature Feature,
    long OriginalCoordinateCount,
    long SimplifiedCoordinateCount,
    bool WasSimplified);
