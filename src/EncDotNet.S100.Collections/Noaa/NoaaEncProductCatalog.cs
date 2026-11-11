using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections.Noaa;

/// <summary>
/// A parsed NOAA ENC product catalogue (<c>ENCProdCat.xml</c>, "NOAA Enc
/// Product Catalog Technical Specifications" 1.0): one entry per S-57 cell
/// that NOAA distributes, with its coverage and download.
/// </summary>
/// <param name="Header">The catalogue header.</param>
/// <param name="Cells">The catalogued cells, in document order.</param>
public sealed record NoaaEncProductCatalog(NoaaEncCatalogHeader Header, IReadOnlyList<NoaaEncCell> Cells);

/// <summary>The header of a NOAA ENC product catalogue.</summary>
/// <param name="Title">The catalogue title.</param>
/// <param name="ValidAt">When the catalogue was generated (<c>dt_valid</c>), if present.</param>
/// <param name="AgencyCode">The S-62 producing agency code (550 for NOAA), if present.</param>
public sealed record NoaaEncCatalogHeader(string? Title, DateTimeOffset? ValidAt, int? AgencyCode);

/// <summary>One cell in a NOAA ENC product catalogue.</summary>
public sealed record NoaaEncCell
{
    /// <summary>The 8-character cell name (e.g. <c>US4AK4PM</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The cell's descriptive title (<c>lname</c>).</summary>
    public string? LongName { get; init; }

    /// <summary>The compilation scale denominator (<c>cscale</c>).</summary>
    public int? CompilationScale { get; init; }

    /// <summary>True when the cell's status is <c>Cancelled</c>.</summary>
    public bool IsCancelled { get; init; }

    /// <summary>The US Coast Guard districts the cell lies in.</summary>
    public IReadOnlyList<int> CoastGuardDistricts { get; init; } = [];

    /// <summary>The two-letter state or territory codes the cell lies in (e.g. <c>AK</c>).</summary>
    public IReadOnlyList<string> States { get; init; } = [];

    /// <summary>The NOAA region numbers the cell lies in.</summary>
    public IReadOnlyList<int> Regions { get; init; } = [];

    /// <summary>The download location of the cell's zipped exchange set.</summary>
    public Uri? ZipUri { get; init; }

    /// <summary>When the zip was published.</summary>
    public DateTimeOffset? ZipDateTime { get; init; }

    /// <summary>The zip's size in bytes.</summary>
    public long? ZipSize { get; init; }

    /// <summary>The edition number (<c>edtn</c>).</summary>
    public int? Edition { get; init; }

    /// <summary>The latest update number (<c>updn</c>).</summary>
    public int? Update { get; init; }

    /// <summary>The update application date (<c>uadt</c>).</summary>
    public DateOnly? UpdateApplicationDate { get; init; }

    /// <summary>The issue date (<c>isdt</c>).</summary>
    public DateOnly? IssueDate { get; init; }

    /// <summary>
    /// The coverage panels (<c>cov/panel</c>). Cancelled cells have none.
    /// Longitudes are as published, which may run beyond −180 for the
    /// western Pacific.
    /// </summary>
    public IReadOnlyList<NoaaEncPanel> Panels { get; init; } = [];
}

/// <summary>One coverage panel of a NOAA ENC cell.</summary>
/// <param name="Number">The panel number.</param>
/// <param name="IsInterior">True for an interior ring (<c>type</c> <c>I</c>), false for an exterior ring (<c>E</c>).</param>
/// <param name="Vertices">The ring's vertices.</param>
public sealed record NoaaEncPanel(int Number, bool IsInterior, IReadOnlyList<GeoPosition> Vertices);
