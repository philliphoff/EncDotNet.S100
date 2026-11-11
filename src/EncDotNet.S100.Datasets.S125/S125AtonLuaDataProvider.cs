using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.S125;

/// <summary>
/// Implements the S-100 Part 9A Lua host API over S-125 aids to navigation
/// projected onto the S-101 model (<see cref="S125AtonPortrayalProjection"/>),
/// so the bundled S-101 Portrayal Catalogue's AtoN rules can portray them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Feature ids</b>: GML <c>gml:id</c> strings are mapped to sequential
/// numeric ids for the Lua side and mapped back in <c>HostPortrayalEmit</c>,
/// so emitted instructions reference the S-125 feature's <c>gml:id</c>.
/// </para>
/// <para>
/// <b>Spatial records</b>: GML features embed their geometry, whereas the
/// S-101 rules inspect shared spatial records — a light's flare direction and
/// the vertical offset of its description both depend on the other features
/// associated with the same point (S-101 <c>LightFlareAndDescription</c>,
/// <c>GetColocatedTextCount</c>). Point features at the same position are
/// therefore given one shared synthetic point record, reproducing S-101's
/// structure / equipment co-location. Curves and surfaces get per-feature
/// synthetic curve, composite-curve and surface records.
/// </para>
/// <para>
/// Attribute and type information comes from the S-101 Feature Catalogue,
/// since the rules being run are S-101 rules.
/// </para>
/// </remarks>
public sealed class S125AtonLuaDataProvider : ILuaDataProvider
{
    private readonly FeatureCatalogue _fc;
    private readonly Action<string> _trace;
    private readonly Dictionary<double, S125ProjectedAton> _featureById = new();
    private readonly Dictionary<string, double> _numericIdByGmlId = new(StringComparer.Ordinal);
    private readonly Dictionary<double, List<double>> _equipmentByStructure = new();
    private readonly Dictionary<double, List<double>> _structureByEquipment = new();

    // Synthetic spatial records, keyed by spatial id.
    private readonly Dictionary<string, Dictionary<string, object?>> _spatialData = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<double>> _featuresBySpatial = new(StringComparer.Ordinal);
    private readonly Dictionary<double, (string Id, string Type)> _spatialByFeature = new();

    private Dictionary<string, FeatureType>? _featureTypeByCode;
    private Dictionary<string, InformationType>? _infoTypeByCode;
    private Dictionary<string, SimpleAttribute>? _simpleAttrByCode;
    private Dictionary<string, ComplexAttribute>? _complexAttrByCode;

    private readonly List<EmittedInstruction> _emitted = new();

    /// <summary>
    /// Initialises the provider over the portrayable features of an S-125 dataset.
    /// </summary>
    /// <param name="dataset">The S-125 dataset.</param>
    /// <param name="s101FeatureCatalogue">The S-101 feature catalogue the rules are written against.</param>
    /// <param name="trace">
    /// Optional sink for Lua <c>Debug.Trace</c> output; defaults to
    /// <see cref="System.Diagnostics.Trace"/>.
    /// </param>
    public S125AtonLuaDataProvider(S125Dataset dataset, FeatureCatalogue s101FeatureCatalogue, Action<string>? trace = null)
        : this(S125AtonPortrayalProjection.Project(
                dataset ?? throw new ArgumentNullException(nameof(dataset)),
                s101FeatureCatalogue ?? throw new ArgumentNullException(nameof(s101FeatureCatalogue))),
            s101FeatureCatalogue,
            trace)
    {
    }

