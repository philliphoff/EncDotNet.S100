using System.Collections.Immutable;
using System.Globalization;
using EncDotNet.S100.Features;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Implements the S-100 Lua Host API functions by bridging an <see cref="S101Dataset"/>
/// and <see cref="FeatureCatalogue"/> to an <see cref="ILuaContext"/>. This is the C#
/// side of the S-100 Part 9A Lua Portrayal Model host interface.
/// </summary>
public sealed class S101LuaDataProvider
{
    private const byte RcnmPoint = 110;
    private const byte RcnmMultiPoint = 115;
    private const byte RcnmCurveSegment = 120;
    private const byte RcnmCompositeCurve = 125;
    private const byte RcnmSurface = 130;
    private const byte OrientationReverse = 2;

    private readonly S101Document _doc;
    private readonly FeatureCatalogue _fc;

    // Lookup indices built lazily
    private Dictionary<uint, S101FeatureRecord>? _featureById;
    private Dictionary<string, FeatureType>? _featureTypeByCode;
    private Dictionary<string, InformationType>? _infoTypeByCode;
    private Dictionary<string, SimpleAttribute>? _simpleAttrByCode;
    private Dictionary<string, ComplexAttribute>? _complexAttrByCode;

    // Collected drawing instruction output
    private readonly List<(string FeatureRef, string Instructions, string ObservedParams)> _emitted = new();

