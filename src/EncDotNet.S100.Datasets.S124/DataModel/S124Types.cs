using System.Collections.ObjectModel;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S124.DataModel;

/// <summary>
/// Strongly-typed identifier for a navigational warning: the
/// <c>messageSeriesIdentifier</c> complex attribute (S-124 Editions 1.0.0
/// and 2.0.0).
/// </summary>
public sealed record S124MessageSeriesIdentifier
{
    /// <summary>The sequential warning number within the series.</summary>
    public int? WarningNumber { get; init; }

    /// <summary>The calendar year the warning was issued.</summary>
    public int? Year { get; init; }

    /// <summary>
    /// The promulgating agency (e.g. <c>"NGA"</c>, <c>"USCG"</c>):
    /// <c>productionAgency</c> in Edition 1.0.0,
    /// <c>agencyResponsibleForProduction</c> in 2.0.0.
    /// </summary>
    public string? ProductionAgency { get; init; }

    /// <summary>The series name, when present (e.g. <c>"HYDROLANT"</c>).</summary>
    public string? NameOfSeries { get; init; }

    /// <summary>
    /// The originating country, when present: <c>country</c> in Edition
    /// 1.0.0, <c>nationality</c> in 2.0.0.
    /// </summary>
    public string? Country { get; init; }

    /// <summary>
    /// The warning type code (Edition 2.0.0 <c>warningType</c>: 1 local,
    /// 2 coastal, 3 sub-area, 4 NAVAREA, …), when present.
    /// </summary>
    public int? WarningType { get; init; }

    /// <summary>The interoperability identifier (Edition 2.0.0), when present.</summary>
    public string? InteroperabilityIdentifier { get; init; }
}

/// <summary>
/// A validity range from a <c>fixedDateRange</c> complex attribute. Either
/// end may be open.
/// </summary>
/// <remarks>
/// Edition 2.0.0 gives each end as a date (<c>dateStart</c>,
/// <c>dateEnd</c>) and an optional UTC time of day
/// (<c>timeOfDayStart</c>, <c>timeOfDayEnd</c>); a date without a time is
/// the start of that day. Edition 1.0.0 datasets may give a date or a
/// date-time.
/// </remarks>
/// <param name="Start">The start (UTC), or <c>null</c> when open.</param>
/// <param name="End">The end (UTC), or <c>null</c> when open.</param>
public sealed record S124DateRange(DateTimeOffset? Start, DateTimeOffset? End)
{
    /// <summary>Whether <paramref name="time"/> falls in the range (both ends inclusive).</summary>
    /// <param name="time">The time.</param>
    /// <returns><c>true</c> when inside.</returns>
    public bool Contains(DateTimeOffset time) =>
        (Start is null || time >= Start) && (End is null || time <= End);
}