    /// <summary>
    /// Initialises the provider over already-projected aids.
    /// </summary>
    /// <param name="aids">The projected aids (see <see cref="S125AtonPortrayalProjection.Project"/>).</param>
    /// <param name="s101FeatureCatalogue">The S-101 feature catalogue the rules are written against.</param>
    /// <param name="trace">Optional sink for Lua <c>Debug.Trace</c> output.</param>
    public S125AtonLuaDataProvider(
        IReadOnlyList<S125ProjectedAton> aids, FeatureCatalogue s101FeatureCatalogue, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(aids);
        ArgumentNullException.ThrowIfNull(s101FeatureCatalogue);
        _fc = s101FeatureCatalogue;
        _trace = trace ?? (message => System.Diagnostics.Trace.WriteLine(message));

        double id = 1;
        foreach (var aid in aids)
        {
            _featureById[id] = aid;
            _numericIdByGmlId[aid.Source.Id] = id;
            id++;
        }

        BuildStructureEquipment();
        BuildSpatialRecords();
    }

    /// <summary>The number of S-125 features handed to the S-101 rules.</summary>
    public int FeatureCount => _featureById.Count;

    /// <inheritdoc/>
    public IReadOnlyList<EmittedInstruction> EmittedInstructions => _emitted;

    /// <inheritdoc/>
    public IReadOnlyList<string> PostLoadScripts =>
    [
        S101LuaDataProvider.SpatialAssociationShim,
        S101LuaDataProvider.HostGetSpatialShim,
        S101LuaDataProvider.ContainsGuard,
        S101LuaDataProvider.FeatureNamePatch,
    ];

    /// <inheritdoc/>
    /// <remarks>
    /// Accepts either the <c>gml:id</c> carried by emitted instructions or the
    /// numeric id used inside the Lua engine; returns the S-101 feature type
    /// the feature is portrayed as.
    /// </remarks>
    public string? GetFeatureTypeCode(string featureRef)
    {
        if (string.IsNullOrEmpty(featureRef)) return null;
        if (_numericIdByGmlId.TryGetValue(featureRef, out var mapped))
            return _featureById[mapped].S101FeatureType;
        return double.TryParse(featureRef, NumberStyles.Float, CultureInfo.InvariantCulture, out var id)
            && _featureById.TryGetValue(id, out var aid)
            ? aid.S101FeatureType
            : null;
    }

