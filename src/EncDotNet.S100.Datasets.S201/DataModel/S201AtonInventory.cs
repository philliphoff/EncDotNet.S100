using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S201.DataModel;

/// <summary>
/// Strongly-typed projection of an <see cref="S201Dataset"/> as an
/// authoritative AtoN inventory (S-201 Edition 2.0.0).
/// </summary>
/// <remarks>
/// <para>
/// The inventory exposes typed AtoN objects (structures, equipment,
/// lights, electronic AIS aids), back-resolved equipment ↔ host-structure
/// subordination chains, lifecycle attributes, AtoN status timelines,
/// and aggregation / association graphs.
/// </para>
/// <para>
/// Projection issues — unresolved xlinks, attribute parse failures,
/// duplicate identifiers — surface as <see cref="ProjectionDiagnostic"/>
/// entries rather than exceptions. The projection only throws when the
/// source dataset has no features and no information types (i.e. is
/// fully empty).
/// </para>
/// <para>
/// <b>Distinct from S-125:</b> the S-201 typed model exposes equipment
/// ↔ host-structure subordination, AtoN lifecycle, AtoN status
/// timelines, positioning / fixing-method bindings, AIS-AtoN MMSI, and
/// remote-monitoring-system metadata — none of which appear in the
/// (ECDIS-focused) S-125 typed model.
/// </para>
/// </remarks>
public sealed class S201AtonInventory
{
    /// <summary>The dataset identifier carried by the source GML <c>Dataset</c> element.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>The S-201 product identifier (typically <c>"S-201"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>Every AtoN object in the dataset, in source order.</summary>
    public required ImmutableArray<S201AtonObject> AtoNs { get; init; }

    /// <summary>Typed view of <see cref="AtoNs"/> filtered to <see cref="S201StructureObject"/>s.</summary>
    public ImmutableArray<S201StructureObject> Structures { get; init; } =
        ImmutableArray<S201StructureObject>.Empty;

    /// <summary>Typed view of <see cref="AtoNs"/> filtered to <see cref="S201Equipment"/> (includes lights).</summary>
    public ImmutableArray<S201Equipment> Equipment { get; init; } =
        ImmutableArray<S201Equipment>.Empty;

    /// <summary>Typed view of <see cref="AtoNs"/> filtered to <see cref="S201ElectronicAtoN"/>s.</summary>
    public ImmutableArray<S201ElectronicAtoN> ElectronicAtoNs { get; init; } =
        ImmutableArray<S201ElectronicAtoN>.Empty;

    /// <summary>AtoN aggregations declared in the dataset.</summary>
    public ImmutableArray<S201AtonAggregation> Aggregations { get; init; } =
        ImmutableArray<S201AtonAggregation>.Empty;

    /// <summary>AtoN associations declared in the dataset.</summary>
    public ImmutableArray<S201AtonAssociation> Associations { get; init; } =
        ImmutableArray<S201AtonAssociation>.Empty;

    /// <summary>AtoN status information records (FC: <c>AtonStatusInformation</c>).</summary>
    public ImmutableArray<S201AtonStatusInformation> StatusInformation { get; init; } =
        ImmutableArray<S201AtonStatusInformation>.Empty;

    /// <summary>Positioning information records (FC: <c>PositioningInformation</c>).</summary>
    public ImmutableArray<S201PositioningInformationRecord> PositioningInformation { get; init; } =
        ImmutableArray<S201PositioningInformationRecord>.Empty;

    /// <summary>Fixing-method records (FC: <c>AtoNFixingMethod</c>).</summary>
    public ImmutableArray<S201AtoNFixingMethodRecord> FixingMethods { get; init; } =
        ImmutableArray<S201AtoNFixingMethodRecord>.Empty;

    /// <summary>Spatial quality records (FC: <c>SpatialQuality</c>).</summary>
    public ImmutableArray<S201SpatialQuality> SpatialQualities { get; init; } =
        ImmutableArray<S201SpatialQuality>.Empty;

    /// <summary>The originating feature-bag dataset.</summary>
    public required S201Dataset Source { get; init; }

    /// <summary>
    /// Projects a feature-bag <see cref="S201Dataset"/> into the typed
    /// data model. Issues encountered during projection are reported
    /// via <paramref name="diagnostics"/>; the projection only throws
    /// for a fully empty dataset.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the dataset contains neither features nor information
    /// types.
    /// </exception>
    public static S201AtonInventory From(S201Dataset dataset, out IReadOnlyList<ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.Features.IsDefaultOrEmpty && dataset.InformationTypes.IsDefaultOrEmpty)
            throw new InvalidOperationException("Dataset contains no features and no information types.");

        var ctx = new ProjectionContext(BuildXlinkResolver(dataset));

        // Pass 1: project information types (no cross-refs).
        var statusInfo = ProjectStatusInformation(dataset, ctx);
        var positioning = ProjectPositioningInformation(dataset, ctx);
        var fixing = ProjectFixingMethods(dataset, ctx);
        var spatialQuality = ProjectSpatialQuality(dataset, ctx);

        var statusInfoById = statusInfo.ToImmutableDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var positioningById = positioning.ToImmutableDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var fixingById = fixing.ToImmutableDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

        // Pass 2: project all AtoN features (cross-refs left empty for now).
        var atons = ImmutableArray.CreateBuilder<S201AtonObject>();
        var aggregationFeatures = new List<S201Feature>();
        var associationFeatures = new List<S201Feature>();

