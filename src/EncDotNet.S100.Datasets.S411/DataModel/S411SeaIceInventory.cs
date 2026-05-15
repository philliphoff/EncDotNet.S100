using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S411.DataModel;

/// <summary>
/// Strongly-typed projection of an <see cref="S411Dataset"/> as an inventory
/// of sea-ice and lake-ice features (S-411 Edition 1.2.1).
/// </summary>
/// <remarks>
/// <para>
/// The projection mirrors the S-128 / S-201 pattern: a static
/// <see cref="From"/> factory walks the source feature bag and produces typed
/// subclasses of <see cref="S411IceFeature"/> (<see cref="S411SeaIce"/>,
/// <see cref="S411LakeIce"/>, <see cref="S411Iceberg"/>, <see cref="S411IceEdge"/>,
/// <see cref="S411IceLead"/>, <see cref="S411IceThickness"/>,
/// <see cref="S411SnowCover"/>, <see cref="S411StageOfMelt"/>) with
/// <see cref="S411DataCoverage"/> broken out separately and everything else
/// landing on <see cref="S411OtherFeature"/>.
/// </para>
/// <para>
/// Feature-type normalisation maps the JCOMM lowercase short codes
/// (<c>seaice</c>, <c>lacice</c>, <c>icebrg</c>, <c>icelne</c>, <c>icethk</c>,
/// <c>snwcvr</c>, <c>stgmlt</c>, …) to the canonical PascalCase Feature
/// Catalogue class names (<c>SeaIce</c>, <c>LakeIce</c>, <c>Iceberg</c>,
/// <c>IceEdge</c>, …). Both the JCOMM and IHO sample shapes therefore land on
/// the same typed subclass, and consumers can dispatch on
/// <see cref="S411IceFeature.NormalizedFeatureType"/> without caring which
/// shape the dataset was emitted in.
/// </para>
/// <para>
/// S-411 has no information types and no xlink cross-references, so the
/// projection does not need an <see cref="XlinkResolver"/>. The projection
/// still threads a <see cref="ProjectionContext"/> so attribute-parse
/// failures (<c>"attribute.parse.int"</c>, <c>"attribute.parse.double"</c>)
/// surface as diagnostics rather than exceptions.
/// </para>
/// <para>
/// The projection only throws when the source dataset has no features at all.
/// </para>
/// </remarks>
public sealed class S411SeaIceInventory
{
    /// <summary>The S-411 product identifier (typically <c>"S-411"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The dataset identifier carried by the source GML root element.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>
    /// The dataset's issue / observation timestamp, copied verbatim from
    /// <see cref="S411Dataset.IssueDate"/>.
    /// </summary>
    public DateTime? IssueDate { get; init; }

    /// <summary>
    /// All ice features in the dataset, polymorphic over the typed
    /// <see cref="S411IceFeature"/> subclasses (excluding
    /// <see cref="S411DataCoverage"/>, which has its own list).
    /// </summary>
    public required ImmutableArray<S411IceFeature> IceFeatures { get; init; }

    /// <summary>Data-coverage features describing the area for which the dataset carries ice information.</summary>
    public required ImmutableArray<S411DataCoverage> DataCoverages { get; init; }

    /// <summary>
    /// Features whose feature type was not recognised as one of the
    /// dedicated typed subclasses — they round-trip as
    /// <see cref="S411OtherFeature"/> entries with all attributes preserved
    /// on <see cref="S411IceFeature.ExtraAttributes"/>.
    /// </summary>
    public required ImmutableArray<S411OtherFeature> OtherFeatures { get; init; }

    /// <summary>The originating feature-bag dataset.</summary>
    public required S411Dataset Source { get; init; }

    /// <summary>
    /// Projects a feature-bag <see cref="S411Dataset"/> into the typed data
    /// model. Issues encountered during projection are reported via
    /// <paramref name="diagnostics"/>; the projection only throws when the
    /// source dataset has no features at all.
    /// </summary>
    /// <param name="dataset">The source dataset.</param>
    /// <param name="diagnostics">Receives the projection diagnostics as an immutable snapshot.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="dataset"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">If <paramref name="dataset"/> has no features.</exception>
    public static S411SeaIceInventory From(S411Dataset dataset, out IReadOnlyList<ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.Features.IsDefaultOrEmpty)
            throw new InvalidOperationException("Dataset contains no features.");

        // S-411 carries no information types and no xlinks, so an empty
        // resolver suffices — the context exists purely for attribute-parse
        // diagnostics.
        var ctx = new ProjectionContext(XlinkResolver.Build(Array.Empty<KeyValuePair<string, object>>()));

        var ice = ImmutableArray.CreateBuilder<S411IceFeature>();
        var coverages = ImmutableArray.CreateBuilder<S411DataCoverage>();
        var other = ImmutableArray.CreateBuilder<S411OtherFeature>();

