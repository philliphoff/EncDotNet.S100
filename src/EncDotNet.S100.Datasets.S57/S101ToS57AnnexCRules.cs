using System.Collections.Frozen;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

// The Annex C layer of S101ToS57Rules that the forward mapping can't supply:
// the feature classes S-57 → S-101 never produces, the class changes Annex C
// makes by attribute value or primitive (Table 2-1), the objects it adds, and
// the enumeration differences of Table A-2 and §27.
public sealed partial class S101ToS57Rules
{
    private const ushort ObjlAdmare = 1;
    private const ushort ObjlBcnspp = 9;
    private const ushort ObjlBridge = 11;
    private const ushort ObjlBuisgl = 12;
    private const ushort ObjlBoyspp = 19;
    private const ushort ObjlCtnare = 27;
    private const ushort ObjlCtrpnt = 33;
    private const ushort ObjlDamcon = 38;
    private const ushort ObjlHrbfac = 64;
    private const ushort ObjlLndmrk = 74;
    private const ushort ObjlLights = 75;
    private const ushort ObjlLndrgn = 73;
    private const ushort ObjlMorfac = 84;
    private const ushort ObjlObstrn = 86;
    private const ushort ObjlPilpnt = 90;
    private const ushort ObjlRivers = 114;
    private const ushort ObjlRunway = 117;
    private const ushort ObjlSeaare = 119;
    private const ushort ObjlSmcfac = 128;
    private const ushort ObjlSoundg = 129;
    private const ushort ObjlTselne = 145;
    private const ushort ObjlVegatn = 155;
    private const ushort ObjlWedklp = 158;
    private const ushort ObjlArcsln = 161;
    private const ushort ObjlAslxis = 162;
    private const ushort ObjlNewobj = 163;
    internal const ushort ObjlCAggr = 400;