        if (!dataset.Features.IsDefaultOrEmpty)
        {
            foreach (var f in dataset.Features)
            {
                if (IsAggregationContainer(f.FeatureType))
                {
                    aggregationFeatures.Add(f);
                    continue;
                }
                if (IsAssociationContainer(f.FeatureType))
                {
                    associationFeatures.Add(f);
                    continue;
                }

                atons.Add(ProjectAton(f, ctx, statusInfoById, positioningById, fixingById));
            }
        }

        var atonArray = atons.ToImmutable();
        var atonsById = new Dictionary<string, S201AtonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in atonArray)
            atonsById[a.Id] = a;

        // Pass 3: wire equipment ↔ host-structure subordination.
        ResolveSubordination(atonArray, atonsById, ctx);

        // Pass 4: project aggregations / associations (now that AtoNs are indexed).
        var aggregations = ProjectAggregations(aggregationFeatures, atonsById, ctx);
        var associations = ProjectAssociations(associationFeatures, atonsById, ctx);

        // Pass 5: back-fill aggregation / association membership on each AtoN.
        BackFillMembership(atonArray, aggregations, associations);

        // Typed views.
        var structures = atonArray.OfType<S201StructureObject>().ToImmutableArray();
        var equipment = atonArray.OfType<S201Equipment>().ToImmutableArray();
        var electronic = atonArray.OfType<S201ElectronicAtoN>().ToImmutableArray();

        diagnostics = ctx.ToImmutableDiagnostics();
        return new S201AtonInventory
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            AtoNs = atonArray,
            Structures = structures,
            Equipment = equipment,
            ElectronicAtoNs = electronic,
            Aggregations = aggregations,
            Associations = associations,
            StatusInformation = statusInfo,
            PositioningInformation = positioning,
            FixingMethods = fixing,
            SpatialQualities = spatialQuality,
            Source = dataset,
        };
    }

    private static XlinkResolver BuildXlinkResolver(S201Dataset dataset)
    {
        IEnumerable<KeyValuePair<string, object>> All()
        {
            if (!dataset.Features.IsDefaultOrEmpty)
                foreach (var f in dataset.Features)
                    yield return new KeyValuePair<string, object>(f.Id, f);
            if (!dataset.InformationTypes.IsDefaultOrEmpty)
                foreach (var i in dataset.InformationTypes)
                    yield return new KeyValuePair<string, object>(i.Id, i);
        }
        return XlinkResolver.Build(All());
    }

    private static bool IsAggregationContainer(string featureType) =>
        string.Equals(featureType, "AtonAggregation", StringComparison.OrdinalIgnoreCase);

    private static bool IsAssociationContainer(string featureType) =>
        string.Equals(featureType, "AtonAssociation", StringComparison.OrdinalIgnoreCase);

    private static bool IsParentRole(string role) =>
        string.Equals(role, "theParentFeature", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "parent", StringComparison.OrdinalIgnoreCase);

    private static bool IsPeerRole(string role) =>
        string.Equals(role, "peer", StringComparison.OrdinalIgnoreCase);

    // -------------------------------------------------------------------
    // Information-type projections
    // -------------------------------------------------------------------

    private static ImmutableArray<S201AtonStatusInformation> ProjectStatusInformation(S201Dataset dataset, ProjectionContext ctx)
    {
        var b = ImmutableArray.CreateBuilder<S201AtonStatusInformation>();
        if (dataset.InformationTypes.IsDefaultOrEmpty) return b.ToImmutable();
        foreach (var i in dataset.InformationTypes)
        {
            if (!string.Equals(i.TypeCode, "AtonStatusInformation", StringComparison.OrdinalIgnoreCase))
                continue;
            var changeDetails = i.ComplexAttributes
                .FirstOrDefault(c => string.Equals(c.Code, "ChangeDetails", StringComparison.OrdinalIgnoreCase))
                ?.SubAttributes ?? ImmutableDictionary<string, string>.Empty;
            b.Add(new S201AtonStatusInformation
            {
                Id = i.Id,
                ChangeTypes = AttributeParser.TryParseInt(
                    i.Attributes.GetValueOrDefault("ChangeTypes")
                        ?? i.Attributes.GetValueOrDefault("changeTypes"),
                    ctx, i.Id, "ChangeTypes"),
                ChangeDetails = changeDetails,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes, "ChangeTypes", "changeTypes"),
            });
        }
        return b.ToImmutable();
    }

    private static ImmutableArray<S201PositioningInformationRecord> ProjectPositioningInformation(S201Dataset dataset, ProjectionContext ctx)
    {
        var b = ImmutableArray.CreateBuilder<S201PositioningInformationRecord>();
        if (dataset.InformationTypes.IsDefaultOrEmpty) return b.ToImmutable();
        foreach (var i in dataset.InformationTypes)
        {
            if (!string.Equals(i.TypeCode, "PositioningInformation", StringComparison.OrdinalIgnoreCase))
                continue;
            b.Add(new S201PositioningInformationRecord
            {
                Id = i.Id,
                PositioningDevice = i.Attributes.GetValueOrDefault("positioningDevice"),
                PositioningMethod = i.Attributes.GetValueOrDefault("positioningMethod"),
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes, "positioningDevice", "positioningMethod"),
            });
        }
        _ = ctx; // no parse calls yet
        return b.ToImmutable();
    }

    private static ImmutableArray<S201AtoNFixingMethodRecord> ProjectFixingMethods(S201Dataset dataset, ProjectionContext ctx)
    {
        var b = ImmutableArray.CreateBuilder<S201AtoNFixingMethodRecord>();
        if (dataset.InformationTypes.IsDefaultOrEmpty) return b.ToImmutable();
        foreach (var i in dataset.InformationTypes)
        {
            if (!string.Equals(i.TypeCode, "AtoNFixingMethod", StringComparison.OrdinalIgnoreCase))
                continue;
            b.Add(new S201AtoNFixingMethodRecord
            {
                Id = i.Id,
                ReferencePoint = i.Attributes.GetValueOrDefault("referencePoint"),
                HorizontalDatum = AttributeParser.TryParseInt(
                    i.Attributes.GetValueOrDefault("horizontalDatum"), ctx, i.Id, "horizontalDatum"),
                SourceDate = AttributeParser.TryParseDateTimeOffset(
                    i.Attributes.GetValueOrDefault("sourceDate"), ctx, i.Id, "sourceDate"),
                PositioningProcedure = i.Attributes.GetValueOrDefault("positioningProcedure"),
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    "referencePoint", "horizontalDatum", "sourceDate", "positioningProcedure"),
            });
        }
        return b.ToImmutable();
    }

    private static ImmutableArray<S201SpatialQuality> ProjectSpatialQuality(S201Dataset dataset, ProjectionContext ctx)
    {
        var b = ImmutableArray.CreateBuilder<S201SpatialQuality>();
        if (dataset.InformationTypes.IsDefaultOrEmpty) return b.ToImmutable();
        foreach (var i in dataset.InformationTypes)
        {
            if (!string.Equals(i.TypeCode, "SpatialQuality", StringComparison.OrdinalIgnoreCase))
                continue;
            b.Add(new S201SpatialQuality
            {
                Id = i.Id,
                QualityOfHorizontalMeasurement = AttributeParser.TryParseInt(
                    i.Attributes.GetValueOrDefault("qualityOfHorizontalMeasurement"),
                    ctx, i.Id, "qualityOfHorizontalMeasurement"),
                SpatialAccuracy = AttributeParser.TryParseDouble(
                    i.Attributes.GetValueOrDefault("spatialAccuracy"), ctx, i.Id, "spatialAccuracy"),
                ExtraAttributes = ExtraAttributes.ExcludeKnown(i.Attributes,
                    "qualityOfHorizontalMeasurement", "spatialAccuracy"),
            });
        }
        return b.ToImmutable();
    }

    // -------------------------------------------------------------------
    // AtoN projection (dispatch on feature class)
    // -------------------------------------------------------------------

    private static S201AtonObject ProjectAton(S201Feature f, ProjectionContext ctx,
        IReadOnlyDictionary<string, S201AtonStatusInformation> statusInfo,
        IReadOnlyDictionary<string, S201PositioningInformationRecord> positioning,
        IReadOnlyDictionary<string, S201AtoNFixingMethodRecord> fixing)
    {
        // Resolve AtoNStatus information bindings.
        var status = ResolveStatusInformation(f, ctx, statusInfo);
        var (kind, coords) = ProjectGeometry(f);

        // Light? → S201Light (Equipment subclass)
        if (TryGetLightKind(f.FeatureType, out var lightKind))
        {
            return new S201Light
            {
                Id = f.Id,
                FeatureClass = f.FeatureType,
                Source = f,
                GeometryKind = kind,
                Coordinates = coords,
                Kind = lightKind,
                IdCode = f.Attributes.GetValueOrDefault("iDCode"),
                Information = f.Attributes.GetValueOrDefault("information"),
                FeatureNames = ProjectFeatureNames(f, ctx),
                ScaleMinimum = AttributeParser.TryParseInt(
                    f.Attributes.GetValueOrDefault("scaleMinimum"), ctx, f.Id, "scaleMinimum"),
                SourceDate = AttributeParser.TryParseDateTimeOffset(
                    f.Attributes.GetValueOrDefault("sourceDate"), ctx, f.Id, "sourceDate"),
                SourceText = f.Attributes.GetValueOrDefault("source"),
                PictorialRepresentation = f.Attributes.GetValueOrDefault("pictorialRepresentation"),
                InspectionFrequency = f.Attributes.GetValueOrDefault("inspectionFrequency"),
                InspectionRequirements = f.Attributes.GetValueOrDefault("inspectionRequirements"),
                AtoNMaintenanceRecord = f.Attributes.GetValueOrDefault("aToNMaintenanceRecord"),
                InstallationDate = AttributeParser.TryParseDateTimeOffset(
                    f.Attributes.GetValueOrDefault("installationDate"), ctx, f.Id, "installationDate"),
                FixedDateRange = ProjectDateRange(f, "fixedDateRange", ctx),
                PeriodicDateRange = ProjectDateRange(f, "periodicDateRange", ctx),
                SeasonalActionRequired = ImmutableArray<string>.Empty,
                StatusInformation = status,
                RemoteMonitoringSystem = f.Attributes.GetValueOrDefault("remoteMonitoringSystem"),
                Height = AttributeParser.TryParseDouble(
                    f.Attributes.GetValueOrDefault("height"), ctx, f.Id, "height"),
                Status = ParseIntList(f.Attributes.GetValueOrDefault("status"), ctx, f.Id, "status"),
                VerticalDatum = AttributeParser.TryParseInt(
                    f.Attributes.GetValueOrDefault("verticalDatum"), ctx, f.Id, "verticalDatum"),
                VerticalLength = AttributeParser.TryParseDouble(
                    f.Attributes.GetValueOrDefault("verticalLength"), ctx, f.Id, "verticalLength"),
                EffectiveIntensity = AttributeParser.TryParseDouble(
                    f.Attributes.GetValueOrDefault("effectiveIntensity"), ctx, f.Id, "effectiveIntensity"),
                PeakIntensity = AttributeParser.TryParseDouble(
                    f.Attributes.GetValueOrDefault("peakIntensity"), ctx, f.Id, "peakIntensity"),
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, LightKnownAttributes),
            };
        }

        // Electronic AIS AtoN?
        if (TryGetAisAtonKind(f.FeatureType, out var aisKind))
        {
            return new S201ElectronicAtoN
            {
                Id = f.Id,
                FeatureClass = f.FeatureType,
                Source = f,
                GeometryKind = kind,
                Coordinates = coords,
                Kind = aisKind,
                IdCode = f.Attributes.GetValueOrDefault("iDCode"),
                Information = f.Attributes.GetValueOrDefault("information"),
                FeatureNames = ProjectFeatureNames(f, ctx),
                ScaleMinimum = AttributeParser.TryParseInt(
                    f.Attributes.GetValueOrDefault("scaleMinimum"), ctx, f.Id, "scaleMinimum"),
                SourceDate = AttributeParser.TryParseDateTimeOffset(
                    f.Attributes.GetValueOrDefault("sourceDate"), ctx, f.Id, "sourceDate"),
                SourceText = f.Attributes.GetValueOrDefault("source"),
                PictorialRepresentation = f.Attributes.GetValueOrDefault("pictorialRepresentation"),
                InspectionFrequency = f.Attributes.GetValueOrDefault("inspectionFrequency"),
                InspectionRequirements = f.Attributes.GetValueOrDefault("inspectionRequirements"),
                AtoNMaintenanceRecord = f.Attributes.GetValueOrDefault("aToNMaintenanceRecord"),
                InstallationDate = AttributeParser.TryParseDateTimeOffset(
                    f.Attributes.GetValueOrDefault("installationDate"), ctx, f.Id, "installationDate"),
                FixedDateRange = ProjectDateRange(f, "fixedDateRange", ctx),
                PeriodicDateRange = ProjectDateRange(f, "periodicDateRange", ctx),
                SeasonalActionRequired = ImmutableArray<string>.Empty,
                StatusInformation = status,
                AtoNNumber = f.Attributes.GetValueOrDefault("AtoNNumber"),
                MmsiCode = f.Attributes.GetValueOrDefault("mMSICode"),
                Status = ParseIntList(f.Attributes.GetValueOrDefault("status"), ctx, f.Id, "status"),
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, ElectronicKnownAttributes),
            };
        }

        // Equipment? (anything with theParentFeature reference but not a light or AIS — heuristic
        // based on FC `superType=Equipment`; we use the role/structural cue here.)
        var hasParentRef = !f.FeatureReferences.IsDefaultOrEmpty
            && f.FeatureReferences.Any(r => IsParentRole(r.Role));
        if (hasParentRef && !IsKnownStructureType(f.FeatureType))
        {
            return new S201Equipment
            {
                Id = f.Id,
                FeatureClass = f.FeatureType,
                Source = f,
                GeometryKind = kind,
                Coordinates = coords,
                IdCode = f.Attributes.GetValueOrDefault("iDCode"),
                Information = f.Attributes.GetValueOrDefault("information"),
                FeatureNames = ProjectFeatureNames(f, ctx),
                ScaleMinimum = AttributeParser.TryParseInt(
                    f.Attributes.GetValueOrDefault("scaleMinimum"), ctx, f.Id, "scaleMinimum"),
                SourceDate = AttributeParser.TryParseDateTimeOffset(
                    f.Attributes.GetValueOrDefault("sourceDate"), ctx, f.Id, "sourceDate"),
                SourceText = f.Attributes.GetValueOrDefault("source"),
                PictorialRepresentation = f.Attributes.GetValueOrDefault("pictorialRepresentation"),
                InspectionFrequency = f.Attributes.GetValueOrDefault("inspectionFrequency"),
                InspectionRequirements = f.Attributes.GetValueOrDefault("inspectionRequirements"),
                AtoNMaintenanceRecord = f.Attributes.GetValueOrDefault("aToNMaintenanceRecord"),
                InstallationDate = AttributeParser.TryParseDateTimeOffset(
                    f.Attributes.GetValueOrDefault("installationDate"), ctx, f.Id, "installationDate"),
                FixedDateRange = ProjectDateRange(f, "fixedDateRange", ctx),
                PeriodicDateRange = ProjectDateRange(f, "periodicDateRange", ctx),
                SeasonalActionRequired = ImmutableArray<string>.Empty,
                StatusInformation = status,
                RemoteMonitoringSystem = f.Attributes.GetValueOrDefault("remoteMonitoringSystem"),
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, EquipmentKnownAttributes),
            };
        }

        // Structure? (anything with structure-typed attributes or known structure-class names.)
        if (IsKnownStructureType(f.FeatureType) || HasStructureSignal(f))
        {
            var positioningInfo = ResolvePositioningInformation(f, ctx, positioning);
            var fixingInfo = ResolveFixingMethods(f, ctx, fixing);

            return new S201StructureObject
            {
                Id = f.Id,
                FeatureClass = f.FeatureType,
                Source = f,
                GeometryKind = kind,
                Coordinates = coords,
                IdCode = f.Attributes.GetValueOrDefault("iDCode"),
                Information = f.Attributes.GetValueOrDefault("information"),
                FeatureNames = ProjectFeatureNames(f, ctx),
                ScaleMinimum = AttributeParser.TryParseInt(
                    f.Attributes.GetValueOrDefault("scaleMinimum"), ctx, f.Id, "scaleMinimum"),
                SourceDate = AttributeParser.TryParseDateTimeOffset(
                    f.Attributes.GetValueOrDefault("sourceDate"), ctx, f.Id, "sourceDate"),
                SourceText = f.Attributes.GetValueOrDefault("source"),
                PictorialRepresentation = f.Attributes.GetValueOrDefault("pictorialRepresentation"),
                InspectionFrequency = f.Attributes.GetValueOrDefault("inspectionFrequency"),
                InspectionRequirements = f.Attributes.GetValueOrDefault("inspectionRequirements"),
                AtoNMaintenanceRecord = f.Attributes.GetValueOrDefault("aToNMaintenanceRecord"),
                InstallationDate = AttributeParser.TryParseDateTimeOffset(
                    f.Attributes.GetValueOrDefault("installationDate"), ctx, f.Id, "installationDate"),
                FixedDateRange = ProjectDateRange(f, "fixedDateRange", ctx),
                PeriodicDateRange = ProjectDateRange(f, "periodicDateRange", ctx),
                SeasonalActionRequired = ImmutableArray<string>.Empty,
                StatusInformation = status,
                AtoNNumber = f.Attributes.GetValueOrDefault("AtoNNumber"),
                AidAvailabilityCategory = AttributeParser.TryParseInt(
                    f.Attributes.GetValueOrDefault("aidAvailabilityCategory"), ctx, f.Id, "aidAvailabilityCategory"),
                Condition = AttributeParser.TryParseInt(
                    f.Attributes.GetValueOrDefault("condition"), ctx, f.Id, "condition"),
                ContactAddress = f.Attributes.GetValueOrDefault("contactAddress"),
                PositioningInformation = positioningInfo,
                FixingMethods = fixingInfo,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, StructureKnownAttributes),
            };
        }

        // Fallback: generic AtoN object (NavigationLine, DangerousFeature, DataCoverage, etc.).
        return new S201GenericAtonObject
        {
            Id = f.Id,
            FeatureClass = f.FeatureType,
            Source = f,
            GeometryKind = kind,
            Coordinates = coords,
            IdCode = f.Attributes.GetValueOrDefault("iDCode"),
            Information = f.Attributes.GetValueOrDefault("information"),
            FeatureNames = ProjectFeatureNames(f, ctx),
            ScaleMinimum = AttributeParser.TryParseInt(
                f.Attributes.GetValueOrDefault("scaleMinimum"), ctx, f.Id, "scaleMinimum"),
            SourceDate = AttributeParser.TryParseDateTimeOffset(
                f.Attributes.GetValueOrDefault("sourceDate"), ctx, f.Id, "sourceDate"),
            SourceText = f.Attributes.GetValueOrDefault("source"),
            PictorialRepresentation = f.Attributes.GetValueOrDefault("pictorialRepresentation"),
            InspectionFrequency = f.Attributes.GetValueOrDefault("inspectionFrequency"),
            InspectionRequirements = f.Attributes.GetValueOrDefault("inspectionRequirements"),
            AtoNMaintenanceRecord = f.Attributes.GetValueOrDefault("aToNMaintenanceRecord"),
            InstallationDate = AttributeParser.TryParseDateTimeOffset(
                f.Attributes.GetValueOrDefault("installationDate"), ctx, f.Id, "installationDate"),
            FixedDateRange = ProjectDateRange(f, "fixedDateRange", ctx),
            PeriodicDateRange = ProjectDateRange(f, "periodicDateRange", ctx),
            SeasonalActionRequired = ImmutableArray<string>.Empty,
            StatusInformation = status,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, CommonKnownAttributes),
        };
    }

    private static readonly string[] CommonKnownAttributes =
    {
        "iDCode", "information", "scaleMinimum", "sourceDate", "source",
        "pictorialRepresentation", "inspectionFrequency", "inspectionRequirements",
        "aToNMaintenanceRecord", "installationDate",
    };

    private static readonly string[] EquipmentKnownAttributes =
    {
        "iDCode", "information", "scaleMinimum", "sourceDate", "source",
        "pictorialRepresentation", "inspectionFrequency", "inspectionRequirements",
        "aToNMaintenanceRecord", "installationDate", "remoteMonitoringSystem",
    };

    private static readonly string[] LightKnownAttributes =
    {
        "iDCode", "information", "scaleMinimum", "sourceDate", "source",
        "pictorialRepresentation", "inspectionFrequency", "inspectionRequirements",
        "aToNMaintenanceRecord", "installationDate", "remoteMonitoringSystem",
        "height", "status", "verticalDatum", "verticalLength",
        "effectiveIntensity", "peakIntensity",
    };

    private static readonly string[] ElectronicKnownAttributes =
    {
        "iDCode", "information", "scaleMinimum", "sourceDate", "source",
        "pictorialRepresentation", "inspectionFrequency", "inspectionRequirements",
        "aToNMaintenanceRecord", "installationDate",
        "AtoNNumber", "mMSICode", "status",
    };

    private static readonly string[] StructureKnownAttributes =
    {
        "iDCode", "information", "scaleMinimum", "sourceDate", "source",
        "pictorialRepresentation", "inspectionFrequency", "inspectionRequirements",
        "aToNMaintenanceRecord", "installationDate",
        "AtoNNumber", "aidAvailabilityCategory", "condition", "contactAddress",
    };

    private static bool TryGetLightKind(string featureType, out LightKind kind)
    {
        switch (featureType)
        {
            case "LightSectored": kind = LightKind.Sectored; return true;
            case "LightAllAround": kind = LightKind.AllAround; return true;
            case "LightAirObstruction": kind = LightKind.AirObstruction; return true;
            case "LightFogDetector": kind = LightKind.FogDetector; return true;
            case "Light":
                // Generic placeholder used in some encodings — preserve via Unknown kind.
                kind = LightKind.Unknown;
                return true;
        }
        kind = LightKind.Unknown;
        return false;
    }

    private static bool TryGetAisAtonKind(string featureType, out AisAtonKind kind)
    {
        switch (featureType)
        {
            case "VirtualAISAidToNavigation": kind = AisAtonKind.Virtual; return true;
            case "PhysicalAISAidToNavigation": kind = AisAtonKind.Physical; return true;
            case "SyntheticAISAidToNavigation": kind = AisAtonKind.Synthetic; return true;
        }
        kind = AisAtonKind.Unknown;
        return false;
    }

    // Direct StructureObject concrete leaves declared in S-201 Edition 2.0.0 Annex C.
    // This list intentionally excludes Equipment / GenericLight / ElectronicAton subclasses,
    // dataset-metadata features (DataCoverage, etc.), and direct AidsToNavigation children
    // that are not structures (NavigationLine, Topmark, …).
    private static readonly HashSet<string> KnownStructureTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Direct StructureObject concretes and their FC-leaf subclasses.
        "Landmark", "Lighthouse", "LightFloat", "LightVessel",
        "OffshorePlatform", "SiloTank", "Pile", "Building", "Bridge",
        // Beacons.
        "CardinalBeacon", "IsolatedDangerBeacon", "LateralBeacon",
        "SafeWaterBeacon", "SpecialPurposeGeneralBeacon",
        // Buoys.
        "CardinalBuoy", "InstallationBuoy", "IsolatedDangerBuoy",
        "LateralBuoy", "SafeWaterBuoy", "SpecialPurposeGeneralBuoy",
        "EmergencyWreckMarkingBuoy",
        // Generic abstract supertypes used as concrete in some encodings.
        "GenericBeacon", "GenericBuoy", "StructureObject",
    };

    private static bool IsKnownStructureType(string featureType) =>
        KnownStructureTypes.Contains(featureType);

    private static bool HasStructureSignal(S201Feature f) =>
        f.Attributes.ContainsKey("AtoNNumber")
        || f.Attributes.ContainsKey("aidAvailabilityCategory")
        || f.Attributes.ContainsKey("condition")
        || f.Attributes.ContainsKey("contactAddress");

    // -------------------------------------------------------------------
    // Per-feature helpers
    // -------------------------------------------------------------------

    private static ImmutableArray<S201FeatureNameRecord> ProjectFeatureNames(S201Feature f, ProjectionContext ctx)
    {
        if (f.ComplexAttributes.IsDefaultOrEmpty) return ImmutableArray<S201FeatureNameRecord>.Empty;
        var b = ImmutableArray.CreateBuilder<S201FeatureNameRecord>();
        foreach (var ca in f.ComplexAttributes)
        {
            if (!string.Equals(ca.Code, "featureName", StringComparison.OrdinalIgnoreCase))
                continue;
            b.Add(new S201FeatureNameRecord
            {
                Name = ca.SubAttributes.GetValueOrDefault("name"),
                Language = ca.SubAttributes.GetValueOrDefault("language"),
                DisplayName = AttributeParser.TryParseBool(
                    ca.SubAttributes.GetValueOrDefault("displayName"), ctx, f.Id, "displayName"),
            });
        }
        return b.ToImmutable();
    }

    private static S201DateRange? ProjectDateRange(S201Feature f, string code, ProjectionContext ctx)
    {
        if (f.ComplexAttributes.IsDefaultOrEmpty) return null;
        foreach (var ca in f.ComplexAttributes)
        {
            if (!string.Equals(ca.Code, code, StringComparison.OrdinalIgnoreCase))
                continue;
            var start = AttributeParser.TryParseDateTimeOffset(
                ca.SubAttributes.GetValueOrDefault("dateStart"), ctx, f.Id, $"{code}/dateStart");
            var end = AttributeParser.TryParseDateTimeOffset(
                ca.SubAttributes.GetValueOrDefault("dateEnd"), ctx, f.Id, $"{code}/dateEnd");
            if (start is null && end is null) return null;
            return new S201DateRange { Start = start, End = end };
        }
        return null;
    }

    private static ImmutableArray<int> ParseIntList(string? value, ProjectionContext ctx,
        string? relatedId, string? attributeName)
    {
        if (string.IsNullOrEmpty(value)) return ImmutableArray<int>.Empty;
        var b = ImmutableArray.CreateBuilder<int>();
        foreach (var token in value.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var n = AttributeParser.TryParseInt(token, ctx, relatedId, attributeName);
            if (n.HasValue) b.Add(n.Value);
        }
        return b.ToImmutable();
    }

    private static ImmutableArray<S201AtonStatusInformation> ResolveStatusInformation(S201Feature f,
        ProjectionContext ctx, IReadOnlyDictionary<string, S201AtonStatusInformation> byId)
    {
        if (f.InformationReferences.IsDefaultOrEmpty) return ImmutableArray<S201AtonStatusInformation>.Empty;
        var b = ImmutableArray.CreateBuilder<S201AtonStatusInformation>();
        foreach (var r in f.InformationReferences)
        {
            if (!string.Equals(r.Role, "AtoNStatus", StringComparison.OrdinalIgnoreCase))
                continue;
            if (byId.TryGetValue(r.InformationRef, out var info))
                b.Add(info);
            else
                ctx.Warn(
                    $"Unresolved AtoNStatus reference '{r.InformationRef}'.",
                    code: "xlink.unresolved", relatedId: f.Id, relatedAttribute: r.Role);
        }
        return b.ToImmutable();
    }

    private static ImmutableArray<S201PositioningInformationRecord> ResolvePositioningInformation(S201Feature f,
        ProjectionContext ctx, IReadOnlyDictionary<string, S201PositioningInformationRecord> byId)
    {
        if (f.InformationReferences.IsDefaultOrEmpty) return ImmutableArray<S201PositioningInformationRecord>.Empty;
        var b = ImmutableArray.CreateBuilder<S201PositioningInformationRecord>();
        foreach (var r in f.InformationReferences)
        {
            if (!string.Equals(r.Role, "positioningMethod", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.Role, "Positioning", StringComparison.OrdinalIgnoreCase))
                continue;
            if (byId.TryGetValue(r.InformationRef, out var info))
                b.Add(info);
            else
                ctx.Warn(
                    $"Unresolved positioningMethod reference '{r.InformationRef}'.",
                    code: "xlink.unresolved", relatedId: f.Id, relatedAttribute: r.Role);
        }
        return b.ToImmutable();
    }

    private static ImmutableArray<S201AtoNFixingMethodRecord> ResolveFixingMethods(S201Feature f,
        ProjectionContext ctx, IReadOnlyDictionary<string, S201AtoNFixingMethodRecord> byId)
    {
        if (f.InformationReferences.IsDefaultOrEmpty) return ImmutableArray<S201AtoNFixingMethodRecord>.Empty;
        var b = ImmutableArray.CreateBuilder<S201AtoNFixingMethodRecord>();
        foreach (var r in f.InformationReferences)
        {
            if (!string.Equals(r.Role, "fixingMethod", StringComparison.OrdinalIgnoreCase))
                continue;
            if (byId.TryGetValue(r.InformationRef, out var info))
                b.Add(info);
            else
                ctx.Warn(
                    $"Unresolved fixingMethod reference '{r.InformationRef}'.",
                    code: "xlink.unresolved", relatedId: f.Id, relatedAttribute: r.Role);
        }
        return b.ToImmutable();
    }

    // -------------------------------------------------------------------
    // Pass 3: equipment ↔ host-structure subordination
    // -------------------------------------------------------------------

    private static void ResolveSubordination(ImmutableArray<S201AtonObject> atons,
        IReadOnlyDictionary<string, S201AtonObject> byId, ProjectionContext ctx)
    {
        var perStructureMounted = new Dictionary<string, ImmutableArray<S201Equipment>.Builder>(StringComparer.OrdinalIgnoreCase);

        foreach (var atom in atons)
        {
            if (atom is not S201Equipment eq) continue;
            if (atom.Source.FeatureReferences.IsDefaultOrEmpty) continue;
            foreach (var r in atom.Source.FeatureReferences)
            {
                if (!IsParentRole(r.Role)) continue;
                if (!byId.TryGetValue(r.TargetRef, out var target))
                {
                    ctx.Warn(
                        $"Unresolved {r.Role} reference '{r.TargetRef}'.",
                        code: "xlink.unresolved", relatedId: atom.Id, relatedAttribute: r.Role);
                    continue;
                }
                if (target is not S201StructureObject structure)
                {
                    ctx.Warn(
                        $"{r.Role} target '{r.TargetRef}' is not a structure (got {target.GetType().Name}).",
                        code: "xlink.unexpectedType", relatedId: atom.Id, relatedAttribute: r.Role);
                    continue;
                }
                eq.HostStructure = structure;
                if (!perStructureMounted.TryGetValue(structure.Id, out var builder))
                {
                    builder = ImmutableArray.CreateBuilder<S201Equipment>();
                    perStructureMounted[structure.Id] = builder;
                }
                builder.Add(eq);
                break; // one host per equipment per FC
            }
        }

        // Also resolve electronic-AtoN host where present (Physical / Synthetic).
        foreach (var atom in atons)
        {
            if (atom is not S201ElectronicAtoN electronic) continue;
            if (atom.Source.FeatureReferences.IsDefaultOrEmpty) continue;
            foreach (var r in atom.Source.FeatureReferences)
            {
                if (!IsParentRole(r.Role)) continue;
                if (!byId.TryGetValue(r.TargetRef, out var target))
                {
                    ctx.Warn(
                        $"Unresolved {r.Role} reference '{r.TargetRef}'.",
                        code: "xlink.unresolved", relatedId: atom.Id, relatedAttribute: r.Role);
                    continue;
                }
                if (target is S201StructureObject structure)
                    electronic.HostStructure = structure;
                break;
            }
        }

        // Materialise MountedEquipment on each structure.
        foreach (var atom in atons)
        {
            if (atom is not S201StructureObject structure) continue;
            if (!perStructureMounted.TryGetValue(structure.Id, out var builder)) continue;
            structure.MountedEquipment = builder.ToImmutable();
        }
    }

    // -------------------------------------------------------------------
    // Pass 4: aggregations / associations
    // -------------------------------------------------------------------

    private static ImmutableArray<S201AtonAggregation> ProjectAggregations(
        List<S201Feature> features, IReadOnlyDictionary<string, S201AtonObject> byId, ProjectionContext ctx)
    {
        var b = ImmutableArray.CreateBuilder<S201AtonAggregation>(features.Count);
        foreach (var f in features)
        {
            var peers = ResolvePeers(f, byId, ctx);
            b.Add(new S201AtonAggregation
            {
                Id = f.Id,
                CategoryOfAssociation = AttributeParser.TryParseInt(
                    f.Attributes.GetValueOrDefault("CategoryOfAssociation")
                        ?? f.Attributes.GetValueOrDefault("categoryOfAssociation"),
                    ctx, f.Id, "CategoryOfAssociation"),
                Peers = peers,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes,
                    "CategoryOfAssociation", "categoryOfAssociation"),
            });
        }
        return b.ToImmutable();
    }

    private static ImmutableArray<S201AtonAssociation> ProjectAssociations(
        List<S201Feature> features, IReadOnlyDictionary<string, S201AtonObject> byId, ProjectionContext ctx)
    {
        var b = ImmutableArray.CreateBuilder<S201AtonAssociation>(features.Count);
        foreach (var f in features)
        {
            var peers = ResolvePeers(f, byId, ctx);
            b.Add(new S201AtonAssociation
            {
                Id = f.Id,
                CategoryOfAssociation = AttributeParser.TryParseInt(
                    f.Attributes.GetValueOrDefault("CategoryOfAssociation")
                        ?? f.Attributes.GetValueOrDefault("categoryOfAssociation"),
                    ctx, f.Id, "CategoryOfAssociation"),
                Peers = peers,
                ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes,
                    "CategoryOfAssociation", "categoryOfAssociation"),
            });
        }
        return b.ToImmutable();
    }

    private static ImmutableArray<S201AtonObject> ResolvePeers(S201Feature f,
        IReadOnlyDictionary<string, S201AtonObject> byId, ProjectionContext ctx)
    {
        if (f.FeatureReferences.IsDefaultOrEmpty) return ImmutableArray<S201AtonObject>.Empty;
        var b = ImmutableArray.CreateBuilder<S201AtonObject>();
        foreach (var r in f.FeatureReferences)
        {
            if (!IsPeerRole(r.Role)) continue;
            if (byId.TryGetValue(r.TargetRef, out var target))
                b.Add(target);
            else
                ctx.Warn(
                    $"Unresolved peer reference '{r.TargetRef}'.",
                    code: "xlink.unresolved", relatedId: f.Id, relatedAttribute: r.Role);
        }
        return b.ToImmutable();
    }

    private static void BackFillMembership(ImmutableArray<S201AtonObject> atons,
        ImmutableArray<S201AtonAggregation> aggregations,
        ImmutableArray<S201AtonAssociation> associations)
    {
        if (aggregations.IsDefaultOrEmpty && associations.IsDefaultOrEmpty) return;

        var aggBuilders = new Dictionary<string, ImmutableArray<S201AtonAggregation>.Builder>(StringComparer.OrdinalIgnoreCase);
        var assocBuilders = new Dictionary<string, ImmutableArray<S201AtonAssociation>.Builder>(StringComparer.OrdinalIgnoreCase);

        foreach (var agg in aggregations)
        {
            foreach (var peer in agg.Peers)
            {
                if (!aggBuilders.TryGetValue(peer.Id, out var bld))
                {
                    bld = ImmutableArray.CreateBuilder<S201AtonAggregation>();
                    aggBuilders[peer.Id] = bld;
                }
                bld.Add(agg);
            }
        }
        foreach (var ass in associations)
        {
            foreach (var peer in ass.Peers)
            {
                if (!assocBuilders.TryGetValue(peer.Id, out var bld))
                {
                    bld = ImmutableArray.CreateBuilder<S201AtonAssociation>();
                    assocBuilders[peer.Id] = bld;
                }
                bld.Add(ass);
            }
        }

        foreach (var atom in atons)
        {
            if (aggBuilders.TryGetValue(atom.Id, out var aggs))
                atom.Aggregations = aggs.ToImmutable();
            if (assocBuilders.TryGetValue(atom.Id, out var ass))
                atom.Associations = ass.ToImmutable();
        }
    }

    // -------------------------------------------------------------------
    // Geometry
    // -------------------------------------------------------------------

    private static (S201GeometryKind, ImmutableArray<GeoPosition>) ProjectGeometry(S201Feature f)
    {
        switch (f.GeometryType)
        {
            case GmlGeometryType.Point:
                return (S201GeometryKind.Point, f.Points.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Points.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            case GmlGeometryType.Curve:
                return (S201GeometryKind.Curve, f.Curves.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Curves.SelectMany(c => c).Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            case GmlGeometryType.Surface:
                return (S201GeometryKind.Surface, f.ExteriorRing.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.ExteriorRing.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray());
            default:
                return (S201GeometryKind.None, ImmutableArray<GeoPosition>.Empty);
        }
    }
}
