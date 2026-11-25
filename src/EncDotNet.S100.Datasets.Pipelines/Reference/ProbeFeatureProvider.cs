using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>A synthetic feature for probing a rule: a type, a primitive and attribute values.</summary>
/// <param name="Code">The feature type code.</param>
/// <param name="Primitive">The primitive, as Lua's <c>PrimitiveType</c> names it: <c>Point</c>, <c>MultiPoint</c>, <c>Curve</c>, <c>Surface</c> or <c>None</c>.</param>
/// <param name="Attributes">Each simple attribute's encoded values; an attribute left out has none.</param>
internal sealed record ProbeFeature(string Code, string Primitive, IReadOnlyDictionary<string, IReadOnlyList<string>> Attributes);

/// <summary>
/// Implements the S-100 Part 9a Lua host API over synthetic features, so
/// the prober can portray a feature type with chosen attribute values
/// through the catalogue's own rules (design note §7, R13a).
/// </summary>
/// <remarks>
/// <para>
/// Feature ids are 1-based positions in the feature list. Each feature has
/// its own spatial record at 0°, 0° (a point, a short line or a small
/// square), so no feature finds another at its position. The rules see no
/// associations and no information types.
/// </para>
/// <para>
/// Type information comes from the feature catalogue the rules are
/// written against. The S-101 host shims run after <c>main.lua</c>, as for
/// a dataset.
/// </para>
/// </remarks>
internal sealed class ProbeFeatureProvider : ILuaDataProvider
{
    private const double Size = 0.001;

    private readonly IReadOnlyList<ProbeFeature> _features;
    private readonly FeatureCatalogue _fc;
    private readonly List<EmittedInstruction> _emitted = [];
    private Dictionary<string, FeatureType>? _featureTypes;
    private Dictionary<string, SimpleAttribute>? _simpleAttributes;
    private Dictionary<string, ComplexAttribute>? _complexAttributes;

    public ProbeFeatureProvider(IReadOnlyList<ProbeFeature> features, FeatureCatalogue featureCatalogue)
    {
        _features = features;
        _fc = featureCatalogue;
    }

    /// <summary>
    /// Whether the rules reported an error, and fell back to the default
    /// symbology, since <see cref="ResetError"/>.
    /// </summary>
    public bool RuleFailed { get; private set; }

    /// <summary>Clears <see cref="RuleFailed"/> before the next feature.</summary>
    public void ResetError() => RuleFailed = false;

    public IReadOnlyList<EmittedInstruction> EmittedInstructions => _emitted;

    public IReadOnlyList<string> PostLoadScripts =>
    [
        S101LuaDataProvider.SpatialAssociationShim,
        S101LuaDataProvider.HostGetSpatialShim,
        S101LuaDataProvider.ContainsGuard,
        S101LuaDataProvider.FeatureNamePatch,
    ];

    public string? GetFeatureTypeCode(string featureRef) =>
        TryFeature(featureRef, out var feature) ? feature.Code : null;

