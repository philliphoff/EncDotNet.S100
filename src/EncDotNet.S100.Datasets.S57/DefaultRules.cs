using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Compiled-in seed data for the default <see cref="S57S101Mapping"/>.
/// </summary>
/// <remarks>
/// <para>
/// OBJL/ATTL numeric codes are sourced from the IHO S-57 Object &amp; Attribute
/// Catalogues (Appendix A, Edition 3.1). S-101 codes are taken from the
/// IHO S-101 Feature Catalogue (Edition 1.x) and reconciled against the
/// IHO draft "S-57 to S-101 Conversion Guidance" (S-100WG / S-101 PT 6,
/// January 2021).
/// </para>
/// <para>
/// Most rules are flat 1:1 mappings. Cross-class redirects, per-feature
/// attribute overrides and per-value enum remaps are encoded in-line for
/// the specific cases called out by the IHO conversion guidance.
/// </para>
/// </remarks>
internal static class DefaultRules
{
    public static IEnumerable<S57FeatureRule> FeatureRules()
    {
        // Format: F(OBJL, S57 acronym, S-101 Feature Catalogue code).
        yield return F(2, "AIRARE", "AirportAirfield");
        yield return F(3, "ACHBRT", "AnchorBerth");
        yield return F(4, "ACHARE", "AnchorageArea");
        yield return F(5, "BCNCAR", "CardinalBeacon");
        yield return F(6, "BCNISD", "IsolatedDangerBeacon");
        yield return F(7, "BCNLAT", "LateralBeacon");
        yield return F(8, "BCNSAW", "SafeWaterBeacon");
        yield return F(9, "BCNSPP", "SpecialPurposeGeneralBeacon");
        yield return F(10, "BERTHS", "Berth");
        yield return F(11, "BRIDGE", "Bridge");
        yield return F(13, "BUAARE", "BuiltUpArea");
        yield return F(14, "BOYCAR", "CardinalBuoy");
        yield return F(15, "BOYINB", "InstallationBuoy");
        yield return F(16, "BOYISD", "IsolatedDangerBuoy");
        yield return F(17, "BOYLAT", "LateralBuoy");
        yield return F(18, "BOYSAW", "SafeWaterBuoy");
        yield return F(19, "BOYSPP", "SpecialPurposeGeneralBuoy");
        yield return F(28, "CHKPNT", "Checkpoint");
        // COALNE — IHO Conversion Guidance § 4.5.1: default is Coastline;
        // CATCOA is selectively redirected to natureOfSurface for surface-type
        // values (sandy/stony/shingly/coral/shelly shore). Other CATCOA
        // values pass through to categoryOfCoastline (the rule default).
        yield return new S57FeatureRule
        {
            Objl = 30,
            S57Acronym = "COALNE",
            DefaultS101Code = "Coastline",
            AttributeOverrides = ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                new[]
                {
                    new KeyValuePair<string, S57AttributeOverride>(
                        "CATCOA",
                        new S57AttributeOverride
                        {
                            S101CodeByValue = ImmutableDictionary.CreateRange(new[]
                            {
                                new KeyValuePair<string, string>("3", "natureOfSurface"),
                                new KeyValuePair<string, string>("4", "natureOfSurface"),
                                new KeyValuePair<string, string>("5", "natureOfSurface"),
                                new KeyValuePair<string, string>("9", "natureOfSurface"),
                                new KeyValuePair<string, string>("11", "natureOfSurface"),
                            }),
                            ValueRemap = ImmutableDictionary.CreateRange(new[]
                            {
                                new KeyValuePair<string, string?>("3", "4"),   // sandy → sand
                                new KeyValuePair<string, string?>("4", "5"),   // stony → stone
                                new KeyValuePair<string, string?>("5", "7"),   // shingly → pebbles
                                new KeyValuePair<string, string?>("9", "14"),  // coral reef → coral
                                new KeyValuePair<string, string?>("11", "17"), // shelly → shells
                            }),
                        }),
                }),
        };
        // CTRPNT — IHO Conversion Guidance § 4.3: drop in general; redirect
        // CATCTR ∈ {1, 5} to Landmark with value-remapped categoryOfLandmark.
        yield return new S57FeatureRule
        {
            Objl = 33,
            S57Acronym = "CTRPNT",
            DefaultS101Code = null,
            Redirects = ImmutableArray.Create(new S57FeatureRedirect
            {
                ConditionAttribute = "CATCTR",
                ConditionValues = ImmutableArray.Create("1", "5"),
                TargetS101Code = "Landmark",
                AttributeOverrides = ImmutableDictionary.CreateRange(
                    StringComparer.OrdinalIgnoreCase,
                    new[]
                    {
                        new KeyValuePair<string, S57AttributeOverride>(
                            "CATCTR",
                            new S57AttributeOverride
                            {
                                S101Code = "categoryOfLandmark",
                                ValueRemap = ImmutableDictionary.CreateRange(new[]
                                {
                                    new KeyValuePair<string, string?>("1", "22"), // triangulation mark
                                    new KeyValuePair<string, string?>("5", "23"), // boundary mark
                                }),
                            }),
                    }),
            }),
        };
        yield return F(35, "CRANES", "Crane");
        yield return F(42, "DEPARE", "DepthArea");
        yield return F(43, "DEPCNT", "DepthContour");
        yield return F(44, "DISMAR", "DistanceMark");
        yield return F(45, "DOCARE", "DockArea");
        yield return F(46, "DRGARE", "DredgedArea");
        yield return F(47, "DRYDOC", "DryDock");
        yield return F(51, "FAIRWY", "Fairway");
        yield return F(57, "FLODOC", "FloatingDock");
        yield return F(61, "GATCON", "Gate");
        yield return F(62, "GRIDRN", "Gridiron");
        yield return F(64, "HRBFAC", "HarbourFacility");
        yield return F(65, "HULKES", "Hulk");
        yield return F(69, "LAKARE", "Lake");
        yield return F(71, "LNDARE", "LandArea");
        yield return F(72, "LNDELV", "LandElevation");
        yield return F(73, "LNDRGN", "LandRegion");
        yield return F(74, "LNDMRK", "Landmark");
        yield return F(75, "LIGHTS", "LightAllAround");
        yield return F(79, "LOKBSN", "LockBasin");
        yield return F(85, "NAVLNE", "NavigationLine");
        yield return F(86, "OBSTRN", "Obstruction");
        yield return F(90, "PILPNT", "Pile");
        yield return F(95, "PONTON", "Pontoon");
        yield return F(106, "RAILWY", "Railway");
        yield return F(107, "RAPIDS", "Rapids");
        yield return F(112, "RESARE", "RestrictedArea");
        yield return F(114, "RIVERS", "River");
        yield return F(116, "ROADWY", "Road");
        yield return F(122, "SLCONS", "ShorelineConstruction");
        yield return F(126, "SLOTOP", "SlopeTopline");
        yield return F(127, "SLOGRD", "SlopingGround");
        yield return F(128, "SMCFAC", "SmallCraftFacility");
        yield return F(129, "SOUNDG", "Sounding");
        yield return F(153, "UWTROC", "UnderwaterAwashRock");
        yield return F(157, "WATFAL", "Waterfall");
        yield return F(159, "WRECKS", "Wreck");
    }

    public static IEnumerable<S57AttributeRule> AttributeRules()
    {
        // Format: A(ATTL, S57 acronym, S-101 attribute name).
        yield return A(9, "CATBRG", "categoryOfBridge");
        yield return A(13, "CATCAM", "categoryOfCardinalMark");
        yield return A(14, "CATCHP", "categoryOfCheckpoint");
        yield return A(15, "CATCOA", "categoryOfCoastline");
        // CATCTR: appears on CTRPNT only, which is dropped in S-101 unless
        // redirected to Landmark (see CTRPNT feature rule). The default
        // mapping is therefore null; the redirect supplies the override.
        yield return A(16, "CATCTR", null);
        yield return A(19, "CATCRN", "categoryOfCrane");
        yield return A(29, "CATGAT", "categoryOfGate");
        yield return A(30, "CATHAF", "categoryOfHarbourFacility");
        yield return A(31, "CATHLK", "categoryOfHulk");
        yield return A(34, "CATLND", "categoryOfLandRegion");
        yield return A(36, "CATLAM", "categoryOfLateralMark");
        yield return A(37, "CATLIT", "categoryOfLight");
        yield return A(38, "CATMFA", "categoryOfMarineFarmCulture");
        yield return A(42, "CATOBS", "categoryOfObstruction");
        yield return A(57, "CATROD", "categoryOfRoad");
        yield return A(60, "CATSLC", "categoryOfShorelineConstruction");
        yield return A(64, "CATSLO", "categoryOfSlope");
        yield return A(65, "CATSCF", "categoryOfSmallCraftFacility");
        yield return A(71, "CATWRK", "categoryOfWreck");
        yield return A(75, "COLOUR", "colour");
        yield return A(76, "COLPAT", "colourPattern");
        yield return A(77, "COMCHA", "communicationChannel");
        yield return A(81, "CONDTN", "condition");
        yield return A(82, "CONRAD", "radarConspicuous");
        yield return A(83, "CONVIS", "visualProminence");
        yield return A(87, "DRVAL1", "depthRangeMinimumValue");
        yield return A(88, "DRVAL2", "depthRangeMaximumValue");
        yield return A(90, "ELEVAT", "elevation");
        yield return A(93, "EXPSOU", "expositionOfSounding");
        yield return A(95, "HEIGHT", "height");
        yield return A(112, "NATCON", "natureOfConstruction");
        yield return A(113, "NATSUR", "natureOfSurface");
        yield return A(125, "QUASOU", "qualityOfVerticalMeasurement");
        yield return A(133, "SCAMIN", "scaleMinimum");
        yield return A(149, "STATUS", "status");
        yield return A(174, "VALDCO", "valueOfDepthContour");
        yield return A(179, "VALSOU", "valueOfSounding");
        yield return A(181, "VERCLR", "verticalClearanceValue");
        yield return A(182, "VERCCL", "verticalClearanceClosed");
        yield return A(183, "VERCOP", "verticalClearanceOpen");
        yield return A(184, "VERCSA", "verticalClearanceSafe");
        yield return A(186, "VERLEN", "verticalLength");
        yield return A(187, "WATLEV", "waterLevelEffect");
    }

    private static S57FeatureRule F(ushort objl, string acronym, string? s101)
        => new() { Objl = objl, S57Acronym = acronym, DefaultS101Code = s101 };

    private static S57AttributeRule A(ushort attl, string acronym, string? s101)
        => new() { Attl = attl, S57Acronym = acronym, DefaultS101Code = s101 };
}
