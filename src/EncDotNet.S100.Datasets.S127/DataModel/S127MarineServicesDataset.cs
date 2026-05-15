using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S127.DataModel;

/// <summary>
/// Strongly-typed projection of an <see cref="S127Dataset"/> as a graph
/// of typed S-127 features (S-127 Edition 2.0.0 — Marine Resources and
/// Services).
/// </summary>
/// <remarks>
/// <para>
/// The projection follows the S-124 / S-125 / S-128 / S-201 pattern: a
/// static <see cref="From"/> factory walks the source feature bag and
/// produces typed shapes (<see cref="S127PilotBoardingPlace"/>,
/// <see cref="S127RouteingMeasure"/>,
/// <see cref="S127VesselTrafficServiceArea"/>,
/// <see cref="S127SignalStation"/>, <see cref="S127RegulatedArea"/>,
/// <see cref="S127ShipReportingService"/>, <see cref="S127Authority"/>)
/// implementing <see cref="IS127Feature"/>. FC codes that the typed
/// model does not break out individually fall through to
/// <see cref="S127OtherFeature"/>; geometry survives in all cases.
/// </para>
/// <para>
/// Feature-to-feature <c>xlink:href</c> bindings (e.g. the
/// <c>theAuthority</c> role on a service feature) are resolved into
/// typed <see cref="IS127Feature"/> references after a two-pass
/// projection so that referent objects are fully constructed before
/// binding. Unresolved targets surface as
/// <see cref="ProjectionDiagnostic"/> entries with code
/// <c>"xlink.unresolved"</c>; the projection never throws for a
/// missing target.
/// </para>
/// <para>
/// S-127 Edition 2.0.0 declares no information types; the
/// <see cref="S127Dataset.InformationTypes"/> bag is preserved for
/// forward compatibility but the projection does nothing with it
/// today.
/// </para>
/// </remarks>
public sealed class S127MarineServicesDataset
{
    /// <summary>The dataset identifier carried by the source GML root <c>gml:id</c>.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>The S-127 product identifier (typically <c>"S-127"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>
    /// All typed features in source order. Includes every concrete typed
    /// shape (<see cref="S127PilotBoardingPlace"/> et al.) plus
    /// <see cref="S127Authority"/> and <see cref="S127OtherFeature"/>.
    /// </summary>
    public required ImmutableArray<IS127Feature> Features { get; init; }

    /// <summary>Convenience filter — every <see cref="S127Authority"/> in the dataset.</summary>
    public required ImmutableArray<S127Authority> Authorities { get; init; }

    /// <summary>Convenience filter — every <see cref="S127OtherFeature"/> in the dataset.</summary>
    public required ImmutableArray<S127OtherFeature> OtherFeatures { get; init; }

    /// <summary>The originating feature-bag dataset.</summary>
    public required S127Dataset Source { get; init; }

    /// <summary>
    /// Projects a feature-bag <see cref="S127Dataset"/> into the typed
    /// data model. Issues encountered during projection are reported via
    /// <paramref name="diagnostics"/>; the projection only throws when
    /// the source dataset is fully empty.
    /// </summary>
    /// <param name="dataset">The source dataset to project.</param>
    /// <param name="diagnostics">
    /// Receives the accumulated projection diagnostics (unresolved
    /// xlinks, attribute parse failures, etc.) as an immutable snapshot.
    /// </param>
    /// <exception cref="ArgumentNullException">If <paramref name="dataset"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the source dataset contains no features and no
    /// information types.
    /// </exception>
    public static S127MarineServicesDataset From(
        S127Dataset dataset,
        out IReadOnlyList<ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.Features.IsDefaultOrEmpty && dataset.InformationTypes.IsDefaultOrEmpty)
            throw new InvalidOperationException("Dataset contains no features and no information types.");

