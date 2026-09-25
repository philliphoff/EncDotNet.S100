namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// One <c>dataCoverage</c> entry of a <see cref="DatasetDiscoveryMetadata"/>
/// record: a coverage polygon plus the display-scale band in which the
/// dataset is intended to be shown there.
/// </summary>
/// <remarks>
/// S-100 Part 17. See <see cref="DatasetDiscoveryMetadata.ResolveMinimumDisplayScale"/>
/// and <see cref="DatasetDiscoveryMetadata.ResolveMaximumDisplayScale"/> for how the
/// scale bands of multiple coverages are combined.
/// </remarks>
public sealed class DataCoverage
{
    /// <summary>
    /// The raw XML of the <c>boundingPolygon</c> element, serialized as a
    /// string (the geometry is not parsed), or <see langword="null"/> when absent.
    /// </summary>
    public string? BoundingPolygon { get; init; }

    /// <summary>
    /// The <c>maximumDisplayScale</c> denominator (e.g. <c>22000</c> for 1:22 000):
    /// the most zoomed-in scale at which the coverage is intended to be displayed.
    /// <see langword="null"/> when absent or not an integer.
    /// </summary>
    public int? MaximumDisplayScale { get; init; }

    /// <summary>
    /// The <c>minimumDisplayScale</c> denominator: the most zoomed-out scale at
    /// which the coverage is intended to be displayed.
    /// <see langword="null"/> when absent or not an integer.
    /// </summary>
    public int? MinimumDisplayScale { get; init; }
}
