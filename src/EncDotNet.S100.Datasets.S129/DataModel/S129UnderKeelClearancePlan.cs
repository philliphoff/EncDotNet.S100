using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S129.DataModel;

/// <summary>
/// Strongly-typed projection of an <see cref="S129Dataset"/> as a single
/// Under Keel Clearance Management plan (S-129 Edition 2.0.0).
/// </summary>
/// <remarks>
/// <para>
/// An S-129 dataset carries one UKC plan per file: a single
/// <c>UnderKeelClearancePlan</c> metadata feature, a single
/// <c>UnderKeelClearancePlanArea</c> surface feature describing the
/// plan's spatial extent, zero or more <c>UnderKeelClearance*Area</c>
/// surfaces marking non-navigable / almost-non-navigable regions, and an
/// ordered sequence of <c>UnderKeelClearanceControlPoint</c> features
/// carrying the per-waypoint UKC time-step measurements.
/// </para>
/// <para>
/// This typed projection surfaces all of the above as strongly-typed
/// records, with control points ordered by
/// <see cref="S129ControlPoint.ExpectedPassingTime"/> (stable; gaps are
/// preserved — per S-129 expert checklist, the typed model does not
/// interpolate across explicit gaps declared by the producer).
/// </para>
/// <para>
/// Cross-product links — to the source S-421 route, to the S-102
/// bathymetry the producer used as input, and to the S-104 water-level
/// forecast — are <em>textual</em> in S-129 Edition 2.0.0: the producer
/// records identifiers (<c>vesselID</c>, <c>sourceRouteName</c> /
/// <c>sourceRouteVersion</c>) rather than GML <c>xlink:href</c> URLs.
/// They are surfaced as <see cref="S129ExternalReference"/> values with
/// no eager resolution; downstream code is free to resolve them
/// against its own catalogue.
/// </para>
/// <para>
/// Projection issues — duplicate plan / plan-area features, attribute
/// parse failures, unresolved xlinks — surface as
/// <see cref="ProjectionDiagnostic"/> entries rather than exceptions.
/// The projection only throws when the source dataset is fully empty
/// (no features).
/// </para>
/// </remarks>
public sealed class S129UnderKeelClearancePlan
{
    /// <summary>The dataset identifier carried by the source GML <c>Dataset</c> element.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>The S-129 product identifier (typically <c>"S-129"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The plan metadata header, or <c>null</c> if absent.</summary>
    public S129UkcPlanMetadata? Plan { get; init; }

    /// <summary>The plan area surface, or <c>null</c> if absent.</summary>
    public S129UkcPlanArea? PlanArea { get; init; }

    /// <summary>Non-navigable surface features.</summary>
    public ImmutableArray<S129NonNavigableArea> NonNavigableAreas { get; init; } =
        ImmutableArray<S129NonNavigableArea>.Empty;

    /// <summary>Almost-non-navigable surface features.</summary>
    public ImmutableArray<S129AlmostNonNavigableArea> AlmostNonNavigableAreas { get; init; } =
        ImmutableArray<S129AlmostNonNavigableArea>.Empty;

    /// <summary>
    /// Control points carrying the per-waypoint UKC time-step measurements,
    /// ordered by <see cref="S129ControlPoint.ExpectedPassingTime"/> (control
    /// points with no expected-passing-time are sorted last, in their
    /// source-document order).
    /// </summary>
    public ImmutableArray<S129ControlPoint> ControlPoints { get; init; } =
        ImmutableArray<S129ControlPoint>.Empty;

    /// <summary>The originating feature-bag dataset.</summary>
    public required S129Dataset Source { get; init; }