    /// <summary>
    /// S-101 classes that Annex C converts to an S-57 collection (<c>C_AGGR</c>)
    /// of the features they aggregate, keyed by class with the S-101 feature
    /// association whose components become the members, and the S-57 object a
    /// surface instance also yields (e.g. a <c>TrafficSeparationScheme</c>
    /// surface → <c>SEAARE</c>, §15.23).
    /// </summary>
    internal static readonly FrozenDictionary<string, (string Association, string? SurfaceAcronym, ushort SurfaceObjl)> Aggregations =
        new Dictionary<string, (string, string?, ushort)>(StringComparer.Ordinal)
        {
            ["RangeSystem"] = ("RangeSystemAggregation", null, 0),
            ["FairwaySystem"] = ("FairwayAggregation", "SEAARE", ObjlSeaare),
            ["TwoWayRoute"] = ("TwoWayRouteAggregation", "SEAARE", ObjlSeaare),
            ["DeepWaterRoute"] = ("DeepWaterRouteAggregation", "SEAARE", ObjlSeaare),
            ["TrafficSeparationScheme"] = ("TrafficSeparationSchemeAggregation", "SEAARE", ObjlSeaare),
            ["ArchipelagicSeaLane"] = ("ASLAggregation", "SEAARE", ObjlSeaare),
            ["MooringTrot"] = ("MooringTrotAggregation", "SEAARE", ObjlSeaare),
            ["IslandGroup"] = ("IslandAggregation", "LNDRGN", ObjlLndrgn),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static void AddAnnexC(
        S57S101Mapping forward,
        Dictionary<string, S101ToS57FeatureRule> features,
        Dictionary<string, S101ToS57AttributeRule> attributes,
        Dictionary<(string, string), S101ToS57AttributeRule> featureAttributes)
    {
        // ── Feature classes S-57 → S-101 never produces ───────────────────
        features["StructureOverNavigableWater"] = Rule("StructureOverNavigableWater", ObjlBuisgl, "BUISGL", "8.7") with
        {
            // §8.7: a covered bulk or passenger terminal is also a HRBFAC.
            Companions =
            [
                When(Rule("StructureOverNavigableWater", ObjlHrbfac, "HRBFAC", "8.7", ("CATHAF", "11")), "categoryOfStructure", "2"),
                When(Rule("StructureOverNavigableWater", ObjlHrbfac, "HRBFAC", "8.7", ("CATHAF", "8")), "categoryOfStructure", "5"),
            ],
        };
        // §6.7 / §6.8: a span is a BRIDGE that carries its Bridge's attributes.
        Feature(features, "SpanFixed", ObjlBridge, "BRIDGE", "6.7");
        Feature(features, "SpanOpening", ObjlBridge, "BRIDGE", "6.8");
        Feature(features, "FreePortArea", 60, "FRPARE", "16.19");
        Feature(features, "Seagrass", ObjlWedklp, "WEDKLP", "12.3", ("CATWED", "3"));
        Feature(features, "WindTurbine", ObjlLndmrk, "LNDMRK", "7.4", ("CATLMK", "19"), ("CONVIS", "2"));
        Feature(features, "ArchipelagicSeaLaneArea", ObjlArcsln, "ARCSLN", "15.24");
        Feature(features, "ArchipelagicSeaLaneAxis", ObjlAslxis, "ASLXIS", "15.25");
        Feature(features, "RadarLine", 99, "RADLNE", "15.29");
        Feature(features, "RadarRange", 100, "RADRNG", "15.30");
        Feature(features, "CustomZone", 37, "CUSZNE", "16.15");
        Feature(features, "StraightTerritorialSeaBaseline", 132, "STSLNE", "16.23");
        Feature(features, "TrafficSeparationSchemeCrossing", 147, "TSSCRS", "15.21");
        Feature(features, "TrafficSeparationSchemeRoundabout", 149, "TSSRON", "15.22");
        Feature(features, "RadarReflector", 101, "RADRFL", "20.18");
        Feature(features, "DepthNoBottomFound", ObjlSoundg, "SOUNDG", "11.8", ("QUASOU", "5"));
        Feature(features, "FoulGround", ObjlObstrn, "OBSTRN", "13.7", ("CATOBS", "7"), ("WATLEV", "3"));
        Feature(features, "DiscolouredWater", ObjlCtnare, "CTNARE", "13.8", ("INFORM", "Discoloured water"));
        Feature(features, "EmergencyWreckMarkingBuoy", ObjlBoyspp, "BOYSPP", "20.6", ("CATSPM", "27"));
        Feature(features, "LightFogDetector", ObjlLights, "LIGHTS", "19.4", ("CATLIT", "7"));
        Feature(features, "LightAirObstruction", ObjlLights, "LIGHTS", "19.5", ("CATLIT", "6"));
        Feature(features, "MooringArea", 4, "ACHARE", "16.4");
        Feature(features, "PilotageDistrict", ObjlAdmare, "ADMARE", "16.26", ("JRSDTN", "3"));
        Feature(features, "MarinePollutionRegulationsArea", ObjlAdmare, "ADMARE", "16.28", ("JRSDTN", "3"));
        Feature(features, "VesselTrafficServiceArea", ObjlAdmare, "ADMARE", "22.2", ("JRSDTN", "3"));
        Feature(features, "TidalStreamPanelData", 138, "TS_PAD", "10.5");
        features["Helipad"] = Rule("Helipad", ObjlRunway, "RUNWAY", "6.5", ("CATRUN", "2")) with
        {
            Companions = [OnPrimitive(Rule("Helipad", ObjlLndmrk, "LNDMRK", "6.5", ("CATLMK", ""), ("CONVIS", "2")), S57GeometricPrimitive.Point)],
        };
        features["ContinentalShelfArea"] = Rule("ContinentalShelfArea", 32, "COSARE", "16.14") with { PrimitiveChange = S101ToS57PrimitiveChange.CurveToArea };
        features["CollisionRegulationsLimit"] = Rule("CollisionRegulationsLimit", ObjlCtnare, "CTNARE", "16.27", ("INFORM", "Collision regulations limit")) with
        {
            PrimitiveChange = S101ToS57PrimitiveChange.CurveToArea,
        };

        // §21.3: a virtual AIS aid to navigation is a NEWOBJ whose class
        // definition, name and symbol instruction follow from its type.
        var virtualAis = Feature(features, "VirtualAISAidToNavigation", ObjlNewobj, "NEWOBJ", "21.3");
        features["VirtualAISAidToNavigation"] = virtualAis with
        {
            Redirects = VirtualAisTypes.Select(t => When(
                Rule("VirtualAISAidToNavigation", ObjlNewobj, "NEWOBJ", "27.199",
                    ("CLSDEF", t.Definition),
                    ("CLSNAM", $"Virtual AtoN, {t.Name}"),
                    ("INFORM", $"Virtual AtoN, {t.Name}; {t.Definition}."),
                    ("SYMINS", $"SY(BRTHNO01);SY({t.Symbol});TX('V-AIS',3,2,2,'15110',2,0,CHMGD,11)")),
                "virtualAISAidToNavigationType", t.Value)).ToList(),
        };

        // Aggregations become C_AGGR (§5.5, §8.23, §15.6, §15.8, §15.11,
        // §15.15, §15.23, §15.26); the translator builds the members.
        foreach (var (code, _) in Aggregations)
            Feature(features, code, ObjlCAggr, "C_AGGR", code == "FairwaySystem" ? "15.8" : FeatureClause(code));

        // ── Class changes by attribute value or primitive ─────────────────
        Redirect(features, "Landmark",
            When(Rule("Landmark", ObjlCtrpnt, "CTRPNT", "7.2", ("CATCTR", "1")), "categoryOfLandmark", "22"),
            When(Rule("Landmark", ObjlCtrpnt, "CTRPNT", "7.2", ("CATCTR", "5")), "categoryOfLandmark", "23"),
            When(Rule("Landmark", ObjlBridge, "BRIDGE", "7.2", ("CATBRG", "1")), "categoryOfLandmark", "26"),
            When(Rule("Landmark", ObjlDamcon, "DAMCON", "7.2"), "categoryOfLandmark", "27"));
        Redirect(features, "Pile",
            When(Rule("Pile", ObjlCtnare, "CTNARE", "8.4", ("INFORM", "Area of piles")), "categoryOfPile", ["5"], S57GeometricPrimitive.Area),
            OnPrimitive(Rule("Pile", ObjlMorfac, "MORFAC", "8.4", ("CATMOR", "5")), S57GeometricPrimitive.Line, S57GeometricPrimitive.Area),
            When(Rule("Pile", ObjlBcnspp, "BCNSPP", "8.4", ("CONRAD", "3"), ("BCNSHP", "1"), ("CATSPM", "52"), ("COLOUR", "")), "radarConspicuous", [], S57GeometricPrimitive.Point),
            When(Rule("Pile", ObjlMorfac, "MORFAC", "27.47", ("CATMOR", "5")), "categoryOfPile", "8"));
        Redirect(features, "Lake", When(Rule("Lake", ObjlRivers, "RIVERS", "5.10"), "status", "5") with { ConsumesAttribute = false });
        Redirect(features, "ShorelineConstruction",
            When(Rule("ShorelineConstruction", ObjlMorfac, "MORFAC", "8.6", ("CATMOR", "4")), "categoryOfShorelineConstruction", "23"));
        Redirect(features, "CableSubmarine",
            When(Rule("CableSubmarine", ObjlMorfac, "MORFAC", "14.2", ("CATMOR", "6")), "categoryOfCable", "9"));
        Redirect(features, "MooringBuoy",
            When(Rule("MooringBuoy", ObjlSmcfac, "SMCFAC", "20.8", ("CATSCF", "29")), "visitorsMooring", []));
        Redirect(features, "Obstruction",
            When(Rule("Obstruction", ObjlVegatn, "VEGATN", "13.6", ("CATVEG", "7")), "categoryOfObstruction", ["23"], S57GeometricPrimitive.Area));
        Redirect(features, "SeparationZoneOrLine",
            OnPrimitive(Rule("SeparationZoneOrLine", ObjlTselne, "TSELNE", "15.19"), S57GeometricPrimitive.Line));

        // Table 2-1: primitives S-57 doesn't allow on the class.
        foreach (var code in new[] { "AdministrationArea", "ContiguousZone", "ExclusiveEconomicZone", "TerritorialSeaArea" })
            Change(features, code, S101ToS57PrimitiveChange.CurveToArea);
        foreach (var code in new[] { "CoastGuardStation", "RescueStation", "SignalStationTraffic", "SignalStationWarning" })
            Change(features, code, S101ToS57PrimitiveChange.SurfaceToPoint);
        Change(features, "LandRegion", S101ToS57PrimitiveChange.DropCurve);

        // ── Objects Annex C adds ──────────────────────────────────────────
        // §30 inTheWater: a structure standing in the water gets a PILPNT.
        foreach (var code in new[] { "Building", "Landmark", "SiloTank", "WindTurbine", "FortifiedStructure", "Crane" })
            Companion(features, code, When(Rule(code, ObjlPilpnt, "PILPNT", "30"), "inTheWater", [], S57GeometricPrimitive.Point));
        // §14.1 / §27.100: a flare stack on a platform is a LNDMRK.
        Companion(features, "OffshorePlatform", When(Rule("OffshorePlatform", ObjlLndmrk, "LNDMRK", "27.100", ("CATLMK", "6"), ("CONVIS", "2")), "flareStack", []));
        // §16.2 / §27.113: a disputed area also gets a CTNARE.
        foreach (var code in new[] { "AdministrationArea", "ContiguousZone", "ExclusiveEconomicZone", "TerritorialSeaArea", "FisheryZone", "ContinentalShelfArea", "CustomZone" })
            Companion(features, code, When(Rule(code, ObjlCtnare, "CTNARE", "27.113", ("INFORM", "In dispute")), "inDispute", []));

        // ── Attributes ────────────────────────────────────────────────────
        // Direct copies with no forward rule to invert (§27, §28).
        Attribute(attributes, forward, "categoryOfDock", "CATDOC", 22);
        Attribute(attributes, forward, "categoryOfInstallationBuoy", "CATINB", 33);
        Attribute(attributes, forward, "estimatedRangeOfTransmission", "ESTRNG", 91);
        Attribute(attributes, forward, "iceFactor", "ICEFAC", 101);
        Attribute(attributes, forward, "valueOfMaximumRange", "VALMXR", 177);
        Attribute(attributes, forward, "minimumBerthDepth", "DRVAL1", 87);
        Attribute(attributes, forward, "dredgedDate", "SORDAT", 147);
        Attribute(attributes, forward, "sweptDate", "SORDAT", 147);
        Attribute(attributes, forward, "frequencyShoreStationTransmits", "SIGFRQ", 139);
        Attribute(attributes, forward, "scaleValueMaximum", "SCVAL1", 134);
        Attribute(attributes, forward, "scaleValueMinimum", "SCVAL2", 135);
        Attribute(attributes, forward, "measurementDistanceMaximum", "SDISMX", 145);
        Attribute(attributes, forward, "measurementDistanceMinimum", "SDISMN", 146);
        Attribute(attributes, forward, "qualityOfHorizontalMeasurement", "QUAPOS", 402);
        Attribute(attributes, forward, "categoryOfZoneOfConfidenceInData", "CATZOC", 72);

        // §8.7 / §27.68 / §27.40: attributes with no S-57 counterpart whose
        // values become text or another attribute.
        Ensure(attributes, "categoryOfStructure", "", 0);
        Ensure(attributes, "categoryOfMooringArea", "CATACH", 8);
        // §27.15: only dangerous cargo converts: an explosives anchorage, or
        // text on a berth.
        Ensure(attributes, "categoryOfCargo", "", 0);
        featureAttributes[("AnchorageArea", "categoryOfCargo")] = new S101ToS57AttributeRule(
            "categoryOfCargo", 8, "CATACH", "27.15",
            Enumerable.Range(1, 15).ToDictionary(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture), v => v == 7 ? "4" : (string?)null));

        // Text the S-57 attribute can't hold folds into INFORM (§27).
        Inform(attributes, "maximumPermittedDraught", "Maximum draught permitted = {0} metres", "27.125");
        Inform(attributes, "maximumPermittedVesselLength", "Maximum vessel length permitted = {0} metres", "27.126");
        Inform(attributes, "measuredDistance", "Measured distance = {0} metres", "27.127");
        Inform(attributes, "contactInstructions", "Contact instructions: {0}", "27.79");
        Inform(attributes, "referenceLocation", "Reference location: {0}", "27.149");
        Inform(attributes, "isMRCC", "Maritime Rescue and Coordination Centre", "27.115");

        // Table A-2 / §27: value remaps and values that fold into INFORM.
        Remap(attributes, featureAttributes, "categoryOfDolphin", "27.22", ("1", "1"), ("2", "2"), ("3", "1"), ("4", "1"));
        Remap(attributes, featureAttributes, "categoryOfCable", "27.12", ("10", "4"));
        Remap(attributes, featureAttributes, "categoryOfPipelinePipe", "27.49", ("7", "5"));
        Remap(attributes, featureAttributes, "categoryOfShorelineConstruction", "27.61", ("22", "6"));
        Remap(attributes, featureAttributes, "techniqueOfVerticalMeasurement", "27.174", ("15", "7"), ("17", "14"), ("18", "6"));
        Remap(attributes, featureAttributes, "marksNavigationalSystemOf", "27.124", ("11", "10"));
        Remap(attributes, featureAttributes, "categoryOfMooringArea", "27.40", ("1", "8"));
        // §6.6 / §27.3 / §27.4: only bridgeConstruction 2–5 and bridgeFunction
        // 3–4 have a CATBRG value.
        Remap(attributes, featureAttributes, "bridgeConstruction", "27.3", ("1", null));
        Remap(attributes, featureAttributes, "bridgeFunction", "27.4", ("1", null), ("2", null));
        Remap(attributes, featureAttributes, "categoryOfOffshoreProductionArea", "27.44", ("6", null));

        attributes["categoryOfCargo"] = attributes["categoryOfCargo"] with
        {
            InformText = new Dictionary<string, string>(StringComparer.Ordinal) { ["7"] = "Dangerous or hazardous cargo berth" },
            Clause = "27.15",
        };
        Fold(attributes, featureAttributes, "categoryOfAirportAirfield", "27.9", ("9", "Search and rescue airfield"));
        Fold(attributes, featureAttributes, "categoryOfAnchorage", "27.10", ("14", "Waiting anchorage"));
        Fold(attributes, featureAttributes, "categoryOfCable", "27.12", ("7", "Ferry cable"));
        Fold(attributes, featureAttributes, "categoryOfConveyor", "27.18", ("3", "Flume"), ("4", "Lift/elevator"));
        Fold(attributes, featureAttributes, "categoryOfCrane", "27.19", ("6", "Goliath crane"));
        Fold(attributes, featureAttributes, "categoryOfFerry", "27.25", ("5", "High speed ferry"));
        Fold(attributes, featureAttributes, "categoryOfFortifiedStructure", "27.28", ("8", "Fortified submarine shelter"), ("9", "Rampart"));
        Fold(attributes, featureAttributes, "categoryOfHarbourFacility", "27.30", ("14", "Service harbour"), ("15", "Pilotage service"));
        Fold(attributes, featureAttributes, "categoryOfHulk", "27.31", ("6", "Casino"), ("7", "Training vessel"));
        Fold(attributes, featureAttributes, "categoryOfLandRegion", "27.34", ("21", "Wadi"));
        Fold(attributes, featureAttributes, "categoryOfLandmark", "27.35", ("24", "Observation wheel"), ("25", "Torii"));
        Fold(attributes, featureAttributes, "categoryOfMooringArea", "27.40", ("2", "Mooring area for visitors"), ("3", "Mooring area for tankers"));
        Fold(attributes, featureAttributes, "categoryOfObstruction", "27.42",
            ("12", "Wave energy device"), ("13", "Subsurface ocean data acquisition system"), ("14", "Artificial reef"),
            ("15", "Template"), ("16", "Manifold"), ("17", "Submerged pingo"), ("18", "Remains of platform"),
            ("19", "Scientific instrument"), ("20", "Underwater turbine"), ("21", "Active submarine volcano"),
            ("22", "Shark net"), ("23", "Mangrove"));
        Fold(attributes, featureAttributes, "categoryOfOffshorePlatform", "27.43", ("11", "Floating oil tank"));
        Fold(attributes, featureAttributes, "categoryOfOffshoreProductionArea", "27.44",
            ("2", "Wave farm"), ("3", "Current farm"), ("5", "Seabed material extraction area"), ("6", "Solar farm"));
        Fold(attributes, featureAttributes, "categoryOfPile", "27.47", ("5", "Piling"), ("6", "Area of piles"), ("7", "Pipe"));
        Fold(attributes, featureAttributes, "categoryOfProductionArea", "27.51", ("11", "Production plant"), ("12", "Solar farm"));
        Fold(attributes, featureAttributes, "categoryOfPylon", "27.52", ("6", "Pipeline pylon"));
        Fold(attributes, featureAttributes, "categoryOfRadioStation", "27.55", ("19", "Radio telephone station"), ("20", "AIS base station"));
        Fold(attributes, featureAttributes, "categoryOfRestrictedArea", "27.57",
            ("29", "Disengagement area"), ("30", "Port security area"), ("31", "Coral sanctuary"), ("32", "Recreation area"));
        Fold(attributes, featureAttributes, "categoryOfSeaArea", "27.60", ("55", "Intertidal cay"), ("56", "Submarine volcano"));
        Fold(attributes, featureAttributes, "categoryOfShorelineConstruction", "27.61", ("20", "Swimming facility"));
        Fold(attributes, featureAttributes, "categoryOfSpecialPurposeMark", "27.67", ("59", "Fish aggregating device"), ("63", "Wave recorder"));
        Fold(attributes, featureAttributes, "categoryOfStructure", "27.68", ("1", "Boathouse"), ("3", "Covered wharf"), ("4", "Covered service terminal"));
        Fold(attributes, featureAttributes, "function", "27.103",
            ("44", "Sea rescue control"), ("45", "Observatory"), ("46", "Ore crusher"), ("47", "Boathouse"), ("48", "Pumping station"));
        Fold(attributes, featureAttributes, "lightVisibility", "27.120", ("9", "Visible in line of range"));
        Fold(attributes, featureAttributes, "marksNavigationalSystemOf", "27.124", ("11", "Main European inland waterway marking system"));
        Fold(attributes, featureAttributes, "natureOfConstruction", "27.136", ("11", "Latticed construction"), ("12", "Glass construction"));
        Fold(attributes, featureAttributes, "product", "27.144", ("23", "Product: Electricity"), ("24", "Product: Ice"), ("25", "Product: Clay"));
        Fold(attributes, featureAttributes, "restriction", "27.155", ("39", "Swimming prohibited"), ("42", "Power-driven vessels prohibited"));
        Fold(attributes, featureAttributes, "signalGeneration", "27.161", ("5", "Radio activated"), ("6", "Call activated"));
        Fold(attributes, featureAttributes, "status", "27.171", ("28", "Buoyed"));
        Fold(attributes, featureAttributes, "surveyType", "28.23",
            ("7", "Full coverage"), ("8", "Systematic survey"), ("9", "Non-systematic survey"), ("10", "Inadequately surveyed"),
            ("11", "Spot-sounding survey"), ("12", "Found by levelling"), ("13", "Mechanically swept survey"));
        Fold(attributes, featureAttributes, "techniqueOfVerticalMeasurement", "27.174", ("16", "Synthetic aperture radar"));
        Fold(attributes, featureAttributes, "verticalDatum", "27.196", ("44", "Vertical Datum = Baltic Sea chart datum 2000"));
        // §27.10: a reported anchorage is CATACH 1 with the fact in INFORM.
        Remap(attributes, featureAttributes, "categoryOfAnchorage", "27.10", ("15", "1"));
        FoldKeeping(attributes, featureAttributes, "categoryOfAnchorage", "27.10", ("15", "Reported anchorage"));
        Remap(attributes, featureAttributes, "categoryOfSpecialPurposeMark", "27.67", ("57", "27"), ("58", "27"), ("60", "27"), ("61", "27"), ("62", "27"));
        FoldKeeping(attributes, featureAttributes, "categoryOfSpecialPurposeMark", "27.67", ("57", "Ice mark"), ("58", "Nature reserve mark"), ("60", "Wreck mark"), ("61", "Customs mark"), ("62", "Causeway mark"));
        // §27.201: visual prominence 3 (prominent) has no CONVIS value.
        Remap(attributes, featureAttributes, "visualProminence", "27.201", ("3", null));
        // §27.68: a covered terminal is a harbour facility (on the HRBFAC
        // companion); the BUISGL takes the others as text.
        Remap(attributes, featureAttributes, "categoryOfStructure", "27.68", ("2", null), ("5", null));
    }

