using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S125.DataModel;

/// <summary>
/// Strongly-typed projection of an <see cref="S125Dataset"/> as a
/// collection of aids to navigation plus supporting status / quality
/// information types (S-125 Edition 1.0.0).
/// </summary>
/// <remarks>
/// <para>
/// The projection mirrors the S-421 / S-124 pattern: a static
/// <see cref="From"/> factory walks the source feature bag and produces a
/// graph of typed shapes (<see cref="S125Buoy"/>, <see cref="S125Beacon"/>,
/// <see cref="S125Light"/>, <see cref="S125AisAton"/>,
/// <see cref="S125Structure"/>, <see cref="S125Equipment"/>) implementing
/// the common <see cref="IS125Aid"/> contract. Each aid has its
/// <see cref="IS125Aid.Status"/> resolved by following the source
/// feature's <c>AtoNStatus</c> information binding, and its
/// <see cref="IS125Aid.HostStructure"/> resolved by following the
/// <c>parent</c> role of the <c>StructureEquipment</c> feature
/// association.
/// </para>
/// <para>
/// Projection issues — unresolved xlinks, attribute parse failures —
/// surface as <see cref="ProjectionDiagnostic"/> entries rather than
/// exceptions. The projection only throws when the source dataset has
/// no features and no information types (i.e. is fully empty).
/// </para>
/// </remarks>
public sealed class S125AtonDataset
{
    /// <summary>The dataset identifier carried by the source GML <c>Dataset</c> element.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>The S-125 product identifier (typically <c>"S-125"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>All typed aids to navigation in the dataset (buoys, beacons, lights, AIS aids, structures, equipment).</summary>
    public required ImmutableArray<IS125Aid> Aids { get; init; }

    /// <summary>All <c>AtonStatusInformation</c> info types in the dataset, keyed by GML id.</summary>
    public required ImmutableArray<S125AtonStatusInformation> StatusInformation { get; init; }

    /// <summary>All <c>AtonStatusIndication</c> features in the dataset.</summary>
    public required ImmutableArray<S125AtonStatusIndication> StatusIndications { get; init; }

    /// <summary>All <c>SpatialQuality</c> info types in the dataset.</summary>
    public required ImmutableArray<S125SpatialQuality> SpatialQualities { get; init; }

    /// <summary>All AtoN aggregations / associations in the dataset.</summary>
    public required ImmutableArray<S125Aggregation> Aggregations { get; init; }

    /// <summary>
    /// All other S-125 features that are not modeled as typed aids
    /// (lines, areas, metadata features — see <see cref="S125OtherFeature"/>).
    /// </summary>
    public required ImmutableArray<S125OtherFeature> OtherFeatures { get; init; }

    /// <summary>The originating feature-bag dataset.</summary>
    public required S125Dataset Source { get; init; }

    /// <summary>
    /// Projects a feature-bag <see cref="S125Dataset"/> into the typed
    /// data model. Issues encountered during projection are reported via
    /// <paramref name="diagnostics"/>; the projection only throws for a
    /// fully empty source.
    /// </summary>
    /// <param name="dataset">The source dataset to project.</param>
    /// <param name="diagnostics">
    /// Receives the accumulated projection diagnostics (unresolved xlinks,
    /// parse failures, etc.) as an immutable snapshot.
    /// </param>
    /// <exception cref="ArgumentNullException">If <paramref name="dataset"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the dataset contains neither features nor information
    /// types.
    /// </exception>
    public static S125AtonDataset From(S125Dataset dataset, out IReadOnlyList<ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.Features.IsDefaultOrEmpty && dataset.InformationTypes.IsDefaultOrEmpty)
            throw new InvalidOperationException("Dataset contains no features and no information types.");

        // Project supporting info types up-front so xlink resolution can
        // hand back fully-typed objects (and shared status instances stay
        // shared across all aids that bind to them).
        var statusInfoById = new Dictionary<string, S125AtonStatusInformation>(StringComparer.OrdinalIgnoreCase);
        var spatialQualityById = new Dictionary<string, S125SpatialQuality>(StringComparer.OrdinalIgnoreCase);

