using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S411.DataModel;

/// <summary>
/// The geometry primitive kind of an S-411 feature
/// (S-411 Edition 1.2.1 Annex A — Feature Catalogue).
/// </summary>
public enum S411GeometryKind
{
    /// <summary>No geometry (rare for S-411; tolerated by the projection).</summary>
    None,

    /// <summary>Single point — e.g. an <c>Iceberg</c> position or <c>IceThickness</c> sample.</summary>
    Point,

    /// <summary>Curve — e.g. <c>IceEdge</c>, <c>IcebergLimit</c>, <c>LimitOfAllKnownIce</c>.</summary>
    Curve,

    /// <summary>Surface exterior ring — e.g. <c>SeaIce</c>, <c>LakeIce</c>, <c>DataCoverage</c>.</summary>
    Surface,
}

/// <summary>
/// A typed WMO egg-code-style attribute bundle carried by sea-ice and lake-ice
/// features (S-411 Edition 1.2.1 Annex A — sea-ice / lake-ice WMO concentration,
/// stage of development, and form of ice).
/// </summary>
/// <remarks>
/// <para>
/// S-411 producers populate the egg code through two different vocabularies in
/// real-world data:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>JCOMM operational shape</b> (e.g. Canadian Ice Service feeds) — short
/// lowercase codes <c>iceact</c> (total concentration), <c>iceapc</c> (partial
/// concentrations), <c>icesod</c> (stages of development), <c>iceflz</c> (forms
/// of ice / floe size). The list-valued attributes are emitted as Python
/// list-style strings such as <c>[20, 30, 4]</c>; their raw text is preserved
/// verbatim in <see cref="PartialConcentrationsRaw"/>, <see cref="StagesOfDevelopmentRaw"/>,
/// and <see cref="FormsOfIceRaw"/> because the producer's tokenisation isn't
/// standardised.
/// </description></item>
/// <item><description>
/// <b>IHO 1.2.1 sample shape</b> — Feature Catalogue attribute names
/// (<c>totalConcentration</c>, <c>snowDepth</c>, …). When the typed model can
/// surface a single integer value it does so on <see cref="TotalConcentration"/>;
/// the egg-code list fields stay <c>null</c> for this shape.
/// </description></item>
/// </list>
/// <para>
/// The bundle is only attached to features whose Feature Catalogue class
/// carries the egg code (currently <see cref="S411SeaIce"/> and
/// <see cref="S411LakeIce"/>).
/// </para>
/// </remarks>
public sealed record S411EggCode
{
    /// <summary>
    /// Total ice concentration as a single value, parsed from
    /// <c>iceact</c> (JCOMM) or <c>totalConcentration</c> (IHO sample).
    /// </summary>
    public int? TotalConcentration { get; init; }

    /// <summary>
    /// Raw text of the JCOMM <c>iceapc</c> attribute (partial concentrations),
    /// preserved verbatim because producers serialise it as a Python-list-style
    /// string (e.g. <c>"[20, 30, 4]"</c>) rather than the standard WMO
    /// tokenisation.
    /// </summary>
    public string? PartialConcentrationsRaw { get; init; }

    /// <summary>Raw text of the JCOMM <c>icesod</c> attribute (stages of development).</summary>
    public string? StagesOfDevelopmentRaw { get; init; }

    /// <summary>Raw text of the JCOMM <c>iceflz</c> attribute (forms of ice / floe size).</summary>
    public string? FormsOfIceRaw { get; init; }

    /// <summary>
    /// Snow depth in centimetres parsed from the IHO <c>snowDepth</c>
    /// attribute (S-411 Edition 1.2.1 Annex A).
    /// </summary>
    public double? SnowDepth { get; init; }

    /// <summary>
    /// Returns <c>true</c> if every component is unset — used by the
    /// projection to elide an entirely empty bundle.
    /// </summary>
    public bool IsEmpty =>
        TotalConcentration is null
        && PartialConcentrationsRaw is null
        && StagesOfDevelopmentRaw is null
        && FormsOfIceRaw is null
        && SnowDepth is null;
}

/// <summary>
/// Shared shape carried by every typed S-411 feature.
/// </summary>
public interface IS411IceFeature
{
    /// <summary>The GML identifier of the source feature.</summary>
    string Id { get; }

    /// <summary>
    /// Canonical PascalCase Feature Catalogue class name (e.g. <c>"SeaIce"</c>,
    /// <c>"Iceberg"</c>). Both the JCOMM short codes and the IHO PascalCase
    /// names normalise to this value, so callers can dispatch on it without
    /// caring which GML shape the dataset uses.
    /// </summary>
    string NormalizedFeatureType { get; }

    /// <summary>
    /// The raw feature type element name from the source GML (e.g.
    /// <c>"seaice"</c> for JCOMM, <c>"SeaIce"</c> for IHO).
    /// </summary>
    string SourceFeatureType { get; }

    /// <summary>The geometry primitive kind of the source feature.</summary>
    S411GeometryKind GeometryKind { get; }

    /// <summary>Coordinates whose semantics depend on <see cref="GeometryKind"/>.</summary>
    ImmutableArray<GeoPosition> Coordinates { get; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    ImmutableDictionary<string, string> ExtraAttributes { get; }

    /// <summary>The originating raw feature.</summary>
    S411Feature Source { get; }
}