    // §27.199: per virtualAISAidToNavigationType value, the type name, the
    // S-52 symbol and the class definition.
    internal static readonly (string Value, string Name, string Symbol, string Definition)[] VirtualAisTypes =
    [
        ("1", "North Cardinal", "BCNCAR01", "A Virtual object which indicates navigable water lies northwards"),
        ("2", "East Cardinal", "BCNCAR02", "A Virtual object which indicates navigable water lies eastwards"),
        ("3", "South Cardinal", "BCNCAR03", "A Virtual object which indicates navigable water lies southwards"),
        ("4", "West Cardinal", "BCNCAR04", "A Virtual object which indicates navigable water lies westwards"),
        ("5", "Port Lateral", "BOYLAT24", "A Virtual object marking the port side of a channel"),
        ("6", "Starboard Lateral", "BOYLAT13", "A Virtual object marking the starboard side of a channel"),
        ("7", "Port Lateral", "BOYLAT23", "A Virtual object marking the port side of a channel"),
        ("8", "Starboard Lateral", "BOYLAT14", "A Virtual object marking the starboard side of a channel"),
        ("9", "Isolated Danger", "BCNISD21", "A Virtual object marking an isolated danger"),
        ("10", "Safe Water", "BOYSAW12", "A Virtual object marking safe water"),
        ("11", "Special Purpose", "BOYSPP11", "A Virtual object used to mark an area or feature referred to in nautical documents"),
        ("12", "Emergency Wreck Marking", "BOYSPP11", "A Virtual object marking a wreck"),
    ];

