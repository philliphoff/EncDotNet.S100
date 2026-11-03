using System.Collections.Frozen;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>How <see cref="S101ToS57Translator"/> covers one S-65 Annex C clause.</summary>
/// <param name="Clause">The Annex C clause, e.g. <c>6.6</c>.</param>
/// <param name="Kind"><c>feature</c>, <c>information</c>, <c>attribute</c>, <c>complex</c>, <c>association</c> or <c>general</c>.</param>
/// <param name="S101Code">The S-101 feature, information type or attribute code.</param>
/// <param name="Treatment">
/// <c>rule</c> (converted by a rule), <c>general</c> (guidance with no
/// conversion of its own), <c>assembled</c> (converted inside the
/// complex attribute or structure that carries it), <c>structural</c> (by a
/// structural inverse), <c>dropped</c> (a deliberate drop Annex C calls for)
/// or <c>uncovered</c>.
/// </param>
/// <param name="Target">What it becomes, or why it is dropped.</param>
public sealed record S101ToS57ClauseCoverage(string Clause, string Kind, string S101Code, string Treatment, string Target);

/// <summary>
/// The coverage of S-65 Annex C §3–§30 by <see cref="S101ToS57Translator"/>
/// and <see cref="S101ToS57Rules.Default"/>: every feature type, information
/// type and attribute clause, with the rule that converts it or the reason it
/// is dropped.
/// </summary>
public static class S101ToS57Coverage
{
    // Classes the translator converts through other features (§6.6, §21.2,
    // §23.1, §3.9, §3.10, §3.12).
    private static readonly FrozenDictionary<string, string> StructuralClasses = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Bridge"] = "BRIDGE, one per span when it has spans, tied by C_AGGR (§6.6)",
        ["PhysicalAISAidToNavigation"] = "INFORM text on the structure it equips (§21.2)",
        ["SoundingDatum"] = "DSPM SDAT for the commonest datum, M_SDAT for the others (§3.9)",
        ["VerticalDatumOfData"] = "DSPM VDAT for the commonest datum, M_VDAT for the others (§3.10)",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> DroppedClasses = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TextPlacement"] = "cartographic features are prohibited in S-57 (§2.5.8.1, §23.1)",
        ["UpdateInformation"] = "not converted (§3.12)",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Complex attributes the translator assembles, with what they become.
    private static readonly FrozenDictionary<string, string> AssembledComplexes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["featureName"] = "OBJNAM / NOBJNM by name usage and language",
        ["information"] = "INFORM / NINFOM, TXTDSC / NTXTDS by language",
        ["multiplicityOfFeatures"] = "INFORM \"n features\" or \"Multiple features\"",
        ["rhythmOfLight"] = "LITCHR, SIGGRP, SIGPER, SIGSEQ",
        ["signalSequence"] = "SIGSEQ, phases joined by +, eclipses in brackets",
        ["fixedDateRange"] = "DATSTA / DATEND",
        ["periodicDateRange"] = "PERSTA / PEREND",
        ["surveyDateRange"] = "SURSTA / SUREND",
        ["zoneOfConfidence"] = "CATZOC, POSACC, SOUACC",
        ["surfaceCharacteristics"] = "positional NATSUR / NATQUA, / before an underlying layer",
        ["horizontalClearanceFixed"] = "HORCLR, HORACC",
        ["horizontalClearanceOpen"] = "HORCLR, HORACC",
        ["verticalClearanceFixed"] = "VERCLR, VERACC",
        ["verticalClearanceClosed"] = "VERCCL, VERACC",
        ["verticalClearanceOpen"] = "VERCOP, VERACC",
        ["verticalClearanceSafe"] = "VERCSA, VERACC",
        ["orientation"] = "ORIENT",
        ["speed"] = "CURVEL",
        ["radarWaveLength"] = "RADWAL \"wavelength-band\" pairs",
        ["valueOfLocalMagneticAnomaly"] = "VALLMA, degrees to minutes",
        ["sourceIndication"] = "SORIND",
        ["horizontalPositionUncertainty"] = "POSACC",
        ["verticalUncertainty"] = "SOUACC, or VERACC on a clearance",
        ["vesselSpeedLimit"] = "INFORM \"Speed limit is n units\", first instance",
        ["frequencyPair"] = "SIGFRQ",
        ["measuredDistanceValue"] = "INFORM \"n units\"",
        ["tidalStreamPanelValues"] = "TS_TSP",
        ["tidalStreamValue"] = "TS_TSP direction and rate per hour",
        ["topmark"] = "TOPMAR, slave of the structure",
        ["sectorCharacteristics"] = "LIGHTS, one per light sector",
        ["lightSector"] = "LIGHTS, one per light sector",
        ["sectorLimit"] = "SECTR1 / SECTR2",
        ["sectorLimitOne"] = "SECTR1",
        ["sectorLimitTwo"] = "SECTR2",
        ["directionalCharacter"] = "CATLIT 1 (and 16 for a moiré effect), ORIENT",
        ["sectorInformation"] = "INFORM / NINFOM on the sector's LIGHTS",
        ["shapeInformation"] = "INFORM / NINFOM on the TOPMAR",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> DroppedComplexes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["featuresDetected"] = "S-101 enhancement",
        ["onlineResource"] = "S-101 enhancement",
        ["scheduleByDayOfWeek"] = "S-101 enhancement",
        ["timeIntervalsByDayOfWeek"] = "S-101 enhancement",
        ["spatialAccuracy"] = "on spatial records, which the S-101 reader doesn't associate with Spatial Quality (§24.5)",
        ["telecommunications"] = "S-101 enhancement",
        ["contactAddress"] = "S-101 enhancement",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Simple attributes converted inside the complex attribute that carries
    // them (or, for a few, by the translator directly).
    private static readonly FrozenDictionary<string, string> AssembledAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["name"] = "OBJNAM / NOBJNM (featureName)",
        ["text"] = "INFORM / NINFOM (information)",
        ["fileReference"] = "TXTDSC / NTXTDS (information)",
        ["lightCharacteristic"] = "LITCHR (rhythmOfLight, sectorCharacteristics)",
        ["signalGroup"] = "SIGGRP",
        ["signalPeriod"] = "SIGPER",
        ["signalDuration"] = "SIGSEQ (signalSequence)",
        ["signalStatus"] = "SIGSEQ brackets (signalSequence)",
        ["dateStart"] = "DATSTA / PERSTA / SURSTA",
        ["dateEnd"] = "DATEND / PEREND / SUREND",
        ["natureOfSurface"] = "NATSUR (surfaceCharacteristics)",
        ["natureOfSurfaceQualifyingTerms"] = "NATQUA (surfaceCharacteristics)",
        ["underlyingLayer"] = "the / separator in NATSUR / NATQUA",
        ["horizontalClearanceValue"] = "HORCLR",
        ["horizontalDistanceUncertainty"] = "HORACC",
        ["verticalClearanceValue"] = "VERCLR / VERCCL / VERCOP / VERCSA",
        ["uncertaintyFixed"] = "POSACC / SOUACC / VERACC",
        ["orientationValue"] = "ORIENT",
        ["speedMaximum"] = "CURVEL (speed)",
        ["waveLengthValue"] = "RADWAL",
        ["radarBand"] = "RADWAL",
        ["magneticAnomalyValue"] = "VALLMA",
        ["source"] = "SORIND",
        ["colour"] = "COLOUR",
        ["lightVisibility"] = "LITVIS",
        ["valueOfNominalRange"] = "VALNMR",
        ["sectorBearing"] = "SECTR1 / SECTR2",
        ["moireEffect"] = "CATLIT 16",
        ["topmarkDaymarkShape"] = "TOPSHP",
        ["colourPattern"] = "COLPAT",
        ["speedLimit"] = "INFORM (vesselSpeedLimit)",
        ["speedUnits"] = "INFORM (vesselSpeedLimit)",
        ["waterwayDistance"] = "INFORM (measuredDistanceValue)",
        ["distanceUnitOfMeasurement"] = "INFORM (measuredDistanceValue)",
        ["referenceTide"] = "TS_TSP HW / LW",
        ["timeRelativeToTide"] = "TS_TSP hour",
        ["stationNumber"] = "TS_TSP",
        ["stationName"] = "TS_TSP",
        ["multiplicityKnown"] = "INFORM (multiplicityOfFeatures)",
        ["numberOfFeatures"] = "INFORM (multiplicityOfFeatures)",
        ["mMSICode"] = "OBJNAM \"name; MMSI n\"",
        ["virtualAISAidToNavigationType"] = "NEWOBJ CLSDEF, CLSNAM, INFORM, SYMINS",
        ["visitorsMooring"] = "SMCFAC CATSCF 29",
        ["flareStack"] = "a LNDMRK CATLMK 6",
        ["inDispute"] = "a CTNARE \"In dispute\"",
        ["optimumDisplayScale"] = "DSPM CSCL and M_CSCL CSCALE (§3.5)",
        ["frequencyShoreStationTransmits"] = "SIGFRQ",
        ["categoryOfZoneOfConfidenceInData"] = "CATZOC",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> DroppedAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["uncertaintyVariableFactor"] = "no depth or length to scale it by on the feature (§28.27)",
        ["frequencyShoreStationReceives"] = "S-101 enhancement (§24.1)",
        ["verticalClearanceUnlimited"] = "no S-57 encoding",
        ["sectorArcExtension"] = "portrayal only (§30)",
        ["headline"] = "S-101 enhancement (§29.9)",
        ["inTheWater"] = "portrayal only; a PILPNT where a structure stands in the water (§30)",
        ["maximumDisplayScale"] = "§3.5",
        ["minimumDisplayScale"] = "§3.5",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Feature clauses whose title doesn't match the Feature Catalogue name, so
    // the generated clause table misses them.
    private static readonly (string Clause, string Code)[] UnmatchedFeatureClauses =
    [
        ("6.2", "Building"), ("8.3", "Hulk"), ("8.4", "Pile"), ("14.2", "CableSubmarine"), ("14.3", "CableArea"),
        ("14.4", "PipelineSubmarineOnLand"), ("15.8", "FairwaySystem"), ("16.20", "HarbourAreaAdministrative"),
        ("19.5", "LightAirObstruction"), ("22.2", "VesselTrafficServiceArea"), ("22.4", "SignalStationWarning"),
        ("22.5", "SignalStationTraffic"),
    ];

