using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Resolves a click location to the overlay sources' features under it, such
/// as own-ship, as ordinary <see cref="PickHit"/>s with no dataset (data source
/// design note P5). The click handler passes them to <see cref="IPickService"/>,
/// which puts them ahead of the datasets' hits.
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
    IReadOnlyList<PickHit> Pick(MPoint mapPoint, double resolution);
}