    /// <inheritdoc/>
    public void RegisterHostFunctions(ILuaContext lua)
    {
        ArgumentNullException.ThrowIfNull(lua);

        lua.SetGlobal("HostDebugTrace", (Action<string>)(msg => _trace($"[S125 Lua] {msg}")));
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

        lua.SetGlobal("HostGetFeatureIDs", (Func<List<object>>)(() => _featureById.Keys.Select(k => (object)k).ToList()));
        lua.SetGlobal("HostFeatureGetCode", (Func<double, string>)(fid =>
            _featureById.TryGetValue(fid, out var aid) ? aid.S101FeatureType : ""));
        lua.SetGlobal("HostFeatureGetSimpleAttribute",
            (Func<double, string, string, List<object>>)HostFeatureGetSimpleAttribute);
        lua.SetGlobal("HostFeatureGetComplexAttributeCount",
            (Func<double, string, string, double>)HostFeatureGetComplexAttributeCount);
        lua.SetGlobal("HostFeatureGetSpatialAssociations",
            (Func<double, object?>)HostFeatureGetSpatialAssociations);
        lua.SetGlobal("HostFeatureGetAssociatedFeatureIDs",
            (Func<double, string, string?, List<object>>)HostFeatureGetAssociatedFeatureIDs);
        lua.SetGlobal("HostFeatureGetAssociatedInformationIDs",
            (Func<double, string, string?, List<object>>)((_, _, _) => []));

        // No information types are projected: the S-125 information types
        // (AtonStatusInformation, SpatialQuality) are portrayed by the S-125
        // catalogue, not the S-101 rules.
        lua.SetGlobal("HostInformationTypeGetCode", (Func<double, string>)(_ => ""));
        lua.SetGlobal("HostInformationTypeGetSimpleAttribute",
            (Func<double, string, string, List<object>>)((_, _, _) => []));
        lua.SetGlobal("HostInformationTypeGetComplexAttributeCount",
            (Func<double, string, string, double>)((_, _, _) => 0));

        lua.SetGlobal("HostGetSpatialData", (Func<string, object?>)(sid =>
            _spatialData.TryGetValue(sid, out var data) ? data : null));
        lua.SetGlobal("HostSpatialGetAssociatedFeatureIDs", (Func<string, List<object>>)(sid =>
            _featuresBySpatial.TryGetValue(sid, out var ids) ? ids.Select(i => (object)i).ToList() : []));
        lua.SetGlobal("HostSpatialGetAssociatedInformationIDs",
            (Func<string, string, string?, List<object>>)((_, _, _) => []));

        lua.SetGlobal("HostGetFeatureTypeCodes", (Func<List<object>>)(() => _fc.FeatureTypes.Select(ft => (object)ft.Code).ToList()));
        lua.SetGlobal("HostGetInformationTypeCodes", (Func<List<object>>)(() => _fc.InformationTypes.Select(it => (object)it.Code).ToList()));
        lua.SetGlobal("HostGetSimpleAttributeTypeCodes", (Func<List<object>>)(() => _fc.SimpleAttributes.Select(a => (object)a.Code).ToList()));
        lua.SetGlobal("HostGetComplexAttributeTypeCodes", (Func<List<object>>)(() => _fc.ComplexAttributes.Select(a => (object)a.Code).ToList()));
        lua.SetGlobal("HostGetRoleTypeCodes", (Func<List<object>>)(() => _fc.Roles.Select(r => (object)r.Code).ToList()));
        lua.SetGlobal("HostGetInformationAssociationTypeCodes", (Func<List<object>>)(() => _fc.InformationAssociations.Select(a => (object)a.Code).ToList()));
        lua.SetGlobal("HostGetFeatureAssociationTypeCodes", (Func<List<object>>)(() => _fc.FeatureAssociations.Select(a => (object)a.Code).ToList()));
        lua.SetGlobal("HostGetFeatureTypeInfo",
            (Func<string, IReadOnlyDictionary<string, object?>>)HostGetFeatureTypeInfo);
        lua.SetGlobal("HostGetInformationTypeInfo",
            (Func<string, IReadOnlyDictionary<string, object?>>)HostGetInformationTypeInfo);
        lua.SetGlobal("HostGetSimpleAttributeTypeInfo",
            (Func<string, IReadOnlyDictionary<string, object?>>)HostGetSimpleAttributeTypeInfo);
        lua.SetGlobal("HostGetComplexAttributeTypeInfo",
            (Func<string, IReadOnlyDictionary<string, object?>>)HostGetComplexAttributeTypeInfo);

        lua.SetGlobal("HostPortrayalEmit", (Func<string, string, string, bool>)HostPortrayalEmit);
        lua.SetGlobal("HostDebuggerEntry", (Action<string, string?>)((action, message) =>
        {
            if (action == "trace" && message is not null)
                _trace($"[S125 Lua] {message}");
        }));
    }

    // ── Feature data access ───────────────────────────────────────────

    private List<object> HostFeatureGetSimpleAttribute(double featureId, string attributePath, string attributeCode)
    {
        if (!_featureById.TryGetValue(featureId, out var aid))
            return [];
        var scope = ResolveScope(aid.Attributes, attributePath);
        var result = new List<object>();
        foreach (var node in scope)
        {
            if (!node.IsComplex && string.Equals(node.Code, attributeCode, StringComparison.Ordinal))
                result.Add(node.Value!);
        }
        return result;
    }

    private double HostFeatureGetComplexAttributeCount(double featureId, string attributePath, string attributeCode)
    {
        if (!_featureById.TryGetValue(featureId, out var aid))
            return 0;
        return ResolveScope(aid.Attributes, attributePath)
            .Count(n => n.IsComplex && string.Equals(n.Code, attributeCode, StringComparison.Ordinal));
    }

