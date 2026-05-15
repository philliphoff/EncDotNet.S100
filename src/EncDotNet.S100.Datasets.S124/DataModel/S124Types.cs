using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S124.DataModel;

/// <summary>
/// Strongly-typed identifier for a navigational warning as defined by
/// S-124 § (Edition 1.0.0) <c>messageSeriesIdentifier</c>.
/// </summary>
public sealed record S124MessageSeriesIdentifier
{
    /// <summary>The sequential warning number within the series.</summary>
    public int? WarningNumber { get; init; }

    /// <summary>The calendar year the warning was issued.</summary>
    public int? Year { get; init; }

    /// <summary>The promulgating agency (e.g. <c>"NGA"</c>, <c>"USCG"</c>).</summary>
    public string? ProductionAgency { get; init; }

    /// <summary>The series name, when present (e.g. <c>"HYDROLANT"</c>).</summary>
    public string? NameOfSeries { get; init; }

    /// <summary>The originating country code, when present.</summary>
    public string? Country { get; init; }
}

/// <summary>
/// Typed projection of the <c>NavwarnPreamble</c> information type
/// (S-124 § Edition 1.0.0): the header carrying identification,
/// validity, classification, and locality of the warning.
/// </summary>
public sealed class S124NavwarnPreamble
{
    /// <summary>The GML identifier of the source <c>NavwarnPreamble</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Strongly-typed message series identifier (warning number / year / agency).</summary>
    public S124MessageSeriesIdentifier? MessageSeriesIdentifier { get; init; }

    /// <summary>Free-text general area (e.g. <c>"Gulf of Mexico"</c>).</summary>
    public string? GeneralArea { get; init; }

    /// <summary>Free-text locality (e.g. <c>"Galveston Ship Channel"</c>).</summary>
    public string? Locality { get; init; }

    /// <summary>Title of the warning, when supplied.</summary>
    public string? Title { get; init; }

    /// <summary>NAVAREA classification, when supplied.</summary>
    public string? NavareaCode { get; init; }

    /// <summary>NAVTEX coverage subject, when supplied.</summary>
    public string? Navtex { get; init; }

    /// <summary>The promulgating authority's name, when supplied.</summary>
    public string? PromulgatingAuthority { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>NavwarnPart</c> feature (S-124 § Edition 1.0.0):
/// a single part of a warning carrying restriction, warning information
/// text, and per-part geometry.
/// </summary>
public sealed class S124NavwarnPart
{
    /// <summary>The GML identifier of the source <c>NavwarnPart</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The restriction code, when present.</summary>
    public int? Restriction { get; init; }

    /// <summary>The warning information text from <c>warningInformation/information</c>.</summary>
    public string? WarningInformation { get; init; }

    /// <summary>The category code, when present.</summary>
    public int? Category { get; init; }

    /// <summary>Affected areas referenced via the <c>areaAffected</c> association.</summary>
    public ImmutableArray<S124AffectedArea> AffectedAreas { get; init; } = ImmutableArray<S124AffectedArea>.Empty;

    /// <summary>Text placements referenced via the <c>TextAssociation</c> association.</summary>
    public ImmutableArray<S124TextPlacement> TextPlacements { get; init; } = ImmutableArray<S124TextPlacement>.Empty;

    /// <summary>The geometry primitive kind of the source feature.</summary>
    public S124GeometryKind GeometryKind { get; init; }

    /// <summary>The coordinates of the source feature (semantics depend on <see cref="GeometryKind"/>).</summary>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>NavwarnAreaAffected</c> feature (S-124 § Edition 1.0.0).
/// </summary>
public sealed class S124AffectedArea
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The restriction code, when present.</summary>
    public int? Restriction { get; init; }

    /// <summary>Geometry primitive kind.</summary>
    public S124GeometryKind GeometryKind { get; init; }

    /// <summary>Coordinates whose semantics depend on <see cref="GeometryKind"/>.</summary>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>TextPlacement</c> feature (S-124 § Edition 1.0.0).
/// </summary>
public sealed class S124TextPlacement
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The placement point, when supplied.</summary>
    public GeoPosition? Position { get; init; }

    /// <summary>The text content, when supplied.</summary>
    public string? Text { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>References</c> information type
/// (S-124 § Edition 1.0.0): a reference to another warning.
/// </summary>
public sealed class S124WarningReference
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The reference category code (cancellation, supersession, etc.), when present.</summary>
    public int? ReferenceCategory { get; init; }

    /// <summary>The referenced message identifier (e.g. <c>"HYDROLANT 0412/2026"</c>).</summary>
    public string? MessageReference { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>SpatialQuality</c> information type
/// (S-124 § Edition 1.0.0).
/// </summary>
public sealed class S124SpatialQuality
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The quality-of-position code, when present.</summary>
    public int? QualityOfPosition { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// The geometry primitive kind of an S-124 feature.
/// </summary>
public enum S124GeometryKind
{
    /// <summary>No geometry.</summary>
    None,
    /// <summary>Single point (<see cref="GeoPosition"/>).</summary>
    Point,
    /// <summary>Curve (ordered sequence of <see cref="GeoPosition"/>).</summary>
    Curve,
    /// <summary>Surface exterior ring (closed sequence of <see cref="GeoPosition"/>).</summary>
    Surface,
}
