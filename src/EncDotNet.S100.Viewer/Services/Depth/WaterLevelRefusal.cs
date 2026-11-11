using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Why S-104 water level covering a picked location was not applied: the
/// charted depth and the water level are on different vertical datums, or
/// one of them doesn't declare a datum (S-98 Appendix D, S-164
/// IncompatibleDatums). The card shows the charted depth and says why.
/// </summary>
/// <param name="Agreement">
/// <see cref="DatumAgreement.Different"/> or <see cref="DatumAgreement.Unknown"/>
/// (an undeclared datum, or a code outside the S-100 list).
/// </param>
/// <param name="DepthSource">The kind of charted depth (S-102 or S-101).</param>
/// <param name="DepthDatumCode">The charted depth's datum, or <c>null</c>.</param>
/// <param name="WaterLevelDatasetId">The most preferred S-104 dataset covering the point.</param>
/// <param name="WaterLevelDatumCode">That dataset's datum, or <c>null</c>.</param>
internal sealed record WaterLevelRefusal(
    DatumAgreement Agreement,
    BaseDepthSource DepthSource,
    int? DepthDatumCode,
    string WaterLevelDatasetId,
    int? WaterLevelDatumCode);
