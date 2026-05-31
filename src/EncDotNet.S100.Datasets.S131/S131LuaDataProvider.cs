using System.Collections.Immutable;
using System.Globalization;
using EncDotNet.S100.Features;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.S131;

/// <summary>
/// Implements the S-100 Part 9A Lua Host API functions by bridging an
/// <see cref="S131Dataset"/> (GML-encoded) and <see cref="FeatureCatalogue"/>
/// to an <see cref="ILuaContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// S-131 is the first product in this codebase that combines GML data
/// encoding (S-100 Part 10b) with Lua portrayal (S-100 Part 9A). The host
/// API surface is identical to S-101 (<see cref="S101LuaDataProvider"/>),
/// but the underlying data source is a parsed GML document rather than
/// ISO 8211 records.
/// </para>
/// <para>
/// <b>ID mapping</b>: GML uses string <c>gml:id</c> identifiers; the Lua
/// API expects <c>double</c> feature IDs. This provider assigns sequential
/// integer IDs starting at 1 to features and 100_001 to information types,
/// maintaining a bidirectional index.
/// </para>
/// <para>
/// <b>Spatial records</b>: S-101 features reference separate spatial records
/// (RCNM 110/120/130). S-131 GML features embed geometry directly. This
/// provider synthesizes spatial association structures from the feature's
/// own geometry so the Lua shims can construct proper Spatial objects.
/// </para>
/// </remarks>
public sealed class S131LuaDataProvider
{
    // Synthetic spatial IDs are formed as "{featureNumericId}:geom".
    // Spatial "types" mirror S-101 RCNM values for the Lua shims.

    private readonly S131Dataset _dataset;
    private readonly FeatureCatalogue _fc;

    // ID mappings: sequential integer ↔ GML gml:id
    private readonly Dictionary<double, S131Feature> _featureById = new();
    private readonly Dictionary<double, S131InformationType> _infoById = new();
    private readonly Dictionary<string, double> _gmlIdToNumericId = new(StringComparer.Ordinal);

    // FC lookups
    private Dictionary<string, FeatureType>? _featureTypeByCode;
    private Dictionary<string, InformationType>? _infoTypeByCode;
    private Dictionary<string, SimpleAttribute>? _simpleAttrByCode;
    private Dictionary<string, ComplexAttribute>? _complexAttrByCode;

    private readonly List<(string FeatureRef, string Instructions, string ObservedParams)> _emitted = new();

    /// <summary>
    /// Resolves a Lua-side feature reference (the stringified synthetic
    /// numeric id passed to <c>HostPortrayalEmit</c>) to its S-131
    /// feature-type code (e.g. <c>Berth</c>, <c>MooringBuoy</c>).
    /// </summary>
    /// <returns>
    /// The FC code on success, or <see langword="null"/> when
    /// <paramref name="featureRef"/> cannot be parsed or no feature is
    /// associated with the id.
    /// </returns>
    public string? TryGetFeatureTypeCode(string featureRef)
    {
        if (string.IsNullOrEmpty(featureRef)) return null;
        if (!double.TryParse(featureRef, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }
        return _featureById.TryGetValue(id, out var feat) ? feat.FeatureType : null;
    }

    /// <summary>
    /// Initialises a new <see cref="S131LuaDataProvider"/> for the given dataset
    /// and feature catalogue.
    /// </summary>
    public S131LuaDataProvider(S131Dataset dataset, FeatureCatalogue featureCatalogue)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(featureCatalogue);

        _dataset = dataset;
        _fc = featureCatalogue;

        // Assign sequential IDs to features (1-based)
        double id = 1;
        foreach (var feat in dataset.Features)
        {
            _featureById[id] = feat;
            _gmlIdToNumericId[feat.Id] = id;
            id++;
        }

        // Assign sequential IDs to information types (offset to avoid collisions)
        double infoId = 100_001;
        foreach (var info in dataset.InformationTypes)
        {
            _infoById[infoId] = info;
            _gmlIdToNumericId[info.Id] = infoId;
            infoId++;
        }
    }

