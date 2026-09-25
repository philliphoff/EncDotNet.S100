namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Transforms coordinates from one CRS to another.
/// </summary>
public interface ICrsTransform
{
    /// <summary>Transforms a coordinate pair from the source CRS to the target CRS.</summary>
    /// <param name="x">
    /// X coordinate in the source CRS: longitude in decimal degrees for a
    /// geographic CRS, easting for a projected one.
    /// </param>
    /// <param name="y">
    /// Y coordinate in the source CRS: latitude in decimal degrees for a
    /// geographic CRS, northing for a projected one.
    /// </param>
    /// <returns>The coordinate in the target CRS, in the same X/Y (longitude/latitude or easting/northing) order.</returns>
    (double X, double Y) Transform(double x, double y);

    /// <summary>True if source and target CRS are the same (no-op transform).</summary>
    bool IsIdentity { get; }
}

/// <summary>
/// Identity transform — used when source and target CRS are the same.
/// </summary>
public sealed class IdentityCrsTransform : ICrsTransform
{
    /// <summary>The shared instance.</summary>
    public static IdentityCrsTransform Instance { get; } = new();

    /// <summary>Returns the input coordinate unchanged.</summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns><c>(x, y)</c>.</returns>
    public (double X, double Y) Transform(double x, double y) => (x, y);

    /// <summary>Always <see langword="true"/>.</summary>
    public bool IsIdentity => true;
}

/// <summary>
/// Creates <see cref="ICrsTransform"/> instances for a given source/target CRS pair.
/// </summary>
public interface ICrsTransformFactory
{
    /// <summary>
    /// Creates a transform from <paramref name="sourceCrs"/> to <paramref name="targetCrs"/>.
    /// Returns <see cref="IdentityCrsTransform"/> if they are the same.
    /// </summary>
    ICrsTransform Create(string sourceCrs, string targetCrs);
}
