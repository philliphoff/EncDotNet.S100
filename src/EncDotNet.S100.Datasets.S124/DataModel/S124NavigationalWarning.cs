using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S124.DataModel;

/// <summary>
/// Strongly-typed projection of an <see cref="S124Dataset"/> as a single
/// navigational warning (S-124 Edition 1.0.0).
/// </summary>
/// <remarks>
/// <para>
/// The S-124 GML profile carries one navigational warning per dataset:
/// a single <c>NavwarnPreamble</c> information type plus one or more
/// <c>NavwarnPart</c> features, optionally referencing
/// <c>NavwarnAreaAffected</c> and <c>TextPlacement</c> features through
/// the FC associations <c>areaAffected</c> and <c>TextAssociation</c>.
/// </para>
/// <para>
/// Projection issues — duplicate or missing preamble, unresolved xlinks,
/// attribute parse failures — surface as
/// <see cref="ProjectionDiagnostic"/> entries rather than exceptions.
/// The projection only throws when the source dataset has no features
/// and no information types (i.e. is fully empty).
/// </para>
/// </remarks>
public sealed class S124NavigationalWarning
{
    /// <summary>The dataset identifier carried by the source GML <c>DataSet</c> element.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>The S-124 product identifier (typically <c>"S-124"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The preamble for this warning, or <c>null</c> if absent.</summary>
    public S124NavwarnPreamble? Preamble { get; init; }

    /// <summary>The parts that make up this warning.</summary>
    public required ImmutableArray<S124NavwarnPart> Parts { get; init; }

    /// <summary>References to other warnings (cancellations, supersessions, ...).</summary>
    public ImmutableArray<S124WarningReference> References { get; init; } =
        ImmutableArray<S124WarningReference>.Empty;

    /// <summary>Spatial quality records.</summary>
    public ImmutableArray<S124SpatialQuality> SpatialQualities { get; init; } =
        ImmutableArray<S124SpatialQuality>.Empty;

    /// <summary>The originating feature-bag dataset.</summary>
    public required S124Dataset Source { get; init; }

    /// <summary>
    /// Projects a feature-bag <see cref="S124Dataset"/> into the typed
    /// data model. Issues encountered during projection are reported
    /// via <paramref name="diagnostics"/>; the projection only throws
    /// for a fully empty dataset.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the dataset contains neither features nor information
    /// types.
    /// </exception>
    public static S124NavigationalWarning From(S124Dataset dataset, out IReadOnlyList<ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.Features.IsDefaultOrEmpty && dataset.InformationTypes.IsDefaultOrEmpty)
            throw new InvalidOperationException("Dataset contains no features and no information types.");

        var ctx = new ProjectionContext(BuildXlinkResolver(dataset));

        var preamble = ResolvePreamble(dataset, ctx);
        var references = ResolveReferences(dataset, ctx);
        var spatialQualities = ResolveSpatialQualities(dataset, ctx);

        // Index features for xlink-driven resolution.
        var areaFeatures = dataset.Features
            .Where(f => string.Equals(f.FeatureType, "NavwarnAreaAffected", StringComparison.OrdinalIgnoreCase))
            .ToImmutableDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
        var textFeatures = dataset.Features
            .Where(f => string.Equals(f.FeatureType, "TextPlacement", StringComparison.OrdinalIgnoreCase))
            .ToImmutableDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

        var parts = ImmutableArray.CreateBuilder<S124NavwarnPart>();
        foreach (var f in dataset.Features)
        {
            if (!string.Equals(f.FeatureType, "NavwarnPart", StringComparison.OrdinalIgnoreCase))
                continue;
            parts.Add(ProjectPart(f, ctx));
        }

