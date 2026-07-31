namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// One point on the total-available-water-depth curve for a picked location:
/// the tide-adjusted depth at a single time step.
/// </summary>
/// <param name="Time">The time step (UTC).</param>
/// <param name="DepthMeters">
/// The tide-adjusted depth in metres — base depth plus the S-104 water-level
/// height at this step — or <c>null</c> when the tide value at this step is
/// NoData.
/// </param>
internal readonly record struct DepthOverTimePoint(
    DateTime Time,
    double? DepthMeters);
