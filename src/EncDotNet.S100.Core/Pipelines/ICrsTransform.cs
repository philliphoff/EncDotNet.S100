namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Transforms coordinates from one CRS to another.
/// </summary>
public interface ICrsTransform
{
    /// <summary>Transforms a coordinate pair from the source CRS to the target CRS.</summary>
    (double X, double Y) Transform(double x, double y);

    /// <summary>True if source and target CRS are the same (no-op transform).</summary>
    bool IsIdentity { get; }
}

/// <summary>
/// Identity transform — used when source and target CRS are the same.
/// </summary>
public sealed class IdentityCrsTransform : ICrsTransform
{
    public static IdentityCrsTransform Instance { get; } = new();

    public (double X, double Y) Transform(double x, double y) => (x, y);
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
