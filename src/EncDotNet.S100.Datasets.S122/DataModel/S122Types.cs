using System.Collections.ObjectModel;
using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S122.DataModel;

/// <summary>The geometry primitive kind of an S-122 feature.</summary>
/// <remarks>
/// Geometry-less feature instances are allowed by the S-122 Feature
/// Catalogue (e.g. an area that delegates its boundary to an associated
/// <c>InformationArea</c>); renderers and consumers must tolerate
/// <see cref="None"/>.
/// </remarks>
public enum S122GeometryKind
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

/// <summary>
/// Common shape implemented by every typed S-122 feature projection
/// (S-122 FC 2.0.0 § Feature Types). Allows generic consumers to walk
/// <see cref="S122MarineProtectedAreaDataset.Features"/> without
/// switching on concrete type.
/// </summary>
public interface IS122Feature
{
    /// <summary>The GML identifier of the source feature.</summary>
    string Id { get; }

    /// <summary>The S-122 feature type code (the GML element local name).</summary>
    string FeatureType { get; }

    /// <summary>Geometry primitive kind of the source feature.</summary>
    S122GeometryKind GeometryKind { get; }

    /// <summary>Coordinates whose semantics depend on <see cref="GeometryKind"/>.</summary>
    IReadOnlyList<GeoPosition> Coordinates { get; }

    /// <summary>The raw <c>xlink:href</c> cross-references from the source feature.</summary>
    IReadOnlyList<GmlReference> References { get; }

    /// <summary>Information-type bindings resolved from <see cref="References"/>.</summary>
    IReadOnlyList<S122InformationReference> InformationReferences { get; }

    /// <summary>Feature-to-feature bindings resolved from <see cref="References"/>.</summary>
    IReadOnlyList<S122FeatureReference> FeatureReferences { get; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    IReadOnlyDictionary<string, string> ExtraAttributes { get; }
}

/// <summary>
/// Common shape implemented by every typed S-122 information-type
/// projection (S-122 FC 2.0.0 § Information Types).
/// </summary>
public interface IS122InformationType
{
    /// <summary>The GML identifier.</summary>
    string Id { get; }

    /// <summary>The S-122 information-type code (the GML element local name).</summary>
    string TypeCode { get; }

    /// <summary>The raw <c>xlink:href</c> cross-references.</summary>
    IReadOnlyList<GmlReference> References { get; }

    /// <summary>Information-type bindings resolved from <see cref="References"/>.</summary>
    IReadOnlyList<S122InformationReference> InformationReferences { get; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    IReadOnlyDictionary<string, string> ExtraAttributes { get; }
}

/// <summary>
/// A typed binding from a referring object to an information-type
/// target, resolved from an <c>xlink:href</c> by
/// <see cref="S122MarineProtectedAreaDataset.From"/>.
/// </summary>
/// <param name="Role">The GML role (containing element local name, e.g. <c>theAuthority</c>).</param>
/// <param name="ArcRole">The optional <c>xlink:arcrole</c>.</param>
/// <param name="Target">The resolved typed information type.</param>
public sealed record S122InformationReference(
    string Role,
    string? ArcRole,
    IS122InformationType Target);

/// <summary>
/// A typed binding from a referring object to a feature target,
/// resolved from an <c>xlink:href</c> by
/// <see cref="S122MarineProtectedAreaDataset.From"/>.
/// </summary>
/// <param name="Role">The GML role (containing element local name, e.g. <c>theCartographicText</c>).</param>
/// <param name="ArcRole">The optional <c>xlink:arcrole</c>.</param>
/// <param name="Target">The resolved typed feature.</param>
public sealed record S122FeatureReference(
    string Role,
    string? ArcRole,
    IS122Feature Target);

/// <summary>
/// Common base for the three S-122 RxN (Recommendations / Regulations /
/// Restrictions) concrete information-type projections, which all
/// derive from the FC-abstract <c>AbstractRxN</c> (S-122 FC 2.0.0
/// §AbstractRxN).
/// </summary>
public abstract class S122AbstractRxN : IS122InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public abstract string TypeCode { get; }

    /// <summary>The category-of-authority code (S-122 FC §categoryOfAuthority), when present.</summary>
    public int? CategoryOfAuthority { get; init; }

    /// <summary>The RxN code (S-122 FC §rxNCode), when present.</summary>
    public string? RxNCode { get; init; }

    /// <summary>The text content payload (S-122 FC §textContent), when present.</summary>
    public string? TextContent { get; init; }

