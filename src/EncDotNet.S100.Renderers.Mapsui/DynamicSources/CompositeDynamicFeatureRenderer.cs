using EncDotNet.S100.DynamicSources;
using Mapsui;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// Composes a list of <see cref="IDynamicFeatureRenderer"/> by
/// ordered fallthrough on <see cref="IDynamicFeatureRenderer.CanRender"/>.
/// The first matching renderer handles the feature; the
/// <see cref="DefaultDynamicFeatureRenderer"/> typically sits last as
/// a catch-all.
/// </summary>
public sealed class CompositeDynamicFeatureRenderer : IDynamicFeatureRenderer
{
    private readonly IReadOnlyList<IDynamicFeatureRenderer> _renderers;

    /// <summary>
    /// Creates a composite over an ordered sequence of renderers.
    /// </summary>
    public CompositeDynamicFeatureRenderer(IEnumerable<IDynamicFeatureRenderer> renderers)
    {
        ArgumentNullException.ThrowIfNull(renderers);
        _renderers = renderers.ToArray();
    }

    /// <inheritdoc />
    public bool CanRender(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        foreach (var r in _renderers)
        {
            if (r.CanRender(feature)) return true;
        }
        return false;
    }

    /// <inheritdoc />
    public IEnumerable<IFeature> Render(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        foreach (var r in _renderers)
        {
            if (r.CanRender(feature))
            {
                return r.Render(feature);
            }
        }
        return Array.Empty<IFeature>();
    }
}