    private static S101ToS57FeatureRule Rule(string s101Code, ushort objl, string acronym, string clause, params (string Acronym, string Value)[] fixedAttributes)
        => new(s101Code, objl, acronym, clause,
            fixedAttributes.Length == 0
                ? NoFixedAttributes
                : fixedAttributes.ToDictionary(a => a.Acronym, a => a.Value, StringComparer.Ordinal));

    private static S101ToS57FeatureRule Feature(
        Dictionary<string, S101ToS57FeatureRule> features, string s101Code, ushort objl, string acronym, string clause,
        params (string Acronym, string Value)[] fixedAttributes)
        => features[s101Code] = Rule(s101Code, objl, acronym, clause, fixedAttributes);

    private static S101ToS57FeatureRedirect When(S101ToS57FeatureRule target, string attribute, params string[] values)
        => new(target, attribute, values, []);

    private static S101ToS57FeatureRedirect When(S101ToS57FeatureRule target, string attribute, string[] values, params S57GeometricPrimitive[] primitives)
        => new(target, attribute, values, primitives);

    private static S101ToS57FeatureRedirect OnPrimitive(S101ToS57FeatureRule target, params S57GeometricPrimitive[] primitives)
        => new(target, null, [], primitives);

    private static void Redirect(Dictionary<string, S101ToS57FeatureRule> features, string s101Code, params S101ToS57FeatureRedirect[] redirects)
        => features[s101Code] = features[s101Code] with { Redirects = [.. features[s101Code].Redirects, .. redirects] };