        // Diagnostics for the info-type pass are gathered with a temporary
        // context whose xlink index is empty — info types don't follow
        // xlinks themselves. The diagnostics are merged into the final
        // result.
        var emptyResolver = XlinkResolver.Build(Array.Empty<KeyValuePair<string, object>>());
        var preCtx = new ProjectionContext(emptyResolver);

        foreach (var i in dataset.InformationTypes)
        {
            if (string.Equals(i.TypeCode, "AtonStatusInformation", StringComparison.OrdinalIgnoreCase))
            {
                var info = ProjectStatusInformation(i, preCtx);
                if (!string.IsNullOrEmpty(info.Id))
                    statusInfoById[info.Id] = info;
            }
            else if (string.Equals(i.TypeCode, "SpatialQuality", StringComparison.OrdinalIgnoreCase))
            {
                var sq = ProjectSpatialQuality(i, preCtx);
                if (!string.IsNullOrEmpty(sq.Id))
                    spatialQualityById[sq.Id] = sq;
            }
        }

        // Build the xlink resolver over everything addressable by gml:id.
        // Aids are added lazily through a feature-id → feature lookup; the
        // typed aids are constructed in two passes so HostStructure can
        // reference fully-constructed peers.
        var featureById = dataset.Features.ToImmutableDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
        var resolver = BuildXlinkResolver(featureById, statusInfoById, spatialQualityById);
        var ctx = new ProjectionContext(resolver);
        foreach (var d in preCtx.Diagnostics) ctx.Report(d);

        // Pass 1: project every feature into a typed shape, but with
        // HostStructure left null so we don't depend on construction order.
        var aidsById = new Dictionary<string, IS125Aid>(StringComparer.OrdinalIgnoreCase);
        var aidsList = ImmutableArray.CreateBuilder<IS125Aid>();
        var indications = ImmutableArray.CreateBuilder<S125AtonStatusIndication>();
        var aggregations = ImmutableArray.CreateBuilder<S125Aggregation>();
        var otherFeatures = ImmutableArray.CreateBuilder<S125OtherFeature>();

        foreach (var f in dataset.Features)
        {
            switch (f.FeatureType)
            {
                case "AtonStatusIndication":
                    indications.Add(ProjectStatusIndication(f, statusInfoById, ctx));
                    break;
                case "AtonAggregation":
                case "AtonAssociation":
                    // Members resolved in pass 2 once typed aids exist.
                    aggregations.Add(ProjectAggregation(f, ctx));
                    break;
                default:
                    var aid = TryProjectAid(f, statusInfoById, ctx);
                    if (aid is not null)
                    {
                        aidsList.Add(aid);
                        if (!string.IsNullOrEmpty(aid.Id))
                            aidsById[aid.Id] = aid;
                    }
                    else
                    {
                        otherFeatures.Add(ProjectOtherFeature(f));
                    }
                    break;
            }
        }

        // Pass 2: walk aids and aggregations again to resolve feature-to-
        // feature xlinks now that every aid has been constructed.
        var aidsFinal = ImmutableArray.CreateBuilder<IS125Aid>(aidsList.Count);
        foreach (var aid in aidsList)
        {
            var src = featureById.GetValueOrDefault(aid.Id);
            var host = src is null ? null : ResolveHostStructure(src, aidsById, ctx);
            aidsFinal.Add(host is null ? aid : AttachHost(aid, host));
        }

        for (var i = 0; i < aggregations.Count; i++)
        {
            var current = aggregations[i];
            var src = featureById.GetValueOrDefault(current.Id);
            if (src is null) continue;
            var members = ResolveAggregationMembers(src, aidsById, ctx);
            aggregations[i] = new S125Aggregation
            {
                Id = current.Id,
                Kind = current.Kind,
                CategoryCode = current.CategoryCode,
                Members = members,
                ExtraAttributes = current.ExtraAttributes,
            };
        }

