using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What a pick found (#1401): the features hit, where the pick was and the
/// depth there, and the map it was made in. <see cref="IPickService"/> publishes it;
/// Object Information shows it. A result with no hits clears Object Information.
/// </summary>
/// <param name="Hits">
/// The features hit, topmost first: an overlay source's (own-ship, which has
/// no dataset), then the datasets'.
/// </param>
/// <param name="Location">Where the pick was, or <see langword="null"/> for one with no location (feature search, reference navigation).</param>
/// <param name="LocationDepth">
/// The depth over time at <paramref name="Location"/> when the point is water
/// and a base depth resolves; <see langword="null"/> otherwise.
/// </param>
/// <param name="Map">
/// The map the pick was made in (split map design note D11, #1763): the
/// pane clicked, or the <c>map</c> of an MCP <c>pick_features</c>.
/// <see langword="null"/> for a pick made in no map in particular (feature
/// search, reference navigation, an MCP pick without <c>map</c>), which
/// Object Information reports at the active map.
/// </param>
internal sealed record PickResult(
    IReadOnlyList<PickHit> Hits,
    PickLocation? Location = null,
    LocationDepthResult? LocationDepth = null,
    MapViewId? Map = null)
{
    /// <summary>A pick that found nothing; it clears Object Information.</summary>
    public static PickResult Empty { get; } = new([]);

    /// <summary>Whether the pick hit nothing.</summary>
    public bool IsEmpty => Hits.Count == 0;
}
