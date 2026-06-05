using System;
using Mapsui;

namespace EncDotNet.S100.Renderers.Mapsui.Simplification;

/// <summary>
/// Public helpers for working with simplified features produced by
/// <see cref="SimplificationCache"/>.  Most consumers only need
/// <see cref="GetOriginal"/> to walk back from a simplified clone to
/// the unsimplified feature for picking / info-on-click.
/// </summary>
public static class Simplification
{
    /// <summary>
    /// Field key under which a simplified <see cref="IFeature"/>
    /// carries a back-reference to the original (unsimplified) feature.
    /// Use <see cref="GetOriginal"/> rather than reading this key
    /// directly.
    /// </summary>
    public const string OriginalFeatureKey = "S100.OriginalFeature";

    /// <summary>
    /// Returns the original (unsimplified) feature for a possibly
    /// simplified one.  When <paramref name="feature"/> is itself the
    /// original, returns it unchanged.  Safe to call on any
    /// <see cref="IFeature"/>; the back-reference is only set on
    /// clones produced by the simplification cache.
    /// </summary>
    public static IFeature GetOriginal(IFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature[OriginalFeatureKey] is IFeature original ? original : feature;
    }
}