    // Clauses with no S-101 class or attribute of their own: guidance,
    // overviews and associations.
    private static readonly S101ToS57ClauseCoverage[] OtherClauses =
    [
        new("3.1", "general", "horizontal uncertainty", "assembled", "POSACC from horizontalPositionUncertainty; QUAPOS on spatial records isn't read (§24.5)"),
        new("3.2", "general", "vertical uncertainty", "assembled", "SOUACC from verticalUncertainty"),
        new("3.3", "general", "hierarchy of metadata", "general", "no conversion"),
        new("5.1", "general", "cliffs", "general", "guidance; the features convert by their own clauses"),
        new("5.2", "general", "cuttings and embankments", "general", "guidance; the features convert by their own clauses"),
        new("7.1", "general", "landmark encoding combinations", "general", "guidance; the features convert by their own clauses"),
        new("8.1", "general", "works in progress", "general", "guidance; the features convert by their own clauses"),
        new("10.1", "general", "tidal data", "general", "guidance; the features convert by their own clauses"),
        new("11.1", "general", "generalisation of depth portrayal", "general", "no conversion"),
        new("11.2", "general", "representation of depth", "general", "no conversion"),
        new("11.9", "general", "areas with inadequate depth information", "general", "guidance; the features convert by their own clauses"),
        new("13.1", "general", "danger lines", "general", "guidance; the features convert by their own clauses"),
        new("13.3", "general", "doubtful dangers", "general", "guidance; the features convert by their own clauses"),
        new("15.2", "general", "traffic lanes", "general", "guidance; the features convert by their own clauses"),
        new("15.3", "general", "traffic separation schemes", "general", "guidance; the features convert by their own clauses"),
        new("16.1", "general", "boundaries and limits", "general", "guidance; the features convert by their own clauses"),
        new("16.2", "general", "maritime jurisdiction areas", "structural", "a CTNARE \"In dispute\" for a disputed area; curves widened to areas"),
        new("17.1", "general", "minefields", "general", "guidance; RESARE by §17.8"),
        new("17.2", "general", "degaussing ranges", "general", "guidance; RESARE by §17.8"),
        new("17.3", "general", "nature reserves", "general", "guidance; RESARE by §17.8"),
        new("17.4", "general", "speed limits", "assembled", "INFORM \"Speed limit is n units\" (vesselSpeedLimit)"),
        new("17.5", "general", "anchoring restricted", "general", "guidance; RESARE by §17.8"),
        new("17.6", "general", "areas to be avoided", "general", "guidance; RESARE by §17.8"),
        new("17.7", "general", "environmentally sensitive sea areas", "general", "guidance; RESARE by §17.8"),
        new("18.1", "general", "parts of navigational aids", "assembled", "topmark → TOPMAR, light sectors → LIGHTS"),
        new("18.2", "general", "relationships", "structural", "Structure/Equipment → master and slaves (FFPT)"),
        new("18.3", "general", "buoyage systems", "general", "M_NSYS by §3.6 / §3.7"),
        new("19.1", "general", "lights", "general", "guidance; the light classes convert by their own clauses"),
        new("21.1", "general", "AIS aids to navigation", "general", "guidance; §21.2 and §21.3"),
        new("25.1", "association", "AdditionalInformation", "assembled", "information types fold into the feature's attributes (§24)"),
        new("25.2", "association", "AidsToNavigationAssociation", "structural", "C_ASSO of the feature and its aids"),
        new("25.3", "association", "ASLAggregation", "structural", "C_AGGR"),
        new("25.4", "association", "BridgeAggregation", "structural", "C_AGGR of the bridge's BRIDGE, PYLONS and PONTON objects"),
        new("25.5", "association", "CautionAreaAssociation", "dropped", "not converted"),
        new("25.6", "association", "DeepWaterRouteAggregation", "structural", "C_AGGR"),
        new("25.7", "association", "FairwayAggregation", "structural", "C_AGGR"),
        new("25.8", "association", "FairwayAuxiliary", "structural", "C_ASSO of the fairway and its auxiliary features"),
        new("25.9", "association", "IslandAggregation", "structural", "C_AGGR"),
        new("25.10", "association", "MooringTrotAggregation", "structural", "C_AGGR"),
        new("25.11", "association", "PilotageDistrictAssociation", "structural", "C_AGGR of the ADMARE and its PILBOP, which take PILDST / NPLDST"),
        new("25.12", "association", "QualityOfBathymetricDataComposition", "dropped", "not converted (§24.5)"),
        new("25.13", "association", "RangeSystemAggregation", "structural", "C_AGGR"),
        new("25.14", "association", "RoofedStructureAggregation", "structural", "C_AGGR of the PYLONS and BUISGL"),
        new("25.15", "association", "SpatialAssociation", "dropped", "not converted (§24.5)"),
        new("25.16", "association", "StructureEquipment", "structural", "master and slaves (FFPT)"),
        new("25.17", "association", "TextAssociation", "dropped", "not converted (§23.1)"),
        new("25.18", "association", "TrafficSeparationSchemeAggregation", "structural", "C_AGGR"),
        new("25.19", "association", "TwoWayRouteAggregation", "structural", "C_AGGR"),
        new("25.20", "association", "UpdateAggregation", "dropped", "not converted (§3.12)"),
        new("25.21", "association", "UpdatedInformation", "dropped", "not converted (§3.12)"),
        new("27.130", "attribute", "moireEffect", "assembled", "CATLIT 16"),
        new("30.1", "attribute", "defaultClearanceDepth", "dropped", "portrayal only"),
        new("30.2", "attribute", "inTheWater", "structural", "a PILPNT where a structure stands in the water"),
        new("30.3", "attribute", "sectorArcExtension", "dropped", "portrayal only"),
        new("30.4", "attribute", "surroundingDepth", "dropped", "portrayal only"),
    ];

