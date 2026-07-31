namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Identifies which S-104 dataset supplied the tide series for a picked
/// location, after deterministic selection among overlapping candidates.
/// Surfaced in the depth card so the mariner can see which source is in use.
/// </summary>
/// <param name="DatasetId">Identifier of the selected S-104 dataset.</param>
/// <param name="VerticalDatumCode">
/// The selected dataset's vertical datum as an S-100 register code, or
/// <c>null</c> when absent.
/// </param>
internal sealed record LocationTideSelection(
    string DatasetId,
    int? VerticalDatumCode);
