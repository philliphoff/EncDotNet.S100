using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S124.DataModel;

/// <summary>
/// Strongly-typed projection of an <see cref="S124Dataset"/> as a single
/// navigational warning (S-124 Editions 1.0.0 and 2.0.0).
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
    public required IReadOnlyList<S124NavwarnPart> Parts { get; init; }

    /// <summary>References to other warnings (cancellations, supersessions, ...).</summary>
    public IReadOnlyList<S124WarningReference> References { get; init; } =
        [];

    /// <summary>Spatial quality records.</summary>
    public IReadOnlyList<S124SpatialQuality> SpatialQualities { get; init; } =
        [];

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

        if (dataset.Features.Count == 0 && dataset.InformationTypes.Count == 0)
            throw new InvalidOperationException("Dataset contains no features and no information types.");

        var ctx = new ProjectionContext(BuildXlinkResolver(dataset));

        var preamble = ResolvePreamble(dataset, ctx);
        var references = ResolveReferences(dataset, ctx);
        var spatialQualities = ResolveSpatialQualities(dataset, ctx);

        // Index features for xlink-driven resolution.
        var areaFeatures = dataset.Features
            .Where(f => string.Equals(f.FeatureType, "NavwarnAreaAffected", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
        var textFeatures = dataset.Features
            .Where(f => string.Equals(f.FeatureType, "TextPlacement", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

        var parts = new List<S124NavwarnPart>();
        foreach (var f in dataset.Features)
        {
            if (!string.Equals(f.FeatureType, "NavwarnPart", StringComparison.OrdinalIgnoreCase))
                continue;
            parts.Add(ProjectPart(f, ctx));
        }

        diagnostics = ctx.ToDiagnosticsSnapshot();
        return new S124NavigationalWarning
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            Preamble = preamble,
            Parts = parts,
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
        var msiBlock = p.ComplexAttributes.FirstOrDefault(c =>
            string.Equals(c.Code, "messageSeriesIdentifier", StringComparison.OrdinalIgnoreCase));
        var msi = msiBlock is null ? null : ProjectMessageSeriesIdentifier(msiBlock, p.Id, ctx);

        // Edition 2.0.0 gives the general area and title as complex
        // attributes with a text per language; the first is used.
        var generalArea = p.Attributes.GetValueOrDefault("generalArea")
            ?? Complex(p, "generalArea")?.SubAttributes.GetValueOrDefault("locationName.text");
        var title = p.Attributes.GetValueOrDefault("title")
            ?? Complex(p, "navwarnTitle")?.SubAttributes.GetValueOrDefault("text");

        return new S124NavwarnPreamble
        {
            Id = p.Id,
            MessageSeriesIdentifier = msi,
            GeneralArea = generalArea,
            Locality = p.Attributes.GetValueOrDefault("locality"),
            Title = title,
            NavareaCode = p.Attributes.GetValueOrDefault("NAVAREA")
                ?? p.Attributes.GetValueOrDefault("navarea"),
            Navtex = p.Attributes.GetValueOrDefault("NAVTEX")
                ?? p.Attributes.GetValueOrDefault("navtex"),
            PromulgatingAuthority = p.Attributes.GetValueOrDefault("promulgatingAuthority"),
            PublicationTime = AttributeParser.TryParseDateTimeOffset(p.Attributes.GetValueOrDefault("publicationTime"), ctx, p.Id, "publicationTime"),
            CancellationDate = AttributeParser.TryParseDateTimeOffset(p.Attributes.GetValueOrDefault("cancellationDate"), ctx, p.Id, "cancellationDate"),
            NavwarnTypeGeneral = Code(p.Attributes, "navwarnTypeGeneral", ctx, p.Id),
            NavwarnTypeGeneralName = p.Attributes.GetValueOrDefault("navwarnTypeGeneral") is { Length: > 0 } general
                && !int.TryParse(general, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? general.Trim()
                    : null,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(p.Attributes,
                "generalArea", "locality", "title", "NAVAREA", "navarea",
                "NAVTEX", "navtex", "promulgatingAuthority", "publicationTime",
                "cancellationDate", "navwarnTypeGeneral", "navwarnTypeGeneral.code"),
        };
    }

    private static S124MessageSeriesIdentifier ProjectMessageSeriesIdentifier(S124ComplexAttribute block, string relatedId, ProjectionContext ctx)
    {
        var sub = block.SubAttributes;
        return new S124MessageSeriesIdentifier
        {
            WarningNumber = AttributeParser.TryParseInt(sub.GetValueOrDefault("warningNumber"), ctx, relatedId, "warningNumber"),
            Year = AttributeParser.TryParseInt(sub.GetValueOrDefault("year"), ctx, relatedId, "year"),
            ProductionAgency = sub.GetValueOrDefault("productionAgency") ?? sub.GetValueOrDefault("agencyResponsibleForProduction"),
            NameOfSeries = sub.GetValueOrDefault("nameOfSeries"),
            Country = sub.GetValueOrDefault("country") ?? sub.GetValueOrDefault("nationality"),
            WarningType = Code(sub, "warningType", ctx, relatedId),
            InteroperabilityIdentifier = sub.GetValueOrDefault("interoperabilityIdentifier"),
        };
    }

    private static S124ComplexAttribute? Complex(S124InformationType info, string code) =>
        info.ComplexAttributes.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// An enumeration or code list value: its <c>code</c> attribute
    /// (Edition 2.0.0, kept as <c>"&lt;name&gt;.code"</c>), else the value
    /// itself (Edition 1.0.0 writes the number).
    /// </summary>
    private static int? Code(IReadOnlyDictionary<string, string> attributes, string name, ProjectionContext ctx, string relatedId) =>
        attributes.TryGetValue(name + ".code", out var code)
            ? AttributeParser.TryParseInt(code, ctx, relatedId, name)
            : AttributeParser.TryParseInt(attributes.GetValueOrDefault(name), ctx, relatedId, name);

    private static IReadOnlyList<S124WarningReference> ResolveReferences(S124Dataset dataset, ProjectionContext ctx)
    {
        var b = new List<S124WarningReference>();
        foreach (var i in dataset.InformationTypes)
        {
            if (!string.Equals(i.TypeCode, "References", StringComparison.OrdinalIgnoreCase))
                continue;
            b.Add(new S124WarningReference
            {
                Id = i.Id,
                ReferenceCategory = Code(i.Attributes, "referenceCategory", ctx, i.Id),
                MessageReference = i.Attributes.GetValueOrDefault("messageReference"),
                Warnings = i.ComplexAttributes
                    .Where(c => string.Equals(c.Code, "messageSeriesIdentifier", StringComparison.OrdinalIgnoreCase))
                    .Select(c => ProjectMessageSeriesIdentifier(c, i.Id, ctx))
                    .ToArray(),
                NoMessageOnHand = AttributeParser.TryParseBool(i.Attributes.GetValueOrDefault("noMessageOnHand"), ctx, i.Id, "noMessageOnHand"),
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    "referenceCategory", "referenceCategory.code", "messageReference", "noMessageOnHand"),
            });
        }
        return b;
    }

    private static IReadOnlyList<S124SpatialQuality> ResolveSpatialQualities(S124Dataset dataset, ProjectionContext ctx)
    {
        var b = new List<S124SpatialQuality>();
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
        return b;
    }

    private static S124NavwarnPart ProjectPart(S124Feature f, ProjectionContext ctx)
    {
        // warningInformation/information text from the complex attribute payload:
        // the text itself in Edition 1.0.0, information/text in 2.0.0, which also
        // gives the warning type (navwarnTypeDetails, a code list).
        string? warningText = null;
        string? navwarnType = null;
        int? navwarnTypeCode = null;
        var information = new List<S124WarningText>();
        var winfo = f.ComplexAttributes.FirstOrDefault(c =>
            string.Equals(c.Code, "warningInformation", StringComparison.OrdinalIgnoreCase));
        if (winfo is not null)
        {
            warningText = winfo.SubAttributes.GetValueOrDefault("information.text")
                ?? winfo.SubAttributes.GetValueOrDefault("information");
            foreach (var group in (winfo as S124ComplexAttribute)?.Groups ?? [])
            {
                if (string.Equals(group.Code, "information", StringComparison.OrdinalIgnoreCase)
                    && group.SubAttributes.GetValueOrDefault("text") is { } text)
                {
                    information.Add(new S124WarningText(
                        group.SubAttributes.GetValueOrDefault("language") is { Length: > 0 } language ? language.Trim() : null,
                        text));
                }
            }
            if (information.Count == 0 && warningText is not null)
                information.Add(new S124WarningText(null, warningText));
            navwarnType = winfo.SubAttributes.GetValueOrDefault("navwarnTypeDetails") is { Length: > 0 } type ? type.Trim() : null;
            navwarnTypeCode = int.TryParse(winfo.SubAttributes.GetValueOrDefault("navwarnTypeDetails.code"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
                ? code
                : null;
        }

        var (kind, coords) = ProjectGeometry(f);

        // Resolve associated areas and text placements via xlinks.
        var areas = new List<S124AffectedArea>();
        var texts = new List<S124TextPlacement>();
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
            NavwarnType = navwarnType,
            NavwarnTypeCode = navwarnTypeCode,
            Id = f.Id,
            Restriction = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("restriction"), ctx, f.Id, "restriction"),
            Category = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("category"), ctx, f.Id, "category"),
            ValidityRanges = ProjectValidity(f, ctx),
            WarningInformation = warningText,
            Information = information,
            AffectedAreas = areas,
            TextPlacements = texts,
            GeometryKind = kind,
            Coordinates = coords,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "restriction", "category"),
        };
    }

    private static IReadOnlyList<S124DateRange> ProjectValidity(S124Feature f, ProjectionContext ctx)
    {
        var ranges = new List<S124DateRange>();
        foreach (var range in f.ComplexAttributes.Where(c => string.Equals(c.Code, "fixedDateRange", StringComparison.OrdinalIgnoreCase)))
        {
            var sub = range.SubAttributes;
            var start = Moment(sub, "dateStart", "timeOfDayStart", ctx, f.Id);
            var end = Moment(sub, "dateEnd", "timeOfDayEnd", ctx, f.Id);
            if (start is not null || end is not null)
                ranges.Add(new S124DateRange(start, end));
        }

        return ranges;
    }

    // Edition 2.0.0: <dateStart><S100:date>…</S100:date></dateStart> plus an
    // optional timeOfDayStart (S-100 Time, UTC). Edition 1.0.0: the date or
    // date-time as the element's text.
    private static DateTimeOffset? Moment(IReadOnlyDictionary<string, string> sub, string dateName, string timeName, ProjectionContext ctx, string relatedId)
    {
        var date = sub.GetValueOrDefault(dateName) ?? sub.GetValueOrDefault(dateName + ".date");
        if (string.IsNullOrWhiteSpace(date))
            return null;
        var time = sub.GetValueOrDefault(timeName);
        if (S100DateTimes.Combine(date, time) is { } moment)
            return moment;

        ctx.Warn(
            $"Could not parse '{date}'{(time is null ? "" : $" '{time}'")} as a date and time of day.",
            code: "attribute.parse.datetime",
            relatedId: relatedId,
            relatedAttribute: dateName);
        return null;
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
        if (f.Points.Count > 0)
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

    private static (S124GeometryKind, IReadOnlyList<GeoPosition>) ProjectGeometry(S124Feature f)
    {
        switch (f.GeometryType)
        {
            case S100GeometryType.Point:
                return (S124GeometryKind.Point, f.Points.Count == 0
                    ? []
                    : f.Points.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToArray());
            case S100GeometryType.Curve:
                return (S124GeometryKind.Curve, f.Curves.Count == 0
                    ? []
                    : f.Curves.SelectMany(c => c).Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToArray());
            case S100GeometryType.Surface:
                return (S124GeometryKind.Surface, f.ExteriorRing.Count == 0
                    ? []
                    : f.ExteriorRing.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToArray());
            default:
                return (S124GeometryKind.None, []);
        }
    }
}