        diagnostics = ctx.ToImmutableDiagnostics();
        return new S125AtonDataset
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            Aids = aidsFinal.ToImmutable(),
            StatusInformation = statusInfoById.Values.ToImmutableArray(),
            StatusIndications = indications.ToImmutable(),
            SpatialQualities = spatialQualityById.Values.ToImmutableArray(),
            Aggregations = aggregations.ToImmutable(),
            OtherFeatures = otherFeatures.ToImmutable(),
            Source = dataset,
        };
    }

    // ── Xlink resolver build ──────────────────────────────────────────

    private static XlinkResolver BuildXlinkResolver(
        ImmutableDictionary<string, S125Feature> featureById,
        IReadOnlyDictionary<string, S125AtonStatusInformation> statusById,
        IReadOnlyDictionary<string, S125SpatialQuality> spatialQualityById)
    {
        IEnumerable<KeyValuePair<string, object>> All()
        {
            foreach (var kv in featureById)
                yield return new KeyValuePair<string, object>(kv.Key, kv.Value);
            foreach (var kv in statusById)
                yield return new KeyValuePair<string, object>(kv.Key, kv.Value);
            foreach (var kv in spatialQualityById)
                yield return new KeyValuePair<string, object>(kv.Key, kv.Value);
        }
        return XlinkResolver.Build(All());
    }

    // ── Info-type projections ─────────────────────────────────────────

    private static S125AtonStatusInformation ProjectStatusInformation(S125InformationType i, ProjectionContext ctx)
    {
        var changeTypeCode = AttributeParser.TryParseInt(
            i.Attributes.GetValueOrDefault("changeTypes"), ctx, i.Id, "changeTypes");
        var changeType = changeTypeCode switch
        {
            1 => S125ChangeType.AdvanceNoticeOfChange,
            2 => S125ChangeType.Discrepancy,
            3 => S125ChangeType.ProposedChange,
            4 => S125ChangeType.TemporaryChange,
            5 => S125ChangeType.PermanentChange,
            _ => S125ChangeType.Unknown,
        };

        var fixedRange = TryProjectDateRange(i.ComplexAttributes, "fixedDateRange", ctx, i.Id);
        var periodic = i.ComplexAttributes
            .Where(c => string.Equals(c.Code, "periodicDateRange", StringComparison.OrdinalIgnoreCase))
            .Select(c => BuildDateRange(c, ctx, i.Id))
            .Where(r => r is not null)
            .Select(r => r!)
            .ToImmutableArray();

        return new S125AtonStatusInformation
        {
            Id = i.Id,
            ChangeTypeCode = changeTypeCode,
            ChangeType = changeType,
            ChangeDetails = i.Attributes.GetValueOrDefault("changeDetails"),
            FixedDateRange = fixedRange,
            PeriodicDateRanges = periodic,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes, "changeTypes", "changeDetails"),
        };
    }

    private static S125SpatialQuality ProjectSpatialQuality(S125InformationType i, ProjectionContext ctx)
    {
        return new S125SpatialQuality
        {
            Id = i.Id,
            QualityOfPosition = AttributeParser.TryParseInt(
                i.Attributes.GetValueOrDefault("qualityOfPosition"), ctx, i.Id, "qualityOfPosition"),
            ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes, "qualityOfPosition"),
        };
    }

    private static S125DateRange? TryProjectDateRange(
        ImmutableArray<S125ComplexAttribute> complex, string code,
        ProjectionContext ctx, string relatedId)
    {
        var block = complex.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
        return block is null ? null : BuildDateRange(block, ctx, relatedId);
    }

    private static S125DateRange? BuildDateRange(S125ComplexAttribute block, ProjectionContext ctx, string relatedId)
    {
        var start = AttributeParser.TryParseDateTimeOffset(
            block.SubAttributes.GetValueOrDefault("dateStart"), ctx, relatedId, "dateStart");
        var end = AttributeParser.TryParseDateTimeOffset(
            block.SubAttributes.GetValueOrDefault("dateEnd"), ctx, relatedId, "dateEnd");
        if (start is null && end is null) return null;
        return new S125DateRange { Start = start, End = end };
    }

    // ── Feature projections ───────────────────────────────────────────

    /// <summary>
    /// Attempts to project a feature into a typed <see cref="IS125Aid"/>.
    /// Returns <c>null</c> when the feature type is not an AtoN class
    /// (caller falls back to <see cref="S125OtherFeature"/>).
    /// </summary>
    private static IS125Aid? TryProjectAid(
        S125Feature f,
        IReadOnlyDictionary<string, S125AtonStatusInformation> statusById,
        ProjectionContext ctx)
    {
        var position = ExtractPoint(f);
        var status = ResolveStatus(f, statusById, ctx);
        var extras = f.Attributes; // No consumed keys at this level; subclasses may consume more in the future.

        switch (f.FeatureType)
        {
            // ── Buoys ──
            case "LateralBuoy":
            case "CardinalBuoy":
            case "IsolatedDangerBuoy":
            case "SafeWaterBuoy":
            case "SpecialPurposeGeneralBuoy":
            case "EmergencyWreckMarkingBuoy":
            case "MooringBuoy":
            case "InstallationBuoy":
                return new S125Buoy
                {
                    Id = f.Id,
                    FeatureType = f.FeatureType,
                    Kind = ClassifyBuoy(f.FeatureType),
                    Position = position,
                    Status = status,
                    ExtraAttributes = extras,
                };
            // ── Beacons ──
            case "LateralBeacon":
            case "CardinalBeacon":
            case "IsolatedDangerBeacon":
            case "SafeWaterBeacon":
            case "SpecialPurposeGeneralBeacon":
                return new S125Beacon
                {
                    Id = f.Id,
                    FeatureType = f.FeatureType,
                    Kind = ClassifyBeacon(f.FeatureType),
                    Position = position,
                    Status = status,
                    ExtraAttributes = extras,
                };
            // ── Lights ──
            case "LightAllAround":
            case "LightSectored":
            case "LightAirObstruction":
            case "LightFloat":
            case "LightVessel":
            case "LightFogDetector":
                return new S125Light
                {
                    Id = f.Id,
                    FeatureType = f.FeatureType,
                    Kind = ClassifyLight(f.FeatureType),
                    Position = position,
                    Status = status,
                    ExtraAttributes = extras,
                };
            // ── AIS ──
            case "PhysicalAISAidToNavigation":
            case "SyntheticAISAidToNavigation":
            case "VirtualAISAidToNavigation":
                return new S125AisAton
                {
                    Id = f.Id,
                    FeatureType = f.FeatureType,
                    Kind = ClassifyAis(f.FeatureType),
                    Position = position,
                    Status = status,
                    ExtraAttributes = extras,
                };
            // ── Structures ──
            case "Landmark":
            case "Daymark":
            case "OffshorePlatform":
            case "Pile":
            case "SiloTank":
            case "WindTurbine":
            case "Topmark":
                return new S125Structure
                {
                    Id = f.Id,
                    FeatureType = f.FeatureType,
                    Kind = ClassifyStructure(f.FeatureType),
                    Position = position,
                    Status = status,
                    ExtraAttributes = extras,
                };
            // ── Equipment ──
            case "FogSignal":
            case "RadarReflector":
            case "Retroreflector":
            case "RadarTransponderBeacon":
            case "RadioStation":
                return new S125Equipment
                {
                    Id = f.Id,
                    FeatureType = f.FeatureType,
                    Kind = ClassifyEquipment(f.FeatureType),
                    Position = position,
                    Status = status,
                    ExtraAttributes = extras,
                };
            default:
                return null;
        }
    }

    private static S125BuoyKind ClassifyBuoy(string ft) => ft switch
    {
        "LateralBuoy" => S125BuoyKind.Lateral,
        "CardinalBuoy" => S125BuoyKind.Cardinal,
        "IsolatedDangerBuoy" => S125BuoyKind.IsolatedDanger,
        "SafeWaterBuoy" => S125BuoyKind.SafeWater,
        "SpecialPurposeGeneralBuoy" => S125BuoyKind.SpecialPurposeGeneral,
        "EmergencyWreckMarkingBuoy" => S125BuoyKind.EmergencyWreckMarking,
        "MooringBuoy" => S125BuoyKind.Mooring,
        "InstallationBuoy" => S125BuoyKind.Installation,
        _ => S125BuoyKind.Unknown,
    };

    private static S125BeaconKind ClassifyBeacon(string ft) => ft switch
    {
        "LateralBeacon" => S125BeaconKind.Lateral,
        "CardinalBeacon" => S125BeaconKind.Cardinal,
        "IsolatedDangerBeacon" => S125BeaconKind.IsolatedDanger,
        "SafeWaterBeacon" => S125BeaconKind.SafeWater,
        "SpecialPurposeGeneralBeacon" => S125BeaconKind.SpecialPurposeGeneral,
        _ => S125BeaconKind.Unknown,
    };

    private static S125LightKind ClassifyLight(string ft) => ft switch
    {
        "LightAllAround" => S125LightKind.AllAround,
        "LightSectored" => S125LightKind.Sectored,
        "LightAirObstruction" => S125LightKind.AirObstruction,
        "LightFloat" => S125LightKind.Float,
        "LightVessel" => S125LightKind.Vessel,
        "LightFogDetector" => S125LightKind.FogDetector,
        _ => S125LightKind.Unknown,
    };

    private static S125AisKind ClassifyAis(string ft) => ft switch
    {
        "PhysicalAISAidToNavigation" => S125AisKind.Physical,
        "SyntheticAISAidToNavigation" => S125AisKind.Synthetic,
        "VirtualAISAidToNavigation" => S125AisKind.Virtual,
        _ => S125AisKind.Unknown,
    };

    private static S125StructureKind ClassifyStructure(string ft) => ft switch
    {
        "Landmark" => S125StructureKind.Landmark,
        "Daymark" => S125StructureKind.Daymark,
        "OffshorePlatform" => S125StructureKind.OffshorePlatform,
        "Pile" => S125StructureKind.Pile,
        "SiloTank" => S125StructureKind.SiloTank,
        "WindTurbine" => S125StructureKind.WindTurbine,
        "Topmark" => S125StructureKind.Topmark,
        _ => S125StructureKind.Unknown,
    };

    private static S125EquipmentKind ClassifyEquipment(string ft) => ft switch
    {
        "FogSignal" => S125EquipmentKind.FogSignal,
        "RadarReflector" => S125EquipmentKind.RadarReflector,
        "Retroreflector" => S125EquipmentKind.Retroreflector,
        "RadarTransponderBeacon" => S125EquipmentKind.RadarTransponderBeacon,
        "RadioStation" => S125EquipmentKind.RadioStation,
        _ => S125EquipmentKind.Unknown,
    };

    private static S125AtonStatusIndication ProjectStatusIndication(
        S125Feature f,
        IReadOnlyDictionary<string, S125AtonStatusInformation> statusById,
        ProjectionContext ctx)
    {
        return new S125AtonStatusIndication
        {
            Id = f.Id,
            Position = ExtractPoint(f),
            ExpectedOutage = TryProjectDateRange(f.ComplexAttributes, "expectedOutage", ctx, f.Id),
            Status = ResolveStatus(f, statusById, ctx),
            ExtraAttributes = f.Attributes,
        };
    }

    private static S125Aggregation ProjectAggregation(S125Feature f, ProjectionContext ctx)
    {
        var isAggregation = string.Equals(f.FeatureType, "AtonAggregation", StringComparison.OrdinalIgnoreCase);
        var categoryKey = isAggregation ? "categoryOfAggregation" : "categoryOfAssociation";
        return new S125Aggregation
        {
            Id = f.Id,
            Kind = isAggregation ? S125AggregationKind.Aggregation : S125AggregationKind.Association,
            CategoryCode = AttributeParser.TryParseInt(
                f.Attributes.GetValueOrDefault(categoryKey), ctx, f.Id, categoryKey),
            ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, categoryKey),
        };
    }

    private static S125OtherFeature ProjectOtherFeature(S125Feature f)
    {
        var (kind, coords) = ProjectGeometry(f);
        return new S125OtherFeature
        {
            Id = f.Id,
            FeatureType = f.FeatureType,
            GeometryKind = kind,
            Coordinates = coords,
            ExtraAttributes = f.Attributes,
        };
    }

    // ── Xlink resolution helpers ──────────────────────────────────────

    private static S125AtonStatusInformation? ResolveStatus(
        S125Feature f,
        IReadOnlyDictionary<string, S125AtonStatusInformation> statusById,
        ProjectionContext ctx)
    {
        foreach (var r in f.InformationReferences)
        {
            if (!string.Equals(r.Role, "AtoNStatus", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(r.Role, "AtonStatus", StringComparison.OrdinalIgnoreCase))
                continue;
            if (statusById.TryGetValue(r.InformationRef, out var info))
                return info;
            ctx.Warn(
                $"Unresolved {r.Role} reference '#{r.InformationRef}'.",
                code: "xlink.unresolved",
                relatedId: f.Id,
                relatedAttribute: r.Role);
        }
        return null;
    }

    private static IS125Aid? ResolveHostStructure(
        S125Feature f,
        IReadOnlyDictionary<string, IS125Aid> aidsById,
        ProjectionContext ctx)
    {
        foreach (var r in f.FeatureReferences)
        {
            if (!string.Equals(r.Role, "parent", StringComparison.OrdinalIgnoreCase))
                continue;
            if (aidsById.TryGetValue(r.FeatureRef, out var host))
                return host;
            ctx.Warn(
                $"Unresolved {r.Role} reference '#{r.FeatureRef}'.",
                code: "xlink.unresolved",
                relatedId: f.Id,
                relatedAttribute: r.Role);
        }
        return null;
    }

    private static ImmutableArray<IS125Aid> ResolveAggregationMembers(
        S125Feature f,
        IReadOnlyDictionary<string, IS125Aid> aidsById,
        ProjectionContext ctx)
    {
        if (f.FeatureReferences.IsDefaultOrEmpty) return ImmutableArray<IS125Aid>.Empty;
        var b = ImmutableArray.CreateBuilder<IS125Aid>();
        foreach (var r in f.FeatureReferences)
        {
            if (aidsById.TryGetValue(r.FeatureRef, out var aid))
            {
                b.Add(aid);
            }
            else
            {
                ctx.Warn(
                    $"Unresolved {r.Role} reference '#{r.FeatureRef}'.",
                    code: "xlink.unresolved",
                    relatedId: f.Id,
                    relatedAttribute: r.Role);
            }
        }
        return b.ToImmutable();
    }

    // ── Geometry helpers ──────────────────────────────────────────────

    private static GeoPosition? ExtractPoint(S125Feature f)
    {
        if (f.GeometryType != GmlGeometryType.Point || f.Points.IsDefaultOrEmpty)
            return null;
        var (lat, lon) = f.Points[0];
        return new GeoPosition(lat, lon);
    }

    private static (S125GeometryKind, ImmutableArray<GeoPosition>) ProjectGeometry(S125Feature f)
    {
        switch (f.GeometryType)
        {
            case GmlGeometryType.Point:
                return (S125GeometryKind.Point, f.Points.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Points.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            case GmlGeometryType.Curve:
                return (S125GeometryKind.Curve, f.Curves.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Curves.SelectMany(c => c).Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            case GmlGeometryType.Surface:
                return (S125GeometryKind.Surface, f.ExteriorRing.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.ExteriorRing.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            default:
                return (S125GeometryKind.None, ImmutableArray<GeoPosition>.Empty);
        }
    }

    // ── Host-attach helper (returns a new aid with HostStructure set) ──

    private static IS125Aid AttachHost(IS125Aid aid, IS125Aid host) => aid switch
    {
        S125Buoy b => new S125Buoy
        {
            Id = b.Id, FeatureType = b.FeatureType, Kind = b.Kind,
            Position = b.Position, Status = b.Status,
            HostStructure = host, ExtraAttributes = b.ExtraAttributes,
        },
        S125Beacon b => new S125Beacon
        {
            Id = b.Id, FeatureType = b.FeatureType, Kind = b.Kind,
            Position = b.Position, Status = b.Status,
            HostStructure = host, ExtraAttributes = b.ExtraAttributes,
        },
        S125Light l => new S125Light
        {
            Id = l.Id, FeatureType = l.FeatureType, Kind = l.Kind,
            Position = l.Position, Status = l.Status,
            HostStructure = host, ExtraAttributes = l.ExtraAttributes,
        },
        S125AisAton a => new S125AisAton
        {
            Id = a.Id, FeatureType = a.FeatureType, Kind = a.Kind,
            Position = a.Position, Status = a.Status,
            HostStructure = host, ExtraAttributes = a.ExtraAttributes,
        },
        S125Structure s => new S125Structure
        {
            Id = s.Id, FeatureType = s.FeatureType, Kind = s.Kind,
            Position = s.Position, Status = s.Status,
            HostStructure = host, ExtraAttributes = s.ExtraAttributes,
        },
        S125Equipment e => new S125Equipment
        {
            Id = e.Id, FeatureType = e.FeatureType, Kind = e.Kind,
            Position = e.Position, Status = e.Status,
            HostStructure = host, ExtraAttributes = e.ExtraAttributes,
        },
        _ => aid,
    };
}