    /// <summary>Drawing instructions emitted during portrayal execution.</summary>
    public IReadOnlyList<(string FeatureRef, string Instructions, string ObservedParams)> EmittedInstructions => _emitted;

    /// <summary>
    /// Registers all Host* functions and the Debug table on the given Lua context.
    /// The registered functions implement the S-100 Part 9A host interface that the
    /// S-131 Lua portrayal rules call.
    /// </summary>
    public void RegisterHostFunctions(ILuaContext lua)
    {
        // Debug table
        lua.SetGlobal("HostDebugTrace", (Action<string>)(msg => Console.WriteLine($"[S131 Lua] {msg}")));
        lua.Execute("""
            Debug = {
                Trace = function(msg) if msg then HostDebugTrace(tostring(msg)) end end,
                Break = function() end,
                StartPerformance = function(...) end,
                StopPerformance = function(...) end,
                ResetPerformance = function(...) end,
                FirstChanceError = function(...) end,
            }
            """);

        // Feature data access
        lua.SetGlobal("HostGetFeatureIDs", (Func<List<object>>)HostGetFeatureIDs);
        lua.SetGlobal("HostFeatureGetCode", (Func<double, string>)HostFeatureGetCode);
        lua.SetGlobal("HostFeatureGetSimpleAttribute",
            (Func<double, string, string, List<object>>)HostFeatureGetSimpleAttribute);
        lua.SetGlobal("HostFeatureGetComplexAttributeCount",
            (Func<double, string, string, double>)HostFeatureGetComplexAttributeCount);
        lua.SetGlobal("HostFeatureGetSpatialAssociations",
            (Func<double, object?>)HostFeatureGetSpatialAssociations);
        lua.SetGlobal("HostFeatureGetAssociatedFeatureIDs",
            (Func<double, string, string?, List<object>>)HostFeatureGetAssociatedFeatureIDs);
        lua.SetGlobal("HostFeatureGetAssociatedInformationIDs",
            (Func<double, string, string?, List<object>>)HostFeatureGetAssociatedInformationIDs);

        // Information type data access
        lua.SetGlobal("HostInformationTypeGetCode", (Func<double, string>)HostInformationTypeGetCode);
        lua.SetGlobal("HostInformationTypeGetSimpleAttribute",
            (Func<double, string, string, List<object>>)HostInformationTypeGetSimpleAttribute);
        lua.SetGlobal("HostInformationTypeGetComplexAttributeCount",
            (Func<double, string, string, double>)HostInformationTypeGetComplexAttributeCount);

        // Spatial data access
        lua.SetGlobal("HostGetSpatialData", (Func<string, object?>)HostGetSpatialData);
        lua.SetGlobal("HostSpatialGetAssociatedFeatureIDs",
            (Func<string, List<object>>)HostSpatialGetAssociatedFeatureIDs);
        lua.SetGlobal("HostSpatialGetAssociatedInformationIDs",
            (Func<string, string, string?, List<object>>)HostSpatialGetAssociatedInformationIDs);

        // Type system (Feature Catalogue)
        lua.SetGlobal("HostGetFeatureTypeCodes", (Func<List<object>>)HostGetFeatureTypeCodes);
        lua.SetGlobal("HostGetInformationTypeCodes", (Func<List<object>>)HostGetInformationTypeCodes);
        lua.SetGlobal("HostGetSimpleAttributeTypeCodes", (Func<List<object>>)HostGetSimpleAttributeTypeCodes);
        lua.SetGlobal("HostGetComplexAttributeTypeCodes", (Func<List<object>>)HostGetComplexAttributeTypeCodes);
        lua.SetGlobal("HostGetRoleTypeCodes", (Func<List<object>>)HostGetRoleTypeCodes);
        lua.SetGlobal("HostGetInformationAssociationTypeCodes", (Func<List<object>>)HostGetInformationAssociationTypeCodes);
        lua.SetGlobal("HostGetFeatureAssociationTypeCodes", (Func<List<object>>)HostGetFeatureAssociationTypeCodes);
        lua.SetGlobal("HostGetFeatureTypeInfo",
            (Func<string, IReadOnlyDictionary<string, object?>>)HostGetFeatureTypeInfo);
        lua.SetGlobal("HostGetInformationTypeInfo",
            (Func<string, IReadOnlyDictionary<string, object?>>)HostGetInformationTypeInfo);
        lua.SetGlobal("HostGetSimpleAttributeTypeInfo",
            (Func<string, IReadOnlyDictionary<string, object?>>)HostGetSimpleAttributeTypeInfo);
        lua.SetGlobal("HostGetComplexAttributeTypeInfo",
            (Func<string, IReadOnlyDictionary<string, object?>>)HostGetComplexAttributeTypeInfo);

        // Output
        lua.SetGlobal("HostPortrayalEmit",
            (Func<string, string, string, bool>)HostPortrayalEmit);

        // Debugger
        lua.SetGlobal("HostDebuggerEntry", (Action<string, string?>)((action, message) =>
        {
            if (action == "trace" && message is not null)
                Console.WriteLine($"[S131 Lua] {message}");
        }));
    }