    /// <summary>The coverage of every Annex C clause in the bundled clause table.</summary>
    public static IReadOnlyList<S101ToS57ClauseCoverage> Clauses { get; } = Build(S101ToS57Rules.Default);

    private static List<S101ToS57ClauseCoverage> Build(S101ToS57Rules rules)
    {
        var rows = new List<S101ToS57ClauseCoverage>();
        foreach (var (code, clause) in S101ToS57AnnexCClauses.FeatureTypes.Concat(UnmatchedFeatureClauses.Select(c => KeyValuePair.Create(c.Code, c.Clause))))
        {
            if (StructuralClasses.TryGetValue(code, out var structural))
                rows.Add(new(clause, "feature", code, "structural", structural));
            else if (DroppedClasses.TryGetValue(code, out var dropped))
                rows.Add(new(clause, "feature", code, "dropped", dropped));
            else if (rules.ResolveFeature(code) is { } rule)
                rows.Add(new(clause, "feature", code, "rule", Describe(rule)));
            else
                rows.Add(new(clause, "feature", code, "uncovered", string.Empty));
        }

        foreach (var (code, clause) in S101ToS57AnnexCClauses.InformationTypes)
        {
            rows.Add(new(clause, "information", code, code is "ServiceHours" or "NonStandardWorkingDay" or "SpatialQuality" ? "dropped" : "assembled", code switch
            {
                "NauticalInformation" => "INFORM / NINFOM, TXTDSC / NTXTDS, PICREP on the feature",
                "ContactDetails" => "CALSGN, COMCHA, SIGFRQ and INFORM on the feature",
                "SpatialQuality" => "on spatial records, which the S-101 reader doesn't associate (§24.5)",
                _ => "no S-57 encoding",
            }));
        }

        foreach (var (code, clause) in S101ToS57AnnexCClauses.SimpleAttributes)
        {
            if (rules.AttributeRules.TryGetValue(code, out var rule))
                rows.Add(new(clause, "attribute", code, "rule", rule.InformFormat is not null ? "INFORM text" : rule.Attl == 0 ? "INFORM text for some values" : rule.S57Acronym));
            else if (AssembledAttributes.TryGetValue(code, out var assembled))
                rows.Add(new(clause, "attribute", code, "assembled", assembled));
            else if (S101ToS57Rules.NotConvertedClause(string.Empty, code) is { } notConverted)
                rows.Add(new(clause, "attribute", code, "dropped", $"S-101 enhancement (§{notConverted})"));
            else if (DroppedAttributes.TryGetValue(code, out var dropped))
                rows.Add(new(clause, "attribute", code, "dropped", dropped));
            else if (rules.FeatureAttributeRules.Keys.FirstOrDefault(k => k.Attribute == code) is { Feature: not null } key)
                rows.Add(new(clause, "attribute", code, "rule", $"{rules.FeatureAttributeRules[key].S57Acronym} on {key.Feature}"));
            else
                rows.Add(new(clause, "attribute", code, "uncovered", string.Empty));
        }

        foreach (var (code, clause) in S101ToS57AnnexCClauses.ComplexAttributes)
        {
            if (AssembledComplexes.TryGetValue(code, out var assembled))
                rows.Add(new(clause, "complex", code, "assembled", assembled));
            else if (DroppedComplexes.TryGetValue(code, out var dropped))
                rows.Add(new(clause, "complex", code, "dropped", dropped));
            else
                rows.Add(new(clause, "complex", code, "uncovered", string.Empty));
        }

        rows.AddRange(OtherClauses);
        return rows.OrderBy(r => Order(r.Clause)).ThenBy(r => r.S101Code, StringComparer.Ordinal).ToList();
    }

