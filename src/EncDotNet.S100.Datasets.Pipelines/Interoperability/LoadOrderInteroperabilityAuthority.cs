using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Strict dataset-by-dataset <see cref="IInteroperabilityAuthority"/>:
/// preserves the order in which <see cref="LayerStackBuilder.Build"/>
/// presents entries (top-of-UI dataset wins) and <em>ignores the
/// S-98 display plane entirely</em>.
/// </summary>
/// <remarks>
/// <para>
/// Use this when the user (or host application) needs the classic
/// GIS "load order is paint order" semantics — every layer of dataset
/// N paints above every layer of dataset N-1, regardless of what
/// S-98 plane its features conceptually belong to. Intended for
/// debugging, comparison studies, and any deployment that has its
/// own non-S-98 stacking policy.
/// </para>
/// <para>
/// <see cref="GetDefaultPlane"/> still returns the S-98 default
/// plane for informational purposes (e.g. layer-controls UI that
/// labels each layer with its conceptual plane), but
/// <see cref="Sort"/> does not consult it.
/// </para>
/// <para>
/// Tiebreaker semantics inside a single dataset: entries are emitted
/// in the order the processor produced them, then by
/// <see cref="LayerStackEntry.WithinPlanePriority"/>. The
/// <c>WithinPlanePriority</c> still serves to express the
/// "areas underneath line work" ordering within e.g. S-101 so the
/// dataset's own visual integrity is preserved.
/// </para>
/// </remarks>
public sealed class LoadOrderInteroperabilityAuthority : IInteroperabilityAuthority
{
    private readonly IInteroperabilityAuthority _planeOracle;

    /// <summary>
    /// Constructs a load-order authority that delegates default-plane
    /// lookups to <paramref name="planeOracle"/>. Useful when the
    /// host wants informational plane labels from a custom table
    /// while still using strict load-order stacking.
    /// </summary>
    public LoadOrderInteroperabilityAuthority(IInteroperabilityAuthority planeOracle)
    {
        System.ArgumentNullException.ThrowIfNull(planeOracle);
        _planeOracle = planeOracle;
    }

    /// <inheritdoc />
    public S98DisplayPlane GetDefaultPlane(string productSpec, string? featureTypeOrLayerKind = null)
        => _planeOracle.GetDefaultPlane(productSpec, featureTypeOrLayerKind);

    /// <inheritdoc />
    /// <remarks>
    /// Stable sort on <see cref="LayerStackEntry.WithinPlanePriority"/>
    /// alone. The cross-dataset paint order is the order in which
    /// <see cref="LayerStackBuilder"/> hands the entries to this
    /// method — bottom-of-UI dataset first. The OrderBy stability
    /// guarantees we preserve that interleaving.
    /// </remarks>
    public IReadOnlyList<LayerStackEntry> Sort(IEnumerable<LayerStackEntry> entries)
    {
        System.ArgumentNullException.ThrowIfNull(entries);
        return entries.OrderBy(e => e.WithinPlanePriority).ToList();
    }
}