    // ── Feature Data Access ────────────────────────────────────────────

    private List<object> HostGetFeatureIDs()
    {
        return _featureById.Keys.Select(k => (object)k).ToList();
    }

    private string HostFeatureGetCode(double featureId)
    {
        return _featureById.TryGetValue(featureId, out var feat)
            ? feat.FeatureType
            : "";
    }

    private List<object> HostFeatureGetSimpleAttribute(double featureId, string attributePath, string attributeCode)
    {
        if (!_featureById.TryGetValue(featureId, out var feat))
            return [];

        return GetSimpleAttributeValues(feat.Attributes, feat.ComplexAttributes, attributePath, attributeCode);
    }

    private double HostFeatureGetComplexAttributeCount(double featureId, string attributePath, string attributeCode)
    {
        if (!_featureById.TryGetValue(featureId, out var feat))
            return 0;

        return CountComplexAttributes(feat.ComplexAttributes, attributePath, attributeCode);
    }

    /// <summary>
    /// Returns synthetic spatial association data for the given feature.
    /// S-131 GML features embed geometry directly; this method creates
    /// a spatial association structure compatible with the Lua shims.
    /// </summary>
    private object? HostFeatureGetSpatialAssociations(double featureId)
    {
        if (!_featureById.TryGetValue(featureId, out var feat))
            return null;

        if (feat.GeometryType == GmlGeometryType.None)
            return null;

        var spatialType = feat.GeometryType switch
        {
            GmlGeometryType.Point => "Point",
            GmlGeometryType.Curve => "Curve",
            GmlGeometryType.Surface => "Surface",
            _ => "None",
        };