/// <summary>
/// Typed projection of the <c>NavwarnPreamble</c> information type
/// (S-124 Editions 1.0.0 and 2.0.0): the header carrying identification,
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

    /// <summary>When the warning was published (Edition 2.0.0 <c>publicationTime</c>), when supplied.</summary>
    public DateTimeOffset? PublicationTime { get; init; }

    /// <summary>
    /// When the warning cancels itself (Edition 2.0.0
    /// <c>cancellationDate</c>): the dataset is valid before this time.
    /// <c>null</c> means valid until a cancellation dataset is issued
    /// (S-124 Ed 2.0.0 §9.2.1).
    /// </summary>
    public DateTimeOffset? CancellationDate { get; init; }

    /// <summary>The general warning type code (Edition 2.0.0 <c>navwarnTypeGeneral</c>), when supplied.</summary>
    public int? NavwarnTypeGeneral { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public IReadOnlyDictionary<string, string> ExtraAttributes { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>NavwarnPart</c> feature (S-124 Editions 1.0.0 and 2.0.0):
/// a single part of a warning carrying restriction, warning information
/// text, and per-part geometry.
/// </summary>
public sealed class S124NavwarnPart
{
    /// <summary>The GML identifier of the source <c>NavwarnPart</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The restriction code, when present.</summary>
    public int? Restriction { get; init; }

    /// <summary>
    /// The warning information text: <c>warningInformation/information</c> in
    /// Edition 1.0.0, <c>warningInformation/information/text</c> in 2.0.0 (the
    /// first, when there is one per language).
    /// </summary>
    public string? WarningInformation { get; init; }

    /// <summary>
    /// The warning type label from <c>warningInformation/navwarnTypeDetails</c>
    /// (Edition 2.0.0, e.g. "Cable Operations"), when present.
    /// </summary>
    public string? NavwarnType { get; init; }

    /// <summary>The warning type code from <c>navwarnTypeDetails</c>'s <c>code</c> attribute, when present.</summary>
    public int? NavwarnTypeCode { get; init; }

    /// <summary>The category code, when present.</summary>
    public int? Category { get; init; }

    /// <summary>
    /// When the hazard applies: one range per <c>fixedDateRange</c>, in
    /// document order. Empty when the part gives none.
    /// </summary>
    public IReadOnlyList<S124DateRange> ValidityRanges { get; init; } = [];

    /// <summary>Affected areas referenced via the <c>areaAffected</c> association.</summary>
    public IReadOnlyList<S124AffectedArea> AffectedAreas { get; init; } = [];

    /// <summary>Text placements referenced via the <c>TextAssociation</c> association.</summary>
    public IReadOnlyList<S124TextPlacement> TextPlacements { get; init; } = [];

    /// <summary>The geometry primitive kind of the source feature.</summary>
    public S124GeometryKind GeometryKind { get; init; }

    /// <summary>The coordinates of the source feature (semantics depend on <see cref="GeometryKind"/>).</summary>
    public IReadOnlyList<GeoPosition> Coordinates { get; init; } = [];

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public IReadOnlyDictionary<string, string> ExtraAttributes { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>NavwarnAreaAffected</c> feature (S-124 Editions 1.0.0 and 2.0.0).
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
    public IReadOnlyList<GeoPosition> Coordinates { get; init; } = [];

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public IReadOnlyDictionary<string, string> ExtraAttributes { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>TextPlacement</c> feature (S-124 Editions 1.0.0 and 2.0.0).
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
    public IReadOnlyDictionary<string, string> ExtraAttributes { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>References</c> information type
/// (S-124 Editions 1.0.0 and 2.0.0): a reference to other warnings, such
/// as a cancellation.
/// </summary>
public sealed class S124WarningReference
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The reference category code, when present: 1 warning cancellation,
    /// 2 warning reference, 3 in-force (Edition 2.0.0 <c>referenceCategory</c>).
    /// </summary>
    public int? ReferenceCategory { get; init; }

    /// <summary>The referenced message identifier (Edition 1.0.0, e.g. <c>"HYDROLANT 0412/2026"</c>).</summary>
    public string? MessageReference { get; init; }

    /// <summary>
    /// The referenced warnings' identifiers (Edition 2.0.0
    /// <c>messageSeriesIdentifier</c>, one per referenced warning).
    /// </summary>
    public IReadOnlyList<S124MessageSeriesIdentifier> Warnings { get; init; } = [];

    /// <summary>Whether the issuer holds none of the referenced messages (Edition 2.0.0 <c>noMessageOnHand</c>), when present.</summary>
    public bool? NoMessageOnHand { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public IReadOnlyDictionary<string, string> ExtraAttributes { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>SpatialQuality</c> information type
/// (S-124 Editions 1.0.0 and 2.0.0).
/// </summary>
public sealed class S124SpatialQuality
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The quality-of-position code, when present.</summary>
    public int? QualityOfPosition { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public IReadOnlyDictionary<string, string> ExtraAttributes { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;
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