    public void RegisterHostFunctions(ILuaContext lua)
    {
        ArgumentNullException.ThrowIfNull(lua);

        // main.lua reports a rule's error through Debug.Trace before drawing
        // the default symbology; that's how a probe knows the rule failed.
        lua.SetGlobal("HostDebugTrace", (Action<string>)OnTrace);
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

        lua.SetGlobal("HostGetFeatureIDs", (Func<List<object>>)(() =>
            Enumerable.Range(1, _features.Count).Select(i => (object)(double)i).ToList()));
        lua.SetGlobal("HostFeatureGetCode", (Func<double, string>)(id => Feature(id)?.Code ?? string.Empty));
        lua.SetGlobal("HostFeatureGetSimpleAttribute", (Func<double, string, string, List<object>>)HostFeatureGetSimpleAttribute);
        lua.SetGlobal("HostFeatureGetComplexAttributeCount", (Func<double, string, string, double>)((_, _, _) => 0));
        lua.SetGlobal("HostFeatureGetSpatialAssociations", (Func<double, object?>)HostFeatureGetSpatialAssociations);
        lua.SetGlobal("HostFeatureGetAssociatedFeatureIDs", (Func<double, string, string?, List<object>>)((_, _, _) => []));
        lua.SetGlobal("HostFeatureGetAssociatedInformationIDs", (Func<double, string, string?, List<object>>)((_, _, _) => []));

        lua.SetGlobal("HostInformationTypeGetCode", (Func<double, string>)(_ => string.Empty));
        lua.SetGlobal("HostInformationTypeGetSimpleAttribute", (Func<double, string, string, List<object>>)((_, _, _) => []));
        lua.SetGlobal("HostInformationTypeGetComplexAttributeCount", (Func<double, string, string, double>)((_, _, _) => 0));

        lua.SetGlobal("HostGetSpatialData", (Func<string, object?>)HostGetSpatialData);
        lua.SetGlobal("HostSpatialGetAssociatedFeatureIDs", (Func<string, List<object>>)HostSpatialGetAssociatedFeatureIDs);
        lua.SetGlobal("HostSpatialGetAssociatedInformationIDs", (Func<string, string, string?, List<object>>)((_, _, _) => []));

        lua.SetGlobal("HostGetFeatureTypeCodes", (Func<List<object>>)(() => _fc.FeatureTypes.Select(t => (object)t.Code).ToList()));
        lua.SetGlobal("HostGetInformationTypeCodes", (Func<List<object>>)(() => _fc.InformationTypes.Select(t => (object)t.Code).ToList()));
        lua.SetGlobal("HostGetSimpleAttributeTypeCodes", (Func<List<object>>)(() => _fc.SimpleAttributes.Select(a => (object)a.Code).ToList()));
        lua.SetGlobal("HostGetComplexAttributeTypeCodes", (Func<List<object>>)(() => _fc.ComplexAttributes.Select(a => (object)a.Code).ToList()));
        lua.SetGlobal("HostGetRoleTypeCodes", (Func<List<object>>)(() => _fc.Roles.Select(r => (object)r.Code).ToList()));
        lua.SetGlobal("HostGetInformationAssociationTypeCodes", (Func<List<object>>)(() => _fc.InformationAssociations.Select(a => (object)a.Code).ToList()));
        lua.SetGlobal("HostGetFeatureAssociationTypeCodes", (Func<List<object>>)(() => _fc.FeatureAssociations.Select(a => (object)a.Code).ToList()));
        lua.SetGlobal("HostGetFeatureTypeInfo", (Func<string, IReadOnlyDictionary<string, object?>>)HostGetFeatureTypeInfo);
        lua.SetGlobal("HostGetInformationTypeInfo", (Func<string, IReadOnlyDictionary<string, object?>>)HostGetInformationTypeInfo);
        lua.SetGlobal("HostGetSimpleAttributeTypeInfo", (Func<string, IReadOnlyDictionary<string, object?>>)HostGetSimpleAttributeTypeInfo);
        lua.SetGlobal("HostGetComplexAttributeTypeInfo", (Func<string, IReadOnlyDictionary<string, object?>>)HostGetComplexAttributeTypeInfo);

        lua.SetGlobal("HostPortrayalEmit", (Func<string, string, string, bool>)((featureRef, instructions, observed) =>
        {
            _emitted.Add(new EmittedInstruction
            {
                FeatureRef = featureRef,
                InstructionString = instructions,
                ObservedParameters = observed,
            });
            return true;
        }));
        lua.SetGlobal("HostDebuggerEntry", (Action<string, string?>)((action, message) =>
        {
            if (action == "trace" && message is not null) OnTrace(message);
        }));
    }

    private void OnTrace(string message)
    {
        if (message.StartsWith("Error", StringComparison.Ordinal)) RuleFailed = true;
    }

    private ProbeFeature? Feature(double id)
    {
        var index = (int)id - 1;
        return index >= 0 && index < _features.Count && index + 1 == id ? _features[index] : null;
    }

    private bool TryFeature(string featureRef, out ProbeFeature feature)
    {
        feature = null!;
        if (!double.TryParse(featureRef, NumberStyles.Float, CultureInfo.InvariantCulture, out var id)) return false;
        var found = Feature(id);
        if (found is null) return false;
        feature = found;
        return true;
    }

    private List<object> HostFeatureGetSimpleAttribute(double featureId, string attributePath, string attributeCode)
    {
        // Only top-level simple attributes are probed.
        if (!string.IsNullOrEmpty(attributePath) || Feature(featureId) is not { } feature) return [];
        return feature.Attributes.TryGetValue(attributeCode, out var values) ? values.Select(v => (object)v).ToList() : [];
    }