    private static void Companion(Dictionary<string, S101ToS57FeatureRule> features, string s101Code, S101ToS57FeatureRedirect companion)
    {
        if (features.TryGetValue(s101Code, out var rule))
            features[s101Code] = rule with { Companions = [.. rule.Companions, companion] };
    }

    private static void Change(Dictionary<string, S101ToS57FeatureRule> features, string s101Code, S101ToS57PrimitiveChange change)
    {
        if (features.TryGetValue(s101Code, out var rule))
            features[s101Code] = rule with { PrimitiveChange = change };
    }

    private static void Attribute(
        Dictionary<string, S101ToS57AttributeRule> attributes, S57S101Mapping forward, string s101Code, string acronym, ushort attl)
    {
        if (attributes.ContainsKey(s101Code)) return;
        if (forward.TryGetAttl(acronym, out var known)) attl = known;
        attributes[s101Code] = new S101ToS57AttributeRule(s101Code, attl, acronym, AttributeClause(s101Code), NoRemap);
    }

    // A rule for an attribute with no forward rule, so that remaps and INFORM
    // folds have one to attach to; ATTL 0 writes no S-57 attribute.
    private static void Ensure(Dictionary<string, S101ToS57AttributeRule> attributes, string s101Code, string acronym, ushort attl)
    {
        if (!attributes.ContainsKey(s101Code))
            attributes[s101Code] = new S101ToS57AttributeRule(s101Code, attl, acronym, AttributeClause(s101Code), NoRemap);
    }

