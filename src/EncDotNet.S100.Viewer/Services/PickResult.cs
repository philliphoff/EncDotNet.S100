using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What a pick found (#1401): the features hit, the dynamic-source hits, where
/// the pick was and the depth there. <see cref="IPickService"/> publishes it;
/// Object Information shows it. A result with no hits of either kind clears
/// Object Information.
/// </summary>
/// <param name="Hits">The dataset features hit, topmost first.</param>
/// <param name="DynamicHits">The dynamic-source hits (AIS, own ship).</param>
/// <param name="Location">Where the pick was, or <see langword="null"/> for one with no location (feature search, reference navigation).</param>
/// <param name="LocationDepth">
/// The depth over time at <paramref name="Location"/> when the point is water
/// and a base depth resolves; <see langword="null"/> otherwise.
/// </param>
internal sealed record PickResult(
    IReadOnlyList<PickHit> Hits,
    IReadOnlyList<DynamicPickHit> DynamicHits,
    PickLocation? Location = null,
    LocationDepthResult? LocationDepth = null)
{
    /// <summary>A pick that found nothing; it clears Object Information.</summary>
    public static PickResult Empty { get; } = new([], []);

    /// <summary>Whether the pick hit nothing.</summary>
    public bool IsEmpty => Hits.Count == 0 && DynamicHits.Count == 0;
}
