using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S131.DataModel;

/// <summary>
/// Strongly-typed "Pass 2" projection of an <see cref="S131Dataset"/>
/// as a graph of typed harbour-infrastructure features and information
/// types (S-131 FC Edition 1.0.0 / PC Edition 2.0.0).
/// </summary>
/// <remarks>
/// <para>
/// The projection follows the same pattern used by the other GML-encoded
/// products (S-122 / S-124 / S-125 / S-127 / S-128 / S-129 / S-201 /
/// S-411 / S-421): a static <see cref="From"/> factory walks the raw
/// feature bag, builds an <see cref="XlinkResolver"/> over every
/// addressable <c>gml:id</c>, and emits typed records with resolved
/// cross-references. Issues surface as
/// <see cref="ProjectionDiagnostic"/> entries rather than exceptions;
/// the only fatal condition is a fully empty source dataset.
/// </para>
/// <para>
/// Feature-type discrimination is performed against the static FC enum
/// lists in <see cref="S131Types"/>; the projection does <b>not</b>
/// walk the FC supertype graph at runtime. Schema-level introspection
/// is handled by the Feature Catalogue reader and the Lua data
/// provider (see <see cref="S131LuaDataProvider"/>).
/// </para>
/// <para>
/// <b>Portrayal stays untouched.</b> The typed model is independent
/// of <see cref="S131LuaDataProvider"/> / <see cref="S131LuaRuleExecutor"/> —
/// portrayal continues to consume the raw <see cref="S131Feature"/>
/// graph through the Lua Host API.
/// </para>
/// </remarks>
public sealed class S131HarbourInfrastructureDataset
{
    /// <summary>The dataset identifier carried by the source GML <c>Dataset</c> element.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>The S-131 product identifier (typically <c>"S-131"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>Every typed feature in the dataset, in source order.</summary>
    public required ImmutableArray<IS131Feature> Features { get; init; }

    /// <summary>Every typed information type in the dataset, in source order.</summary>
    public required ImmutableArray<IS131InformationType> InformationTypes { get; init; }

    /// <summary>Typed view of <see cref="Features"/> filtered to <see cref="S131HarbourInfrastructure"/>.</summary>
    public ImmutableArray<S131HarbourInfrastructure> HarbourInfrastructure { get; init; } =
        ImmutableArray<S131HarbourInfrastructure>.Empty;

    /// <summary>Typed view of <see cref="Features"/> filtered to <see cref="S131LayoutFeature"/>.</summary>
    public ImmutableArray<S131LayoutFeature> LayoutFeatures { get; init; } =
        ImmutableArray<S131LayoutFeature>.Empty;

    /// <summary>Typed view of <see cref="Features"/> filtered to <see cref="S131MetadataFeature"/>.</summary>
    public ImmutableArray<S131MetadataFeature> MetadataFeatures { get; init; } =
        ImmutableArray<S131MetadataFeature>.Empty;

    /// <summary>Typed view of <see cref="Features"/> filtered to <see cref="S131OtherFeature"/> (FC misses).</summary>
    public ImmutableArray<S131OtherFeature> OtherFeatures { get; init; } =
        ImmutableArray<S131OtherFeature>.Empty;

    /// <summary>Typed view of <see cref="InformationTypes"/> filtered to <see cref="S131Authority"/>.</summary>
    public ImmutableArray<S131Authority> Authorities { get; init; } =
        ImmutableArray<S131Authority>.Empty;

    /// <summary>Typed view of <see cref="InformationTypes"/> filtered to <see cref="S131ContactDetails"/>.</summary>
    public ImmutableArray<S131ContactDetails> ContactDetails { get; init; } =
        ImmutableArray<S131ContactDetails>.Empty;

    /// <summary>Typed view of <see cref="InformationTypes"/> filtered to <see cref="S131RxNInformation"/>.</summary>
    public ImmutableArray<S131RxNInformation> RxNInformation { get; init; } =
        ImmutableArray<S131RxNInformation>.Empty;