        foreach (var f in dataset.Features)
        {
            var normalised = NormaliseFeatureType(f.FeatureType);
            var geometryKind = ProjectGeometryKind(f.GeometryType);
            var coords = ProjectCoordinates(f);

            switch (normalised)
            {
                case "SeaIce":
                    ice.Add(new S411SeaIce
                    {
                        Id = f.Id,
                        NormalizedFeatureType = normalised,
                        SourceFeatureType = f.FeatureType,
                        GeometryKind = geometryKind,
                        Coordinates = coords,
                        EggCode = BuildEggCode(f, ctx),
                        ExtraAttributes = ExcludeEggCodeAndKnown(f.Attributes),
                        Source = f,
                    });
                    break;

                case "LakeIce":
                    ice.Add(new S411LakeIce
                    {
                        Id = f.Id,
                        NormalizedFeatureType = normalised,
                        SourceFeatureType = f.FeatureType,
                        GeometryKind = geometryKind,
                        Coordinates = coords,
                        EggCode = BuildEggCode(f, ctx),
                        ExtraAttributes = ExcludeEggCodeAndKnown(f.Attributes),
                        Source = f,
                    });
                    break;

                case "Iceberg":
                    ice.Add(new S411Iceberg
                    {
                        Id = f.Id,
                        NormalizedFeatureType = normalised,
                        SourceFeatureType = f.FeatureType,
                        GeometryKind = geometryKind,
                        Coordinates = coords,
                        IcebergSizeCode = AttributeParser.TryParseInt(
                            f.Attributes.GetValueOrDefault("icebergSize"), ctx, f.Id, "icebergSize"),
                        ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "icebergSize"),
                        Source = f,
                    });
                    break;

                case "IceEdge":
                    ice.Add(new S411IceEdge
                    {
                        Id = f.Id,
                        NormalizedFeatureType = normalised,
                        SourceFeatureType = f.FeatureType,
                        GeometryKind = geometryKind,
                        Coordinates = coords,
                        ExtraAttributes = f.Attributes,
                        Source = f,
                    });
                    break;

                case "IceLead":
                    ice.Add(new S411IceLead
                    {
                        Id = f.Id,
                        NormalizedFeatureType = normalised,
                        SourceFeatureType = f.FeatureType,
                        GeometryKind = geometryKind,
                        Coordinates = coords,
                        IceLeadStatusCode = AttributeParser.TryParseInt(
                            f.Attributes.GetValueOrDefault("iceLeadStatus"), ctx, f.Id, "iceLeadStatus"),
                        ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "iceLeadStatus"),
                        Source = f,
                    });
                    break;