    private static void Inform(Dictionary<string, S101ToS57AttributeRule> attributes, string s101Code, string format, string clause)
        => attributes[s101Code] = new S101ToS57AttributeRule(s101Code, 102, "INFORM", clause, NoRemap) { InformFormat = format };

    // Adds value remaps to the general rule for the attribute and to every
    // class-specific rule for it.
    private static void Remap(
        Dictionary<string, S101ToS57AttributeRule> attributes,
        Dictionary<(string, string), S101ToS57AttributeRule> featureAttributes,
        string s101Code, string clause, params (string From, string? To)[] remap)
    {
        foreach (var (key, rule) in RulesFor(attributes, featureAttributes, s101Code))
        {
            var merged = new Dictionary<string, string?>(rule.ValueRemap, StringComparer.Ordinal);
            foreach (var (from, to) in remap) merged[from] = to;
            Set(attributes, featureAttributes, key, rule with { ValueRemap = merged, Clause = clause });
        }
    }

    // Adds values that fold into INFORM to the general rule for the attribute
    // and to every class-specific rule for it; the value itself isn't written.
    private static void Fold(
        Dictionary<string, S101ToS57AttributeRule> attributes,
        Dictionary<(string, string), S101ToS57AttributeRule> featureAttributes,
        string s101Code, string clause, params (string Value, string Text)[] texts)
        => Fold(attributes, featureAttributes, s101Code, clause, keepValue: false, texts);