    /// <summary>The originating raw feature-bag dataset.</summary>
    public required S131Dataset Source { get; init; }

    /// <summary>
    /// Projects a raw <see cref="S131Dataset"/> into the typed data
    /// model. Issues encountered during projection are reported via
    /// <paramref name="diagnostics"/>; the projection only throws when
    /// the source dataset is fully empty.
    /// </summary>
    /// <param name="dataset">The source dataset to project.</param>
    /// <param name="diagnostics">
    /// Receives the accumulated projection diagnostics (unresolved
    /// xlinks, parse failures, unknown codes, duplicate identifiers)
    /// as an immutable snapshot.
    /// </param>
    /// <exception cref="ArgumentNullException">If <paramref name="dataset"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the dataset contains neither features nor information
    /// types.
    /// </exception>
    public static S131HarbourInfrastructureDataset From(
        S131Dataset dataset,
        out IReadOnlyList<ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.Features.IsDefaultOrEmpty && dataset.InformationTypes.IsDefaultOrEmpty)
            throw new InvalidOperationException("Dataset contains no features and no information types.");

        // Pre-pass: project every information type into a stub (without
        // its resolved references) so feature projection can dereference
        // xlink targets. Info-type-to-info-type refs (e.g. Authority →
        // ContactDetails) are filled in by a follow-up back-fill pass.
        var emptyResolver = XlinkResolver.Build(Array.Empty<KeyValuePair<string, object>>());
        var preCtx = new ProjectionContext(emptyResolver);
        var infoTypeById = new Dictionary<string, IS131InformationType>(StringComparer.OrdinalIgnoreCase);
        var infoTypeList = ImmutableArray.CreateBuilder<IS131InformationType>();

