using EncDotNet.S100.Datasets.S104;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// A candidate S-104 tide source for a picked location, already sampled at
/// the point by the caller. <see cref="DepthAssimilationService"/> selects one
/// candidate deterministically (finest resolution, then latest issuance) when
/// several overlap.
/// </summary>
/// <param name="DatasetId">Stable identifier of the source dataset (surfaced in the card).</param>
/// <param name="SpacingDegrees">
/// The grid resolution as a representative cell spacing in degrees; the
/// smallest value is the finest and wins selection.
/// </param>
/// <param name="IssueDate">
/// The dataset issue date used to break resolution ties (latest wins), or
/// <c>null</c> when unknown.
/// </param>
/// <param name="VerticalDatumCode">
/// The S-104 dataset's declared vertical datum as an S-100 register code, or
/// <c>null</c> when absent. Compared against the S-102 base datum to flag
/// unreconciled datums.
/// </param>
/// <param name="Series">
/// The tide series sampled at the pick, or <c>null</c> when the point falls
/// outside this dataset's grid (the candidate is then ineligible).
/// </param>
internal sealed record S104TideCandidate(
    string DatasetId,
    double SpacingDegrees,
    DateTime? IssueDate,
    int? VerticalDatumCode,
    S104TimeSeries? Series);
