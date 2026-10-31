using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// The S-104 water level applied at a picked location: the series the
/// water-level field may read there, in the shared preference order
/// (<see cref="IWaterLevelSource.Locate"/>), already limited to those on the
/// charted depth's vertical datum. Surfaced in the depth card so the mariner
/// can see which source is in use.
/// </summary>
/// <param name="DatasetId">Identifier of the most preferred S-104 dataset.</param>
/// <param name="VerticalDatumCode">
/// That dataset's vertical datum as an S-100 register code, or <c>null</c>
/// when absent.
/// </param>
/// <param name="Series">
/// The series to read at each time, most preferred first, through
/// <see cref="WaterLevelSnapshot.Resolve(IReadOnlyList{IWaterLevelSeries}, DateTime, int?)"/>,
/// so the card gives the passage check's height for the same time.
/// </param>
internal sealed record LocationTideSelection(
    string DatasetId,
    int? VerticalDatumCode,
    IReadOnlyList<IWaterLevelSeries> Series);