    // Spatial ids: P:n (point), M:n (multipoint), C:n (curve), CC:n (composite
    // curve), R:n and RC:n (a surface's ring), S:n (surface), E0:n, E1:n (ends).
    private object? HostFeatureGetSpatialAssociations(double featureId)
    {
        if (Feature(featureId) is not { } feature) return null;
        var key = Key(featureId);
        (string Id, string Type)? spatial = feature.Primitive switch
        {
            "Point" => ($"P:{key}", "Point"),
            "MultiPoint" => ($"M:{key}", "MultiPoint"),
            "Curve" => ($"CC:{key}", "CompositeCurve"),
            "Surface" => ($"S:{key}", "Surface"),
            _ => null,
        };
        if (spatial is not { } s) return null;
        return new List<object> { SpatialRef(s.Id, s.Type) };
    }

    private List<object> HostSpatialGetAssociatedFeatureIDs(string spatialId)
    {
        var colon = spatialId.IndexOf(':');
        if (colon < 0) return [];
        var prefix = spatialId[..colon];
        if (prefix is not ("P" or "M" or "CC" or "S")) return [];
        return double.TryParse(spatialId.AsSpan(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var id) && Feature(id) is not null
            ? [id]
            : [];
    }

    private object? HostGetSpatialData(string spatialId)
    {
        var colon = spatialId.IndexOf(':');
        if (colon < 0) return null;
        var key = spatialId[(colon + 1)..];
        return spatialId[..colon] switch
        {
            "P" or "E0" => Point(0, 0),
            "E1" => Point(Size, 0),
            "RE" => Point(0, 0),
            "M" => new Dictionary<string, object?>
            {
                ["RecordType"] = "MultiPoint",
                ["Points"] = new List<object>
                {
                    new Dictionary<string, object?> { ["X"] = "0", ["Y"] = "0", ["Z"] = "10" },
                },
            },
            "C" => Curve($"E0:{key}", $"E1:{key}", []),
            "CC" => CompositeCurve($"C:{key}"),
            "R" => Curve($"RE:{key}", $"RE:{key}", [(Size, 0), (Size, Size), (0, Size)]),
            "RC" => CompositeCurve($"R:{key}"),
            "S" => new Dictionary<string, object?>
            {
                ["RecordType"] = "Surface",
                ["ExteriorRing"] = SpatialRef($"RC:{key}", "CompositeCurve"),
                ["InteriorRings"] = new List<object>(),
            },
            _ => null,
        };
    }

    private static string Key(double id) => id.ToString("R", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Point(double x, double y) => new()
    {
        ["RecordType"] = "Point",
        ["X"] = x.ToString("R", CultureInfo.InvariantCulture),
        ["Y"] = y.ToString("R", CultureInfo.InvariantCulture),
    };

    private static Dictionary<string, object?> Curve(string start, string end, IReadOnlyList<(double X, double Y)> control) => new()
    {
        ["RecordType"] = "Curve",
        ["StartPointID"] = start,
        ["EndPointID"] = end,
        ["ControlPoints"] = control
            .Select(p => (object)new Dictionary<string, object?>
            {
                ["X"] = p.X.ToString("R", CultureInfo.InvariantCulture),
                ["Y"] = p.Y.ToString("R", CultureInfo.InvariantCulture),
            })
            .ToList(),
    };

    private static Dictionary<string, object?> CompositeCurve(string curve) => new()
    {
        ["RecordType"] = "CompositeCurve",
        ["CurveAssociations"] = new List<object> { SpatialRef(curve, "Curve") },
    };

    private static Dictionary<string, object?> SpatialRef(string id, string type) => new()
    {
        ["SpatialID"] = id,
        ["SpatialType"] = type,
        ["Orientation"] = "Forward",
    };

    // ── Type information, from the feature catalogue ───────────────────

    private IReadOnlyDictionary<string, object?> HostGetFeatureTypeInfo(string code)
    {
        _featureTypes ??= _fc.FeatureTypes.GroupBy(t => t.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _featureTypes.TryGetValue(code, out var type)
            ? ObjectTypeInfo("FeatureType", type.Code, type.Name, type.AttributeBindings, type.IsAbstract)
            : new Dictionary<string, object?>();
    }

    private IReadOnlyDictionary<string, object?> HostGetInformationTypeInfo(string code)
    {
        var type = _fc.InformationTypes.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.Ordinal));
        return type is null
            ? new Dictionary<string, object?>()
            : ObjectTypeInfo("InformationType", type.Code, type.Name, type.AttributeBindings, type.IsAbstract);
    }

    private IReadOnlyDictionary<string, object?> HostGetSimpleAttributeTypeInfo(string code)
    {
        _simpleAttributes ??= _fc.SimpleAttributes.GroupBy(a => a.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (!_simpleAttributes.TryGetValue(code, out var attribute)) return new Dictionary<string, object?>();

        var listed = new Dictionary<string, object?> { ["Type"] = "array:ListedValue" };
        for (var i = 0; i < attribute.ListedValues.Count; i++)
        {
            var value = attribute.ListedValues[i];
            listed[(i + 1).ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["Type"] = "ListedValue",
                ["Label"] = value.Label,
                ["Code"] = double.TryParse(value.Code, CultureInfo.InvariantCulture, out var c) ? c : 0.0,
            };
        }

        return new Dictionary<string, object?>
        {
            ["Type"] = "SimpleAttribute",
            ["Code"] = attribute.Code,
            ["Name"] = attribute.Name,
            ["ValueType"] = attribute.ValueType,
            ["ListedValues"] = listed,
        };
    }

    private IReadOnlyDictionary<string, object?> HostGetComplexAttributeTypeInfo(string code)
    {
        _complexAttributes ??= _fc.ComplexAttributes.GroupBy(a => a.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (!_complexAttributes.TryGetValue(code, out var attribute)) return new Dictionary<string, object?>();

        var bindings = new Dictionary<string, object?> { ["Type"] = "array:AttributeBinding" };
        foreach (var binding in attribute.SubAttributeBindings)
        {
            bindings[binding.AttributeRef] = new Dictionary<string, object?>
            {
                ["Type"] = "AttributeBinding",
                ["AttributeCode"] = binding.AttributeRef,
                ["LowerMultiplicity"] = (double)binding.Multiplicity.Lower,
                ["UpperMultiplicity"] = binding.Multiplicity.Upper is int upper ? (double)upper : null,
            };
        }

        return new Dictionary<string, object?>
        {
            ["Type"] = "ComplexAttribute",
            ["Code"] = attribute.Code,
            ["Name"] = attribute.Name,
            ["AttributeBindings"] = bindings,
        };
    }

    private static IReadOnlyDictionary<string, object?> ObjectTypeInfo(
        string type, string code, string name, IReadOnlyList<AttributeBinding> attributeBindings, bool isAbstract)
    {
        var bindings = new Dictionary<string, object?> { ["Type"] = "array:AttributeBinding" };
        foreach (var binding in attributeBindings) bindings[binding.AttributeRef] = BindingInfo(binding);

        return new Dictionary<string, object?>
        {
            ["Type"] = type,
            ["Code"] = code,
            ["Name"] = name,
            ["Abstract"] = isAbstract,
            ["AttributeBindings"] = bindings,
        };
    }

    private static Dictionary<string, object?> BindingInfo(AttributeBinding binding) => new()
    {
        ["Type"] = "AttributeBinding",
        ["AttributeCode"] = binding.AttributeRef,
        ["LowerMultiplicity"] = (double)binding.Multiplicity.Lower,
        ["UpperMultiplicity"] = binding.Multiplicity.Upper is int upper ? (double)upper : null,
        ["Sequential"] = binding.Sequential,
    };
}

/// <summary>Creates the <see cref="ProbeFeatureProvider"/> of one probe session, and keeps it for the prober to read.</summary>
internal sealed class ProbeFeatureProviderFactory(IReadOnlyList<ProbeFeature> features, FeatureCatalogue featureCatalogue)
    : ILuaDataProviderFactory
{
    public ProbeFeatureProvider? Provider { get; private set; }

    public ILuaDataProvider Create(MarinerSettings mariner) => Provider = new ProbeFeatureProvider(features, featureCatalogue);
}