        var infoIdsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in dataset.InformationTypes)
        {
            if (!string.IsNullOrEmpty(i.Id) && !infoIdsSeen.Add(i.Id))
                preCtx.Warn(
                    $"Duplicate gml:id '{i.Id}' on information type.",
                    code: "s131.id.duplicate",
                    relatedId: i.Id);

            var typed = ProjectInformationType(i, preCtx);
            infoTypeList.Add(typed);
            if (!string.IsNullOrEmpty(typed.Id))
                infoTypeById[typed.Id] = typed;
        }

        // Pass 1: build xlink resolver over every typed info type plus
        // the raw feature graph (typed features are constructed in
        // pass 2). Features cannot reference each other at runtime via
        // the resolver yet — that's fine: feature-to-feature refs in
        // the resolved reference list are resolved against typed peers
        // in a final pass below.
        var featureById = new Dictionary<string, S131Feature>(StringComparer.OrdinalIgnoreCase);
        var ctx = new ProjectionContext(BuildXlinkResolver(dataset, infoTypeById, featureById));
        foreach (var d in preCtx.Diagnostics) ctx.Report(d);

        var featureIdsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in dataset.Features)
        {
            if (!string.IsNullOrEmpty(f.Id))
            {
                featureById[f.Id] = f;
                if (!featureIdsSeen.Add(f.Id))
                    ctx.Warn(
                        $"Duplicate gml:id '{f.Id}' on feature.",
                        code: "s131.id.duplicate",
                        relatedId: f.Id);
            }
        }

        // Pass 2: project each feature using the resolver. References
        // are resolved eagerly because xlink targets in S-131 are
        // either information types (already projected) or other
        // features (resolved against featureById; we attach typed
        // feature peers in a final back-fill pass).
        var typedFeatures = ImmutableArray.CreateBuilder<IS131Feature>(dataset.Features.Length);
        var typedById = new Dictionary<string, IS131Feature>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in dataset.Features)
        {
            var typed = ProjectFeature(f, infoTypeById, ctx);
            typedFeatures.Add(typed);
            if (!string.IsNullOrEmpty(typed.Id))
                typedById[typed.Id] = typed;
        }

        // Final pass: back-fill any ResolvedReference whose target was
        // a feature (we constructed those after the resolver was built).
        var finalFeatures = ImmutableArray.CreateBuilder<IS131Feature>(typedFeatures.Count);
        foreach (var typed in typedFeatures)
        {
            finalFeatures.Add(BackfillFeatureReferences(typed, typedById));
        }

        // Info-type back-fill: resolve outgoing xlinks on information
        // types (e.g. Authority → ContactDetails / Applicability) now
        // that every typed info type has been built. Authority's typed
        // shortcuts (ContactDetails / Applicability) are populated here.
        var finalInfoTypes = ImmutableArray.CreateBuilder<IS131InformationType>(infoTypeList.Count);
        foreach (var typed in infoTypeList)
        {
            finalInfoTypes.Add(BackfillInformationTypeReferences(typed, infoTypeById, ctx));
        }

        diagnostics = ctx.ToImmutableDiagnostics();

        var finalFeaturesImm = finalFeatures.ToImmutable();
        var finalInfoTypesImm = finalInfoTypes.ToImmutable();

        return new S131HarbourInfrastructureDataset
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            Features = finalFeaturesImm,
            InformationTypes = finalInfoTypesImm,
            HarbourInfrastructure = finalFeaturesImm.OfType<S131HarbourInfrastructure>().ToImmutableArray(),
            LayoutFeatures = finalFeaturesImm.OfType<S131LayoutFeature>().ToImmutableArray(),
            MetadataFeatures = finalFeaturesImm.OfType<S131MetadataFeature>().ToImmutableArray(),
            OtherFeatures = finalFeaturesImm.OfType<S131OtherFeature>().ToImmutableArray(),
            Authorities = finalInfoTypesImm.OfType<S131Authority>().ToImmutableArray(),
            ContactDetails = finalInfoTypesImm.OfType<S131ContactDetails>().ToImmutableArray(),
            RxNInformation = finalInfoTypesImm.OfType<S131RxNInformation>().ToImmutableArray(),
            Source = dataset,
        };
    }

    // ── Xlink resolver build ──────────────────────────────────────────

    private static XlinkResolver BuildXlinkResolver(
        S131Dataset dataset,
        IReadOnlyDictionary<string, IS131InformationType> infoTypesById,
        IReadOnlyDictionary<string, S131Feature> featureById)
    {
        IEnumerable<KeyValuePair<string, object>> All()
        {
            foreach (var kv in infoTypesById)
                yield return new KeyValuePair<string, object>(kv.Key, kv.Value);
            // Features are indexed by raw graph at this point; the
            // back-fill pass replaces them with typed peers.
            foreach (var f in dataset.Features)
                if (!string.IsNullOrEmpty(f.Id))
                    yield return new KeyValuePair<string, object>(f.Id, f);
        }
        return XlinkResolver.Build(All());
    }

    // ── Information-type projection ───────────────────────────────────

    private static IS131InformationType ProjectInformationType(S131InformationType i, ProjectionContext ctx)
    {
        return i.TypeCode switch
        {
            "Authority" => new S131Authority
            {
                Id = i.Id,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            "ContactDetails" => new S131ContactDetails
            {
                Id = i.Id,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            "Applicability" => new S131Applicability
            {
                Id = i.Id,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            "AvailablePortServices" => new S131AvailablePortServices
            {
                Id = i.Id,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            "Entrance" => new S131Entrance
            {
                Id = i.Id,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            "ServiceHours" => new S131ServiceHours
            {
                Id = i.Id,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            "NonStandardWorkingDay" => new S131NonStandardWorkingDay
            {
                Id = i.Id,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            "SpatialQuality" => new S131SpatialQuality
            {
                Id = i.Id,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            "NauticalInformation" => new S131RxNInformation
            {
                Id = i.Id,
                TypeCode = i.TypeCode,
                Kind = S131RxNKind.NauticalInformation,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            "Recommendations" => new S131RxNInformation
            {
                Id = i.Id,
                TypeCode = i.TypeCode,
                Kind = S131RxNKind.Recommendations,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            "Regulations" => new S131RxNInformation
            {
                Id = i.Id,
                TypeCode = i.TypeCode,
                Kind = S131RxNKind.Regulations,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            "Restrictions" => new S131RxNInformation
            {
                Id = i.Id,
                TypeCode = i.TypeCode,
                Kind = S131RxNKind.Restrictions,
                ExtraAttributes = i.Attributes,
                Source = i,
            },
            _ => ProjectUnknownInformationType(i, ctx),
        };
    }

    private static S131OtherInformationType ProjectUnknownInformationType(
        S131InformationType i, ProjectionContext ctx)
    {
        ctx.Report(new ProjectionDiagnostic
        {
            Severity = DiagnosticSeverity.Info,
            Message = $"Information type code '{i.TypeCode}' is not in the S-131 FC enumeration.",
            Code = "s131.information.unknown",
            RelatedId = i.Id,
        });
        return new S131OtherInformationType
        {
            Id = i.Id,
            TypeCode = i.TypeCode,
            ExtraAttributes = i.Attributes,
            Source = i,
        };
    }

    // ── Feature projection ────────────────────────────────────────────

    private static IS131Feature ProjectFeature(
        S131Feature f,
        IReadOnlyDictionary<string, IS131InformationType> infoTypesById,
        ProjectionContext ctx)
    {
        var geometry = ProjectGeometry(f);
        var resolved = ResolveReferences(f, infoTypesById, ctx);

        if (TryClassifyHarbourInfrastructure(f.FeatureType, out var harbourKind))
        {
            return new S131HarbourInfrastructure
            {
                Id = f.Id,
                FeatureType = f.FeatureType,
                Kind = harbourKind,
                Geometry = geometry,
                ResolvedReferences = resolved,
                ExtraAttributes = f.Attributes,
                Source = f,
            };
        }
        if (TryClassifyLayout(f.FeatureType, out var layoutKind))
        {
            return new S131LayoutFeature
            {
                Id = f.Id,
                FeatureType = f.FeatureType,
                Kind = layoutKind,
                Geometry = geometry,
                ResolvedReferences = resolved,
                ExtraAttributes = f.Attributes,
                Source = f,
            };
        }
        if (TryClassifyMetadata(f.FeatureType, out var metaKind))
        {
            return new S131MetadataFeature
            {
                Id = f.Id,
                FeatureType = f.FeatureType,
                Kind = metaKind,
                Geometry = geometry,
                ResolvedReferences = resolved,
                ExtraAttributes = f.Attributes,
                Source = f,
            };
        }

        ctx.Report(new ProjectionDiagnostic
        {
            Severity = DiagnosticSeverity.Info,
            Message = $"Feature type code '{f.FeatureType}' is not in the S-131 FC enumeration.",
            Code = "s131.feature.unknown",
            RelatedId = f.Id,
        });
        return new S131OtherFeature
        {
            Id = f.Id,
            FeatureType = f.FeatureType,
            Geometry = geometry,
            ResolvedReferences = resolved,
            ExtraAttributes = f.Attributes,
            Source = f,
        };
    }

    // ── Reference resolution ──────────────────────────────────────────

    private static ImmutableArray<S131ResolvedReference> ResolveReferences(
        S131Feature f,
        IReadOnlyDictionary<string, IS131InformationType> infoTypesById,
        ProjectionContext ctx)
    {
        if (f.References.IsDefaultOrEmpty)
            return ImmutableArray<S131ResolvedReference>.Empty;

        var b = ImmutableArray.CreateBuilder<S131ResolvedReference>(f.References.Length);
        foreach (var r in f.References)
        {
            // Prefer typed info-type peers; the back-fill pass swaps in
            // typed features for feature-to-feature refs.
            object? target = infoTypesById.TryGetValue(r.TargetRef, out var info)
                ? info
                : null;

            if (target is null)
            {
                // Defer raising xlink.unresolved until back-fill — the
                // reference may still resolve to a typed feature peer.
                target = ctx.Xlinks.ResolveAny(
                    "#" + r.TargetRef, r.Role, ctx, relatedId: f.Id);
            }

            if (target is null)
            {
                ctx.Warn(
                    $"Dangling {r.Role} reference '#{r.TargetRef}'.",
                    code: "s131.reference.dangling",
                    relatedId: f.Id,
                    relatedAttribute: r.Role);
            }

            b.Add(new S131ResolvedReference
            {
                Role = r.Role,
                TargetRef = r.TargetRef,
                Target = target,
            });
        }
        return b.ToImmutable();
    }

    private static IS131Feature BackfillFeatureReferences(
        IS131Feature typed,
        IReadOnlyDictionary<string, IS131Feature> typedById)
    {
        if (typed.ResolvedReferences.IsDefaultOrEmpty)
            return typed;

        var b = ImmutableArray.CreateBuilder<S131ResolvedReference>(typed.ResolvedReferences.Length);
        var changed = false;
        foreach (var r in typed.ResolvedReferences)
        {
            // If a reference still points at the raw S131Feature graph
            // (the resolver indexed it that way), swap in the typed
            // peer constructed during pass 2.
            if (r.Target is S131Feature && typedById.TryGetValue(r.TargetRef, out var typedPeer))
            {
                b.Add(new S131ResolvedReference
                {
                    Role = r.Role,
                    TargetRef = r.TargetRef,
                    Target = typedPeer,
                });
                changed = true;
            }
            else
            {
                b.Add(r);
            }
        }

        if (!changed) return typed;

        var resolved = b.ToImmutable();
        return typed switch
        {
            S131HarbourInfrastructure h => new S131HarbourInfrastructure
            {
                Id = h.Id, FeatureType = h.FeatureType, Kind = h.Kind,
                Geometry = h.Geometry, ResolvedReferences = resolved,
                ExtraAttributes = h.ExtraAttributes, Source = h.Source,
            },
            S131LayoutFeature l => new S131LayoutFeature
            {
                Id = l.Id, FeatureType = l.FeatureType, Kind = l.Kind,
                Geometry = l.Geometry, ResolvedReferences = resolved,
                ExtraAttributes = l.ExtraAttributes, Source = l.Source,
            },
            S131MetadataFeature m => new S131MetadataFeature
            {
                Id = m.Id, FeatureType = m.FeatureType, Kind = m.Kind,
                Geometry = m.Geometry, ResolvedReferences = resolved,
                ExtraAttributes = m.ExtraAttributes, Source = m.Source,
            },
            S131OtherFeature o => new S131OtherFeature
            {
                Id = o.Id, FeatureType = o.FeatureType,
                Geometry = o.Geometry, ResolvedReferences = resolved,
                ExtraAttributes = o.ExtraAttributes, Source = o.Source,
            },
            _ => typed,
        };
    }

    private static IS131InformationType BackfillInformationTypeReferences(
        IS131InformationType typed,
        IReadOnlyDictionary<string, IS131InformationType> infoTypesById,
        ProjectionContext ctx)
    {
        var src = typed.Source;
        if (src.References.IsDefaultOrEmpty)
            return typed;

        var resolved = ImmutableArray.CreateBuilder<S131ResolvedReference>(src.References.Length);
        S131ContactDetails? contactShortcut = null;
        S131Applicability? applicabilityShortcut = null;

        foreach (var r in src.References)
        {
            IS131InformationType? target = null;
            if (!string.IsNullOrEmpty(r.TargetRef) && infoTypesById.TryGetValue(r.TargetRef, out var peer))
                target = peer;

            if (target is null)
            {
                ctx.Warn(
                    $"Unresolved {r.Role} reference '#{r.TargetRef}'.",
                    code: "xlink.unresolved",
                    relatedId: src.Id,
                    relatedAttribute: r.Role);
                ctx.Warn(
                    $"Dangling {r.Role} reference '#{r.TargetRef}'.",
                    code: "s131.reference.dangling",
                    relatedId: src.Id,
                    relatedAttribute: r.Role);
            }

            resolved.Add(new S131ResolvedReference
            {
                Role = r.Role,
                TargetRef = r.TargetRef,
                Target = target,
            });

            contactShortcut ??= target as S131ContactDetails;
            applicabilityShortcut ??= target as S131Applicability;
        }

        var resolvedImm = resolved.ToImmutable();
        return typed switch
        {
            S131Authority a => new S131Authority
            {
                Id = a.Id,
                ContactDetails = contactShortcut,
                Applicability = applicabilityShortcut,
                ResolvedReferences = resolvedImm,
                ExtraAttributes = a.ExtraAttributes,
                Source = a.Source,
            },
            S131ContactDetails c => new S131ContactDetails
            {
                Id = c.Id, ResolvedReferences = resolvedImm,
                ExtraAttributes = c.ExtraAttributes, Source = c.Source,
            },
            S131Applicability ap => new S131Applicability
            {
                Id = ap.Id, ResolvedReferences = resolvedImm,
                ExtraAttributes = ap.ExtraAttributes, Source = ap.Source,
            },
            S131AvailablePortServices aps => new S131AvailablePortServices
            {
                Id = aps.Id, ResolvedReferences = resolvedImm,
                ExtraAttributes = aps.ExtraAttributes, Source = aps.Source,
            },
            S131Entrance e => new S131Entrance
            {
                Id = e.Id, ResolvedReferences = resolvedImm,
                ExtraAttributes = e.ExtraAttributes, Source = e.Source,
            },
            S131ServiceHours s => new S131ServiceHours
            {
                Id = s.Id, ResolvedReferences = resolvedImm,
                ExtraAttributes = s.ExtraAttributes, Source = s.Source,
            },
            S131NonStandardWorkingDay n => new S131NonStandardWorkingDay
            {
                Id = n.Id, ResolvedReferences = resolvedImm,
                ExtraAttributes = n.ExtraAttributes, Source = n.Source,
            },
            S131SpatialQuality q => new S131SpatialQuality
            {
                Id = q.Id, ResolvedReferences = resolvedImm,
                ExtraAttributes = q.ExtraAttributes, Source = q.Source,
            },
            S131RxNInformation rx => new S131RxNInformation
            {
                Id = rx.Id, TypeCode = rx.TypeCode, Kind = rx.Kind,
                ResolvedReferences = resolvedImm,
                ExtraAttributes = rx.ExtraAttributes, Source = rx.Source,
            },
            S131OtherInformationType o => new S131OtherInformationType
            {
                Id = o.Id, TypeCode = o.TypeCode,
                ResolvedReferences = resolvedImm,
                ExtraAttributes = o.ExtraAttributes, Source = o.Source,
            },
            _ => typed,
        };
    }

    // ── Geometry projection ───────────────────────────────────────────

    private static S131Geometry ProjectGeometry(S131Feature f)
    {
        switch (f.GeometryType)
        {
            case GmlGeometryType.None:
                return S131Geometry.Empty;
            case GmlGeometryType.Point:
                return new S131Geometry
                {
                    GeometryType = S131GeometryType.Point,
                    Points = f.Points.IsDefaultOrEmpty
                        ? ImmutableArray<GeoPosition>.Empty
                        : f.Points.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray(),
                };
            case GmlGeometryType.Curve:
                return new S131Geometry
                {
                    GeometryType = S131GeometryType.Curve,
                    Curves = f.Curves.IsDefaultOrEmpty
                        ? ImmutableArray<ImmutableArray<GeoPosition>>.Empty
                        : f.Curves.Select(c =>
                            c.IsDefaultOrEmpty
                                ? ImmutableArray<GeoPosition>.Empty
                                : c.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray())
                            .ToImmutableArray(),
                };
            case GmlGeometryType.Surface:
                return new S131Geometry
                {
                    GeometryType = S131GeometryType.Surface,
                    ExteriorRing = f.ExteriorRing.IsDefaultOrEmpty
                        ? ImmutableArray<GeoPosition>.Empty
                        : f.ExteriorRing.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray(),
                    InteriorRings = f.InteriorRings.IsDefaultOrEmpty
                        ? ImmutableArray<ImmutableArray<GeoPosition>>.Empty
                        : f.InteriorRings.Select(r =>
                            r.IsDefaultOrEmpty
                                ? ImmutableArray<GeoPosition>.Empty
                                : r.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray())
                            .ToImmutableArray(),
                };
            default:
                return S131Geometry.Empty;
        }
    }

    // ── Family classifiers (FC Ed 1.0.0 §B.2) ─────────────────────────

    private static bool TryClassifyHarbourInfrastructure(string code, out S131HarbourInfrastructureKind kind)
    {
        kind = code switch
        {
            "AutomatedGuidedVehicle" => S131HarbourInfrastructureKind.AutomatedGuidedVehicle,
            "Bollard" => S131HarbourInfrastructureKind.Bollard,
            "Dolphin" => S131HarbourInfrastructureKind.Dolphin,
            "DryDock" => S131HarbourInfrastructureKind.DryDock,
            "FloatingDock" => S131HarbourInfrastructureKind.FloatingDock,
            "Gridiron" => S131HarbourInfrastructureKind.Gridiron,
            "HarbourFacility" => S131HarbourInfrastructureKind.HarbourFacility,
            "LockBasin" => S131HarbourInfrastructureKind.LockBasin,
            "LockBasinPart" => S131HarbourInfrastructureKind.LockBasinPart,
            "MooringBuoy" => S131HarbourInfrastructureKind.MooringBuoy,
            "OnshorePowerFacility" => S131HarbourInfrastructureKind.OnshorePowerFacility,
            "ShipLift" => S131HarbourInfrastructureKind.ShipLift,
            "StraddleCarrier" => S131HarbourInfrastructureKind.StraddleCarrier,
            _ => S131HarbourInfrastructureKind.Unknown,
        };
        return kind != S131HarbourInfrastructureKind.Unknown;
    }

    private static bool TryClassifyLayout(string code, out S131LayoutKind kind)
    {
        kind = code switch
        {
            "AnchorBerth" => S131LayoutKind.AnchorBerth,
            "AnchorageArea" => S131LayoutKind.AnchorageArea,
            "Berth" => S131LayoutKind.Berth,
            "BerthPosition" => S131LayoutKind.BerthPosition,
            "DockArea" => S131LayoutKind.DockArea,
            "DumpingGround" => S131LayoutKind.DumpingGround,
            "FenderLine" => S131LayoutKind.FenderLine,
            "HarbourAreaAdministrative" => S131LayoutKind.HarbourAreaAdministrative,
            "HarbourAreaSection" => S131LayoutKind.HarbourAreaSection,
            "HarbourBasin" => S131LayoutKind.HarbourBasin,
            "MooringWarpingFacility" => S131LayoutKind.MooringWarpingFacility,
            "OuterLimit" => S131LayoutKind.OuterLimit,
            "PilotBoardingPlace" => S131LayoutKind.PilotBoardingPlace,
            "SeaplaneLandingArea" => S131LayoutKind.SeaplaneLandingArea,
            "Terminal" => S131LayoutKind.Terminal,
            "TurningBasin" => S131LayoutKind.TurningBasin,
            "WaterwayArea" => S131LayoutKind.WaterwayArea,
            _ => S131LayoutKind.Unknown,
        };
        return kind != S131LayoutKind.Unknown;
    }

    private static bool TryClassifyMetadata(string code, out S131MetadataKind kind)
    {
        kind = code switch
        {
            "DataCoverage" => S131MetadataKind.DataCoverage,
            "QualityOfNonBathymetricData" => S131MetadataKind.QualityOfNonBathymetricData,
            "SoundingDatum" => S131MetadataKind.SoundingDatum,
            "TextPlacement" => S131MetadataKind.TextPlacement,
            "VerticalDatumOfData" => S131MetadataKind.VerticalDatumOfData,
            _ => S131MetadataKind.Unknown,
        };
        return kind != S131MetadataKind.Unknown;
    }
}
