namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// One point on the total-available-water-depth curve for a picked location:
/// the tide-adjusted depth at a single time step.
/// </summary>
/// <param name="Time">The time step (UTC).</param>
/// <param name="DepthMeters">
/// The tide-adjusted depth in metres — base depth plus the S-104 water-level
/// height at this step — or <c>null</c> when no water level applies at this
/// step (NoData, or outside every series).
/// </param>
/// <param name="UncertaintyMeters">
/// The uncertainty of the adjusted depth at this step: the S-102 and S-104
/// uncertainties combined as independent, √(σ₁₀₂² + σ₁₀₄²), counting only
/// the ones given; <c>null</c> when neither is.
/// </param>
internal readonly record struct DepthOverTimePoint(
    DateTime Time,
    double? DepthMeters,
    double? UncertaintyMeters = null);