    // As Fold, for values a remap also gives an S-57 value.
    private static void FoldKeeping(
        Dictionary<string, S101ToS57AttributeRule> attributes,
        Dictionary<(string, string), S101ToS57AttributeRule> featureAttributes,
        string s101Code, string clause, params (string Value, string Text)[] texts)
        => Fold(attributes, featureAttributes, s101Code, clause, keepValue: true, texts);

    private static void Fold(
        Dictionary<string, S101ToS57AttributeRule> attributes,
        Dictionary<(string, string), S101ToS57AttributeRule> featureAttributes,
        string s101Code, string clause, bool keepValue, (string Value, string Text)[] texts)
    {
        foreach (var (key, rule) in RulesFor(attributes, featureAttributes, s101Code))
        {
            var inform = new Dictionary<string, string>(rule.InformText, StringComparer.Ordinal);
            var remap = new Dictionary<string, string?>(rule.ValueRemap, StringComparer.Ordinal);
            foreach (var (value, text) in texts)
            {
                inform[value] = text;
                if (!keepValue) remap[value] = null;
            }
            Set(attributes, featureAttributes, key, rule with { InformText = inform, ValueRemap = remap, Clause = clause });
        }
    }

    private static IEnumerable<((string Feature, string Attribute)? Key, S101ToS57AttributeRule Rule)> RulesFor(
        Dictionary<string, S101ToS57AttributeRule> attributes,
        Dictionary<(string, string), S101ToS57AttributeRule> featureAttributes,
        string s101Code)
    {
        var found = new List<((string, string)?, S101ToS57AttributeRule)>();
        if (attributes.TryGetValue(s101Code, out var general)) found.Add((null, general));
        foreach (var (key, rule) in featureAttributes)
        {
            if (key.Item2 == s101Code) found.Add((key, rule));
        }
        return found;
    }

    private static void Set(
        Dictionary<string, S101ToS57AttributeRule> attributes,
        Dictionary<(string, string), S101ToS57AttributeRule> featureAttributes,
        (string, string)? key, S101ToS57AttributeRule rule)
    {
        if (key is { } k) featureAttributes[k] = rule;
        else attributes[rule.S101Code] = rule;
    }
}
