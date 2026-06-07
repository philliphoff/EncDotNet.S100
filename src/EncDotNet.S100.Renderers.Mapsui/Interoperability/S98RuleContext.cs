using System.Collections.Generic;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Inputs to a single <see cref="S98InteroperabilityRule"/> evaluation:
/// the set of currently-loaded datasets plus optional mariner state
/// (used by rules whose effect depends on viewer settings, e.g. the
/// safety-contour exception to R-101-102-B).
/// </summary>
/// <param name="LoadedDatasets">
/// Snapshot of every dataset currently known to the loader. Order is
/// not significant — rules query by product spec / active flag.
/// </param>
/// <param name="Mariner">
/// Active mariner-settings snapshot, or <c>null</c> if rules should
/// fall back to <see cref="MarinerSettings.Default"/>. Rules that
/// honour MSC.232(82) §5.8 (safety contour) read
/// <see cref="MarinerSettings.SafetyContour"/> from this snapshot.
/// </param>
public sealed record S98RuleContext(
    IReadOnlyList<LoadedDatasetInfo> LoadedDatasets,
    MarinerSettings? Mariner = null)
{
    /// <summary>Mariner settings to use during evaluation; never <c>null</c>.</summary>
    public MarinerSettings EffectiveMariner => Mariner ?? MarinerSettings.Default;
}
