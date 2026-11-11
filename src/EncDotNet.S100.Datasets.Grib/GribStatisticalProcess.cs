namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// One time range specification of a product definition template 4.8 field
/// (statistically processed over a time interval): for example the maximum
/// over 3 hours.
/// </summary>
/// <param name="Type">
/// The statistical process (code table 4.10): <c>0</c> average, <c>1</c>
/// accumulation, <c>2</c> maximum, <c>3</c> minimum, <c>4</c> difference
/// (end minus start), <c>6</c> standard deviation, and so on.
/// </param>
/// <param name="TimeIncrementType">
/// How the time is incremented between the processed fields (code table 4.11),
/// for example <c>2</c> for successive forecast times from the same run.
/// </param>
/// <param name="Length">The length of the time range the process covers.</param>
/// <param name="Increment">The time between successive fields used in the process; a value of 0 means continuous processing.</param>
public sealed record GribStatisticalProcess(
    byte Type,
    byte TimeIncrementType,
    GribTimeRange Length,
    GribTimeRange Increment)
{
    /// <summary>Code table 4.10: average.</summary>
    public const byte Average = 0;

    /// <summary>Code table 4.10: accumulation.</summary>
    public const byte Accumulation = 1;

    /// <summary>Code table 4.10: maximum.</summary>
    public const byte Maximum = 2;

    /// <summary>Code table 4.10: minimum.</summary>
    public const byte Minimum = 3;
}
