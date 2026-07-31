namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// The assimilated depth picture for a picked location: the chosen base depth,
/// the selected tide source and tide-adjusted depth curve (when an S-104 grid
/// overlaps), the uncertainty band (when the base is S-102 bathymetry), and a
/// flag warning that the base and tide vertical datums are not reconciled.
/// </summary>
/// <param name="Base">The chosen base depth and its source.</param>
/// <param name="Tide">
/// The selected S-104 tide source, or <c>null</c> in the partial state where a
/// base depth exists but no S-104 grid overlaps the point.
/// </param>
/// <param name="DepthOverTime">
/// The tide-adjusted depth curve (base + tide at each step), ordered by time;
/// empty when <see cref="Tide"/> is <c>null</c>.
/// </param>
/// <param name="UncertaintyMeters">
/// The base-depth vertical uncertainty in metres, populated only when the base
/// is S-102 bathymetry; <c>null</c> otherwise.
/// </param>
/// <param name="DatumsNotReconciled">
/// <c>true</c> when the base (S-102) and selected tide (S-104) vertical datums
/// could not be confirmed as the same datum, so the absolute baseline carries
/// a caveat (the curve shape remains valid). Always <c>false</c> when there is
/// no S-102 base or no selected tide.
/// </param>
internal sealed record LocationDepthResult(
    BaseDepthResult Base,
    LocationTideSelection? Tide,
    IReadOnlyList<DepthOverTimePoint> DepthOverTime,
    double? UncertaintyMeters,
    bool DatumsNotReconciled);
