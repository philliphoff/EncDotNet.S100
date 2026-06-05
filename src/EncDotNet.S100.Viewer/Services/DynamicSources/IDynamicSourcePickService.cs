using System.Collections.Generic;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Resolves a click location to one or more dynamic-feature hits.
/// Sibling to <see cref="IPickService"/>: the click handler queries
/// both, then forwards the merged result to the pick report panel.
/// </summary>
/// <remarks>
/// Returns an empty list when no dynamic source is registered, when
/// the registry is unattached (still booting), or when the click is
/// outside every source's hit-test tolerance. Callers therefore do
/// not need to handle a "no dynamic sources" case specially.
/// </remarks>
internal interface IDynamicSourcePickService
{
    /// <summary>
    /// Hit-test all visible dynamic sources at the given click point.
    /// </summary>
    /// <param name="mapPoint">Click position in Spherical Mercator world units.</param>
    /// <param name="resolution">Map units per device pixel at the current zoom.</param>
    /// <returns>Hits ordered by ascending distance from the click.</returns>
    IReadOnlyList<DynamicPickHit> Pick(MPoint mapPoint, double resolution);
}