    public S101LuaDataProvider(S101Dataset dataset, FeatureCatalogue featureCatalogue)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(featureCatalogue);
        _doc = dataset.Document;
        _fc = featureCatalogue;
    }

    /// <summary>Drawing instructions emitted during portrayal execution.</summary>
    public IReadOnlyList<(string FeatureRef, string Instructions, string ObservedParams)> EmittedInstructions => _emitted;

    /// <summary>
    /// Registers all Host* functions and the Debug table on the given Lua context.
    /// </summary>
    public void RegisterHostFunctions(ILuaContext lua)
    {
        // Debug table — route Trace to host for diagnostics
        lua.SetGlobal("HostDebugTrace", (Action<string>)(msg => Console.WriteLine($"[Lua] {msg}")));
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

        // Spatial data access — HostGetSpatial is implemented as a Lua shim that
        // calls HostGetSpatialData (C#) to get raw data, then constructs proper Lua
        // Spatial objects via CreatePoint/CreateCurve/etc.
        lua.SetGlobal("HostGetSpatialData", (Func<string, object?>)HostGetSpatialData);
        lua.SetGlobal("HostSpatialGetAssociatedFeatureIDs",
            (Func<string, List<object>>)HostSpatialGetAssociatedFeatureIDs);
        lua.SetGlobal("HostSpatialGetAssociatedInformationIDs",
            (Func<string, string, string?, List<object>>)HostSpatialGetAssociatedInformationIDs);

        // Type system (Feature catalogue)
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

        // Debugger — HostDebuggerEntry(action, [message, [depth]])
        lua.SetGlobal("HostDebuggerEntry", (Action<string, string?>)((action, message) =>
        {
            if (action == "trace" && message is not null)
                Console.WriteLine($"[Lua] {message}");
        }));
    }

    // ── Feature Data Access ────────────────────────────────────────────

    private List<object> HostGetFeatureIDs()
    {
        return _doc.Features.Select(f => (object)(double)f.RecordId).ToList();
    }

    private string HostFeatureGetCode(double featureId)
    {
        var feat = GetFeature((uint)featureId);
        return _doc.FeatureTypeCatalogue.TryGetValue(feat.FeatureTypeCode, out var name)
            ? name : feat.FeatureTypeCode.ToString();
    }

    private List<object> HostFeatureGetSimpleAttribute(double featureId, string attributePath, string attributeCode)
    {
        var feat = GetFeature((uint)featureId);
        var attrs = ResolveAttributeScope(feat.Attributes, attributePath);
        return GetSimpleAttributeValues(attrs, attributeCode);
    }

    private double HostFeatureGetComplexAttributeCount(double featureId, string attributePath, string attributeCode)
    {
        var feat = GetFeature((uint)featureId);
        var attrs = ResolveAttributeScope(feat.Attributes, attributePath);
        return CountComplexAttributes(attrs, attributeCode);
    }

    private object? HostFeatureGetSpatialAssociations(double featureId)
    {
        var feat = GetFeature((uint)featureId);
        if (feat.SpatialAssociations.Length == 0) return null;

        // Return raw spatial association data as a List<object> (marshals to
        // a 1-indexed Lua table). Each element is a dictionary with string
        // SpatialType/Orientation names so the Lua side can look them up
        // against the global SpatialType/Orientation tables.
        var result = new List<object>();
        foreach (var spa in feat.SpatialAssociations)
        {
            var dict = new Dictionary<string, object?>
            {
                ["SpatialID"] = $"{spa.RecordName}:{spa.RecordId}",
                ["SpatialType"] = ResolveSpatialTypeName(spa.RecordName),
                ["Orientation"] = spa.Orientation == OrientationReverse ? "Reverse" : "Forward",
            };
            result.Add(dict);
        }

        return result;
    }

    private List<object> HostFeatureGetAssociatedFeatureIDs(double featureId, string associationCode, string? roleCode)
    {
        var feat = GetFeature((uint)featureId);
        var result = new List<object>();

        foreach (var facs in feat.FeatureAssociations)
        {
            // Match on association code from the catalogue
            var assocName = _doc.FeatureAssociationCatalogue.TryGetValue(facs.NumericCode, out var name) ? name : "";
            if (!string.Equals(assocName, associationCode, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add((double)facs.RecordId);
        }

        return result;
    }

    private List<object> HostFeatureGetAssociatedInformationIDs(double featureId, string associationCode, string? roleCode)
    {
        var feat = GetFeature((uint)featureId);
        var result = new List<object>();

        foreach (var inas in feat.InformationAssociations)
        {
            var assocName = _doc.InformationAssociationCatalogue.TryGetValue(inas.NumericCode, out var name) ? name : "";
            if (!string.Equals(assocName, associationCode, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add((double)inas.RecordId);
        }

        return result;
    }

    // ── Information Type Data Access ───────────────────────────────────

    private string HostInformationTypeGetCode(double infoId)
    {
        if (_doc.InformationTypes.TryGetValue((uint)infoId, out var info))
        {
            return _doc.InformationTypeCatalogue.TryGetValue(info.InformationTypeCode, out var name)
                ? name : info.InformationTypeCode.ToString();
        }
        return "";
    }

    private List<object> HostInformationTypeGetSimpleAttribute(double infoId, string attributePath, string attributeCode)
    {
        if (_doc.InformationTypes.TryGetValue((uint)infoId, out var info))
        {
            var attrs = ResolveAttributeScope(info.Attributes, attributePath);
            return GetSimpleAttributeValues(attrs, attributeCode);
        }
        return new List<object>();
    }

    private double HostInformationTypeGetComplexAttributeCount(double infoId, string attributePath, string attributeCode)
    {
        if (_doc.InformationTypes.TryGetValue((uint)infoId, out var info))
        {
            var attrs = ResolveAttributeScope(info.Attributes, attributePath);
            return CountComplexAttributes(attrs, attributeCode);
        }
        return 0;
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
        {
            Console.WriteLine($"[Host] FeatureTypeInfo not found: {code}");
            return new Dictionary<string, object?>();
        }

        var result = BuildObjectTypeInfo(ft.Code, ft.Name, ft.AttributeBindings, ft.IsAbstract);
        if (code == "DepthArea")
        {
            var bindings = (IReadOnlyDictionary<string, object?>)result["AttributeBindings"]!;
            Console.WriteLine($"[Host] DepthArea bindings: {string.Join(", ", bindings.Keys.Where(k => k != "Type"))}");
        }
        return result;
    }

    private IReadOnlyDictionary<string, object?> HostGetInformationTypeInfo(string code)
    {
        EnsureInfoTypeLookup();
        if (!_infoTypeByCode!.TryGetValue(code, out var it))
            return new Dictionary<string, object?>();

        return BuildObjectTypeInfo(it.Code, it.Name, it.AttributeBindings, it.IsAbstract);
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
        _emitted.Add((featureRef, drawingInstructions, observedParams));
        return true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private S101FeatureRecord GetFeature(uint id)
    {
        EnsureFeatureIndex();
        return _featureById!.TryGetValue(id, out var feat)
            ? feat
            : throw new KeyNotFoundException($"Feature {id} not found.");
    }

    private void EnsureFeatureIndex()
    {
        if (_featureById is not null) return;
        _featureById = _doc.Features.ToDictionary(f => f.RecordId);
    }

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
    /// Navigate into the flat attribute list to find sub-attributes under the
    /// complex attribute path. Path format: "complexCode:index;complexCode:index;..."
    /// Returns the sub-attributes within the specified complex attribute scope.
    /// </summary>
    private ImmutableArray<S101Attribute> ResolveAttributeScope(ImmutableArray<S101Attribute> attributes, string attributePath)
    {
        if (string.IsNullOrEmpty(attributePath))
            return attributes;

        // Parse path segments like "verticalClearanceFixed:1"
        var segments = attributePath.Split(';');
        var current = attributes;
        foreach (var segment in segments)
        {
            var colonIdx = segment.IndexOf(':');
            var complexCode = segment[..colonIdx];
            var instanceIndex = int.Parse(segment[(colonIdx + 1)..]);

            // Resolve complex attribute numeric code
            ushort? numericCode = null;
            foreach (var (code, name) in _doc.AttributeTypeCatalogue)
            {
                if (string.Equals(name, complexCode, StringComparison.OrdinalIgnoreCase))
                {
                    numericCode = code;
                    break;
                }
            }

            if (numericCode is null) return ImmutableArray<S101Attribute>.Empty;

            // Find the nth instance of this complex attribute and collect sub-attributes
            int found = 0;
            var subAttrs = ImmutableArray.CreateBuilder<S101Attribute>();
            bool collecting = false;
            foreach (var attr in current)
            {
                if (attr.NumericCode == numericCode && attr.Index == 1)
                {
                    found++;
                    if (found == instanceIndex)
                    {
                        collecting = true;
                        continue; // Skip the complex attribute marker itself
                    }
                    else if (collecting)
                    {
                        break; // Next instance of same complex attribute — done
                    }
                }
                else if (collecting)
                {
                    // Check if this is another top-level/sibling complex attribute (not a sub-attribute)
                    // Sub-attributes have Index values that may vary, but we track them by
                    // whether we've hit another complex attr marker. For simplicity, collect
                    // until we see a code that matches ANY complex attribute at this level.
                    subAttrs.Add(attr);
                }
            }

            current = subAttrs.ToImmutable();
        }

        return current;
    }

    private List<object> GetSimpleAttributeValues(ImmutableArray<S101Attribute> attributes, string attributeCode)
    {
        // Resolve attribute code from name to numeric code
        ushort? numericCode = null;
        foreach (var (code, name) in _doc.AttributeTypeCatalogue)
        {
            if (string.Equals(name, attributeCode, StringComparison.OrdinalIgnoreCase))
            {
                numericCode = code;
                break;
            }
        }

        if (numericCode is null) return new List<object>();

        var result = new List<object>();
        foreach (var attr in attributes)
        {
            if (attr.NumericCode == numericCode && attr.Value.Length > 0)
            {
                result.Add(attr.Value);
            }
        }

        return result;
    }

    private double CountComplexAttributes(ImmutableArray<S101Attribute> attributes, string attributeCode)
    {
        // For complex attributes, count instances with the matching code
        ushort? numericCode = null;
        foreach (var (code, name) in _doc.AttributeTypeCatalogue)
        {
            if (string.Equals(name, attributeCode, StringComparison.OrdinalIgnoreCase))
            {
                numericCode = code;
                break;
            }
        }

        if (numericCode is null) return 0;

        int count = 0;
        foreach (var attr in attributes)
        {
            if (attr.NumericCode == numericCode && attr.Index == 1)
                count++;
        }

        return count;
    }

    private static string ResolveSpatialTypeName(byte rcnm)
    {
        return rcnm switch
        {
            RcnmPoint => "Point",
            RcnmMultiPoint => "MultiPoint",
            RcnmCurveSegment => "Curve",
            RcnmCompositeCurve => "CompositeCurve",
            RcnmSurface => "Surface",
            _ => "None",
        };
    }

    // ── Spatial Data Access ────────────────────────────────────────────

    private static (byte Rcnm, uint Rcid) ParseSpatialId(string spatialId)
    {
        var idx = spatialId.IndexOf(':');
        var rcnm = byte.Parse(spatialId.AsSpan(0, idx));
        var rcid = uint.Parse(spatialId.AsSpan(idx + 1));
        return (rcnm, rcid);
    }

    /// <summary>
    /// Returns raw spatial data as a dictionary that the Lua shim can use
    /// to construct the proper Spatial objects via Create* functions.
    /// </summary>
    private object? HostGetSpatialData(string spatialId)
    {
        var (rcnm, rcid) = ParseSpatialId(spatialId);
        double cmfx = _doc.StructureInfo.CoordinateMultiplicationFactorX;
        double cmfy = _doc.StructureInfo.CoordinateMultiplicationFactorY;
        double cmfz = _doc.StructureInfo.CoordinateMultiplicationFactorZ;
        if (cmfx == 0) cmfx = 10_000_000;
        if (cmfy == 0) cmfy = 10_000_000;
        // CMFZ defaults to S-57 SOMF (10) when zero; S-101 datasets that encode Z
        // explicitly should populate this via the DSSI record.
        if (cmfz == 0) cmfz = 10;

        if (rcnm == RcnmPoint && _doc.Points.TryGetValue(rcid, out var pt))
        {
            return new Dictionary<string, object?>
            {
                ["RecordType"] = "Point",
                ["X"] = (pt.X / cmfx).ToString(CultureInfo.InvariantCulture),
                ["Y"] = (pt.Y / cmfy).ToString(CultureInfo.InvariantCulture),
            };
        }

        if (rcnm == RcnmMultiPoint && _doc.MultiPoints.TryGetValue(rcid, out var mp))
        {
            var points = new List<object>(mp.Points.Length);
            foreach (var (y, x, z) in mp.Points)
            {
                points.Add(new Dictionary<string, object?>
                {
                    ["X"] = (x / cmfx).ToString(CultureInfo.InvariantCulture),
                    ["Y"] = (y / cmfy).ToString(CultureInfo.InvariantCulture),
                    ["Z"] = (z / cmfz).ToString(CultureInfo.InvariantCulture),
                });
            }

            return new Dictionary<string, object?>
            {
                ["RecordType"] = "MultiPoint",
                ["Points"] = points,
            };
        }

        if (rcnm == RcnmCurveSegment && _doc.CurveSegments.TryGetValue(rcid, out var curve))
        {
            // Start and end point associations
            string? startPointId = null;
            string? endPointId = null;
            foreach (var pta in curve.PointAssociations)
            {
                var ptId = $"{pta.RecordName}:{pta.RecordId}";
                if (pta.Topology == 1) startPointId = ptId;      // TOPI=1 begin
                else if (pta.Topology == 2) endPointId = ptId;   // TOPI=2 end
            }

            // Intermediate coordinates as list of {X, Y} dictionaries
            var controlPoints = new List<object>();
            foreach (var (y, x) in curve.IntermediateCoordinates)
            {
                controlPoints.Add(new Dictionary<string, object?>
                {
                    ["X"] = (x / cmfx).ToString(CultureInfo.InvariantCulture),
                    ["Y"] = (y / cmfy).ToString(CultureInfo.InvariantCulture),
                });
            }

            return new Dictionary<string, object?>
            {
                ["RecordType"] = "Curve",
                ["StartPointID"] = startPointId,
                ["EndPointID"] = endPointId,
                ["ControlPoints"] = controlPoints,
            };
        }

        if (rcnm == RcnmCompositeCurve && _doc.CompositeCurves.TryGetValue(rcid, out var cc))
        {
            var components = new List<object>();
            foreach (var comp in cc.CurveComponents)
            {
                components.Add(new Dictionary<string, object?>
                {
                    ["SpatialID"] = $"{comp.RecordName}:{comp.RecordId}",
                    ["SpatialType"] = ResolveSpatialTypeName(comp.RecordName),
                    ["Orientation"] = comp.Orientation == OrientationReverse ? "Reverse" : "Forward",
                });
            }

            return new Dictionary<string, object?>
            {
                ["RecordType"] = "CompositeCurve",
                ["CurveAssociations"] = components,
            };
        }

        if (rcnm == RcnmSurface && _doc.Surfaces.TryGetValue(rcid, out var surface))
        {
            Dictionary<string, object?>? exteriorRing = null;
            var interiorRings = new List<object>();

            foreach (var ring in surface.RingAssociations)
            {
                var ringData = new Dictionary<string, object?>
                {
                    ["SpatialID"] = $"{ring.RecordName}:{ring.RecordId}",
                    ["SpatialType"] = ResolveSpatialTypeName(ring.RecordName),
                    ["Orientation"] = ring.Orientation == OrientationReverse ? "Reverse" : "Forward",
                };

                if (ring.Usage == 1) // Exterior
                    exteriorRing = ringData;
                else                 // Interior
                    interiorRings.Add(ringData);
            }

            return new Dictionary<string, object?>
            {
                ["RecordType"] = "Surface",
                ["ExteriorRing"] = exteriorRing,
                ["InteriorRings"] = interiorRings,
            };
        }

        return null;
    }

    private List<object> HostSpatialGetAssociatedFeatureIDs(string spatialId)
    {
        var (_, rcid) = ParseSpatialId(spatialId);
        var result = new List<object>();

        foreach (var feat in _doc.Features)
        {
            foreach (var spa in feat.SpatialAssociations)
            {
                if (spa.RecordId == rcid)
                {
                    result.Add((double)feat.RecordId);
                    break;
                }
            }
        }

        return result;
    }

    private List<object> HostSpatialGetAssociatedInformationIDs(string spatialId, string associationCode, string? roleCode)
    {
        // S-101 spatial records don't directly carry information associations
        // in our current model. Return empty for now.
        return new List<object>();
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
                ["Code"] = double.TryParse(lv.Code, CultureInfo.InvariantCulture, out var code) ? code : 0.0,
            };
        }
        return dict;
    }
}