    /// <inheritdoc/>
    public IReadOnlyList<GmlReference> References { get; init; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<S122InformationReference> InformationReferences { get; internal set; } =
        [];

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> ExtraAttributes { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>
/// One <c>featureName</c> complex attribute (S-122 FC 2.0.0
/// §featureName): a name in a language, with how it is used on the chart.
/// </summary>
/// <param name="Language">The ISO 639-3 language code (<c>language</c>), when present.</param>
/// <param name="Name">The name (<c>name</c>).</param>
/// <param name="NameUsage">
/// The <c>nameUsage</c> code, when present: 1 default name display,
/// 2 alternate name display, 3 no chart display.
/// </param>
public sealed record S122FeatureName(string? Language, string Name, int? NameUsage)
{
    /// <summary>
    /// The name to show: the first with <see cref="NameUsage"/> 1 (default
    /// name display), else the first that is not marked "no chart display".
    /// </summary>
    /// <param name="names">The names in document order.</param>
    /// <returns>The name, or <c>null</c> when there is none to show.</returns>
    public static string? Preferred(IReadOnlyList<S122FeatureName> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return (names.FirstOrDefault(n => n.NameUsage == 1)
            ?? names.FirstOrDefault(n => n.NameUsage != 3))?.Name;
    }
}

/// <summary>
/// A <c>fixedDateRange</c> or <c>periodicDateRange</c> complex attribute
/// (S-122 FC 2.0.0 §fixedDateRange, §periodicDateRange): start and end
/// dates as <c>S100_TruncatedDate</c> text.
/// </summary>
/// <remarks>
/// Dates keep their source text because a truncated date need not be a
/// full date: a periodic range usually names a day of the year without a
/// year (<c>--03-01</c>). <see cref="Start"/> and <see cref="End"/> give
/// the UTC moments when the text is a full date.
/// </remarks>
/// <param name="DateStart">The <c>dateStart</c> text, when present.</param>
/// <param name="DateEnd">The <c>dateEnd</c> text, when present.</param>
public sealed record S122DateRange(string? DateStart, string? DateEnd)
{
    /// <summary>The start of <see cref="DateStart"/> (UTC) when it is a full date, else <c>null</c>.</summary>
    public DateTimeOffset? Start => S100DateTimes.Combine(DateStart, null);

    /// <summary>The start of <see cref="DateEnd"/> (UTC) when it is a full date, else <c>null</c>.</summary>
    public DateTimeOffset? End => S100DateTimes.Combine(DateEnd, null);
}

/// <summary>
/// Reads the complex attributes the S-122 FC-abstract <c>FeatureType</c>
/// and <c>InformationType</c> share from the reader's attribute bags.
/// </summary>
internal static class S122ComplexAttributes
{
    /// <summary>
    /// The <c>featureName</c> attributes. A plain-text <c>featureName</c>
    /// (as some early datasets write) becomes a name without a language.
    /// Edition 1.0.0's <c>displayName</c> maps to <c>nameUsage</c> 1 or 3.
    /// </summary>
    public static IReadOnlyList<S122FeatureName> FeatureNames(
        IReadOnlyDictionary<string, string> simple,
        IReadOnlyList<S122ComplexAttribute> complex)
    {
        var names = new List<S122FeatureName>();
        foreach (var c in complex)
        {
            if (!string.Equals(c.Code, "featureName", StringComparison.Ordinal)
                || c.SubAttributes.GetValueOrDefault("name") is not { Length: > 0 } name)
                continue;
            int? usage = int.TryParse(c.SubAttributes.GetValueOrDefault("nameUsage"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var u)
                ? u
                : c.SubAttributes.GetValueOrDefault("displayName") is { } display && bool.TryParse(display, out var shown)
                    ? shown ? 1 : 3
                    : null;
            names.Add(new S122FeatureName(c.SubAttributes.GetValueOrDefault("language"), name, usage));
        }

        if (names.Count == 0 && simple.GetValueOrDefault("featureName") is { Length: > 0 } text)
            names.Add(new S122FeatureName(null, text, null));
        return names;
    }

    /// <summary>The first <paramref name="code"/> date range, or <c>null</c>.</summary>
    public static S122DateRange? DateRange(IReadOnlyList<S122ComplexAttribute> complex, string code) =>
        DateRanges(complex, code).FirstOrDefault();

    /// <summary>Every <paramref name="code"/> date range in document order.</summary>
    public static IReadOnlyList<S122DateRange> DateRanges(IReadOnlyList<S122ComplexAttribute> complex, string code) =>
        complex
            .Where(c => string.Equals(c.Code, code, StringComparison.Ordinal))
            .Select(c => new S122DateRange(c.SubAttributes.GetValueOrDefault("dateStart"), c.SubAttributes.GetValueOrDefault("dateEnd")))
            .Where(r => r.DateStart is not null || r.DateEnd is not null)
            .ToArray();
}
