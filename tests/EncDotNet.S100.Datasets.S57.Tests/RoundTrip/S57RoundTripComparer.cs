using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Tests.RoundTrip;

/// <summary>How a round-trip difference is explained.</summary>
internal enum RoundTripClass
{
    /// <summary>A loss the conversion rules call for, with the clause that does.</summary>
    ExpectedLoss,

    /// <summary>A restructuring with an inverse rule.</summary>
    ExpectedRestructure,

    /// <summary>Not explained by any rule; most likely a forward-mapping bug.</summary>
    Unexplained,

    /// <summary>The S-101 intermediate carried the value, but the reverse lost it.</summary>
    ReverseBug,
}

/// <summary>One difference between a source S-57 object and its round-tripped twin.</summary>
/// <param name="ObjectClass">The source S-57 object class acronym (or OBJL).</param>
/// <param name="Attribute">The S-57 attribute acronym, or a pseudo-attribute such as <c>(feature)</c>.</param>
/// <param name="Kind">What differs: <c>missing</c>, <c>added</c>, <c>value</c>, <c>class</c>, <c>primitive</c>, <c>geometry</c>, <c>pointers</c>.</param>
/// <param name="Class">How it is explained.</param>
/// <param name="Reason">The clause or a short reason.</param>
/// <param name="Source">The source value, if any.</param>
/// <param name="Result">The round-tripped value, if any.</param>
internal sealed record RoundTripDifference(
    string ObjectClass, string Attribute, string Kind, RoundTripClass Class, string Reason, string? Source, string? Result)
{
    /// <summary>
    /// The issue tracking the forward-mapping bug behind an unexplained
    /// difference, when it has been triaged.
    /// </summary>
    public string? Issue => Class != RoundTripClass.Unexplained ? null : (ObjectClass, Attribute) switch
    {
        ("BRIDGE" or "PILPNT", "(pointers)") or ("TOPMAR", "(geometry)") => "#1290",
        ("NEWOBJ", "(feature)") => "#1289",
        (_, "(pointers)") => "#1220",
        ("SOUNDG", _) => "#1219",
        (_, "CONRAD" or "CATDIS" or "CATTRK" or "CATTSS") => "#1218",
        (_, "NINFOM" or "NOBJNM" or "NTXTDS") => "#1221",
        ("TSEZNE" or "COSARE" or "163", "(feature)") => "#1222",
        ("TOPMAR", _) or ("M_QUAL", "SOUACC" or "POSACC") or ("SBDARE", "NATSUR" or "NATQUA") => "#1222",
        _ => null,
    };
}

/// <summary>
/// Compares an S-57 cell with the result of translating it to S-101 and back
/// (<see cref="S57ToS101Translator"/> then <see cref="S101ToS57Translator"/>),
/// object by object, and explains each difference.
/// </summary>
/// <remarks>
/// Objects are matched by LNAM (the FOID the forward translation keeps). An
/// object the forward pass merges or splits (sector lights, topmarks) is
/// matched by object class and position instead.
/// </remarks>
internal sealed class S57RoundTripComparer
{
    // S-57 attributes the forward translator assembles into a complex or
    // information type, with the S-101 attribute that carries the value.
    private static readonly Dictionary<string, string> AssembledCarriers = new(StringComparer.Ordinal)
    {
        ["OBJNAM"] = "name",
        ["NOBJNM"] = "name",
        ["INFORM"] = "text",
        ["NINFOM"] = "text",
        ["TXTDSC"] = "fileReference",
        ["NTXTDS"] = "fileReference",
        ["LITCHR"] = "lightCharacteristic",
        ["SIGGRP"] = "signalGroup",
        ["SIGPER"] = "signalPeriod",
        ["SIGSEQ"] = "signalDuration",
        ["DATSTA"] = "dateStart",
        ["DATEND"] = "dateEnd",
        ["PERSTA"] = "dateStart",
        ["PEREND"] = "dateEnd",
        ["SURSTA"] = "dateStart",
        ["SUREND"] = "dateEnd",
        ["CATZOC"] = "categoryOfZoneOfConfidenceInData",
        ["NATSUR"] = "natureOfSurface",
        ["NATQUA"] = "natureOfSurfaceQualifyingTerms",
        ["HORCLR"] = "horizontalClearanceValue",
        ["VERCLR"] = "verticalClearanceValue",
        ["VERCCL"] = "verticalClearanceValue",
        ["VERCOP"] = "verticalClearanceValue",
        ["VERCSA"] = "verticalClearanceValue",
        ["VERACC"] = "uncertaintyFixed",
        ["SORDAT"] = "reportedDate",
        ["SORIND"] = "source",
        ["SCAMIN"] = "scaleMinimum",
        ["VALLMA"] = "magneticAnomalyValue",
        ["RADWAL"] = "waveLengthValue",
        ["CURVEL"] = "speedMaximum",
        ["MLTYLT"] = "numberOfFeatures",
        ["COLOUR"] = "colour",
        ["LITVIS"] = "lightVisibility",
        ["VALNMR"] = "valueOfNominalRange",
        ["SECTR1"] = "sectorBearing",
        ["SECTR2"] = "sectorBearing",
        ["ORIENT"] = "orientationValue",
        ["CATBRG"] = "openingBridge",
        ["TOPSHP"] = "topmarkDaymarkShape",
        ["COLPAT"] = "colourPattern",
    };

    // S-57 object classes with no S-101 counterpart (S-65 Annex B; Annex C
    // §2.1 notes a round trip can therefore never be the identity).
    private static readonly Dictionary<string, string> ClassesWithoutS101 = new(StringComparer.Ordinal)
    {
        ["M_CSCL"] = "Annex B §2.2.6: cuts Data Coverage, CSCALE snapped to Table 2-3 (Annex C §3.5 re-creates one only where coverage scales differ)",
        ["M_HDAT"] = "Annex B: no S-101 class",
        ["M_HOPA"] = "Annex B: no S-101 class",
        ["M_PROD"] = "Annex B: no S-101 class",
        ["M_UNIT"] = "Annex B: no S-101 class",
        ["C_ASSO"] = "Annex B: no S-101 class",
        ["C_AGGR"] = "Annex B: only range systems and bridge collections carry to S-101",
        ["$AREAS"] = "cartographic object",
        ["$LINES"] = "cartographic object",
        ["$CSYMB"] = "cartographic object",
        ["$COMPS"] = "cartographic object",
        ["$TEXTS"] = "cartographic object",
    };