        var featureById = dataset.Features.IsDefaultOrEmpty
            ? ImmutableDictionary<string, S127Feature>.Empty
            : dataset.Features
                .Where(f => !string.IsNullOrEmpty(f.Id))
                .GroupBy(f => f.Id, StringComparer.OrdinalIgnoreCase)
                .ToImmutableDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Pass 1: project every feature into a typed shape without binding
        // feature-to-feature references. We need every typed object to
        // exist before xlink resolution can hand back peers, so the
        // resolver is fed the typed objects after pass 1.
        var pass1 = ImmutableArray.CreateBuilder<IS127Feature>(dataset.Features.IsDefaultOrEmpty ? 0 : dataset.Features.Length);
        var emptyResolver = XlinkResolver.Build(Array.Empty<KeyValuePair<string, object>>());
        var preCtx = new ProjectionContext(emptyResolver);

        if (!dataset.Features.IsDefaultOrEmpty)
        {
            foreach (var f in dataset.Features)
                pass1.Add(Project(f, preCtx));
        }

        // Build xlink resolver over every typed object (by source gml:id).
        var typedById = new Dictionary<string, IS127Feature>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in pass1)
            if (!string.IsNullOrEmpty(t.Id))
                typedById[t.Id] = t;

        var resolver = XlinkResolver.Build(
            typedById.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)));
        var ctx = new ProjectionContext(resolver);
        foreach (var d in preCtx.Diagnostics) ctx.Report(d);

        // Pass 2: resolve theAuthority xlinks for the typed shapes that
        // carry them. Other reference roles are surfaced today only by
        // diagnostics (xlink.unresolved) if the target is missing — the
        // raw FeatureReference list remains on the Source for callers
        // that need it.
        var final = ImmutableArray.CreateBuilder<IS127Feature>(pass1.Count);
        foreach (var typed in pass1)
        {
            final.Add(BindReferences(typed, ctx));
        }

        var features = final.ToImmutable();
        var authorities = features.OfType<S127Authority>().ToImmutableArray();
        var others = features.OfType<S127OtherFeature>().ToImmutableArray();

        diagnostics = ctx.ToImmutableDiagnostics();
        return new S127MarineServicesDataset
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            Features = features,
            Authorities = authorities,
            OtherFeatures = others,
            Source = dataset,
        };
    }

    // ── Geometry ──────────────────────────────────────────────────────

    private static (S127GeometryKind Kind, ImmutableArray<GeoPosition> Coords) ExtractGeometry(S127Feature f)
    {
        switch (f.GeometryType)
        {
            case GmlGeometryType.Point:
                if (f.Points.IsDefaultOrEmpty) return (S127GeometryKind.None, ImmutableArray<GeoPosition>.Empty);
                var pts = f.Points.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray();
                return (S127GeometryKind.Point, pts);
            case GmlGeometryType.Curve:
                if (f.Curves.IsDefaultOrEmpty) return (S127GeometryKind.None, ImmutableArray<GeoPosition>.Empty);
                var curve = f.Curves
                    .SelectMany(c => c)
                    .Select(p => new GeoPosition(p.Latitude, p.Longitude))
                    .ToImmutableArray();
                return (S127GeometryKind.Curve, curve);
            case GmlGeometryType.Surface:
                if (f.ExteriorRing.IsDefaultOrEmpty) return (S127GeometryKind.None, ImmutableArray<GeoPosition>.Empty);
                var ring = f.ExteriorRing.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray();
                return (S127GeometryKind.Surface, ring);
            default:
                return (S127GeometryKind.None, ImmutableArray<GeoPosition>.Empty);
        }
    }

    // ── Per-feature projection (pass 1) ──────────────────────────────

    private static IS127Feature Project(S127Feature f, ProjectionContext ctx)
    {
        var (kind, coords) = ExtractGeometry(f);

        return f.FeatureType switch
        {
            "PilotBoardingPlace" => new S127PilotBoardingPlace
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                Source = f,
                CategoryOfPilotBoardingPlace = AttributeParser.TryParseInt(
                    f.Attributes.GetValueOrDefault("categoryOfPilotBoardingPlace"),
                    ctx, f.Id, "categoryOfPilotBoardingPlace"),
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "categoryOfPilotBoardingPlace"),
            },

            "RouteingMeasure" => new S127RouteingMeasure
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                Source = f,
                ExtraAttributes = f.Attributes,
            },

            "VesselTrafficServiceArea" => new S127VesselTrafficServiceArea
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                Source = f,
                ExtraAttributes = f.Attributes,
            },

            "ShipReportingService" => new S127ShipReportingService
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                Source = f,
                ExtraAttributes = f.Attributes,
            },

            "SignalStationTraffic" => new S127SignalStation
            {
                Id = f.Id,
                FeatureType = f.FeatureType,
                Kind = S127SignalStationKind.Traffic,
                GeometryKind = kind,
                Coordinates = coords,
                Source = f,
                ExtraAttributes = f.Attributes,
            },

            "SignalStationWarning" => new S127SignalStation
            {
                Id = f.Id,
                FeatureType = f.FeatureType,
                Kind = S127SignalStationKind.Warning,
                GeometryKind = kind,
                Coordinates = coords,
                Source = f,
                ExtraAttributes = f.Attributes,
            },

            "Authority" => new S127Authority
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                Source = f,
                AuthorityName = f.Attributes.GetValueOrDefault("authorityName"),
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "authorityName"),
            },

            _ when TryGetRegulatedAreaKind(f.FeatureType, out var areaKind, out var categoryAttr) =>
                BuildRegulatedArea(f, kind, coords, areaKind, categoryAttr, ctx),

            _ => new S127OtherFeature
            {
                Id = f.Id,
                FeatureType = f.FeatureType,
                GeometryKind = kind,
                Coordinates = coords,
                Source = f,
                ExtraAttributes = f.Attributes,
            },
        };
    }

    private static bool TryGetRegulatedAreaKind(string featureType, out S127RegulatedAreaKind kind, out string? categoryAttr)
    {
        switch (featureType)
        {
            case "RestrictedArea":
                kind = S127RegulatedAreaKind.RestrictedArea;
                categoryAttr = "categoryOfRestrictedArea";
                return true;
            case "RestrictedAreaNavigational":
                kind = S127RegulatedAreaKind.RestrictedAreaNavigational;
                categoryAttr = "categoryOfRestrictedArea";
                return true;
            case "MilitaryPracticeArea":
                kind = S127RegulatedAreaKind.MilitaryPracticeArea;
                categoryAttr = "categoryOfMilitaryPracticeArea";
                return true;
            case "CautionArea":
                kind = S127RegulatedAreaKind.CautionArea;
                categoryAttr = null;
                return true;
            case "PiracyRiskArea":
                kind = S127RegulatedAreaKind.PiracyRiskArea;
                categoryAttr = null;
                return true;
            case "ConcentrationOfShippingHazardArea":
                kind = S127RegulatedAreaKind.ConcentrationOfShippingHazardArea;
                categoryAttr = null;
                return true;
            case "SupervisedArea":
                kind = S127RegulatedAreaKind.SupervisedArea;
                categoryAttr = null;
                return true;
            case "LocalPortBroadcastServiceArea":
                kind = S127RegulatedAreaKind.LocalPortBroadcastServiceArea;
                categoryAttr = null;
                return true;
            case "UnderKeelClearanceManagementArea":
                kind = S127RegulatedAreaKind.UnderKeelClearanceManagementArea;
                categoryAttr = null;
                return true;
            case "UnderKeelClearanceAllowanceArea":
                kind = S127RegulatedAreaKind.UnderKeelClearanceAllowanceArea;
                categoryAttr = null;
                return true;
            default:
                kind = S127RegulatedAreaKind.Unknown;
                categoryAttr = null;
                return false;
        }
    }

    private static S127RegulatedArea BuildRegulatedArea(
        S127Feature f,
        S127GeometryKind kind,
        ImmutableArray<GeoPosition> coords,
        S127RegulatedAreaKind areaKind,
        string? categoryAttr,
        ProjectionContext ctx)
    {
        int? code = null;
        ImmutableDictionary<string, string> extras = f.Attributes;
        if (categoryAttr is not null)
        {
            code = AttributeParser.TryParseInt(
                f.Attributes.GetValueOrDefault(categoryAttr), ctx, f.Id, categoryAttr);
            extras = ExtraAttributes.ExcludeKnown(f.Attributes, categoryAttr);
        }

        return new S127RegulatedArea
        {
            Id = f.Id,
            FeatureType = f.FeatureType,
            Kind = areaKind,
            GeometryKind = kind,
            Coordinates = coords,
            Source = f,
            CategoryCode = code,
            ExtraAttributes = extras,
        };
    }

    // ── Reference binding (pass 2) ────────────────────────────────────

    private static IS127Feature BindReferences(IS127Feature typed, ProjectionContext ctx)
    {
        // Only resolve theAuthority on features that expose it as a typed
        // property. All other xlinks remain accessible via Source.FeatureReferences
        // and we deliberately do not emit a diagnostic for unresolved ones
        // here (only the ones the typed projection actively consumes).
        return typed switch
        {
            S127PilotBoardingPlace p => AttachAuthority(p, ResolveAuthority(p.Source, ctx)),
            S127VesselTrafficServiceArea v => AttachAuthority(v, ResolveAuthority(v.Source, ctx)),
            S127ShipReportingService s => AttachAuthority(s, ResolveAuthority(s.Source, ctx)),
            S127SignalStation s => AttachAuthority(s, ResolveAuthority(s.Source, ctx)),
            S127RegulatedArea a => AttachAuthority(a, ResolveAuthority(a.Source, ctx)),
            _ => typed,
        };
    }

    private static S127PilotBoardingPlace AttachAuthority(S127PilotBoardingPlace p, IS127Feature? authority)
    {
        if (authority is null) return p;
        return new S127PilotBoardingPlace
        {
            Id = p.Id,
            GeometryKind = p.GeometryKind,
            Coordinates = p.Coordinates,
            Source = p.Source,
            CategoryOfPilotBoardingPlace = p.CategoryOfPilotBoardingPlace,
            Authority = authority,
            ExtraAttributes = p.ExtraAttributes,
        };
    }

    private static S127VesselTrafficServiceArea AttachAuthority(S127VesselTrafficServiceArea v, IS127Feature? authority)
    {
        if (authority is null) return v;
        return new S127VesselTrafficServiceArea
        {
            Id = v.Id,
            GeometryKind = v.GeometryKind,
            Coordinates = v.Coordinates,
            Source = v.Source,
            Authority = authority,
            ExtraAttributes = v.ExtraAttributes,
        };
    }

    private static S127ShipReportingService AttachAuthority(S127ShipReportingService s, IS127Feature? authority)
    {
        if (authority is null) return s;
        return new S127ShipReportingService
        {
            Id = s.Id,
            GeometryKind = s.GeometryKind,
            Coordinates = s.Coordinates,
            Source = s.Source,
            Authority = authority,
            ExtraAttributes = s.ExtraAttributes,
        };
    }

    private static S127SignalStation AttachAuthority(S127SignalStation s, IS127Feature? authority)
    {
        if (authority is null) return s;
        return new S127SignalStation
        {
            Id = s.Id,
            FeatureType = s.FeatureType,
            Kind = s.Kind,
            GeometryKind = s.GeometryKind,
            Coordinates = s.Coordinates,
            Source = s.Source,
            Authority = authority,
            ExtraAttributes = s.ExtraAttributes,
        };
    }

    private static S127RegulatedArea AttachAuthority(S127RegulatedArea a, IS127Feature? authority)
    {
        if (authority is null) return a;
        return new S127RegulatedArea
        {
            Id = a.Id,
            FeatureType = a.FeatureType,
            Kind = a.Kind,
            GeometryKind = a.GeometryKind,
            Coordinates = a.Coordinates,
            Source = a.Source,
            CategoryCode = a.CategoryCode,
            Authority = authority,
            ExtraAttributes = a.ExtraAttributes,
        };
    }

    private static IS127Feature? ResolveAuthority(S127Feature source, ProjectionContext ctx)
    {
        if (source.FeatureReferences.IsDefaultOrEmpty) return null;

        foreach (var r in source.FeatureReferences)
        {
            if (!string.Equals(r.Role, "theAuthority", StringComparison.Ordinal)) continue;
            return ctx.Xlinks.Resolve<IS127Feature>(r.FeatureRef, r.Role, ctx, source.Id);
        }
        return null;
    }
}
