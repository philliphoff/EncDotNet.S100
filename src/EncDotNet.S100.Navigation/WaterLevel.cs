using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>The encoding of an S-104 water-level source.</summary>
public enum WaterLevelSourceKind
{
    /// <summary>A regular grid (data coding format 2, S-104 Ed 2.0.0 §10.2).</summary>
    Grid,

    /// <summary>A fixed station time series (data coding format 8).</summary>
    Station,
}

/// <summary>Where a water-level series comes from.</summary>
/// <param name="DatasetId">Identifier of the S-104 dataset.</param>
/// <param name="Kind">Grid or station.</param>
/// <param name="VerticalDatum">
/// The dataset's vertical datum as an S-100 register code, or <c>null</c>
/// when it isn't declared (S-104 Ed 2.0.0 §5.2, Table 5-1).
/// </param>
/// <param name="StationId">The station identifier, for a station series.</param>
/// <param name="DistanceMeters">Distance from the sample point to the station, for a station series.</param>
/// <param name="GridCell">The grid cell (<c>row,col</c>), for a grid series.</param>
public sealed record WaterLevelSourceInfo(
    string DatasetId,
    WaterLevelSourceKind Kind,
    int? VerticalDatum,
    string? StationId = null,
    double? DistanceMeters = null,
    string? GridCell = null);

/// <summary>A water-level height read from a series.</summary>
/// <param name="HeightMeters">Height of the water above the vertical datum, in metres.</param>
/// <param name="StepTime">The time step nearest the requested time.</param>
public readonly record struct WaterLevelReading(double HeightMeters, DateTime StepTime);

/// <summary>
/// The water-level time series at one place: a grid cell or a station.
/// </summary>
public interface IWaterLevelSeries
{
    /// <summary>Where the series comes from.</summary>
    WaterLevelSourceInfo Source { get; }

    /// <summary>The first time step (UTC).</summary>
    DateTime Start { get; }

    /// <summary>The last time step (UTC).</summary>
    DateTime End { get; }

    /// <summary>
    /// The water level at <paramref name="utc"/>, interpolated linearly
    /// between the bracketing steps, or <c>null</c> when the value is NoData
    /// (a masked cell). Times outside <see cref="Start"/>…<see cref="End"/>
    /// are the caller's to reject; implementations may clamp.
    /// </summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The reading, or <c>null</c>.</returns>
    WaterLevelReading? ValueAt(DateTime utc);
}

/// <summary>Finds the water-level series that apply at a point.</summary>
public interface IWaterLevelSource
{
    /// <summary>
    /// The series covering a point, most preferred first. Empty when the
    /// point is outside every S-104 dataset's extent.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Longitude in decimal degrees (WGS-84).</param>
    /// <returns>The series, most preferred first.</returns>
    IReadOnlyList<IWaterLevelSeries> Locate(double latitude, double longitude);
}

/// <summary>
/// The rule for choosing between overlapping gridded S-104 datasets, shared
/// with the viewer's pick report: the finest grid wins, then the latest
/// issue, then the ordinally smaller dataset id so the choice never depends
/// on load order.
/// </summary>
public static class WaterLevelPreference
{
    /// <summary>
    /// Compares two candidates; a negative result means <paramref name="spacingA"/>'s
    /// candidate is preferred.
    /// </summary>
    /// <param name="spacingA">Grid spacing of the first candidate (any consistent unit).</param>
    /// <param name="issuedA">Issue time of the first candidate, or <c>null</c>.</param>
    /// <param name="idA">Dataset id of the first candidate.</param>
    /// <param name="spacingB">Grid spacing of the second candidate.</param>
    /// <param name="issuedB">Issue time of the second candidate, or <c>null</c>.</param>
    /// <param name="idB">Dataset id of the second candidate.</param>
    /// <returns>Negative when A is preferred, positive when B is, zero for identical keys.</returns>
    public static int Compare(
        double spacingA, DateTime? issuedA, string idA,
        double spacingB, DateTime? issuedB, string idB)
    {
        var bySpacing = spacingA.CompareTo(spacingB);
        if (bySpacing != 0)
        {
            return bySpacing;
        }

        switch (issuedA, issuedB)
        {
            case ({ } a, { } b) when a != b:
                return b.CompareTo(a);
            case (not null, null):
                return -1;
            case (null, not null):
                return 1;
            default:
                return string.CompareOrdinal(idA, idB);
        }
    }
}

/// <summary>Whether a depth and a water level share a vertical datum.</summary>
public enum DatumAgreement
{
    /// <summary>Both declare the same S-100 vertical datum code.</summary>
    Same,

    /// <summary>Both declare a datum, and the codes differ.</summary>
    Different,

    /// <summary>At least one doesn't declare a datum, so agreement can't be confirmed.</summary>
    Unknown,
}

/// <summary>
/// Compares the vertical datums of a charted depth and a water level.
/// </summary>
/// <remarks>
/// <para>
/// S-102 depths are referred to the vertical datum in the root group's
/// <c>verticalDatum</c> attribute (S-102 Ed 3.0.0 §5.3 and §10.2.1, Table
/// 10-2). S-104 heights are referred to their own <c>verticalDatum</c>
/// (S-104 Ed 2.0.0 §5.2, Table 5-1), and the specification requires it to be
/// consistent with the underlying S-101 and S-102 datasets, with conflicts
/// resolved with the producer. Adding a height to a depth on a different
/// datum gives a wrong total, so the passage check doesn't apply the water
/// level when the codes differ, and reports the mismatch.
/// </para>
/// <para>
/// Both products use the same S-100 vertical datum code list, so equal codes
/// are the same datum. Only <c>verticalDatumReference</c> = 1 (S-100 codes)
/// is allowed in S-102, so codes are compared directly.
/// </para>
/// </remarks>
public static class VerticalDatumCheck
{
    /// <summary>
    /// Compares two datum codes. A code that isn't in the S-100 vertical
    /// datum list (such as an EPSG code written into <c>verticalDatum</c> by
    /// a pre-release producer) counts as undeclared.
    /// </summary>
    /// <param name="depthDatum">The charted depth's datum code, or <c>null</c>.</param>
    /// <param name="waterLevelDatum">The water level's datum code, or <c>null</c>.</param>
    /// <returns>The agreement.</returns>
    public static DatumAgreement Compare(int? depthDatum, int? waterLevelDatum) =>
        !IsKnown(depthDatum, out var d) || !IsKnown(waterLevelDatum, out var w)
            ? DatumAgreement.Unknown
            : d == w ? DatumAgreement.Same : DatumAgreement.Different;

    private static bool IsKnown(int? code, out int value)
    {
        value = code ?? 0;
        return code is { } c && VerticalDatums.TryGetLabel(c, out _);
    }
}