    /// <summary>
    /// Projects a feature-bag <see cref="S129Dataset"/> into the typed data
    /// model. Issues encountered during projection are reported via
    /// <paramref name="diagnostics"/>; the projection only throws for a
    /// fully empty dataset.
    /// </summary>
    /// <param name="dataset">The source dataset.</param>
    /// <param name="diagnostics">Out parameter receiving the projection diagnostics.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <paramref name="dataset"/> contains no features.
    /// </exception>
    public static S129UnderKeelClearancePlan From(
        S129Dataset dataset,
        out IReadOnlyList<ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.Features.IsDefaultOrEmpty)
            throw new InvalidOperationException("Dataset contains no features.");

        var ctx = new ProjectionContext(BuildXlinkResolver(dataset));

        S129UkcPlanMetadata? plan = null;
        S129UkcPlanArea? planArea = null;
        var nonNav = ImmutableArray.CreateBuilder<S129NonNavigableArea>();
        var almostNonNav = ImmutableArray.CreateBuilder<S129AlmostNonNavigableArea>();
        var controlPoints = ImmutableArray.CreateBuilder<S129ControlPoint>();

        foreach (var f in dataset.Features)
        {
            switch (f.FeatureType)
            {
                case var t when t.Equals("UnderKeelClearancePlan", StringComparison.OrdinalIgnoreCase):
                    if (plan is not null)
                        ctx.Warn(
                            "Dataset contains more than one UnderKeelClearancePlan feature; using the first.",
                            code: "feature.duplicate",
                            relatedId: f.Id);
                    else
                        plan = ProjectPlanMetadata(f, ctx);
                    break;

                case var t when t.Equals("UnderKeelClearancePlanArea", StringComparison.OrdinalIgnoreCase):
                    if (planArea is not null)
                        ctx.Warn(
                            "Dataset contains more than one UnderKeelClearancePlanArea feature; using the first.",
                            code: "feature.duplicate",
                            relatedId: f.Id);
                    else
                        planArea = ProjectPlanArea(f, ctx);
                    break;

                case var t when t.Equals("UnderKeelClearanceNonNavigableArea", StringComparison.OrdinalIgnoreCase):
                    nonNav.Add(ProjectNonNavigable(f, ctx));
                    break;

                case var t when t.Equals("UnderKeelClearanceAlmostNonNavigableArea", StringComparison.OrdinalIgnoreCase):
                    almostNonNav.Add(ProjectAlmostNonNavigable(f, ctx));
                    break;

                case var t when t.Equals("UnderKeelClearanceControlPoint", StringComparison.OrdinalIgnoreCase):
                    controlPoints.Add(ProjectControlPoint(f, ctx));
                    break;

                default:
                    // Unknown feature type — preserved on .Source for
                    // forward compatibility.
                    break;
            }
        }

        // Resolve any xlink references carried on features. Current S-129
        // 2.0.0 fixtures do not emit xlinks, but the resolver is wired
        // up so that future producer extensions or tier-1 cross-spec
        // binding work surfaces unresolved-target diagnostics through
        // the same channel as the other typed-model projections.
        foreach (var f in dataset.Features)
        {
            foreach (var r in f.References)
            {
                ctx.Xlinks.ResolveAny(r.Href, r.Role, ctx, f.Id);
            }
        }

        // Sort control points by expected passing time (stable). CPs with
        // no time stay in source-document order at the tail.
        var orderedControlPoints = controlPoints
            .ToImmutable()
            .Sort(static (a, b) =>
            {
                if (a.ExpectedPassingTime is null && b.ExpectedPassingTime is null) return 0;
                if (a.ExpectedPassingTime is null) return 1;
                if (b.ExpectedPassingTime is null) return -1;
                return a.ExpectedPassingTime.Value.CompareTo(b.ExpectedPassingTime.Value);
            });

