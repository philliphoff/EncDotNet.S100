namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// The identification section (Section 1) of a GRIB2 message: who made it,
/// with which tables, and its reference time.
/// </summary>
/// <param name="Centre">The originating centre (common code table C-11), for example <c>7</c> for NCEP or <c>98</c> for ECMWF.</param>
/// <param name="SubCentre">The originating sub-centre, defined by the centre.</param>
/// <param name="MasterTablesVersion">The GRIB master tables version (code table 1.0).</param>
/// <param name="LocalTablesVersion">The local tables version (code table 1.1); <c>0</c> when local tables aren't used.</param>
/// <param name="ReferenceTimeSignificance">
/// What the reference time means (code table 1.2): <c>0</c> analysis, <c>1</c>
/// start of forecast, <c>2</c> verifying time of forecast, <c>3</c> observation time.
/// </param>
/// <param name="ReferenceTime">The reference time, in UTC: for a forecast, the model run time.</param>
/// <param name="ProductionStatus">The production status (code table 1.3), for example <c>0</c> operational.</param>
/// <param name="DataType">The type of processed data (code table 1.4), for example <c>1</c> forecast products.</param>
public sealed record GribIdentification(
    ushort Centre,
    ushort SubCentre,
    byte MasterTablesVersion,
    byte LocalTablesVersion,
    byte ReferenceTimeSignificance,
    DateTime ReferenceTime,
    byte ProductionStatus,
    byte DataType)
{
    /// <summary>
    /// A short name for <see cref="Centre"/>, for example <c>"NCEP"</c>, or
    /// <see langword="null"/> when the centre isn't one of the few this
    /// decoder knows (see <see cref="GribCentres.GetName"/>).
    /// </summary>
    public string? CentreName => GribCentres.GetName(Centre);
}
