namespace EncDotNet.S100.Datasets.Grib.Internal;

/// <summary>
/// The group descriptors of data representation templates 5.2 and 5.3
/// (octets 22 to 47, and 48 and 49 for 5.3).
/// </summary>
/// <param name="GroupSplittingMethod">Code table 5.4: <c>0</c> row by row, <c>1</c> general.</param>
/// <param name="MissingValueManagement">Code table 5.5.</param>
/// <param name="PrimaryMissingSubstitute">The primary missing value substitute, as raw octets (its type follows the original field type).</param>
/// <param name="SecondaryMissingSubstitute">The secondary missing value substitute, as raw octets.</param>
/// <param name="NumberOfGroups">NG, the number of groups.</param>
/// <param name="GroupWidthReference">The reference added to every group width.</param>
/// <param name="GroupWidthBits">The bits used for each group width.</param>
/// <param name="GroupLengthReference">The reference added to every scaled group length.</param>
/// <param name="GroupLengthIncrement">The increment each stored group length is multiplied by.</param>
/// <param name="LastGroupLength">The true length of the last group.</param>
/// <param name="GroupLengthBits">The bits used for each scaled group length.</param>
/// <param name="SpatialDifferencingOrder">For 5.3, the order of spatial differencing (1 or 2); <c>0</c> for 5.2.</param>
/// <param name="ExtraDescriptorOctets">For 5.3, the octets of each extra descriptor (the first values and the minimum difference).</param>
internal sealed record ComplexPackingParameters(
    byte GroupSplittingMethod,
    GribMissingValueManagement MissingValueManagement,
    uint PrimaryMissingSubstitute,
    uint SecondaryMissingSubstitute,
    long NumberOfGroups,
    int GroupWidthReference,
    int GroupWidthBits,
    long GroupLengthReference,
    int GroupLengthIncrement,
    long LastGroupLength,
    int GroupLengthBits,
    int SpatialDifferencingOrder,
    int ExtraDescriptorOctets);