        var spatialId = $"{featureId}:geom";
        var result = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["SpatialID"] = spatialId,
                ["SpatialType"] = spatialType,
                ["Orientation"] = "Forward",
            }
        };

        return result;
    }

    private List<object> HostFeatureGetAssociatedFeatureIDs(double featureId, string associationCode, string? roleCode)
    {
        if (!_featureById.TryGetValue(featureId, out var feat))
            return [];

        var result = new List<object>();
        foreach (var refEntry in feat.References)
        {
            // Match on role/association name
            if (!string.Equals(refEntry.Role, associationCode, StringComparison.OrdinalIgnoreCase))
                continue;

            // Resolve the target's numeric ID — it must be a feature (not info type)
            if (_gmlIdToNumericId.TryGetValue(refEntry.TargetRef, out var targetId)
                && _featureById.ContainsKey(targetId))
            {
                result.Add(targetId);
            }
        }

        return result;
    }

    private List<object> HostFeatureGetAssociatedInformationIDs(double featureId, string associationCode, string? roleCode)
    {
        if (!_featureById.TryGetValue(featureId, out var feat))
            return [];

        var result = new List<object>();
        foreach (var refEntry in feat.References)
        {
            if (!string.Equals(refEntry.Role, associationCode, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_gmlIdToNumericId.TryGetValue(refEntry.TargetRef, out var targetId)
                && _infoById.ContainsKey(targetId))
            {
                result.Add(targetId);
            }
        }

        return result;
    }

    // ── Information Type Data Access ───────────────────────────────────

    private string HostInformationTypeGetCode(double infoId)
    {
        return _infoById.TryGetValue(infoId, out var info) ? info.TypeCode : "";
    }

    private List<object> HostInformationTypeGetSimpleAttribute(double infoId, string attributePath, string attributeCode)
    {
        if (!_infoById.TryGetValue(infoId, out var info))
            return [];

        return GetSimpleAttributeValues(info.Attributes, info.ComplexAttributes, attributePath, attributeCode);
    }

    private double HostInformationTypeGetComplexAttributeCount(double infoId, string attributePath, string attributeCode)
    {
        if (!_infoById.TryGetValue(infoId, out var info))
            return 0;

        return CountComplexAttributes(info.ComplexAttributes, attributePath, attributeCode);
    }

    // ── Spatial Data Access ───────────────────────────────────────────

    /// <summary>
    /// Returns raw spatial data for a synthetic spatial ID. The Lua shim
    /// (<c>HostGetSpatial</c>) converts this to proper Spatial objects via
    /// <c>CreatePoint</c>/<c>CreateCurve</c>/<c>CreateSurface</c>.
    /// </summary>
    private object? HostGetSpatialData(string spatialId)
    {
        // Parse "featureId:geom"
        var colonIdx = spatialId.IndexOf(':');
        if (colonIdx < 0) return null;

        if (!double.TryParse(spatialId.AsSpan(0, colonIdx), CultureInfo.InvariantCulture, out var featureId))
            return null;

        if (!_featureById.TryGetValue(featureId, out var feat))
            return null;

        // Coordinates are already decimal degrees (no COMF/SOMF scaling).
        // Lua expects X=longitude, Y=latitude.
        return feat.GeometryType switch
        {
            GmlGeometryType.Point when feat.Points.Length > 0 =>
                new Dictionary<string, object?>
                {
                    ["RecordType"] = "Point",
                    ["X"] = feat.Points[0].Longitude.ToString(CultureInfo.InvariantCulture),
                    ["Y"] = feat.Points[0].Latitude.ToString(CultureInfo.InvariantCulture),
                },

            GmlGeometryType.Curve when feat.Curves.Length > 0 =>
                BuildCurveData(feat),

            GmlGeometryType.Surface when feat.ExteriorRing.Length > 0 =>
                BuildSurfaceData(feat),

            _ => null,
        };
    }

    private static Dictionary<string, object?> BuildCurveData(S131Feature feat)
    {
        var coords = feat.Curves[0];
        if (coords.Length == 0)
            return new Dictionary<string, object?> { ["RecordType"] = "Point" };

        // Start and end points as separate synthetic spatial associations
        var startId = $"synth:start:{feat.Id}";
        var endId = $"synth:end:{feat.Id}";

        var controlPoints = new List<object>();
        // Intermediate points (skip start and end, which become point associations)
        for (int i = 1; i < coords.Length - 1; i++)
        {
            controlPoints.Add(new Dictionary<string, object?>
            {
                ["X"] = coords[i].Longitude.ToString(CultureInfo.InvariantCulture),
                ["Y"] = coords[i].Latitude.ToString(CultureInfo.InvariantCulture),
            });
        }

        return new Dictionary<string, object?>
        {
            ["RecordType"] = "Curve",
            ["StartPointID"] = startId,
            ["EndPointID"] = endId,
            ["ControlPoints"] = controlPoints,
            // Stash start/end coordinate data for HostGetSpatialData lookup
            ["_startX"] = coords[0].Longitude.ToString(CultureInfo.InvariantCulture),
            ["_startY"] = coords[0].Latitude.ToString(CultureInfo.InvariantCulture),
            ["_endX"] = coords[^1].Longitude.ToString(CultureInfo.InvariantCulture),
            ["_endY"] = coords[^1].Latitude.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static Dictionary<string, object?> BuildSurfaceData(S131Feature feat)
    {
        // Build exterior ring as a composite curve with a single curve segment
        // The Lua shim expects Surface { ExteriorRing: SpatialAssociation, InteriorRings: [SpatialAssociation] }
        // For simplicity, we represent surfaces as coordinate lists embedded directly.
        // The Lua shim's CreateSurface expects ring associations, but since S-131 GML
        // embeds all geometry inline, we provide a simplified Surface representation.

        var extRingId = $"synth:ext:{feat.Id}";
        var exteriorRing = new Dictionary<string, object?>
        {
            ["SpatialID"] = extRingId,
            ["SpatialType"] = "CompositeCurve",
            ["Orientation"] = "Forward",
        };

        var interiorRings = new List<object>();
        for (int i = 0; i < feat.InteriorRings.Length; i++)
        {
            interiorRings.Add(new Dictionary<string, object?>
            {
                ["SpatialID"] = $"synth:int:{feat.Id}:{i}",
                ["SpatialType"] = "CompositeCurve",
                ["Orientation"] = "Forward",
            });
        }

        return new Dictionary<string, object?>
        {
            ["RecordType"] = "Surface",
            ["ExteriorRing"] = exteriorRing,
            ["InteriorRings"] = interiorRings,
        };
    }

    private List<object> HostSpatialGetAssociatedFeatureIDs(string spatialId)
    {
        // Parse feature ID from synthetic spatial ID "featureId:geom"
        var colonIdx = spatialId.IndexOf(':');
        if (colonIdx < 0) return [];

        if (double.TryParse(spatialId.AsSpan(0, colonIdx), CultureInfo.InvariantCulture, out var featureId)
            && _featureById.ContainsKey(featureId))
        {
            return [(object)featureId];
        }

        return [];
    }

    private List<object> HostSpatialGetAssociatedInformationIDs(string spatialId, string associationCode, string? roleCode)
    {
        return [];
    }

    // ── Type System (Feature Catalogue) ───────────────────────────────

    private List<object> HostGetFeatureTypeCodes()
        => _fc.FeatureTypes.Select(ft => (object)ft.Code).ToList();

    private List<object> HostGetInformationTypeCodes()
        => _fc.InformationTypes.Select(it => (object)it.Code).ToList();

    private List<object> HostGetSimpleAttributeTypeCodes()
        => _fc.SimpleAttributes.Select(a => (object)a.Code).ToList();

    private List<object> HostGetComplexAttributeTypeCodes()
        => _fc.ComplexAttributes.Select(a => (object)a.Code).ToList();

    private List<object> HostGetRoleTypeCodes()
        => _fc.Roles.Select(r => (object)r.Code).ToList();

    private List<object> HostGetInformationAssociationTypeCodes()
        => _fc.InformationAssociations.Select(a => (object)a.Code).ToList();

    private List<object> HostGetFeatureAssociationTypeCodes()
        => _fc.FeatureAssociations.Select(a => (object)a.Code).ToList();

    private IReadOnlyDictionary<string, object?> HostGetFeatureTypeInfo(string code)
    {
        EnsureFeatureTypeLookup();
        if (!_featureTypeByCode!.TryGetValue(code, out var ft))
            return new Dictionary<string, object?>();

        var allBindings = ResolveFeatureTypeBindings(ft);
        return BuildObjectTypeInfo(ft.Code, ft.Name, allBindings, ft.IsAbstract);
    }

    private IReadOnlyDictionary<string, object?> HostGetInformationTypeInfo(string code)
    {
        EnsureInfoTypeLookup();
        if (!_infoTypeByCode!.TryGetValue(code, out var it))
            return new Dictionary<string, object?>();

        var allBindings = ResolveInfoTypeBindings(it);
        return BuildObjectTypeInfo(it.Code, it.Name, allBindings, it.IsAbstract);
    }

    private IReadOnlyDictionary<string, object?> HostGetSimpleAttributeTypeInfo(string code)
    {
        EnsureSimpleAttrLookup();
        if (!_simpleAttrByCode!.TryGetValue(code, out var sa))
            return new Dictionary<string, object?>();

        return new Dictionary<string, object?>
        {
            ["Type"] = "SimpleAttribute",
            ["Code"] = sa.Code,
            ["Name"] = sa.Name,
            ["ValueType"] = sa.ValueType,
            ["ListedValues"] = BuildListedValues(sa.ListedValues),
        };
    }

    private IReadOnlyDictionary<string, object?> HostGetComplexAttributeTypeInfo(string code)
    {
        EnsureComplexAttrLookup();
        if (!_complexAttrByCode!.TryGetValue(code, out var ca))
            return new Dictionary<string, object?>();

        var bindings = new Dictionary<string, object?> { ["Type"] = "array:AttributeBinding" };
        foreach (var ab in ca.SubAttributeBindings)
        {
            bindings[ab.AttributeRef] = new Dictionary<string, object?>
            {
                ["Type"] = "AttributeBinding",
                ["AttributeCode"] = ab.AttributeRef,
                ["LowerMultiplicity"] = (double)ab.Multiplicity.Lower,
                ["UpperMultiplicity"] = ab.Multiplicity.Upper is int u ? (double)u : null,
            };
        }

        return new Dictionary<string, object?>
        {
            ["Type"] = "ComplexAttribute",
            ["Code"] = ca.Code,
            ["Name"] = ca.Name,
            ["AttributeBindings"] = bindings,
        };
    }

    // ── Output ─────────────────────────────────────────────────────────

    private bool HostPortrayalEmit(string featureRef, string drawingInstructions, string observedParams)
    {
        // The Lua pipeline emits numeric feature IDs (e.g. "5") as the
        // feature reference, but the Mapsui geometry provider is keyed by
        // the GML gml:id attribute (e.g. "AnchorageArea.1"). Translate so
        // that the renderer can locate the feature's geometry.
        if (double.TryParse(featureRef, NumberStyles.Number, CultureInfo.InvariantCulture, out var numericId)
            && _featureById.TryGetValue(numericId, out var feat))
        {
            featureRef = feat.Id;
        }

        _emitted.Add((featureRef, drawingInstructions, observedParams));
        return true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private void EnsureFeatureTypeLookup()
    {
        _featureTypeByCode ??= _fc.FeatureTypes
            .ToDictionary(ft => ft.Code, StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureInfoTypeLookup()
    {
        _infoTypeByCode ??= _fc.InformationTypes
            .ToDictionary(it => it.Code, StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureSimpleAttrLookup()
    {
        _simpleAttrByCode ??= _fc.SimpleAttributes
            .ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureComplexAttrLookup()
    {
        _complexAttrByCode ??= _fc.ComplexAttributes
            .ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walk the feature type inheritance chain and collect all attribute bindings,
    /// starting from the most-derived type up to the root.
    /// </summary>
    private List<AttributeBinding> ResolveFeatureTypeBindings(FeatureType ft)
    {
        EnsureFeatureTypeLookup();
        var bindings = new List<AttributeBinding>(ft.AttributeBindings);
        var seen = new HashSet<string>(bindings.Select(b => b.AttributeRef), StringComparer.OrdinalIgnoreCase);
        var current = ft.SuperType;
        while (current is not null && _featureTypeByCode!.TryGetValue(current, out var parent))
        {
            foreach (var ab in parent.AttributeBindings)
            {
                if (seen.Add(ab.AttributeRef))
                    bindings.Add(ab);
            }
            current = parent.SuperType;
        }
        return bindings;
    }

    /// <summary>
    /// Walk the information type inheritance chain and collect all attribute bindings.
    /// </summary>
    private List<AttributeBinding> ResolveInfoTypeBindings(InformationType it)
    {
        EnsureInfoTypeLookup();
        var bindings = new List<AttributeBinding>(it.AttributeBindings);
        var seen = new HashSet<string>(bindings.Select(b => b.AttributeRef), StringComparer.OrdinalIgnoreCase);
        var current = it.SuperType;
        while (current is not null && _infoTypeByCode!.TryGetValue(current, out var parent))
        {
            foreach (var ab in parent.AttributeBindings)
            {
                if (seen.Add(ab.AttributeRef))
                    bindings.Add(ab);
            }
            current = parent.SuperType;
        }
        return bindings;
    }

    /// <summary>
    /// Gets simple attribute values, navigating into complex attribute
    /// scope if <paramref name="attributePath"/> is non-empty.
    /// </summary>
    /// <remarks>
    /// The Lua API uses a path format like <c>"complexCode:1"</c> or
    /// <c>"complexCode:1;subComplex:2"</c> to navigate into complex
    /// attributes. For GML attributes this maps to complex attribute
    /// instances by index.
    /// </remarks>
    private static List<object> GetSimpleAttributeValues(
        ImmutableDictionary<string, string> simpleAttrs,
        ImmutableArray<S131ComplexAttribute> complexAttrs,
        string attributePath,
        string attributeCode)
    {
        if (string.IsNullOrEmpty(attributePath))
        {
            // Direct simple attribute lookup
            if (simpleAttrs.TryGetValue(attributeCode, out var value))
                return [(object)value];
            return [];
        }

        // Navigate complex attribute path
        var segments = attributePath.Split(';');
        var currentComplex = complexAttrs;

        foreach (var segment in segments)
        {
            var colonIdx = segment.IndexOf(':');
            if (colonIdx < 0) return [];

            var complexCode = segment[..colonIdx];
            if (!int.TryParse(segment.AsSpan(colonIdx + 1), out var instanceIndex))
                return [];

            // Find the nth instance of this complex attribute
            int found = 0;
            S131ComplexAttribute? target = null;
            foreach (var ca in currentComplex)
            {
                if (string.Equals(ca.Code, complexCode, StringComparison.OrdinalIgnoreCase))
                {
                    found++;
                    if (found == instanceIndex)
                    {
                        target = ca;
                        break;
                    }
                }
            }

            if (target is null) return [];

            // For the last segment, look up the attribute code in the sub-attributes
            // For intermediate segments, we'd need nested complex attributes
            // (S-131's single-level IGmlComplexAttribute handles the common case)
            if (target.SubAttributes.TryGetValue(attributeCode, out var subValue))
                return [(object)subValue];
        }

        return [];
    }

    private static double CountComplexAttributes(
        ImmutableArray<S131ComplexAttribute> complexAttrs,
        string attributePath,
        string attributeCode)
    {
        if (string.IsNullOrEmpty(attributePath))
        {
            return complexAttrs.Count(ca =>
                string.Equals(ca.Code, attributeCode, StringComparison.OrdinalIgnoreCase));
        }

        // Navigate path then count at the resolved scope
        // For single-level complex attributes this is typically 0
        return 0;
    }

    private static IReadOnlyDictionary<string, object?> BuildObjectTypeInfo(
        string code,
        string name,
        IReadOnlyList<AttributeBinding> attributeBindings,
        bool isAbstract)
    {
        var bindings = new Dictionary<string, object?> { ["Type"] = "array:AttributeBinding" };
        foreach (var ab in attributeBindings)
        {
            bindings[ab.AttributeRef] = new Dictionary<string, object?>
            {
                ["Type"] = "AttributeBinding",
                ["AttributeCode"] = ab.AttributeRef,
                ["LowerMultiplicity"] = (double)ab.Multiplicity.Lower,
                ["UpperMultiplicity"] = ab.Multiplicity.Upper is int u ? (double)u : null,
                ["Sequential"] = ab.Sequential,
            };
        }

        return new Dictionary<string, object?>
        {
            ["Type"] = "FeatureType",
            ["Code"] = code,
            ["Name"] = name,
            ["Abstract"] = isAbstract,
            ["AttributeBindings"] = bindings,
        };
    }

    private static IReadOnlyDictionary<string, object?> BuildListedValues(IReadOnlyList<ListedValue> values)
    {
        var dict = new Dictionary<string, object?> { ["Type"] = "array:ListedValue" };
        for (int i = 0; i < values.Count; i++)
        {
            var lv = values[i];
            dict[(i + 1).ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["Type"] = "ListedValue",
                ["Label"] = lv.Label,
                ["Code"] = double.TryParse(lv.Code, CultureInfo.InvariantCulture, out var c) ? c : 0.0,
            };
        }
        return dict;
    }
}