    // Classes with no forward rule that S-65 Annex B says aren't converted.
    private static readonly Dictionary<string, string> UnknownClassReasons = new(StringComparer.Ordinal)
    {
        ["TS_TIS"] = "Annex B §3.3.2: no S-101 equivalent (kept as S57UnknownObject, which Annex C doesn't convert)",
    };

    // Object classes the forward translation restructures and the reverse inverts.
    private static readonly HashSet<string> RestructuredClasses = new(StringComparer.Ordinal)
    {
        "BRIDGE", "DAMCON", "CTRPNT", "MORFAC", "TOPMAR", "C_AGGR",
    };

    // List attributes whose order is significant (colours read in order).
    private static readonly HashSet<string> OrderedLists = new(StringComparer.Ordinal)
    {
        "COLOUR", "NATSUR", "NATQUA",
    };

    private readonly S57S101Mapping _mapping = S57S101Mapping.Default;
    private readonly S101FeatureAttributeBindings _bindings = S101FeatureAttributeBindings.Default;
    private readonly S101AllowedEnumValues _allowed = S101AllowedEnumValues.Default;
    private readonly Dictionary<ushort, string> _attributeAcronyms;
    private readonly Dictionary<ushort, string> _classAcronyms;

    public S57RoundTripComparer()
    {
        _attributeAcronyms = _mapping.AttributeRules.Values.ToDictionary(r => r.Attl, r => r.S57Acronym);
        foreach (var (acronym, attl) in KnownAttls)
            _attributeAcronyms.TryAdd(attl, acronym);
        // The S-57 object class enum names the codes; the mapping's acronyms
        // are not used, so a mis-numbered rule shows up under its real class.
        _classAcronyms = [];
    }

    private static readonly (string, ushort)[] KnownAttls =
    [
        ("INFORM", 102), ("NINFOM", 300), ("TXTDSC", 158), ("NTXTDS", 304), ("OBJNAM", 116), ("NOBJNM", 301),
        ("LITCHR", 107), ("SIGSEQ", 143), ("DATSTA", 86), ("DATEND", 85), ("PERSTA", 119), ("PEREND", 118),
        ("SURSTA", 152), ("SUREND", 151), ("CATZOC", 72), ("NATQUA", 114), ("HORCLR", 98), ("SORDAT", 147),
        ("SORIND", 148), ("VALLMA", 175), ("RADWAL", 126), ("CURVEL", 84), ("MLTYLT", 110), ("HORACC", 97),
        ("VERACC", 180), ("POSACC", 401), ("SOUACC", 144), ("QUAPOS", 402), ("HORDAT", 400), ("RECDAT", 128), ("RECIND", 129),
        ("CSCALE", 80), ("NPLDST", 302), ("PILDST", 121), ("CATRUN", 58),
        ("CATSPM", 66), ("TOPSHP", 171), ("COLPAT", 76), ("CATCOV", 18),
    ];

    private string ClassName(int objl)
        => _classAcronyms.GetValueOrDefault((ushort)objl)
            ?? (Enum.IsDefined((S57ObjectCode)objl) ? ((S57ObjectCode)objl).ToString() : objl.ToString(CultureInfo.InvariantCulture));

    private string AttributeName(int attl) => _attributeAcronyms.GetValueOrDefault((ushort)attl, attl.ToString(CultureInfo.InvariantCulture));

