using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S122.DataModel;

/// <summary>
/// Strongly-typed projection of an <see cref="S122Dataset"/> as a
/// catalogue of Marine Protected Areas and related zoned features
/// (S-122 FC 2.0.0 § Marine Protected Area Product Specification).
/// </summary>
/// <remarks>
/// <para>
/// S-122 datasets carry a heterogeneous collection of zoned
/// features (<see cref="S122MarineProtectedArea"/>,
/// <see cref="S122RestrictedArea"/>,
/// <see cref="S122VesselTrafficServiceArea"/>, …) plus a set of
/// non-geographic information types (<see cref="S122Authority"/>,
/// <see cref="S122Regulations"/>, <see cref="S122ContactDetails"/>,
/// <see cref="S122SpatialQuality"/>, …) which features bind to via
/// <c>xlink:href</c> associations such as <c>theAuthority</c>,
/// <c>theInformation</c>, <c>theCartographicText</c>.
/// </para>
/// <para>
/// The projection mirrors the S-125 / S-201 pattern: a static
/// <see cref="From"/> factory walks the source feature bag and produces
/// a graph of typed shapes. Each typed feature exposes both the raw
/// <see cref="IS122Feature.References"/> collection and the resolved
/// <see cref="IS122Feature.InformationReferences"/> /
/// <see cref="IS122Feature.FeatureReferences"/> bindings.
/// </para>
/// <para>
/// Projection issues — unresolved xlinks, attribute parse failures —
/// surface as <see cref="ProjectionDiagnostic"/> entries rather than
/// exceptions. The projection only throws when the source dataset has
/// no features and no information types (i.e. is fully empty).
/// </para>
/// </remarks>
public sealed class S122MarineProtectedAreaDataset
{
    /// <summary>The dataset identifier carried by the source GML <c>Dataset</c> element.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>The S-122 product identifier (typically <c>"S-122"</c> or <c>"INT.IHO.S-122…"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>All typed features in the dataset (every concrete S-122 feature type).</summary>
    public required ImmutableArray<IS122Feature> Features { get; init; }

    /// <summary>All typed information types in the dataset.</summary>
    public required ImmutableArray<IS122InformationType> InformationTypes { get; init; }

    /// <summary>Typed Marine Protected Area features (S-122 FC §MarineProtectedArea).</summary>
    public ImmutableArray<S122MarineProtectedArea> MarineProtectedAreas =>
        Features.OfType<S122MarineProtectedArea>().ToImmutableArray();

    /// <summary>Typed Restricted Area features (S-122 FC §RestrictedArea).</summary>
    public ImmutableArray<S122RestrictedArea> RestrictedAreas =>
        Features.OfType<S122RestrictedArea>().ToImmutableArray();

    /// <summary>Typed Vessel Traffic Service Area features (S-122 FC §VesselTrafficServiceArea).</summary>
    public ImmutableArray<S122VesselTrafficServiceArea> VesselTrafficServiceAreas =>
        Features.OfType<S122VesselTrafficServiceArea>().ToImmutableArray();

    /// <summary>The originating feature-bag dataset.</summary>
    public required S122Dataset Source { get; init; }

    /// <summary>
    /// Projects a feature-bag <see cref="S122Dataset"/> into the typed
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
    /// Thrown if the dataset contains neither features nor information types.
    /// </exception>
    public static S122MarineProtectedAreaDataset From(
        S122Dataset dataset,
        out IReadOnlyList<ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.Features.IsDefaultOrEmpty && dataset.InformationTypes.IsDefaultOrEmpty)
            throw new InvalidOperationException("Dataset contains no features and no information types.");

        // Project information types first (without resolved references) so the
        // xlink index can hand back fully-typed targets when features /
        // info-types resolve their bindings in the second pass.
        var emptyResolver = XlinkResolver.Build(Array.Empty<KeyValuePair<string, object>>());
        var preCtx = new ProjectionContext(emptyResolver);