        diagnostics = ctx.ToImmutableDiagnostics();
        return new S124NavigationalWarning
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            Preamble = preamble,
            Parts = parts.ToImmutable(),
            References = references,
            SpatialQualities = spatialQualities,
            Source = dataset,
        };
    }

    private static XlinkResolver BuildXlinkResolver(S124Dataset dataset)
    {
        IEnumerable<KeyValuePair<string, object>> All()
        {
            foreach (var f in dataset.Features)
                yield return new KeyValuePair<string, object>(f.Id, f);
            foreach (var i in dataset.InformationTypes)
                yield return new KeyValuePair<string, object>(i.Id, i);
        }
        return XlinkResolver.Build(All());
    }

    private static S124NavwarnPreamble? ResolvePreamble(S124Dataset dataset, ProjectionContext ctx)
    {
        var matches = dataset.InformationTypes
            .Where(i => string.Equals(i.TypeCode, "NavwarnPreamble", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0) return null;
        if (matches.Count > 1)
        {
            ctx.Report(new ProjectionDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Message = $"Dataset contains {matches.Count} NavwarnPreamble information types; using the first.",
                Code = "feature.duplicate",
                RelatedId = matches[0].Id,
            });
        }
        return ProjectPreamble(matches[0], ctx);
    }

    private static S124NavwarnPreamble ProjectPreamble(S124InformationType p, ProjectionContext ctx)
    {
        S124MessageSeriesIdentifier? msi = null;
        var msiBlock = p.ComplexAttributes.FirstOrDefault(c =>
            string.Equals(c.Code, "messageSeriesIdentifier", StringComparison.OrdinalIgnoreCase));
        if (msiBlock is not null)
        {
            var sub = msiBlock.SubAttributes;
            msi = new S124MessageSeriesIdentifier
            {
                WarningNumber = AttributeParser.TryParseInt(sub.GetValueOrDefault("warningNumber"), ctx, p.Id, "warningNumber"),
                Year = AttributeParser.TryParseInt(sub.GetValueOrDefault("year"), ctx, p.Id, "year"),
                ProductionAgency = sub.GetValueOrDefault("productionAgency"),
                NameOfSeries = sub.GetValueOrDefault("nameOfSeries"),
                Country = sub.GetValueOrDefault("country"),
            };
        }

        return new S124NavwarnPreamble
        {
            Id = p.Id,
            MessageSeriesIdentifier = msi,
            GeneralArea = p.Attributes.GetValueOrDefault("generalArea"),
            Locality = p.Attributes.GetValueOrDefault("locality"),
            Title = p.Attributes.GetValueOrDefault("title"),
            NavareaCode = p.Attributes.GetValueOrDefault("NAVAREA")
                ?? p.Attributes.GetValueOrDefault("navarea"),
            Navtex = p.Attributes.GetValueOrDefault("NAVTEX")
                ?? p.Attributes.GetValueOrDefault("navtex"),
            PromulgatingAuthority = p.Attributes.GetValueOrDefault("promulgatingAuthority"),
            ExtraAttributes = ExtraAttributes.ExcludeKnown(p.Attributes,
                "generalArea", "locality", "title", "NAVAREA", "navarea",
                "NAVTEX", "navtex", "promulgatingAuthority"),
        };
    }

    private static ImmutableArray<S124WarningReference> ResolveReferences(S124Dataset dataset, ProjectionContext ctx)
    {
        var b = ImmutableArray.CreateBuilder<S124WarningReference>();
        foreach (var i in dataset.InformationTypes)
        {
            if (!string.Equals(i.TypeCode, "References", StringComparison.OrdinalIgnoreCase))
                continue;
            b.Add(new S124WarningReference
            {
                Id = i.Id,
                ReferenceCategory = AttributeParser.TryParseInt(
                    i.Attributes.GetValueOrDefault("referenceCategory"), ctx, i.Id, "referenceCategory"),
                MessageReference = i.Attributes.GetValueOrDefault("messageReference"),
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    "referenceCategory", "messageReference"),
            });
        }
        return b.ToImmutable();
    }

    private static ImmutableArray<S124SpatialQuality> ResolveSpatialQualities(S124Dataset dataset, ProjectionContext ctx)
    {
        var b = ImmutableArray.CreateBuilder<S124SpatialQuality>();
        foreach (var i in dataset.InformationTypes)
        {
            if (!string.Equals(i.TypeCode, "SpatialQuality", StringComparison.OrdinalIgnoreCase))
                continue;
            b.Add(new S124SpatialQuality
            {
                Id = i.Id,
                QualityOfPosition = AttributeParser.TryParseInt(
                    i.Attributes.GetValueOrDefault("qualityOfPosition"), ctx, i.Id, "qualityOfPosition"),
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes, "qualityOfPosition"),
            });
        }
        return b.ToImmutable();
    }

    private static S124NavwarnPart ProjectPart(S124Feature f, ProjectionContext ctx)
    {
        // warningInformation/information text from the complex attribute payload.
        string? warningText = null;
        var winfo = f.ComplexAttributes.FirstOrDefault(c =>
            string.Equals(c.Code, "warningInformation", StringComparison.OrdinalIgnoreCase));
        if (winfo is not null)
            warningText = winfo.SubAttributes.GetValueOrDefault("information");

        var (kind, coords) = ProjectGeometry(f);

        // Resolve associated areas and text placements via xlinks.
        var areas = ImmutableArray.CreateBuilder<S124AffectedArea>();
        var texts = ImmutableArray.CreateBuilder<S124TextPlacement>();
        foreach (var r in f.References)
        {
            // Roles surfaced by S-124 FC associations: areaAffected, theCartographicText, etc.
            // Resolve any references that target Area or Text features regardless of role spelling.
            var target = ctx.Xlinks.ResolveAny(r.Href, r.Role ?? string.Empty, ctx, f.Id);
            switch (target)
            {
                case S124Feature area when string.Equals(area.FeatureType, "NavwarnAreaAffected", StringComparison.OrdinalIgnoreCase):
                    areas.Add(ProjectAffectedArea(area, ctx));
                    break;
                case S124Feature tp when string.Equals(tp.FeatureType, "TextPlacement", StringComparison.OrdinalIgnoreCase):
                    texts.Add(ProjectTextPlacement(tp, ctx));
                    break;
            }
        }

        return new S124NavwarnPart
        {
            Id = f.Id,
            Restriction = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("restriction"), ctx, f.Id, "restriction"),
            Category = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("category"), ctx, f.Id, "category"),
            WarningInformation = warningText,
            AffectedAreas = areas.ToImmutable(),
            TextPlacements = texts.ToImmutable(),
            GeometryKind = kind,
            Coordinates = coords,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "restriction", "category"),
        };
    }

    private static S124AffectedArea ProjectAffectedArea(S124Feature f, ProjectionContext ctx)
    {
        var (kind, coords) = ProjectGeometry(f);
        return new S124AffectedArea
        {
            Id = f.Id,
            Restriction = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("restriction"), ctx, f.Id, "restriction"),
            GeometryKind = kind,
            Coordinates = coords,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "restriction"),
        };
    }

    private static S124TextPlacement ProjectTextPlacement(S124Feature f, ProjectionContext ctx)
    {
        GeoPosition? position = null;
        if (!f.Points.IsDefaultOrEmpty)
        {
            var (lat, lon) = f.Points[0];
            position = new GeoPosition(lat, lon);
        }
        // The text is carried either as a simple attribute "text" or inside
        // a "textContent" complex attribute. Real-world S-124 fixtures vary;
        // try both shapes before giving up.
        var text = f.Attributes.GetValueOrDefault("text");
        if (text is null)
        {
            var ca = f.ComplexAttributes.FirstOrDefault(c =>
                string.Equals(c.Code, "textContent", StringComparison.OrdinalIgnoreCase));
            if (ca is not null)
                text = ca.SubAttributes.GetValueOrDefault("text")
                    ?? ca.SubAttributes.Values.FirstOrDefault();
        }

        return new S124TextPlacement
        {
            Id = f.Id,
            Position = position,
            Text = text,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "text"),
        };
    }

    private static (S124GeometryKind, ImmutableArray<GeoPosition>) ProjectGeometry(S124Feature f)
    {
        switch (f.GeometryType)
        {
            case GmlGeometryType.Point:
                return (S124GeometryKind.Point, f.Points.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Points.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            case GmlGeometryType.Curve:
                return (S124GeometryKind.Curve, f.Curves.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Curves.SelectMany(c => c).Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            case GmlGeometryType.Surface:
                return (S124GeometryKind.Surface, f.ExteriorRing.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.ExteriorRing.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            default:
                return (S124GeometryKind.None, ImmutableArray<GeoPosition>.Empty);
        }
    }
}