    /// <summary>Compares <paramref name="source"/> with its round trip.</summary>
    /// <param name="source">The original S-57 cell.</param>
    /// <param name="intermediate">The S-101 translation of it.</param>
    /// <param name="result">The S-57 translation of <paramref name="intermediate"/>.</param>
    /// <returns>Every difference found.</returns>
    public List<RoundTripDifference> Compare(S57Document source, S101Document intermediate, S57Document result)
    {
        var differences = new List<RoundTripDifference>();
        var sourceGeometry = new GeometryIndex(source);
        _sourceByLnam = source.FeatureRecords
            .Where(f => f.RecordName.LongName.HasValue)
            .GroupBy(f => f.RecordName.LongName!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var resultGeometry = new GeometryIndex(result);

        var resultByFoid = result.FeatureRecords
            .Select((f, i) => (Feature: f, Index: i))
            .GroupBy(x => Foid(x.Feature))
            .ToDictionary(g => g.Key, g => g.ToList());
        var intermediateByFoid = intermediate.Features
            .GroupBy(f => (Agency: (int)f.ProducingAgency, Fidn: (int)f.FeatureIdentificationNumber, Fids: (int)f.FeatureIdentificationSubdivision))
            .ToDictionary(g => g.Key, g => g.ToList());
        var matched = new HashSet<int>();

        // First pass: match by FOID and object class.
        var unmatched = new List<S57FeatureRecord>();
        var pairs = new List<(S57FeatureRecord Source, S57FeatureRecord Result, bool ByPosition)>();
        foreach (var feature in source.FeatureRecords)
        {
            if (resultByFoid.TryGetValue(Foid(feature), out var candidates))
            {
                var pick = candidates.FirstOrDefault(c => !matched.Contains(c.Index) && c.Feature.ObjectCode == feature.ObjectCode);
                if (pick.Feature is null)
                    pick = candidates.FirstOrDefault(c => !matched.Contains(c.Index));
                if (pick.Feature is not null)
                {
                    matched.Add(pick.Index);
                    pairs.Add((feature, pick.Feature, false));
                    continue;
                }
            }
            unmatched.Add(feature);
        }

        // Second pass: match what the forward pass merged or split by class
        // and position.
        // A collection object (C_AGGR, C_ASSO) has no geometry, so position
        // can't tell one from another.
        foreach (var feature in unmatched)
        {
            var fingerprint = sourceGeometry.Fingerprint(feature);
            var pick = feature.SpatialPointers.Count == 0 ? default : result.FeatureRecords
                .Select((f, i) => (Feature: f, Index: i))
                .FirstOrDefault(c => !matched.Contains(c.Index)
                    && c.Feature.ObjectCode == feature.ObjectCode
                    && resultGeometry.Fingerprint(c.Feature) == fingerprint);
            if (pick.Feature is null && Retiled.Contains(ClassName((int)feature.ObjectCode)))
            {
                // §3.5 re-tiles coverage: an M_COVR / M_CSCL matches one of
                // its class with the same CATCOV / CSCALE wherever it lies.
                pick = result.FeatureRecords
                    .Select((f, i) => (Feature: f, Index: i))
                    .FirstOrDefault(c => !matched.Contains(c.Index)
                        && c.Feature.ObjectCode == feature.ObjectCode
                        && TileKey(c.Feature) == TileKey(feature));
            }
            if (pick.Feature is not null)
            {
                matched.Add(pick.Index);
                pairs.Add((feature, pick.Feature, true));
                continue;
            }
            differences.Add(ExplainMissingFeature(feature, intermediate, intermediateByFoid));
        }

        foreach (var (src, res, byPosition) in pairs)
        {
            var s101 = intermediateByFoid.GetValueOrDefault(Foid(src))?.FirstOrDefault();
            CompareFeature(src, res, s101, intermediate, sourceGeometry, resultGeometry, byPosition, differences);
        }

        // Objects the reverse wrote that match no source object.
        for (int i = 0; i < result.FeatureRecords.Count; i++)
        {
            if (matched.Contains(i)) continue;
            var feature = result.FeatureRecords[i];
            var name = ClassName((int)feature.ObjectCode);
            var reason = AddedReasons.GetValueOrDefault(name);
            differences.Add(new RoundTripDifference(name, "(feature)", "added",
                RestructuredClasses.Contains(name) || reason is not null ? RoundTripClass.ExpectedRestructure : RoundTripClass.Unexplained,
                reason ?? "no source object", null, null));
        }

        CompareTopology(source, result, differences);
        CompareCompilationScale(source, result, differences);
        return differences;
    }

    // DSPM CSCL goes to Data Coverage optimum display scales snapped to
    // Annex B Table 2-3 (§2.2.6), with M_CSCL CSCALE where one cuts the
    // coverage; Annex C §3.5 takes the finest of them back as CSCL.
    private static void CompareCompilationScale(S57Document source, S57Document result, List<RoundTripDifference> differences)
    {
        var before = source.DataSetParameters?.CompilationScale ?? 0;
        var after = result.DataSetParameters?.CompilationScale ?? 0;
        if (before == after) return;

        var finest = source.FeatureRecords
            .Where(f => (int)f.ObjectCode == (int)S57ObjectCode.M_CSCL)
            .SelectMany(f => f.Attributes.Where(a => a.AttributeCode == 80))
            .Select(a => int.TryParse(a.Value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .Where(v => v > 0)
            .Append(before)
            .Where(v => v > 0)
            .DefaultIfEmpty(0)
            .Min();
        var expected = S57ToS101Translator.OptimumDisplayScale(finest) ?? 0;
        differences.Add(after == expected
            ? new("(dataset)", "CSCL", "value", RoundTripClass.ExpectedLoss,
                "Annex B §2.2.6 (optimum display scale from Table 2-3) / Annex C §3.5 (finest optimum display scale)",
                before.ToString(CultureInfo.InvariantCulture), after.ToString(CultureInfo.InvariantCulture))
            : new("(dataset)", "CSCL", "value", RoundTripClass.Unexplained, "compilation scale differs",
                before.ToString(CultureInfo.InvariantCulture), after.ToString(CultureInfo.InvariantCulture)));
    }

    // Objects the reverse translation creates without a source object of
    // their own, with the rule that creates them.
    private static readonly Dictionary<string, string> AddedReasons = new(StringComparer.Ordinal)
    {
        ["TOPMAR"] = "Annex C §18.1 (topmark → TOPMAR)",
        ["M_COVR"] = "Annex C §3.5 (M_COVR tiles the bounding box)",
        ["M_CSCL"] = "Annex C §3.5 (M_CSCL where a coverage scale differs)",
        ["C_AGGR"] = "Annex C §6.6 / §25 (aggregation → C_AGGR)",
        ["DEPARE"] = "Annex C §2.5.1.1 (skin of the earth cut into parts)",
        ["LNDARE"] = "Annex C §2.5.1.1 (skin of the earth cut into parts)",
        ["DRGARE"] = "Annex C §2.5.1.1 (skin of the earth cut into parts)",
        ["UNSARE"] = "Annex C §2.5.1.1 (skin of the earth cut into parts)",
    };

    // Classes §3.5 rebuilds, matched by attribute rather than identity.
    private static readonly HashSet<string> Retiled = new(StringComparer.Ordinal) { "M_COVR", "M_CSCL" };

    private static string TileKey(S57FeatureRecord feature)
        => string.Join(';', feature.Attributes.Where(a => a.AttributeCode is 18 or 80).Select(a => $"{a.AttributeCode}={a.Value?.Trim()}"));

    // Runs S57TopologyCheck on both cells; an issue the result has more of
    // than the source is the reverse translation's.
    private static void CompareTopology(S57Document source, S57Document result, List<RoundTripDifference> differences)
    {
        var before = S57TopologyCheck.Check(source).GroupBy(i => i.Kind).ToDictionary(g => g.Key, g => g.Count());
        foreach (var group in S57TopologyCheck.Check(result).GroupBy(i => i.Kind))
        {
            var extra = group.Count() - before.GetValueOrDefault(group.Key);
            for (int i = 0; i < extra; i++)
            {
                differences.Add(new("(dataset)", group.Key, "topology", RoundTripClass.ReverseBug,
                    "topology check (S57TopologyCheck)", null, group.ElementAt(Math.Min(i, group.Count() - 1)).Detail));
            }
        }
    }

    private RoundTripDifference ExplainMissingFeature(
        S57FeatureRecord feature,
        S101Document intermediate,
        Dictionary<(int, int, int), List<S101FeatureRecord>> intermediateByFoid)
    {
        var name = ClassName((int)feature.ObjectCode);
        var objl = (ushort)(int)feature.ObjectCode;
        if (ClassesWithoutS101.TryGetValue(name, out var reason))
            return new(name, "(feature)", "missing", RoundTripClass.ExpectedLoss, reason, null, null);
        if (name is "M_SDAT" or "M_VDAT")
            return new(name, "(feature)", "missing", RoundTripClass.ExpectedLoss, "Annex C §3.9/§3.10: the dataset datum is stated in DSPM", null, null);

        if (intermediateByFoid.TryGetValue(Foid(feature), out var carriers))
        {
            // A class with no forward rule travels as an S57UnknownObject
            // (drawn "?"), which S-65 Annex C has no clause for.
            if (!RestructuredClasses.Contains(name) && carriers.All(c => intermediate.FeatureTypeCatalogue.GetValueOrDefault(c.FeatureTypeCode) == S57UnknownObject.FeatureTypeCode))
                return UnknownClassReasons.TryGetValue(name, out var unknownReason)
                    ? new(name, "(feature)", "missing", RoundTripClass.ExpectedLoss, unknownReason, null, null)
                    : new(name, "(feature)", "missing", RoundTripClass.Unexplained, "no forward rule (S57UnknownObject)", null, null);
            return RestructuredClasses.Contains(name)
                ? new(name, "(feature)", "missing", RoundTripClass.ExpectedRestructure, "structural inverse", null, null)
                : new(name, "(feature)", "missing", RoundTripClass.ReverseBug, "S-101 carried the feature", null, null);
        }

        if (!_mapping.FeatureRules.ContainsKey(objl))
        {
            if (name == "TOPMAR")
                return new(name, "(feature)", "missing", RoundTripClass.ExpectedRestructure, "topmark not re-created (no topmark-binding master)", null, null);
            return new(name, "(feature)", "missing", RoundTripClass.Unexplained, "no forward rule", null, null);
        }

        var view = _mapping.BuildAcronymView(feature.Attributes);
        if (name == "M_COVR" && view.GetValueOrDefault("CATCOV")?.Trim() == "2")
            return new(name, "(feature)", "missing", RoundTripClass.ExpectedLoss, "forward drops CATCOV 2 (#438); Annex C §3.5 re-tiles the bounding box", null, null);

        var primitive = (int)feature.Primitive switch { 1 => S57GeometryPrimitive.Point, 2 => S57GeometryPrimitive.Curve, 3 => S57GeometryPrimitive.Surface, _ => S57GeometryPrimitive.None };
        if (_mapping.ResolveFeature(objl, view, primitive) is null)
            return new(name, "(feature)", "missing", RoundTripClass.ExpectedLoss, "forward rule drops it (Annex B)", null, null);

        return RestructuredClasses.Contains(name)
            ? new(name, "(feature)", "missing", RoundTripClass.ExpectedRestructure, "structural inverse", null, null)
            : new(name, "(feature)", "missing", RoundTripClass.Unexplained, "forward emitted no feature", null, null);
    }

    private void CompareFeature(
        S57FeatureRecord source,
        S57FeatureRecord result,
        S101FeatureRecord? s101,
        S101Document intermediate,
        GeometryIndex sourceGeometry,
        GeometryIndex resultGeometry,
        bool byPosition,
        List<RoundTripDifference> differences)
    {
        var name = ClassName((int)source.ObjectCode);
        if (source.ObjectCode != result.ObjectCode)
        {
            var resultName = ClassName((int)result.ObjectCode);
            differences.Add(new(name, "(class)", "class",
                RestructuredClasses.Contains(name) ? RoundTripClass.ExpectedRestructure : RoundTripClass.Unexplained,
                RestructuredClasses.Contains(name) ? "class remodelled in S-101 (no unique inverse)" : "class changed", name, resultName));
            return; // attributes of different classes don't compare
        }

        if (source.Primitive != result.Primitive)
        {
            differences.Add(new(name, "(primitive)", "primitive", RoundTripClass.Unexplained, "primitive changed",
                source.Primitive.ToString(), result.Primitive.ToString()));
        }
        else if ((int)source.Primitive == 3)
        {
            CompareArea(name, source, result, sourceGeometry, resultGeometry, differences);
        }
        else if (sourceGeometry.Fingerprint(source) != resultGeometry.Fingerprint(result))
        {
            differences.Add(new(name, "(geometry)", "geometry", RoundTripClass.Unexplained, "positions differ", null, null));
        }

        var sourcePointers = source.FeaturePointers.Count;
        var resultPointers = result.FeaturePointers.Count;
        if (sourcePointers != resultPointers)
        {
            var (cls, reason) = resultPointers > sourcePointers
                ? (RoundTripClass.ExpectedRestructure, "Annex C §18.2 (master/slave)")
                : UnboundSlaves(source) is { Count: > 0 } unbound && sourcePointers - resultPointers <= unbound.Count
                    ? (RoundTripClass.ExpectedLoss, $"S-101 binds no StructureEquipment {string.Join(", ", unbound.Distinct())}")
                    : (RoundTripClass.Unexplained, "FFPT relationships not carried to S-101");
            differences.Add(new(name, "(pointers)", "pointers", cls, reason,
                sourcePointers.ToString(CultureInfo.InvariantCulture), resultPointers.ToString(CultureInfo.InvariantCulture)));
        }

        var sourceAttributes = Attributes(source);
        var resultAttributes = Attributes(result);
        foreach (var attl in sourceAttributes.Keys.Union(resultAttributes.Keys).Order())
        {
            var acronym = AttributeName(attl);
            var hasSource = sourceAttributes.TryGetValue(attl, out var sourceValue);
            var hasResult = resultAttributes.TryGetValue(attl, out var resultValue);
            if (hasSource && hasResult && Equivalent(acronym, sourceValue!, resultValue!, out var orderOnly))
            {
                if (orderOnly)
                    differences.Add(new(name, acronym, "value", RoundTripClass.ExpectedRestructure, "list order", sourceValue, resultValue));
                continue;
            }

            differences.Add(Explain(name, acronym, source, s101, intermediate, byPosition, resultAttributes,
                hasSource ? sourceValue : null, hasResult ? resultValue : null));
        }
    }

    // Compares two areas as polygons: a different outline is a geometry
    // difference; the same outline with other edges is a topology one (edges
    // split where new nodes were made).
    private static void CompareArea(
        string name, S57FeatureRecord source, S57FeatureRecord result,
        GeometryIndex sourceGeometry, GeometryIndex resultGeometry, List<RoundTripDifference> differences)
    {
        var a = sourceGeometry.Area(source);
        var b = resultGeometry.Area(result);
        if (a is null || b is null)
        {
            if (a is not null || b is not null)
                differences.Add(new(name, "(geometry)", "geometry", RoundTripClass.ReverseBug, "area has no closed boundary", null, null));
            return;
        }

        double changed;
        try
        {
            changed = a.SymmetricDifference(b).Area;
        }
        catch (NetTopologySuite.Geometries.TopologyException)
        {
            changed = Math.Abs(a.Area - b.Area);
        }
        if (changed > Math.Max(1, a.Area * 1e-6))
        {
            var (cls, reason) = name switch
            {
                "DEPARE" or "DRGARE" or "LNDARE" or "UNSARE" => (RoundTripClass.ExpectedRestructure, "Annex C §2.5.1.1 (skin of the earth cut)"),
                "M_COVR" or "M_CSCL" => (RoundTripClass.ExpectedRestructure, "Annex C §3.5 (coverage re-tiled)"),
                _ => (RoundTripClass.Unexplained, "outline differs"),
            };
            differences.Add(new(name, "(geometry)", "geometry", cls, reason,
                a.Area.ToString("0", CultureInfo.InvariantCulture), b.Area.ToString("0", CultureInfo.InvariantCulture)));
        }
        else if (sourceGeometry.Fingerprint(source) != resultGeometry.Fingerprint(result))
        {
            differences.Add(new(name, "(edges)", "topology", RoundTripClass.ExpectedRestructure, "same outline, edges split at new nodes", null, null));
        }
    }

    private RoundTripDifference Explain(
        string name, string acronym, S57FeatureRecord source, S101FeatureRecord? s101, S101Document intermediate,
        bool byPosition, Dictionary<int, string> resultAttributes, string? sourceValue, string? resultValue)
    {
        RoundTripDifference D(RoundTripClass cls, string reason)
            => new(name, acronym, sourceValue is null ? "added" : resultValue is null ? "missing" : "value", cls, reason, sourceValue, resultValue);

        // An empty S-57 value is "unknown"; an attribute present but empty and
        // one absent are treated alike where the forward translation drops it.
        if (string.IsNullOrEmpty(sourceValue) && resultValue is null)
            return D(RoundTripClass.ExpectedLoss, "empty (unknown) value not carried");

        // A light merged into a co-located sector light keeps only its sector;
        // its other attributes are the surviving light's.
        if (byPosition && name == "LIGHTS")
            return D(RoundTripClass.ExpectedRestructure, "co-located sector lights merged (forward) and split (Annex C §19.3)");

        // A list of empty values ("," on NATQUA) is unknown too.
        if (sourceValue is not null && sourceValue.Split(',').All(v => v.Trim().Length == 0) && resultValue is null)
            return D(RoundTripClass.ExpectedLoss, "empty (unknown) value not carried");

        if (sourceValue is null)
        {
            if (resultValue == string.Empty)
                return D(RoundTripClass.ExpectedRestructure, "Annex C §2.4.3 (mandatory attribute nulled)");
            if (acronym == "CATCOV")
                return D(RoundTripClass.ExpectedRestructure, "Annex C §3.5 (CATCOV 1 on converted coverage)");
            // §29.42: the forward translation derives the uncertainties of a
            // zone of confidence from its CATZOC (Annex B), which convert back.
            if (name == "M_QUAL" && acronym is "POSACC" or "SOUACC")
                return D(RoundTripClass.ExpectedRestructure, "Annex C §29.42 (uncertainty the forward derives from CATZOC)");
            // A point BRIDGE or DAMCON is a Landmark in S-101, which the
            // forward translation gives visualProminence 2 when CONVIS is
            // absent (Annex B §4.8.5); §7.2 converts it back.
            if (name is "BRIDGE" or "DAMCON" && acronym == "CONVIS" && resultValue == "2")
                return D(RoundTripClass.ExpectedRestructure, "Annex C §7.2 (Landmark default visual prominence)");
            if (acronym == "INFORM" && resultValue!.EndsWith(" features", StringComparison.Ordinal))
                return D(RoundTripClass.ExpectedRestructure, "Annex C §2.1.1 / §27.131 (MLTYLT → INFORM)");
            return D(RoundTripClass.Unexplained, "value appeared");
        }

        // The clearances of a BRIDGE travel on its SpanFixed / SpanOpening.
        if (name == "BRIDGE" && BridgeSpanAttributes.Contains(acronym) && resultValue is null)
            return D(RoundTripClass.ExpectedRestructure, "carried on the bridge span");

        // §27.140: openingBridge adds CATBRG 1 or 2 to the categories.
        if (acronym == "CATBRG" && resultValue is not null
            && Tokens(resultValue).Except(Tokens(sourceValue)).All(t => t is "1" or "2")
            && Tokens(sourceValue).All(Tokens(resultValue).Contains))
            return D(RoundTripClass.ExpectedRestructure, "Annex C §27.140 (openingBridge → CATBRG 1/2)");

        // National text, names and files that come back merged into INFORM /
        // TXTDSC / OBJNAM were read as English (Annex C §27.177 / §2.4.12.1 /
        // §27.132): the forward translation must give them a language other
        // than English and empty (#1221).
        if (resultValue is null && NationalTwin.TryGetValue(acronym, out var twin)
            && resultAttributes.TryGetValue(twin, out var twinValue) && twinValue.Contains(sourceValue, StringComparison.Ordinal))
            return D(RoundTripClass.Unexplained, "national text read as English");

        // A value the S-101 enumeration doesn't list is dropped by the
        // forward translation (S101AllowedEnumValues).
        if (DroppedEnumTokens(source, acronym, sourceValue, resultValue) is { Count: > 0 } dropped)
            return D(RoundTripClass.ExpectedLoss, $"not in the S-101 enumeration ({string.Join(',', dropped)})");

        // An attribute the forward translation assembles into a complex (or
        // one of a few top-level homes) has nowhere to go on a class that
        // binds none of them.
        // (A class the translator picks in code, such as a NEWOBJ's
        // VirtualAISAidToNavigation, is read from the S-101 intermediate.)
        if (resultValue is null && Homes.TryGetValue(acronym, out var homes)
            && (ResolveForward(source)?.S101Code ?? (s101 is null ? null : intermediate.FeatureTypeCatalogue.GetValueOrDefault(s101.FeatureTypeCode))) is { } home
            && !homes.Any(h => _bindings.Binds(home, h)))
            return D(RoundTripClass.ExpectedLoss, $"{home} binds no {string.Join('/', homes)} in S-101");

        // A TOPMAR folds into its master's topmark complex, which has no date.
        if (resultValue is null && name == "TOPMAR" && acronym == "SORDAT")
            return D(RoundTripClass.ExpectedLoss, "the S-101 topmark complex has no reportedDate");

        // A range-system C_AGGR is a RangeSystem in S-101, which binds no
        // home for some of the collection's attributes.
        if (resultValue is null && name == "C_AGGR" && Homes.TryGetValue(acronym, out var collectionHomes)
            && !collectionHomes.Any(h => _bindings.Binds("RangeSystem", h)))
            return D(RoundTripClass.ExpectedLoss, $"RangeSystem binds no {string.Join('/', collectionHomes)} in S-101");

        // S-101 makes dateEnd mandatory in surveyDateRange and both dates
        // in periodicDateRange, so a lone start date has no home.
        if (resultValue is null && acronym is "SURSTA" or "PERSTA" or "PEREND"
            && !Attributes(source).ContainsKey(acronym == "SURSTA" ? 151 : acronym == "PERSTA" ? 118 : 119))
            return D(RoundTripClass.ExpectedLoss, "the S-101 date range needs both dates (S-101 FC)");

        // Annex B §2.2.6: an M_CSCL only cuts Data Coverage, so only its
        // CSCALE comes back (Annex C §3.5).
        if (resultValue is null && name == "M_CSCL")
            return D(RoundTripClass.ExpectedLoss, "Annex B §2.2.6 (M_CSCL is not a feature; only CSCALE converts)");

        // Annex B §13.1.2: PILDST / NPLDST on a PILBOP aren't converted (a
        // pilotage district is a PilotageDistrict feature in S-101).
        if (resultValue is null && name == "PILBOP" && acronym is "PILDST" or "NPLDST")
            return D(RoundTripClass.ExpectedLoss, "Annex B §13.1.2 (not converted)");

        // §6.4: CATRUN only tells a runway (Runway) from a helipad (Helipad).
        if (name == "RUNWAY" && acronym == "CATRUN" && resultValue is null && sourceValue.Trim() == "1")
            return D(RoundTripClass.ExpectedLoss, "class discriminator (Annex C §6.4)");

        if (acronym == "MLTYLT" && resultValue is null)
            return D(RoundTripClass.ExpectedRestructure, "Annex C §2.1.1 / §27.131 (multiplicityOfFeatures → INFORM)");
        if (acronym == "INFORM" && resultValue is not null
            && resultValue.Split(';').Where(p => p != sourceValue).All(p => p.EndsWith(" features", StringComparison.Ordinal))
            && resultValue.Split(';').Contains(sourceValue))
            return D(RoundTripClass.ExpectedRestructure, "Annex C §2.1.1 / §27.131 (MLTYLT → INFORM)");

        // §27.146 / §27.90: S-101 keeps only true or false, so CONRAD 3
        // comes back as 1 and CATDIS 2 (pole) and 3 (board) as 4.
        if ((acronym, sourceValue.Trim(), resultValue) is ("CONRAD", "3", "1") or ("CATDIS", "2" or "3", "4"))
            return D(RoundTripClass.ExpectedLoss, $"Annex C §{(acronym == "CONRAD" ? "27.146" : "27.90")} (S-101 Boolean)");

        if (resultValue is not null && !S101ToS57Rules.IsS57ListAttribute(acronym)
            && sourceValue.Contains(',') && sourceValue.Split(',')[0].Trim() == resultValue.Trim())
            return D(RoundTripClass.ExpectedLoss, "Annex C §2.4.1 (Enumerated keeps first value)");

        // Annex C says the S-101 attribute isn't converted on this class.
        if (resultValue is null && NotConvertedOnClass(source, s101, intermediate, acronym) is { } notConverted)
            return D(RoundTripClass.ExpectedLoss, $"Annex C §{notConverted} (not converted)");

        // Did the S-101 intermediate carry the value?
        var carried = s101 is not null && Carries(intermediate, s101, acronym, sourceValue);
        if (carried && resultValue is null)
            return D(RoundTripClass.ReverseBug, "S-101 carried the value");

        if (resultValue is null && ForwardDropsByRule(source, acronym))
            return D(RoundTripClass.ExpectedLoss, "forward rule drops it on this class (Annex B)");

        return D(RoundTripClass.Unexplained, resultValue is null ? "not carried to S-101" : "value changed");
    }

    // The clause under which Annex C leaves the S-101 attribute carrying an
    // S-57 attribute unconverted on the source object's S-101 class.
    private string? NotConvertedOnClass(S57FeatureRecord source, S101FeatureRecord? s101, S101Document intermediate, string acronym)
    {
        var s101Class = s101 is not null
            ? intermediate.FeatureTypeCatalogue.GetValueOrDefault(s101.FeatureTypeCode)
            : ResolveForward(source)?.S101Code;
        if (s101Class is null) return null;
        var codes = new List<string>();
        if (AssembledCarriers.TryGetValue(acronym, out var carrier)) codes.Add(carrier);
        if (Homes.TryGetValue(acronym, out var homes)) codes.AddRange(homes);
        if (_mapping.TryGetAttl(acronym, out var attl) && _mapping.AttributeRules.TryGetValue(attl, out var rule) && rule.DefaultS101Code is { } code)
            codes.Add(code);
        if (acronym == "SCAMIN") codes.Add("scaleMinimum");
        foreach (var c in codes)
        {
            if (S101ToS57Rules.NotConvertedClause(s101Class, c) is { } clause) return clause;
        }
        return null;
    }

    // Where the forward translation puts each S-57 attribute it assembles.
    private static readonly Dictionary<string, string[]> Homes = new(StringComparer.Ordinal)
    {
        ["SORDAT"] = ["reportedDate"],
        ["SURSTA"] = ["surveyDateRange"],
        ["SUREND"] = ["surveyDateRange"],
        ["DATSTA"] = ["fixedDateRange"],
        ["DATEND"] = ["fixedDateRange"],
        ["PERSTA"] = ["periodicDateRange"],
        ["PEREND"] = ["periodicDateRange"],
        ["VERCLR"] = ["verticalClearanceFixed"],
        ["VERCCL"] = ["verticalClearanceClosed"],
        ["VERCOP"] = ["verticalClearanceOpen"],
        ["VERCSA"] = ["verticalClearanceSafe"],
        ["HORCLR"] = ["horizontalClearanceFixed", "horizontalClearanceOpen"],
        ["NATSUR"] = ["surfaceCharacteristics", "natureOfSurface"],
        ["NATQUA"] = ["surfaceCharacteristics"],
        ["COLOUR"] = ["colour", "sectorCharacteristics"],
        ["OBJNAM"] = ["featureName"],
        ["NOBJNM"] = ["featureName"],
        ["LITCHR"] = ["rhythmOfLight", "sectorCharacteristics"],
        ["SIGGRP"] = ["rhythmOfLight", "sectorCharacteristics", "signalGroup"],
        ["SIGPER"] = ["rhythmOfLight", "sectorCharacteristics", "signalPeriod"],
        ["CATZOC"] = ["zoneOfConfidence"],
        ["VALLMA"] = ["valueOfLocalMagneticAnomaly"],
        ["RADWAL"] = ["radarWaveLength"],
        ["CURVEL"] = ["speed"],
        ["MLTYLT"] = ["multiplicityOfFeatures"],
        ["SIGSEQ"] = ["rhythmOfLight", "sectorCharacteristics", "signalSequence"],
        ["SOUACC"] = ["verticalUncertainty"],
        ["SCAMIN"] = ["scaleMinimum"],
    };

    private static readonly HashSet<string> BridgeSpanAttributes = new(StringComparer.Ordinal)
    {
        "VERCLR", "VERCCL", "VERCOP", "HORCLR", "HORACC", "VERACC", "VERDAT",
    };

    private static readonly Dictionary<string, int> NationalTwin = new(StringComparer.Ordinal)
    {
        ["NINFOM"] = 102,
        ["NTXTDS"] = 158,
        ["NOBJNM"] = 116,
    };

    private static List<string> Tokens(string? value)
        => value is null ? [] : value.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

    // The slaves of a master whose S-101 classes the Feature Catalogue doesn't
    // let it carry as StructureEquipment, as "structure → equipment".
    private List<string> UnboundSlaves(S57FeatureRecord master)
    {
        var unbound = new List<string>();
        if (ResolveForward(master) is not { } structure) return unbound;
        foreach (var pointer in master.FeaturePointers)
        {
            if (pointer.Relationship != S57RelationshipIndicator.Slave
                || !_sourceByLnam.TryGetValue(pointer.Name, out var slave)
                || ResolveForward(slave) is not { } equipment)
                continue;
            if (!_bindings.BindsFeatureAssociation(structure.S101Code, "StructureEquipment", "theEquipment", equipment.S101Code))
                unbound.Add($"{structure.S101Code} → {equipment.S101Code}");
        }
        return unbound;
    }

    private Dictionary<S57LongName, S57FeatureRecord> _sourceByLnam = [];

    private ResolvedFeature? ResolveForward(S57FeatureRecord feature)
    {
        var objl = (ushort)(int)feature.ObjectCode;
        var view = _mapping.BuildAcronymView(feature.Attributes);
        var primitive = (int)feature.Primitive switch { 1 => S57GeometryPrimitive.Point, 2 => S57GeometryPrimitive.Curve, 3 => S57GeometryPrimitive.Surface, _ => S57GeometryPrimitive.None };
        return _mapping.ResolveFeature(objl, view, primitive);
    }

    // The tokens of the source value the S-101 enumeration rejects, when they
    // account for the whole difference.
    private List<string>? DroppedEnumTokens(S57FeatureRecord feature, string acronym, string sourceValue, string? resultValue)
    {
        if (!_mapping.TryGetAttl(acronym, out var attl) || ResolveForward(feature) is not { } resolved) return null;
        var missing = Tokens(sourceValue).Except(Tokens(resultValue)).ToList();
        if (missing.Count == 0) return null;
        var rejected = new List<string>();
        foreach (var token in missing)
        {
            if (_mapping.ResolveAttribute(attl, token, resolved) is not { } attribute) return null;
            if (!_allowed.IsEnumerated(attribute.S101Code) || _allowed.IsAllowed(attribute.S101Code, attribute.Value)) return null;
            rejected.Add(token);
        }
        return rejected;
    }

    // Whether the forward mapping has a rule for the attribute that resolves to
    // nothing on the source object's class (a by-design drop).
    private bool ForwardDropsByRule(S57FeatureRecord feature, string acronym)
    {
        if (acronym is "SORIND") return true; // S-101 has no general source indication (Annex B)
        if (!_mapping.TryGetAttl(acronym, out var attl)) return false;
        var objl = (ushort)(int)feature.ObjectCode;
        var view = _mapping.BuildAcronymView(feature.Attributes);
        var primitive = (int)feature.Primitive switch { 1 => S57GeometryPrimitive.Point, 2 => S57GeometryPrimitive.Curve, 3 => S57GeometryPrimitive.Surface, _ => S57GeometryPrimitive.None };
        if (_mapping.ResolveFeature(objl, view, primitive) is not { } resolved) return true;
        var value = view.GetValueOrDefault(acronym) ?? string.Empty;
        if (_mapping.ResolveAttribute(attl, value.Split(',')[0], resolved) is not { } attribute) return true;
        return !_bindings.Binds(resolved.S101Code, attribute.S101Code)
            && !AssembledCarriers.ContainsKey(acronym);
    }

    // Whether the S-101 feature (or its NauticalInformation) has the
    // attribute the S-57 value travels in, with a non-empty value.
    private bool Carries(S101Document document, S101FeatureRecord feature, string acronym, string value)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        if (AssembledCarriers.TryGetValue(acronym, out var carrier)) codes.Add(carrier);
        if (_mapping.TryGetAttl(acronym, out var attl) && _mapping.AttributeRules.TryGetValue(attl, out var rule) && rule.DefaultS101Code is { } code)
            codes.Add(code);
        if (codes.Count == 0) return false;

        var tokens = Tokens(value);
        bool Has(IEnumerable<S101Attribute> rows) => rows.Any(r =>
            document.AttributeTypeCatalogue.TryGetValue(r.NumericCode, out var name) && codes.Contains(name)
            && !string.IsNullOrEmpty(r.Value) && (r.Value == value || tokens.Contains(r.Value.Trim())));

        if (Has(feature.Attributes)) return true;
        foreach (var association in feature.InformationAssociations)
        {
            if (document.InformationTypes.TryGetValue(association.RecordId, out var info) && Has(info.Attributes))
                return true;
        }
        return false;
    }

    private static bool Equivalent(string acronym, string a, string b, out bool orderOnly)
    {
        orderOnly = false;
        a = a.Trim();
        b = b.Trim();
        if (a == b) return true;
        if (decimal.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var da)
            && decimal.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
            return da == db;
        if (acronym == "SIGSEQ")
            return SignalSequence(a).SequenceEqual(SignalSequence(b));
        if (S101ToS57Rules.IsS57ListAttribute(acronym))
        {
            var la = a.TrimEnd(',', ' ').Split(',').Select(t => t.Trim()).ToList();
            var lb = b.TrimEnd(',', ' ').Split(',').Select(t => t.Trim()).ToList();
            if (la.SequenceEqual(lb)) return true;
            if (!OrderedLists.Contains(acronym) && la.Order().SequenceEqual(lb.Order()))
            {
                orderOnly = true;
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<string> SignalSequence(string value)
        => value.Split('+').Select(t =>
        {
            t = t.Trim();
            var bracket = t.StartsWith('(');
            var inner = t.Trim('(', ')');
            var normal = decimal.TryParse(inner, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d.ToString("0.###", CultureInfo.InvariantCulture)
                : inner;
            return bracket ? $"({normal})" : normal;
        });

    private static Dictionary<int, string> Attributes(S57FeatureRecord feature)
    {
        var map = new Dictionary<int, string>();
        foreach (var a in feature.Attributes.Concat(feature.NationalAttributes))
            map[a.AttributeCode] = a.Value ?? string.Empty;
        return map;
    }

    private static (int, int, int) Foid(S57FeatureRecord feature)
        => (feature.RecordName.AgencyCode, feature.RecordName.FeatureId, feature.RecordName.FeatureSubdivision);

    // Resolves a feature's spatial pointers to the set of positions it uses.
    private sealed class GeometryIndex
    {
        private readonly Dictionary<(int Rcnm, int Rcid), S57VectorRecord> _vectors = new();

        public GeometryIndex(S57Document document)
        {
            foreach (var v in document.VectorRecords)
                _vectors[(v.RecordName.RecordNameCode, v.RecordName.RecordId)] = v;
        }

        private S57EdgeGraph? _graph;

        // The polygon an area object's edges bound.
        public NetTopologySuite.Geometries.Geometry? Area(S57FeatureRecord feature)
        {
            if (_graph is null)
            {
                uint next = 1;
                _graph = new S57EdgeGraph(() => next++);
                foreach (var v in _vectors.Values.Where(v => v.RecordName.RecordNameCode == 120 && v.Coordinates2D.Count > 0))
                    _graph.AddNode((uint)v.RecordName.RecordId, v.Coordinates2D[0].X, v.Coordinates2D[0].Y);
                foreach (var v in _vectors.Values.Where(v => v.RecordName.RecordNameCode == 130))
                {
                    if (!v.VectorPointers.Any(p => (int)p.Topology == 1) || !v.VectorPointers.Any(p => (int)p.Topology == 2)) continue;
                    var begin = v.VectorPointers.First(p => (int)p.Topology == 1);
                    var end = v.VectorPointers.First(p => (int)p.Topology == 2);
                    if (!_graph.Nodes.ContainsKey((uint)begin.Name.RecordId) || !_graph.Nodes.ContainsKey((uint)end.Name.RecordId)) continue;
                    _graph.Edges[(uint)v.RecordName.RecordId] = new S57EdgeGraph.Edge(
                        (uint)begin.Name.RecordId, (uint)end.Name.RecordId, v.Coordinates2D.Select(c => (c.X, c.Y)).ToArray());
                }
            }
            return _graph.Area(feature.SpatialPointers);
        }

        public string Fingerprint(S57FeatureRecord feature)
        {
            var positions = new SortedSet<(int, int)>();
            foreach (var pointer in feature.SpatialPointers)
                Collect((pointer.Name.RecordNameCode, pointer.Name.RecordId), positions, depth: 0);
            unchecked
            {
                long hash = positions.Count;
                foreach (var (x, y) in positions)
                    hash = hash * 31 + x * 17L + y;
                return $"{positions.Count}:{hash}";
            }
        }

        private void Collect((int, int) key, SortedSet<(int, int)> positions, int depth)
        {
            if (depth > 2 || !_vectors.TryGetValue(key, out var vector)) return;
            foreach (var c in vector.Coordinates2D) positions.Add((c.X, c.Y));
            foreach (var s in vector.Soundings) positions.Add((s.X, s.Y));
            foreach (var p in vector.VectorPointers)
                Collect((p.Name.RecordNameCode, p.Name.RecordId), positions, depth + 1);
        }
    }
}