        var infoTypes = ImmutableArray.CreateBuilder<IS122InformationType>();
        var infoTypesById = new Dictionary<string, IS122InformationType>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in dataset.InformationTypes.IsDefault ? ImmutableArray<S122InformationType>.Empty : dataset.InformationTypes)
        {
            var typed = ProjectInformationType(i, preCtx);
            infoTypes.Add(typed);
            if (!string.IsNullOrEmpty(typed.Id))
                infoTypesById[typed.Id] = typed;
        }

        // Project features (without resolved references) so feature ⇄ feature
        // bindings can be resolved in pass 2.
        var features = ImmutableArray.CreateBuilder<IS122Feature>();
        var featuresById = new Dictionary<string, IS122Feature>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in dataset.Features.IsDefault ? ImmutableArray<S122Feature>.Empty : dataset.Features)
        {
            var typed = ProjectFeature(f, preCtx);
            features.Add(typed);
            if (!string.IsNullOrEmpty(typed.Id))
                featuresById[typed.Id] = typed;
        }

        // Build the xlink resolver over everything addressable by gml:id.
        var resolver = BuildXlinkResolver(featuresById, infoTypesById);
        var ctx = new ProjectionContext(resolver);
        foreach (var d in preCtx.Diagnostics) ctx.Report(d);

        // Pass 2: resolve references on every projected feature / info type.
        foreach (var typed in features)
        {
            var (infoRefs, featRefs) = ResolveReferences(typed.Id, typed.References, ctx);
            ((S122FeatureBase)typed).InformationReferences = infoRefs;
            ((S122FeatureBase)typed).FeatureReferences = featRefs;
        }

        foreach (var typed in infoTypes)
        {
            var (infoRefs, _) = ResolveReferences(typed.Id, typed.References, ctx);
            switch (typed)
            {
                case S122InformationTypeBase b: b.InformationReferences = infoRefs; break;
                case S122AbstractRxN r: r.InformationReferences = infoRefs; break;
            }
        }

        diagnostics = ctx.ToImmutableDiagnostics();
        return new S122MarineProtectedAreaDataset
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            Features = features.ToImmutable(),
            InformationTypes = infoTypes.ToImmutable(),
            Source = dataset,
        };
    }

    private static XlinkResolver BuildXlinkResolver(
        IReadOnlyDictionary<string, IS122Feature> features,
        IReadOnlyDictionary<string, IS122InformationType> infoTypes)
    {
        IEnumerable<KeyValuePair<string, object>> All()
        {
            foreach (var (id, f) in features)
                yield return new KeyValuePair<string, object>(id, f);
            foreach (var (id, i) in infoTypes)
                yield return new KeyValuePair<string, object>(id, i);
        }
        return XlinkResolver.Build(All());
    }

    private static (ImmutableArray<S122InformationReference> infoRefs,
                    ImmutableArray<S122FeatureReference> featRefs)
        ResolveReferences(string referrerId, ImmutableArray<GmlReference> refs, ProjectionContext ctx)
    {
        if (refs.IsDefaultOrEmpty)
            return (ImmutableArray<S122InformationReference>.Empty,
                    ImmutableArray<S122FeatureReference>.Empty);

        var infos = ImmutableArray.CreateBuilder<S122InformationReference>();
        var feats = ImmutableArray.CreateBuilder<S122FeatureReference>();
        foreach (var r in refs)
        {
            var target = ctx.Xlinks.ResolveAny(r.Href, r.Role, ctx, referrerId);
            switch (target)
            {
                case IS122InformationType info:
                    infos.Add(new S122InformationReference(r.Role, r.ArcRole, info));
                    break;
                case IS122Feature feat:
                    feats.Add(new S122FeatureReference(r.Role, r.ArcRole, feat));
                    break;
            }
        }
        return (infos.ToImmutable(), feats.ToImmutable());
    }

    private static IS122Feature ProjectFeature(S122Feature f, ProjectionContext ctx)
    {
        var (kind, coords) = ProjectGeometry(f);

        // Inherited (FC-abstract) attribute keys consumed by the typed base.
        // Every concrete projection adds its own keys via WithBaseInit below.
        static (string?, string?, int?, string?, string?, string?, string?, string?)
            ReadBase(S122Feature feat, ProjectionContext c)
        {
            var attrs = feat.Attributes;
            var interop = attrs.GetValueOrDefault("interoperabilityIdentifier");
            var featName = attrs.GetValueOrDefault("featureName");
            var scaleMin = AttributeParser.TryParseInt(attrs.GetValueOrDefault("scaleMinimum"), c, feat.Id, "scaleMinimum");
            var graphic = attrs.GetValueOrDefault("graphic");
            var srcInd = attrs.GetValueOrDefault("sourceIndication");
            var textC = attrs.GetValueOrDefault("textContent");
            var fixedDr = attrs.GetValueOrDefault("fixedDateRange");
            var perDr = attrs.GetValueOrDefault("periodicDateRange");
            return (interop, featName, scaleMin, graphic, srcInd, textC, fixedDr, perDr);
        }

        var (interopId, featureName, scaleMinimum, graphic, sourceIndication, textContent, fixedDr, periodicDr) =
            ReadBase(f, ctx);

        var baseKeys = new[] {
            "interoperabilityIdentifier", "featureName", "scaleMinimum", "graphic",
            "sourceIndication", "textContent", "fixedDateRange", "periodicDateRange"
        };

        IS122Feature typed = f.FeatureType switch
        {
            "MarineProtectedArea" => new S122MarineProtectedArea
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                InteroperabilityIdentifier = interopId,
                FeatureName = featureName,
                ScaleMinimum = scaleMinimum,
                Graphic = graphic,
                SourceIndication = sourceIndication,
                TextContent = textContent,
                FixedDateRange = fixedDr,
                PeriodicDateRange = periodicDr,
                CategoryOfMarineProtectedArea = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("categoryOfMarineProtectedArea"), ctx, f.Id, "categoryOfMarineProtectedArea"),
                CategoryOfRestrictedArea = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("categoryOfRestrictedArea"), ctx, f.Id, "categoryOfRestrictedArea"),
                Jurisdiction = f.Attributes.GetValueOrDefault("jurisdiction"),
                Restriction = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("restriction"), ctx, f.Id, "restriction"),
                Status = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("status"), ctx, f.Id, "status"),
                Designation = f.Attributes.GetValueOrDefault("designation"),
                References = f.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes,
                    [..baseKeys, "categoryOfMarineProtectedArea", "categoryOfRestrictedArea",
                     "jurisdiction", "restriction", "status", "designation"]),
            },
            "RestrictedArea" => new S122RestrictedArea
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                InteroperabilityIdentifier = interopId,
                FeatureName = featureName,
                ScaleMinimum = scaleMinimum,
                Graphic = graphic,
                SourceIndication = sourceIndication,
                TextContent = textContent,
                FixedDateRange = fixedDr,
                PeriodicDateRange = periodicDr,
                CategoryOfRestrictedArea = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("categoryOfRestrictedArea"), ctx, f.Id, "categoryOfRestrictedArea"),
                Restriction = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("restriction"), ctx, f.Id, "restriction"),
                Status = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("status"), ctx, f.Id, "status"),
                References = f.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes,
                    [..baseKeys, "categoryOfRestrictedArea", "restriction", "status"]),
            },
            "VesselTrafficServiceArea" => new S122VesselTrafficServiceArea
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                InteroperabilityIdentifier = interopId,
                FeatureName = featureName,
                ScaleMinimum = scaleMinimum,
                Graphic = graphic,
                SourceIndication = sourceIndication,
                TextContent = textContent,
                FixedDateRange = fixedDr,
                PeriodicDateRange = periodicDr,
                References = f.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, baseKeys),
            },
            "InformationArea" => new S122InformationArea
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                InteroperabilityIdentifier = interopId,
                FeatureName = featureName,
                ScaleMinimum = scaleMinimum,
                Graphic = graphic,
                SourceIndication = sourceIndication,
                TextContent = textContent,
                FixedDateRange = fixedDr,
                PeriodicDateRange = periodicDr,
                CategoryOfRelationship = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("categoryOfRelationship"), ctx, f.Id, "categoryOfRelationship"),
                ActionOrActivity = f.Attributes.GetValueOrDefault("actionOrActivity"),
                References = f.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes,
                    [..baseKeys, "categoryOfRelationship", "actionOrActivity"]),
            },
            "DataCoverage" => new S122DataCoverage
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                InteroperabilityIdentifier = interopId,
                FeatureName = featureName,
                ScaleMinimum = scaleMinimum,
                Graphic = graphic,
                SourceIndication = sourceIndication,
                TextContent = textContent,
                FixedDateRange = fixedDr,
                PeriodicDateRange = periodicDr,
                MaximumDisplayScale = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("maximumDisplayScale"), ctx, f.Id, "maximumDisplayScale"),
                MinimumDisplayScale = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("minimumDisplayScale"), ctx, f.Id, "minimumDisplayScale"),
                OptimumDisplayScale = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("optimumDisplayScale"), ctx, f.Id, "optimumDisplayScale"),
                References = f.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes,
                    [..baseKeys, "maximumDisplayScale", "minimumDisplayScale", "optimumDisplayScale"]),
            },
            "QualityOfNonBathymetricData" => new S122QualityOfNonBathymetricData
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                InteroperabilityIdentifier = interopId,
                FeatureName = featureName,
                ScaleMinimum = scaleMinimum,
                Graphic = graphic,
                SourceIndication = sourceIndication,
                TextContent = textContent,
                FixedDateRange = fixedDr,
                PeriodicDateRange = periodicDr,
                CategoryOfTemporalVariation = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("categoryOfTemporalVariation"), ctx, f.Id, "categoryOfTemporalVariation"),
                HorizontalDistanceUncertainty = AttributeParser.TryParseDouble(f.Attributes.GetValueOrDefault("horizontalDistanceUncertainty"), ctx, f.Id, "horizontalDistanceUncertainty"),
                HorizontalPositionUncertainty = AttributeParser.TryParseDouble(f.Attributes.GetValueOrDefault("horizontalPositionUncertainty"), ctx, f.Id, "horizontalPositionUncertainty"),
                OrientationUncertainty = AttributeParser.TryParseDouble(f.Attributes.GetValueOrDefault("orientationUncertainty"), ctx, f.Id, "orientationUncertainty"),
                SurveyDateRange = f.Attributes.GetValueOrDefault("surveyDateRange"),
                Information = f.Attributes.GetValueOrDefault("information"),
                References = f.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes,
                    [..baseKeys, "categoryOfTemporalVariation", "horizontalDistanceUncertainty",
                     "horizontalPositionUncertainty", "orientationUncertainty",
                     "surveyDateRange", "information"]),
            },
            "TextPlacement" => new S122TextPlacement
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                InteroperabilityIdentifier = interopId,
                FeatureName = featureName,
                ScaleMinimum = scaleMinimum,
                Graphic = graphic,
                SourceIndication = sourceIndication,
                TextContent = textContent,
                FixedDateRange = fixedDr,
                PeriodicDateRange = periodicDr,
                TextOffsetBearing = AttributeParser.TryParseDouble(f.Attributes.GetValueOrDefault("textOffsetBearing"), ctx, f.Id, "textOffsetBearing"),
                TextOffsetDistance = AttributeParser.TryParseDouble(f.Attributes.GetValueOrDefault("textOffsetDistance"), ctx, f.Id, "textOffsetDistance"),
                TextRotation = AttributeParser.TryParseDouble(f.Attributes.GetValueOrDefault("textRotation"), ctx, f.Id, "textRotation"),
                TextType = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("textType"), ctx, f.Id, "textType"),
                References = f.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes,
                    [..baseKeys, "textOffsetBearing", "textOffsetDistance", "textRotation", "textType"]),
            },
            _ => new S122OtherFeature(f.FeatureType)
            {
                Id = f.Id,
                GeometryKind = kind,
                Coordinates = coords,
                InteroperabilityIdentifier = interopId,
                FeatureName = featureName,
                ScaleMinimum = scaleMinimum,
                Graphic = graphic,
                SourceIndication = sourceIndication,
                TextContent = textContent,
                FixedDateRange = fixedDr,
                PeriodicDateRange = periodicDr,
                References = f.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, baseKeys),
            },
        };

        return typed;
    }

    private static IS122InformationType ProjectInformationType(S122InformationType i, ProjectionContext ctx)
    {
        var baseKeys = new[] {
            "featureName", "fixedDateRange", "periodicDateRange", "graphic", "sourceIndication"
        };
        var featureName = i.Attributes.GetValueOrDefault("featureName");
        var fixedDr = i.Attributes.GetValueOrDefault("fixedDateRange");
        var perDr = i.Attributes.GetValueOrDefault("periodicDateRange");
        var graphic = i.Attributes.GetValueOrDefault("graphic");
        var srcInd = i.Attributes.GetValueOrDefault("sourceIndication");

        IS122InformationType typed = i.TypeCode switch
        {
            "Authority" => new S122Authority
            {
                Id = i.Id,
                FeatureName = featureName,
                FixedDateRange = fixedDr,
                PeriodicDateRange = perDr,
                Graphic = graphic,
                SourceIndication = srcInd,
                CategoryOfAuthority = AttributeParser.TryParseInt(i.Attributes.GetValueOrDefault("categoryOfAuthority"), ctx, i.Id, "categoryOfAuthority"),
                TextContent = i.Attributes.GetValueOrDefault("textContent"),
                References = i.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    [..baseKeys, "categoryOfAuthority", "textContent"]),
            },
            "ContactDetails" => new S122ContactDetails
            {
                Id = i.Id,
                FeatureName = featureName,
                FixedDateRange = fixedDr,
                PeriodicDateRange = perDr,
                Graphic = graphic,
                SourceIndication = srcInd,
                CallName = i.Attributes.GetValueOrDefault("callName"),
                CallSign = i.Attributes.GetValueOrDefault("callSign"),
                CategoryOfCommunicationPreference = AttributeParser.TryParseInt(i.Attributes.GetValueOrDefault("categoryOfCommunicationPreference"), ctx, i.Id, "categoryOfCommunicationPreference"),
                CommunicationChannel = i.Attributes.GetValueOrDefault("communicationChannel"),
                ContactInstructions = i.Attributes.GetValueOrDefault("contactInstructions"),
                Language = i.Attributes.GetValueOrDefault("language"),
                MMSICode = i.Attributes.GetValueOrDefault("mMSICode"),
                References = i.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    [..baseKeys, "callName", "callSign", "categoryOfCommunicationPreference",
                     "communicationChannel", "contactInstructions", "language", "mMSICode"]),
            },
            "Applicability" => new S122Applicability
            {
                Id = i.Id,
                FeatureName = featureName,
                FixedDateRange = fixedDr,
                PeriodicDateRange = perDr,
                Graphic = graphic,
                SourceIndication = srcInd,
                InBallast = AttributeParser.TryParseBool(i.Attributes.GetValueOrDefault("inBallast"), ctx, i.Id, "inBallast"),
                CategoryOfCargo = AttributeParser.TryParseInt(i.Attributes.GetValueOrDefault("categoryOfCargo"), ctx, i.Id, "categoryOfCargo"),
                CategoryOfDangerousOrHazardousCargo = AttributeParser.TryParseInt(i.Attributes.GetValueOrDefault("categoryOfDangerousOrHazardousCargo"), ctx, i.Id, "categoryOfDangerousOrHazardousCargo"),
                CategoryOfVessel = AttributeParser.TryParseInt(i.Attributes.GetValueOrDefault("categoryOfVessel"), ctx, i.Id, "categoryOfVessel"),
                CategoryOfVesselRegistry = AttributeParser.TryParseInt(i.Attributes.GetValueOrDefault("categoryOfVesselRegistry"), ctx, i.Id, "categoryOfVesselRegistry"),
                LogicalConnectives = i.Attributes.GetValueOrDefault("logicalConnectives"),
                ThicknessOfIceCapability = AttributeParser.TryParseDouble(i.Attributes.GetValueOrDefault("thicknessOfIceCapability"), ctx, i.Id, "thicknessOfIceCapability"),
                VesselPerformance = i.Attributes.GetValueOrDefault("vesselPerformance"),
                Destination = i.Attributes.GetValueOrDefault("destination"),
                Information = i.Attributes.GetValueOrDefault("information"),
                References = i.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    [..baseKeys, "inBallast", "categoryOfCargo", "categoryOfDangerousOrHazardousCargo",
                     "categoryOfVessel", "categoryOfVesselRegistry", "logicalConnectives",
                     "thicknessOfIceCapability", "vesselPerformance", "destination", "information"]),
            },
            "NauticalInformation" => new S122NauticalInformation
            {
                Id = i.Id,
                FeatureName = featureName,
                FixedDateRange = fixedDr,
                PeriodicDateRange = perDr,
                Graphic = graphic,
                SourceIndication = srcInd,
                References = i.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes, baseKeys),
            },
            "NonStandardWorkingDay" => new S122NonStandardWorkingDay
            {
                Id = i.Id,
                FeatureName = featureName,
                FixedDateRange = fixedDr,
                PeriodicDateRange = perDr,
                Graphic = graphic,
                SourceIndication = srcInd,
                DateFixed = i.Attributes.GetValueOrDefault("dateFixed"),
                DateVariable = i.Attributes.GetValueOrDefault("dateVariable"),
                Information = i.Attributes.GetValueOrDefault("information"),
                References = i.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    [..baseKeys, "dateFixed", "dateVariable", "information"]),
            },
            "Recommendations" => ProjectRxN(new S122Recommendations { Id = i.Id }, i, ctx, baseKeys),
            "Regulations" => ProjectRxN(new S122Regulations { Id = i.Id }, i, ctx, baseKeys),
            "Restrictions" => ProjectRxN(new S122Restrictions { Id = i.Id }, i, ctx, baseKeys),
            "ServiceHours" => new S122ServiceHours
            {
                Id = i.Id,
                FeatureName = featureName,
                FixedDateRange = fixedDr,
                PeriodicDateRange = perDr,
                Graphic = graphic,
                SourceIndication = srcInd,
                Information = i.Attributes.GetValueOrDefault("information"),
                References = i.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    [..baseKeys, "information"]),
            },
            "SpatialQuality" => new S122SpatialQuality
            {
                Id = i.Id,
                FeatureName = featureName,
                FixedDateRange = fixedDr,
                PeriodicDateRange = perDr,
                Graphic = graphic,
                SourceIndication = srcInd,
                QualityOfHorizontalMeasurement = AttributeParser.TryParseInt(i.Attributes.GetValueOrDefault("qualityOfHorizontalMeasurement"), ctx, i.Id, "qualityOfHorizontalMeasurement"),
                SpatialAccuracy = AttributeParser.TryParseDouble(i.Attributes.GetValueOrDefault("spatialAccuracy"), ctx, i.Id, "spatialAccuracy"),
                References = i.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    [..baseKeys, "qualityOfHorizontalMeasurement", "spatialAccuracy"]),
            },
            _ => new S122OtherInformationType(i.TypeCode)
            {
                Id = i.Id,
                FeatureName = featureName,
                FixedDateRange = fixedDr,
                PeriodicDateRange = perDr,
                Graphic = graphic,
                SourceIndication = srcInd,
                References = i.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes, baseKeys),
            },
        };

        return typed;
    }

    private static T ProjectRxN<T>(T seed, S122InformationType i, ProjectionContext ctx, string[] baseKeys)
        where T : S122AbstractRxN
    {
        // The RxN concrete types share the same attribute shape; only the
        // TypeCode discriminator differs.
        return (T)(object)(seed switch
        {
            S122Recommendations => new S122Recommendations
            {
                Id = i.Id,
                CategoryOfAuthority = AttributeParser.TryParseInt(i.Attributes.GetValueOrDefault("categoryOfAuthority"), ctx, i.Id, "categoryOfAuthority"),
                RxNCode = i.Attributes.GetValueOrDefault("rxNCode"),
                TextContent = i.Attributes.GetValueOrDefault("textContent"),
                References = i.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    [..baseKeys, "categoryOfAuthority", "rxNCode", "textContent"]),
            },
            S122Regulations => new S122Regulations
            {
                Id = i.Id,
                CategoryOfAuthority = AttributeParser.TryParseInt(i.Attributes.GetValueOrDefault("categoryOfAuthority"), ctx, i.Id, "categoryOfAuthority"),
                RxNCode = i.Attributes.GetValueOrDefault("rxNCode"),
                TextContent = i.Attributes.GetValueOrDefault("textContent"),
                References = i.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    [..baseKeys, "categoryOfAuthority", "rxNCode", "textContent"]),
            },
            S122Restrictions => new S122Restrictions
            {
                Id = i.Id,
                CategoryOfAuthority = AttributeParser.TryParseInt(i.Attributes.GetValueOrDefault("categoryOfAuthority"), ctx, i.Id, "categoryOfAuthority"),
                RxNCode = i.Attributes.GetValueOrDefault("rxNCode"),
                TextContent = i.Attributes.GetValueOrDefault("textContent"),
                References = i.References,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    [..baseKeys, "categoryOfAuthority", "rxNCode", "textContent"]),
            },
            _ => throw new InvalidOperationException("Unexpected RxN concrete type."),
        });
    }

    private static (S122GeometryKind, ImmutableArray<GeoPosition>) ProjectGeometry(S122Feature f)
    {
        switch (f.GeometryType)
        {
            case GmlGeometryType.Point:
                return (S122GeometryKind.Point, f.Points.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Points.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            case GmlGeometryType.Curve:
                return (S122GeometryKind.Curve, f.Curves.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Curves.SelectMany(c => c).Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            case GmlGeometryType.Surface:
                return (S122GeometryKind.Surface, f.ExteriorRing.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.ExteriorRing.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            default:
                return (S122GeometryKind.None, ImmutableArray<GeoPosition>.Empty);
        }
    }
}
