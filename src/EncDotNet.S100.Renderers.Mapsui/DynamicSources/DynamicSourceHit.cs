using EncDotNet.S100.DynamicSources;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// A single dynamic-feature hit returned by
/// <see cref="IS100DynamicSourceRegistry.HitTest"/>. Carries the owning source
/// and the picked feature so a host can resolve display metadata.
/// </summary>
/// <param name="Source">The source that owns the picked feature.</param>
/// <param name="Feature">The picked dynamic feature.</param>
/// <param name="DistanceMapUnits">
/// Distance from the click to the feature in Spherical Mercator map units;
/// <c>0</c> when the click landed inside a rendered vessel hull.
/// </param>
public sealed record DynamicSourceHit(
    IDynamicFeatureSource Source,
    DynamicFeature Feature,
    double DistanceMapUnits);