    private static string Describe(S101ToS57FeatureRule rule)
    {
        var text = rule.S57Acronym;
        var fixedValues = rule.FixedAttributes.Where(a => a.Key != "INFORM" && a.Value.Length > 0).Select(a => $"{a.Key} {a.Value}").ToList();
        if (fixedValues.Count > 0) text += " " + string.Join(", ", fixedValues);
        if (rule.FixedAttributes.TryGetValue("INFORM", out var inform) && rule.Redirects.Count == 0) text += $" \"{inform}\"";
        foreach (var group in rule.Redirects.Where(r => r.Target.S57Acronym != rule.S57Acronym).GroupBy(r => r.Target.S57Acronym))
            text += $"; {group.Key} when {string.Join(" or ", group.Select(Condition))}";
        if (rule.Redirects.Where(r => r.Target.S57Acronym == rule.S57Acronym).Select(r => r.Attribute).Distinct().ToList() is [{ } by])
            text += $", its attributes by {by}";
        foreach (var companion in rule.Companions.DistinctBy(c => c.Target.S57Acronym))
            text += $"; plus {companion.Target.S57Acronym} when {Condition(companion)}";
        text += rule.PrimitiveChange switch
        {
            S101ToS57PrimitiveChange.CurveToArea => "; a curve becomes a 0.3 mm area",
            S101ToS57PrimitiveChange.SurfaceToPoint => "; a surface becomes a point",
            S101ToS57PrimitiveChange.DropCurve => "; a curve is dropped",
            _ => string.Empty,
        };
        return text;
    }

    private static string Condition(S101ToS57FeatureRedirect redirect)
    {
        var parts = new List<string>();
        if (redirect.Attribute is { } attribute)
            parts.Add(redirect.Values.Count == 0 ? attribute : $"{attribute} {string.Join("/", redirect.Values)}");
        if (redirect.Primitives.Count > 0)
            parts.Add(string.Join("/", redirect.Primitives.Select(p => p.ToString().ToLowerInvariant())));
        return string.Join(", ", parts);
    }

    private static (int, int) Order(string clause)
    {
        var parts = clause.Split('.');
        return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0);
    }
}
