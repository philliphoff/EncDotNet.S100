using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// The AML facts of a NATO Additional Military Layers cell, as dataset info
/// and <c>describe_library_item</c> report them (#1236, decision 14).
/// </summary>
[Description("NATO Additional Military Layers facts: military overlay data, not a chart. Never use it for depth answers or route checks.")]
public sealed record AmlReport(
    [property: Description("Product code: CLB, ESB, LBO, MFF, RAL or SBO; for AML 3.0 the cell name's product token, or null.")] string? Product,
    [property: Description("Product name, e.g. \"Contour Line Bathymetry\".")] string ProductName,
    [property: Description("The DSID PRSP code, 51–57.")] int ProductSpecification,
    [property: Description("AML product specification edition as written, e.g. \"1.0\" or \"3.0\".")] string? Version,
    [property: Description("Cell title from the header, or null.")] string? Title,
    [property: Description("Protective marking, upper case, as given. Say it before describing the data.")] string? ProtectiveMarking,
    [property: Description("Where the marking came from: \"header\" (DSID COMT), \"m_clas\" (security meta feature) or \"cell-name\" (the name's classification letter, used when the cell gives none).")] string MarkingSource,
    [property: Description("International Defence Organisation (IDO) status, or null.")] string? IdoStatus,
    [property: Description("Owner authority, or null.")] string? OwnerAuthority,
    [property: Description("Handling caveat, or null.")] string? Caveat,
    [property: Description("AML scale band 0–9 (not an ENC usage band), or null.")] int? ScaleBand,
    [property: Description("What the cell name says, or null when it doesn't follow an AML pattern.")] AmlCellNameReport? FromName)
{
    /// <summary>The report of <paramref name="info"/>.</summary>
    /// <param name="info">The cell's AML facts.</param>
    public static AmlReport From(AmlDatasetInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new AmlReport(
            info.ProductCode,
            info.ProductName,
            info.ProductSpecification,
            info.Version,
            info.Title,
            info.ProtectiveMarking,
            MarkingSourceName(info.MarkingSource),
            info.IdoStatus,
            info.OwnerAuthority,
            info.Caveat,
            info.ScaleBand,
            info.FromName is { } name
                ? new AmlCellNameReport(name.Producer, name.Product, name.ScaleBand, name.ClassificationLetter.ToString())
                : null);
    }

    /// <summary>The status hosts report for an AML cell: <c>"drawn"</c> or <c>"recognised-not-drawn"</c>.</summary>
    /// <param name="drawn">Whether the cell is drawn.</param>
    public static string StatusOf(bool drawn) => drawn ? "drawn" : "recognised-not-drawn";

    /// <summary>
    /// Whether SoundCharts draws AML at all, for a cell whose features haven't
    /// been read (a Library item). A loaded cell answers for itself
    /// (<see cref="AmlDatasetData.Drawn"/>): it isn't drawn when none of its
    /// classes has a style.
    /// </summary>
    public static bool Drawn => S100Products.AmlPortrayal.Draws;

    private static string MarkingSourceName(AmlMarkingSource source) => source switch
    {
        AmlMarkingSource.Header => "header",
        AmlMarkingSource.SecurityMetaFeature => "m_clas",
        AmlMarkingSource.CellName => "cell-name",
        _ => "none",
    };
}

/// <summary>What an AML cell name says.</summary>
[Description("What an AML cell name says: producer, product, scale band and classification letter.")]
public sealed record AmlCellNameReport(
    [property: Description("Producer code, e.g. \"UK\" or \"DEU\".")] string Producer,
    [property: Description("Product code from the name, or null.")] string? Product,
    [property: Description("Scale band from the name, 0–9.")] int ScaleBand,
    [property: Description("Classification letter: U (UNCLASSIFIED), R (RESTRICTED), …")] string Marking);

/// <summary>The AML facts list_datasets adds to a dataset.</summary>
[Description("AML facts of a loaded NATO Additional Military Layers dataset.")]
public sealed record AmlDatasetSummary(
    [property: Description("Product code, e.g. \"CLB\", or null for an AML 3.0 cell without one.")] string? Product,
    [property: Description("Protective marking, or null.")] string? ProtectiveMarking,
    [property: Description("Whether SoundCharts draws it; false while AML is recognised, not drawn.")] bool Drawn,
    [property: Description("\"recognised-not-drawn\" or \"drawn\".")] string Status);