                case "IceThickness":
                    ice.Add(new S411IceThickness
                    {
                        Id = f.Id,
                        NormalizedFeatureType = normalised,
                        SourceFeatureType = f.FeatureType,
                        GeometryKind = geometryKind,
                        Coordinates = coords,
                        IceAverageThickness = AttributeParser.TryParseDouble(
                            f.Attributes.GetValueOrDefault("iceAverageThickness"), ctx, f.Id, "iceAverageThickness"),
                        ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "iceAverageThickness"),
                        Source = f,
                    });
                    break;

                case "SnowCover":
                    ice.Add(new S411SnowCover
                    {
                        Id = f.Id,
                        NormalizedFeatureType = normalised,
                        SourceFeatureType = f.FeatureType,
                        GeometryKind = geometryKind,
                        Coordinates = coords,
                        SnowCoverConcentrationCode = AttributeParser.TryParseInt(
                            f.Attributes.GetValueOrDefault("snowCoverConcentration"), ctx, f.Id, "snowCoverConcentration"),
                        ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "snowCoverConcentration"),
                        Source = f,
                    });
                    break;

                case "StageOfMelt":
                    ice.Add(new S411StageOfMelt
                    {
                        Id = f.Id,
                        NormalizedFeatureType = normalised,
                        SourceFeatureType = f.FeatureType,
                        GeometryKind = geometryKind,
                        Coordinates = coords,
                        MeltStageCode = AttributeParser.TryParseInt(
                            f.Attributes.GetValueOrDefault("meltStage"), ctx, f.Id, "meltStage"),
                        ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "meltStage"),
                        Source = f,
                    });
                    break;

                case "DataCoverage":
                    coverages.Add(new S411DataCoverage
                    {
                        Id = f.Id,
                        NormalizedFeatureType = normalised,
                        SourceFeatureType = f.FeatureType,
                        GeometryKind = geometryKind,
                        Coordinates = coords,
                        MinimumDisplayScale = AttributeParser.TryParseInt(
                            f.Attributes.GetValueOrDefault("minimumDisplayScale"), ctx, f.Id, "minimumDisplayScale"),
                        MaximumDisplayScale = AttributeParser.TryParseInt(
                            f.Attributes.GetValueOrDefault("maximumDisplayScale"), ctx, f.Id, "maximumDisplayScale"),
                        ExtraAttributes = ExtraAttributes.ExcludeKnown(
                            f.Attributes, "minimumDisplayScale", "maximumDisplayScale"),
                        Source = f,
                    });
                    break;

                default:
                    other.Add(new S411OtherFeature
                    {
                        Id = f.Id,
                        NormalizedFeatureType = normalised,
                        SourceFeatureType = f.FeatureType,
                        GeometryKind = geometryKind,
                        Coordinates = coords,
                        ExtraAttributes = f.Attributes,
                        Source = f,
                    });
                    break;
            }
        }

        diagnostics = ctx.ToImmutableDiagnostics();
        return new S411SeaIceInventory
        {
            ProductIdentifier = dataset.ProductIdentifier,
            DatasetIdentifier = dataset.DatasetIdentifier,
            IssueDate = dataset.IssueDate,
            IceFeatures = ice.ToImmutable(),
            DataCoverages = coverages.ToImmutable(),
            OtherFeatures = other.ToImmutable(),
            Source = dataset,
        };
    }

    // ── Feature-type normalisation ────────────────────────────────────

    /// <summary>
    /// Maps the JCOMM lowercase short codes to the canonical PascalCase
    /// Feature Catalogue class names. IHO PascalCase names pass through
    /// unchanged.
    /// </summary>
    private static readonly ImmutableDictionary<string, string> ShortCodeMap =
        ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new KeyValuePair<string, string>[]
        {
            new("seaice", "SeaIce"),
            new("lacice", "LakeIce"),
            new("icebrg", "Iceberg"),
            new("brgare", "IcebergArea"),
            new("brglne", "IcebergLimit"),
            new("flobrg", "Floeberg"),
            new("icelne", "IceEdge"),
            new("icelea", "IceLead"),
            new("icethk", "IceThickness"),
            new("icekel", "IceKeelBummock"),
            new("icerdg", "IceRidgeHummock"),
            new("icerft", "IceRafting"),
            new("icefra", "IceFracture"),
            new("iceshr", "IceShear"),
            new("icediv", "IceDivergence"),
            new("icedft", "IceDrift"),
            new("icecom", "IceCompacting"),
            new("snwcvr", "SnowCover"),
            new("stgmlt", "StageOfMelt"),
            new("strptc", "StripsAndPatches"),
            new("jmdbrr", "JammedBrashBarrier"),
            new("i_lead", "LineOfIceLead"),
            new("i_crac", "LineOfIceCrack"),
            new("i_fral", "LineOfIceFracture"),
            new("i_ridg", "LineOfIceRidge"),
            new("i_grhm", "GroundedHummock"),
            new("opnlne", "LimitOfOpenWater"),
            new("lkilne", "LimitOfAllKnownIce"),
        });

    private static string NormaliseFeatureType(string featureType)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        return ShortCodeMap.TryGetValue(featureType, out var pascal)
            ? pascal
            : featureType;
    }

    // ── Egg-code projection ───────────────────────────────────────────

    private static S411EggCode? BuildEggCode(S411Feature f, ProjectionContext ctx)
    {
        // Total concentration: prefer the JCOMM short code when both forms
        // are present, since real-world JCOMM producers occasionally
        // include both for tooling compatibility.
        var totalRaw = f.Attributes.GetValueOrDefault("iceact")
            ?? f.Attributes.GetValueOrDefault("totalConcentration");
        var total = AttributeParser.TryParseInt(totalRaw, ctx, f.Id,
            f.Attributes.ContainsKey("iceact") ? "iceact" : "totalConcentration");

        var partial = f.Attributes.GetValueOrDefault("iceapc");
        var stages = f.Attributes.GetValueOrDefault("icesod");
        var forms = f.Attributes.GetValueOrDefault("iceflz");
        var snow = AttributeParser.TryParseDouble(
            f.Attributes.GetValueOrDefault("snowDepth"), ctx, f.Id, "snowDepth");

        var bundle = new S411EggCode
        {
            TotalConcentration = total,
            PartialConcentrationsRaw = partial,
            StagesOfDevelopmentRaw = stages,
            FormsOfIceRaw = forms,
            SnowDepth = snow,
        };

        return bundle.IsEmpty ? null : bundle;
    }

    private static ImmutableDictionary<string, string> ExcludeEggCodeAndKnown(
        ImmutableDictionary<string, string> source) =>
        ExtraAttributes.ExcludeKnown(
            source,
            "iceact", "iceapc", "icesod", "iceflz",
            "totalConcentration", "snowDepth");

    // ── Geometry projection ───────────────────────────────────────────

    private static S411GeometryKind ProjectGeometryKind(GmlGeometryType type) => type switch
    {
        GmlGeometryType.Point => S411GeometryKind.Point,
        GmlGeometryType.Curve => S411GeometryKind.Curve,
        GmlGeometryType.Surface => S411GeometryKind.Surface,
        _ => S411GeometryKind.None,
    };

    private static ImmutableArray<GeoPosition> ProjectCoordinates(S411Feature f)
    {
        switch (f.GeometryType)
        {
            case GmlGeometryType.Point:
                return f.Points.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Points.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray();

            case GmlGeometryType.Curve:
                if (f.Curves.IsDefaultOrEmpty) return ImmutableArray<GeoPosition>.Empty;
                return f.Curves
                    .SelectMany(c => c)
                    .Select(p => new GeoPosition(p.Latitude, p.Longitude))
                    .ToImmutableArray();

            case GmlGeometryType.Surface:
                return f.ExteriorRing.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.ExteriorRing.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray();

            default:
                return ImmutableArray<GeoPosition>.Empty;
        }
    }
}