    /// <summary>
    /// Navigates a Part 9A attribute path (<c>code:index;code:index…</c>,
    /// 1-based instance indices) into the attribute tree, returning the
    /// children of the addressed complex attribute instance (or the top-level
    /// attributes for an empty path). A malformed or unresolvable path yields
    /// an empty scope.
    /// </summary>
    internal static IReadOnlyList<S125AttributeNode> ResolveScope(IReadOnlyList<S125AttributeNode> attributes, string? attributePath)
    {
        if (string.IsNullOrEmpty(attributePath))
            return attributes;

        var current = attributes;
        foreach (var segment in attributePath.Split(';'))
        {
            var colon = segment.IndexOf(':');
            if (colon <= 0 || !int.TryParse(segment.AsSpan(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                return [];
            var code = segment[..colon];
            var found = 0;
            IReadOnlyList<S125AttributeNode>? next = null;
            foreach (var node in current)
            {
                if (node.IsComplex && string.Equals(node.Code, code, StringComparison.Ordinal) && ++found == index)
                {
                    next = node.Children;
                    break;
                }
            }
            if (next is null)
                return [];
            current = next;
        }
        return current;
    }

    private object? HostFeatureGetSpatialAssociations(double featureId)
    {
        if (!_spatialByFeature.TryGetValue(featureId, out var spatial))
            return null;
        return new List<object>
        {
            new Dictionary<string, object?>
            {
                ["SpatialID"] = spatial.Id,
                ["SpatialType"] = spatial.Type,
                ["Orientation"] = "Forward",
            },
        };
    }

    /// <summary>
    /// Resolves the S-101 <c>StructureEquipment</c> association (roles
    /// <c>theStructure</c> / <c>theEquipment</c>) from the S-125
    /// <c>StructureEquipment</c> association (roles <c>parent</c> /
    /// <c>child</c>). Other associations have no S-101 equivalent and resolve
    /// to nothing.
    /// </summary>
    private List<object> HostFeatureGetAssociatedFeatureIDs(double featureId, string associationCode, string? roleCode)
    {
        if (!string.Equals(associationCode, "StructureEquipment", StringComparison.Ordinal))
            return [];

        var result = new List<object>();
        if (roleCode is null or "theStructure" && _structureByEquipment.TryGetValue(featureId, out var structures))
            result.AddRange(structures.Select(s => (object)s));
        if (roleCode is null or "theEquipment" && _equipmentByStructure.TryGetValue(featureId, out var equipment))
            result.AddRange(equipment.Select(e => (object)e));
        return result;
    }

    private bool HostPortrayalEmit(string featureRef, string drawingInstructions, string observedParams)
    {
        // The Lua side references features by numeric id; the geometry
        // provider is keyed by the S-125 gml:id.
        if (double.TryParse(featureRef, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericId)
            && _featureById.TryGetValue(numericId, out var aid))
        {
            featureRef = aid.Source.Id;
        }

        _emitted.Add(new EmittedInstruction
        {
            FeatureRef = featureRef,
            InstructionString = drawingInstructions,
            ObservedParameters = observedParams,
        });
        return true;
    }

    // ── Associations and spatial records ───────────────────────────────

    private void BuildStructureEquipment()
    {
        void Link(double structure, double equipment)
        {
            if (!_equipmentByStructure.TryGetValue(structure, out var e))
                _equipmentByStructure[structure] = e = [];
            if (!e.Contains(equipment)) e.Add(equipment);
            if (!_structureByEquipment.TryGetValue(equipment, out var s))
                _structureByEquipment[equipment] = s = [];
            if (!s.Contains(structure)) s.Add(structure);
        }

        foreach (var (id, aid) in _featureById)
        {
            foreach (var r in aid.Source.FeatureReferences)
            {
                if (!_numericIdByGmlId.TryGetValue(r.FeatureRef, out var other))
                    continue;
                if (r.Role == "parent") Link(other, id);
                else if (r.Role == "child") Link(id, other);
            }
        }
    }

    private void BuildSpatialRecords()
    {
        var pointIds = new List<(GeoPosition Position, string Id)>();

        foreach (var (fid, aid) in _featureById)
        {
            var f = aid.Source;
            var key = fid.ToString(CultureInfo.InvariantCulture);
            switch (f.GeometryType)
            {
                case S100GeometryType.Point when f.Points.Count > 0:
                    {
                        var pos = f.Points[0];
                        string? sid = null;
                        foreach (var (p, id) in pointIds)
                        {
                            if (S125AtonPortrayalProjection.SamePosition(p, pos)) { sid = id; break; }
                        }
                        if (sid is null)
                        {
                            sid = $"P:{pointIds.Count + 1}";
                            pointIds.Add((pos, sid));
                            _spatialData[sid] = PointData(pos);
                        }
                        Associate(fid, sid, "Point");
                        break;
                    }
                case S100GeometryType.Curve when f.Curves.Count > 0:
                    {
                        var parts = new List<string>();
                        for (int i = 0; i < f.Curves.Count; i++)
                        {
                            if (f.Curves[i].Count >= 2)
                                parts.Add(AddCurve($"{key}:L{i}", f.Curves[i]));
                        }
                        if (parts.Count == 0) break;
                        var ccid = $"CC:{key}";
                        _spatialData[ccid] = CompositeCurveData(parts);
                        Associate(fid, ccid, "CompositeCurve");
                        break;
                    }
                case S100GeometryType.Surface when f.ExteriorRing.Count >= 3:
                    {
                        var exterior = AddRing($"{key}:R0", f.ExteriorRing);
                        var interiors = new List<object>();
                        for (int i = 0; i < f.InteriorRings.Count; i++)
                        {
                            if (f.InteriorRings[i].Count >= 3)
                                interiors.Add(SpatialRef(AddRing($"{key}:R{i + 1}", f.InteriorRings[i]), "CompositeCurve"));
                        }
                        var surfaceId = $"S:{key}";
                        _spatialData[surfaceId] = new Dictionary<string, object?>
                        {
                            ["RecordType"] = "Surface",
                            ["ExteriorRing"] = SpatialRef(exterior, "CompositeCurve"),
                            ["InteriorRings"] = interiors,
                        };
                        Associate(fid, surfaceId, "Surface");
                        break;
                    }
            }
        }
    }

    private void Associate(double featureId, string spatialId, string spatialType)
    {
        _spatialByFeature[featureId] = (spatialId, spatialType);
        if (!_featuresBySpatial.TryGetValue(spatialId, out var list))
            _featuresBySpatial[spatialId] = list = [];
        list.Add(featureId);
    }

    private string AddRing(string key, IReadOnlyList<GeoPosition> ring)
    {
        var curve = AddCurve(key, ring);
        var ccid = $"CC:{key}";
        _spatialData[ccid] = CompositeCurveData([curve]);
        return ccid;
    }

    private string AddCurve(string key, IReadOnlyList<GeoPosition> coordinates)
    {
        var start = $"CP:{key}:s";
        var end = $"CP:{key}:e";
        _spatialData[start] = PointData(coordinates[0]);
        _spatialData[end] = PointData(coordinates[^1]);

        var control = new List<object>(Math.Max(0, coordinates.Count - 2));
        for (int i = 1; i < coordinates.Count - 1; i++)
        {
            control.Add(new Dictionary<string, object?>
            {
                ["X"] = Format(coordinates[i].Longitude),
                ["Y"] = Format(coordinates[i].Latitude),
            });
        }

        var cid = $"C:{key}";
        _spatialData[cid] = new Dictionary<string, object?>
        {
            ["RecordType"] = "Curve",
            ["StartPointID"] = start,
            ["EndPointID"] = end,
            ["ControlPoints"] = control,
        };
        return cid;
    }

    private static Dictionary<string, object?> CompositeCurveData(IReadOnlyList<string> curveIds) => new()
    {
        ["RecordType"] = "CompositeCurve",
        ["CurveAssociations"] = curveIds.Select(c => (object)SpatialRef(c, "Curve")).ToList(),
    };

    private static Dictionary<string, object?> SpatialRef(string id, string type) => new()
    {
        ["SpatialID"] = id,
        ["SpatialType"] = type,
        ["Orientation"] = "Forward",
    };

    // Lua expects X = longitude, Y = latitude, in decimal degrees.
    private static Dictionary<string, object?> PointData(GeoPosition p) => new()
    {
        ["RecordType"] = "Point",
        ["X"] = Format(p.Longitude),
        ["Y"] = Format(p.Latitude),
    };

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    // ── Type system (S-101 feature catalogue) ──────────────────────────

    private IReadOnlyDictionary<string, object?> HostGetFeatureTypeInfo(string code)
    {
        _featureTypeByCode ??= _fc.FeatureTypes.GroupBy(ft => ft.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _featureTypeByCode.TryGetValue(code, out var ft)
            ? BuildObjectTypeInfo("FeatureType", ft.Code, ft.Name, ft.AttributeBindings, ft.IsAbstract)
            : new Dictionary<string, object?>();
    }

    private IReadOnlyDictionary<string, object?> HostGetInformationTypeInfo(string code)
    {
        _infoTypeByCode ??= _fc.InformationTypes.GroupBy(it => it.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _infoTypeByCode.TryGetValue(code, out var it)
            ? BuildObjectTypeInfo("InformationType", it.Code, it.Name, it.AttributeBindings, it.IsAbstract)
            : new Dictionary<string, object?>();
    }

    private IReadOnlyDictionary<string, object?> HostGetSimpleAttributeTypeInfo(string code)
    {
        _simpleAttrByCode ??= _fc.SimpleAttributes.GroupBy(a => a.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (!_simpleAttrByCode.TryGetValue(code, out var sa))
            return new Dictionary<string, object?>();

        var listed = new Dictionary<string, object?> { ["Type"] = "array:ListedValue" };
        for (int i = 0; i < sa.ListedValues.Count; i++)
        {
            var lv = sa.ListedValues[i];
            listed[(i + 1).ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["Type"] = "ListedValue",
                ["Label"] = lv.Label,
                ["Code"] = double.TryParse(lv.Code, CultureInfo.InvariantCulture, out var c) ? c : 0.0,
            };
        }

        return new Dictionary<string, object?>
        {
            ["Type"] = "SimpleAttribute",
            ["Code"] = sa.Code,
            ["Name"] = sa.Name,
            ["ValueType"] = sa.ValueType,
            ["ListedValues"] = listed,
        };
    }

    private IReadOnlyDictionary<string, object?> HostGetComplexAttributeTypeInfo(string code)
    {
        _complexAttrByCode ??= _fc.ComplexAttributes.GroupBy(a => a.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (!_complexAttrByCode.TryGetValue(code, out var ca))
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

    private static IReadOnlyDictionary<string, object?> BuildObjectTypeInfo(
        string type, string code, string name, IReadOnlyList<AttributeBinding> attributeBindings, bool isAbstract)
    {
        var bindings = new Dictionary<string, object?> { ["Type"] = "array:AttributeBinding" };
        foreach (var ab in attributeBindings)
            bindings[ab.AttributeRef] = BindingInfo(ab);

        return new Dictionary<string, object?>
        {
            ["Type"] = type,
            ["Code"] = code,
            ["Name"] = name,
            ["Abstract"] = isAbstract,
            ["AttributeBindings"] = bindings,
        };
    }

    private static Dictionary<string, object?> BindingInfo(AttributeBinding ab) => new()
    {
        ["Type"] = "AttributeBinding",
        ["AttributeCode"] = ab.AttributeRef,
        ["LowerMultiplicity"] = (double)ab.Multiplicity.Lower,
        ["UpperMultiplicity"] = ab.Multiplicity.Upper is int u ? (double)u : null,
        ["Sequential"] = ab.Sequential,
    };
}