        diagnostics = ctx.ToImmutableDiagnostics();
        return new S129UnderKeelClearancePlan
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            Plan = plan,
            PlanArea = planArea,
            NonNavigableAreas = nonNav.ToImmutable(),
            AlmostNonNavigableAreas = almostNonNav.ToImmutable(),
            ControlPoints = orderedControlPoints,
            Source = dataset,
        };
    }

    private static XlinkResolver BuildXlinkResolver(S129Dataset dataset)
    {
        IEnumerable<KeyValuePair<string, object>> All()
        {
            foreach (var f in dataset.Features)
                if (!string.IsNullOrEmpty(f.Id))
                    yield return new KeyValuePair<string, object>(f.Id, f);
        }
        return XlinkResolver.Build(All());
    }

    // ── Per-feature projection ─────────────────────────────────────────

    private static S129UkcPlanMetadata ProjectPlanMetadata(S129Feature f, ProjectionContext ctx)
    {
        S129TimeRange? timeRange = ExtractTimeRange(f, "fixedTimeRange");

        S129ExternalReference? sourceRoute = null;
        if (f.Attributes.TryGetValue("sourceRouteName", out var routeName) && !string.IsNullOrEmpty(routeName))
        {
            sourceRoute = new S129ExternalReference
            {
                Kind = "S-421 route",
                Identifier = routeName,
                Version = f.Attributes.GetValueOrDefault("sourceRouteVersion"),
            };
        }

        return new S129UkcPlanMetadata
        {
            Id = f.Id,
            FixedTimeRange = timeRange,
            GenerationTime = AttributeParser.TryParseDateTimeOffset(
                f.Attributes.GetValueOrDefault("generationTime"), ctx, f.Id, "generationTime"),
            VesselId = f.Attributes.GetValueOrDefault("vesselID"),
            SourceRoute = sourceRoute,
            MaximumDraught = AttributeParser.TryParseDouble(
                f.Attributes.GetValueOrDefault("maximumDraught"), ctx, f.Id, "maximumDraught"),
            UnderKeelClearancePurpose = f.Attributes.GetValueOrDefault("underKeelClearancePurpose"),
            UnderKeelClearanceCalculationRequested =
                f.Attributes.GetValueOrDefault("underKeelClearanceCalculationRequested"),
            ExtraAttributes = ExtraAttributes.ExcludeKnown(
                f.Attributes,
                "generationTime", "vesselID", "sourceRouteName", "sourceRouteVersion",
                "maximumDraught", "underKeelClearancePurpose",
                "underKeelClearanceCalculationRequested"),
        };
    }

    private static S129UkcPlanArea ProjectPlanArea(S129Feature f, ProjectionContext ctx)
    {
        var (kind, coords, holes) = ProjectGeometry(f, ctx);
        return new S129UkcPlanArea
        {
            Id = f.Id,
            GeometryKind = kind,
            Coordinates = coords,
            InteriorRings = holes,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes),
        };
    }

    private static S129NonNavigableArea ProjectNonNavigable(S129Feature f, ProjectionContext ctx)
    {
        var (kind, coords, holes) = ProjectGeometry(f, ctx);
        return new S129NonNavigableArea
        {
            Id = f.Id,
            ScaleMinimum = AttributeParser.TryParseInt(
                f.Attributes.GetValueOrDefault("scaleMinimum"), ctx, f.Id, "scaleMinimum"),
            GeometryKind = kind,
            Coordinates = coords,
            InteriorRings = holes,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "scaleMinimum"),
        };
    }

    private static S129AlmostNonNavigableArea ProjectAlmostNonNavigable(S129Feature f, ProjectionContext ctx)
    {
        var (kind, coords, holes) = ProjectGeometry(f, ctx);
        return new S129AlmostNonNavigableArea
        {
            Id = f.Id,
            ScaleMinimum = AttributeParser.TryParseInt(
                f.Attributes.GetValueOrDefault("scaleMinimum"), ctx, f.Id, "scaleMinimum"),
            GeometryKind = kind,
            Coordinates = coords,
            InteriorRings = holes,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "scaleMinimum"),
        };
    }

    private static S129ControlPoint ProjectControlPoint(S129Feature f, ProjectionContext ctx)
    {
        S129FeatureName? name = null;
        var nameBlock = f.ComplexAttributes.FirstOrDefault(c =>
            string.Equals(c.Code, "featureName", StringComparison.OrdinalIgnoreCase));
        if (nameBlock is not null)
        {
            var sub = nameBlock.SubAttributes;
            name = new S129FeatureName
            {
                Language = sub.GetValueOrDefault("language"),
                Name = sub.GetValueOrDefault("name"),
                NameUsage = sub.GetValueOrDefault("nameUsage"),
            };
        }

        var timeRange = ExtractTimeRange(f, "fixedTimeRange");

        GeoPosition? position = null;
        if (!f.Points.IsDefaultOrEmpty)
        {
            var (lat, lon) = f.Points[0];
            position = new GeoPosition(lat, lon);
        }

        return new S129ControlPoint
        {
            Id = f.Id,
            FeatureName = name,
            ExpectedPassingTime = AttributeParser.TryParseDateTimeOffset(
                f.Attributes.GetValueOrDefault("expectedPassingTime"), ctx, f.Id, "expectedPassingTime"),
            ExpectedPassingSpeed = AttributeParser.TryParseDouble(
                f.Attributes.GetValueOrDefault("expectedPassingSpeed"), ctx, f.Id, "expectedPassingSpeed"),
            DistanceAboveUkcLimit = AttributeParser.TryParseDouble(
                f.Attributes.GetValueOrDefault("distanceAboveUKCLimit"), ctx, f.Id, "distanceAboveUKCLimit"),
            FixedTimeRange = timeRange,
            Position = position,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(
                f.Attributes,
                "expectedPassingTime", "expectedPassingSpeed", "distanceAboveUKCLimit"),
        };
    }

    private static S129TimeRange? ExtractTimeRange(S129Feature f, string complexCode)
    {
        var block = f.ComplexAttributes.FirstOrDefault(c =>
            string.Equals(c.Code, complexCode, StringComparison.OrdinalIgnoreCase));
        if (block is null) return null;

        var sub = block.SubAttributes;
        var startText = sub.GetValueOrDefault("timeStart");
        var endText = sub.GetValueOrDefault("timeEnd");
        if (string.IsNullOrEmpty(startText) && string.IsNullOrEmpty(endText))
            return null;

        DateTimeOffset? start = null;
        DateTimeOffset? end = null;
        if (!string.IsNullOrEmpty(startText) &&
            DateTimeOffset.TryParse(
                startText,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var s))
        {
            start = s;
        }
        if (!string.IsNullOrEmpty(endText) &&
            DateTimeOffset.TryParse(
                endText,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var e))
        {
            end = e;
        }
        return new S129TimeRange { Start = start, End = end };
    }

    private static (S129GeometryKind, ImmutableArray<GeoPosition>, ImmutableArray<ImmutableArray<GeoPosition>>)
        ProjectGeometry(S129Feature f, ProjectionContext ctx)
    {
        switch (f.GeometryType)
        {
            case GmlGeometryType.Point:
                return (
                    S129GeometryKind.Point,
                    f.Points.IsDefaultOrEmpty
                        ? ImmutableArray<GeoPosition>.Empty
                        : f.Points.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray(),
                    ImmutableArray<ImmutableArray<GeoPosition>>.Empty);

            case GmlGeometryType.Surface:
                var ext = f.ExteriorRing.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.ExteriorRing.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray();
                var holes = f.InteriorRings.IsDefaultOrEmpty
                    ? ImmutableArray<ImmutableArray<GeoPosition>>.Empty
                    : f.InteriorRings
                        .Select(r => r.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray())
                        .ToImmutableArray();
                if (ext.IsEmpty)
                    ctx.Warn(
                        "Surface feature has no exterior-ring coordinates.",
                        code: "feature.geometry.missing",
                        relatedId: f.Id);
                return (S129GeometryKind.Surface, ext, holes);

            default:
                return (
                    S129GeometryKind.None,
                    ImmutableArray<GeoPosition>.Empty,
                    ImmutableArray<ImmutableArray<GeoPosition>>.Empty);
        }
    }
}
