namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// How complex packing (templates 5.2 and 5.3) marks missing values inside
/// the packed data (code table 5.5), as opposed to the bitmap.
/// </summary>
public enum GribMissingValueManagement : byte
{
    /// <summary>No missing values are embedded in the packed data.</summary>
    None = 0,

    /// <summary>Primary missing values are embedded: the all-ones value of a group's width.</summary>
    Primary = 1,

    /// <summary>Primary and secondary missing values are embedded: all ones, and all ones minus one.</summary>
    PrimaryAndSecondary = 2,
}
