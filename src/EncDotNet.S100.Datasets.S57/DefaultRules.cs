
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
            AttributeOverrides = new Dictionary<string, S57AttributeOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["CATCOA"] = new S57AttributeOverride
                {
                    S101CodeByValue = new Dictionary<string, string>
                    {
                        ["3"] = "natureOfSurface",
                        ["4"] = "natureOfSurface",
                        ["5"] = "natureOfSurface",
                        ["9"] = "natureOfSurface",
                        ["11"] = "natureOfSurface",
                    },
                    ValueRemap = new Dictionary<string, string?>
                    {
                        ["3"] = "4",   // sandy → sand
                        ["4"] = "5",   // stony → stone
                        ["5"] = "7",   // shingly → pebbles
                        ["9"] = "14",  // coral reef → coral
                        ["11"] = "17", // shelly → shells
                    },
                },
            },
        };
        // CTRPNT — IHO Conversion Guidance § 4.3: drop in general; redirect
        // CATCTR ∈ {1, 5} to Landmark with value-remapped categoryOfLandmark.
        yield return new S57FeatureRule
        {
            Objl = 33,
            S57Acronym = "CTRPNT",
            DefaultS101Code = null,
            Redirects = [new S57FeatureRedirect
            {
                ConditionAttribute = "CATCTR",
                ConditionValues = ["1", "5"],
                TargetS101Code = "Landmark",
                AttributeOverrides = new Dictionary<string, S57AttributeOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CATCTR"] = new S57AttributeOverride
                    {
                        S101Code = "categoryOfLandmark",
                        ValueRemap = new Dictionary<string, string?>
                        {
                            ["1"] = "22", // triangulation mark
                            ["5"] = "23", // boundary mark
                        },
                    },
                },
            }],
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

        // --- Gap coverage: S-57 classes with a direct S-101 FC alias ---
        // Each target is the S-101 Feature Catalogue code declared by that
        // feature type's <S100FC:alias> (= the originating S-57 acronym), and
        // was validated as a concrete (non-abstract) feature type. The set was
        // derived from a 3,636-cell NOAA ENC corpus audit of classes the
        // translator was silently dropping (no mapping rule). See the
        // IHO "S-57 to S-101 Conversion Guidance" for the authoritative
        // conversions; the FC alias is used here as the machine-readable bridge.
        yield return F(1, "ADMARE", "AdministrationArea");
        yield return F(12, "BUISGL", "Building");
        yield return F(20, "CBLARE", "CableArea");
        yield return F(21, "CBLOHD", "CableOverhead");
        yield return F(22, "CBLSUB", "CableSubmarine");
        yield return F(23, "CANALS", "Canal");
        yield return F(25, "CTSARE", "CargoTranshipmentArea");
        yield return F(26, "CAUSWY", "Causeway");
        // CTNARE aliases two S-101 features (DiscolouredWater, CautionArea);
        // CautionArea is the general-case conversion for S-57 caution areas.
        yield return F(27, "CTNARE", "CautionArea");
        yield return F(29, "CGUSTA", "CoastGuardStation");
        yield return F(31, "CONZNE", "ContiguousZone");
        yield return F(34, "CONVYR", "Conveyor");
        yield return F(36, "CURENT", "CurrentNonGravitational");
        yield return F(38, "DAMCON", "Dam");
        yield return F(39, "DAYMAR", "Daymark");
        yield return F(40, "DWRTCL", "DeepWaterRouteCentreline");
        yield return F(41, "DWRTPT", "DeepWaterRoutePart");
        yield return F(48, "DMPGRD", "DumpingGround");
        yield return F(49, "DYKCON", "Dyke");
        yield return F(50, "EXEZNE", "ExclusiveEconomicZone");
        yield return F(52, "FNCLNE", "FenceWall");
        yield return F(53, "FERYRT", "FerryRoute");
        yield return F(54, "FSHZNE", "FisheryZone");
        yield return F(55, "FSHFAC", "FishingFacility");
        yield return F(56, "FSHGRD", "FishingGround");
        yield return F(58, "FOGSIG", "FogSignal");
        yield return F(59, "FORSTC", "FortifiedStructure");
        yield return F(63, "HRBARE", "HarbourAreaAdministrative");
        yield return F(66, "ICEARE", "IceArea");
        yield return F(68, "ISTZNE", "InshoreTrafficZone");
        yield return F(76, "LITFLT", "LightFloat");
        yield return F(78, "LOCMAG", "LocalMagneticAnomaly");
        yield return F(80, "LOGPON", "LogPond");
        yield return F(81, "MAGVAR", "MagneticVariation");
        yield return F(82, "MARCUL", "MarineFarmCulture");
        yield return F(83, "MIPARE", "MilitaryPracticeArea");
        yield return F(87, "OFSPLF", "OffshorePlatform");
        yield return F(88, "OSPARE", "OffshoreProductionArea");
        yield return F(89, "OILBAR", "OilBarrier");
        yield return F(91, "PILBOP", "PilotBoardingPlace");
        yield return F(92, "PIPARE", "SubmarinePipelineArea");
        yield return F(93, "PIPOHD", "PipelineOverhead");
        yield return F(94, "PIPSOL", "PipelineSubmarineOnLand");
        yield return F(96, "PRCARE", "PrecautionaryArea");
        yield return F(97, "PRDARE", "ProductionStorageArea");
        yield return F(98, "PYLONS", "PylonBridgeSupport");
        yield return F(102, "RADSTA", "RadarStation");
        yield return F(103, "RTPBCN", "RadarTransponderBeacon");
        yield return F(104, "RDOCAL", "RadioCallingInPoint");
        yield return F(105, "RDOSTA", "RadioStation");
        yield return F(108, "RCRTCL", "RecommendedRouteCentreline");
        yield return F(109, "RECTRC", "RecommendedTrack");
        yield return F(110, "RCTLPT", "RecommendedTrafficLanePart");
        yield return F(111, "RSCSTA", "RescueStation");
        yield return F(113, "RETRFL", "Retroreflector");
        yield return F(117, "RUNWAY", "Runway");
        yield return F(118, "SNDWAV", "Sandwave");
        yield return F(119, "SEAARE", "SeaAreaNamedWaterArea");
        yield return F(120, "SPLARE", "SeaplaneLandingArea");
        yield return F(121, "SBDARE", "SeabedArea");
        yield return F(123, "SISTAT", "SignalStationTraffic");
        yield return F(124, "SISTAW", "SignalStationWarning");
        yield return F(125, "SILTNK", "SiloTank");
        yield return F(130, "SPRING", "Spring");
        yield return F(134, "SWPARE", "SweptArea");
        yield return F(135, "TESARE", "TerritorialSeaArea");
        yield return F(143, "TIDEWY", "Tideway");
        yield return F(146, "TSSBND", "TrafficSeparationSchemeBoundary");
        yield return F(148, "TSSLPT", "TrafficSeparationSchemeLanePart");
        yield return F(151, "TUNNEL", "Tunnel");
        yield return F(152, "TWRTPT", "TwoWayRoutePart");
        yield return F(154, "UNSARE", "UnsurveyedArea");
        yield return F(155, "VEGATN", "Vegetation");
        yield return F(156, "WATTUR", "WaterTurbulence");
        yield return F(158, "WEDKLP", "WeedKelp");
        yield return F(160, "TS_FEB", "TidalStreamFloodEbb");

        // Meta (M_*) objects. In S-101 several S-57 meta objects become
        // first-class features carrying data-quality / coverage information.
        // M_QUAL carries CATZOC (a bathymetric-quality concept) and therefore
        // converts to QualityOfBathymetricData; QualityOfNonBathymetricData is
        // sourced from M_ACCY (S-57 → S-101 Conversion Guidance).
        yield return F(302, "M_COVR", "DataCoverage");
        yield return F(305, "M_NPUB", "InformationArea");
        yield return F(306, "M_NSYS", "NavigationalSystemOfMarks");
        yield return F(308, "M_QUAL", "QualityOfBathymetricData");
        yield return F(309, "M_SDAT", "SoundingDatum");
        yield return F(310, "M_SREL", "QualityOfSurvey");
        yield return F(312, "M_VDAT", "VerticalDatumOfData");
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

        // --- Gap coverage: S-57 attributes with a direct S-101 FC alias ---
        // Each target is the S-101 simple-attribute code declared by that
        // attribute's <S100FC:alias> (= the originating S-57 acronym), AND is
        // directly bound to one or more feature types in the FC (verified
        // against the FC feature bindings). Derived from the same corpus audit
        // as the feature gap coverage above.
        //
        // Deliberately NOT mapped here (they are sub-attributes of an S-101
        // *complex* attribute, so a flat emission would be non-conformant;
        // they need complex-attribute assembly, like OBJNAM → featureName).
        // Several complexes are now assembled in the translator directly
        // (information, featureName, rhythmOfLight, the date ranges, and
        // zoneOfConfidence/CATZOC). Still deferred:
        //   SECTR1/SECTR2 → sectorBearing (light-sector complex),
        //   HORCLR → horizontalClearanceValue (horizontalClearanceFixed/Open),
        //   NATQUA → natureOfSurfaceQualifyingTerms (surfaceCharacteristics),
        //   SIGSEQ → signalSequence, MLTYLT (light-sector complex),
        //   SORDAT/SORIND (→ complex sourceIndication/reportedDate).
        //
        // Assembled into S-101 complex attributes by S57ToS101Translator (feature
        // binding-gated), NOT emitted here as flat simple attributes:
        //   DATSTA/DATEND → fixedDateRange, PERSTA/PEREND → periodicDateRange,
        //   SURSTA/SUREND → surveyDateRange (each with dateStart/dateEnd).
        //
        // NOTE: enum (E/L) attributes still pass their values through the
        // FC-driven S101AllowedEnumValues check; S-57 enum values that have no
        // S-101 equivalent are reported by the translation diagnostics rather
        // than silently dropping the whole attribute.
        yield return A(2, "BCNSHP", "beaconShape");
        yield return A(4, "BOYSHP", "buoyShape");
        yield return A(7, "CATAIR", "categoryOfAirportAirfield");
        yield return A(8, "CATACH", "categoryOfAnchorage");
        yield return A(10, "CATBUA", "categoryOfBuiltUpArea");
        yield return A(21, "CATDIS", "distanceMarkVisible");
        yield return A(35, "CATLMK", "categoryOfLandmark");
        yield return A(41, "CATNAV", "categoryOfNavigationLine");
        yield return A(45, "CATPLE", "categoryOfPile");
        yield return A(56, "CATREA", "categoryOfRestrictedArea");
        yield return A(66, "CATSPM", "categoryOfSpecialPurposeMark");
        yield return A(92, "EXCLIT", "exhibitionConditionOfLight");
        yield return A(94, "FUNCTN", "function");
        yield return A(99, "HORLEN", "horizontalLength");
        yield return A(106, "LIFCAP", "liftingCapacity");
        yield return A(108, "LITVIS", "lightVisibility");
        yield return A(109, "MARSYS", "marksNavigationalSystemOf");
        yield return A(117, "ORIENT", "orientationValue");
        yield return A(120, "PICREP", "pictorialRepresentation");
        yield return A(123, "PRODCT", "product");
        yield return A(127, "RADIUS", "radius");
        yield return A(131, "RESTRN", "restriction");
        yield return A(141, "SIGGRP", "signalGroup");
        yield return A(142, "SIGPER", "signalPeriod");
        yield return A(156, "TECSOU", "techniqueOfVerticalMeasurement");
        yield return A(172, "TRAFIC", "trafficFlow");
        yield return A(178, "VALNMR", "valueOfNominalRange");
        yield return A(185, "VERDAT", "verticalDatum");

        // --- Gap coverage (2nd wave): simple attributes surfaced once the
        // feature classes above began translating. Same provenance rules:
        // FC <S100FC:alias> = S-57 acronym, single-match, directly feature
        // bound. Mostly the categoryOf* discriminators of the new features.
        yield return A(3, "BUISHP", "buildingShape");
        yield return A(5, "BURDEP", "buriedDepth");
        yield return A(6, "CALSGN", "callSign");
        yield return A(11, "CATCBL", "categoryOfCable");
        yield return A(12, "CATCAN", "categoryOfCanal");
        yield return A(17, "CATCON", "categoryOfConveyor");
        yield return A(20, "CATDAM", "categoryOfDam");
        yield return A(23, "CATDPG", "categoryOfDumpingGround");
        yield return A(24, "CATFNC", "categoryOfFence");
        yield return A(25, "CATFRY", "categoryOfFerry");
        yield return A(26, "CATFIF", "categoryOfFishingFacility");
        yield return A(27, "CATFOG", "categoryOfFogSignal");
        yield return A(28, "CATFOR", "categoryOfFortifiedStructure");
        yield return A(32, "CATICE", "categoryOfIce");
        yield return A(39, "CATMPA", "categoryOfMilitaryPracticeArea");
        yield return A(43, "CATOFP", "categoryOfOffshorePlatform");
        yield return A(44, "CATOLB", "categoryOfOilBarrier");
        yield return A(46, "CATPIL", "categoryOfPilotBoardingPlace");
        yield return A(47, "CATPIP", "categoryOfPipelinePipe");
        yield return A(49, "CATPYL", "categoryOfPylon");
        yield return A(51, "CATRAS", "categoryOfRadarStation");
        yield return A(52, "CATRTB", "categoryOfRadarTransponderBeacon");
        yield return A(53, "CATROS", "categoryOfRadioStation");
        yield return A(54, "CATTRK", "basedOnFixedMarks");
        yield return A(55, "CATRSC", "categoryOfRescueStation");
        yield return A(59, "CATSEA", "categoryOfSeaArea");
        yield return A(61, "CATSIT", "categoryOfSignalStationTraffic");
        yield return A(62, "CATSIW", "categoryOfSignalStationWarning");
        yield return A(63, "CATSIL", "categoryOfSiloTank");
        yield return A(67, "CATTSS", "iMOAdopted");
        yield return A(68, "CATVEG", "categoryOfVegetation");
        yield return A(69, "CATWAT", "categoryOfWaterTurbulence");
        yield return A(70, "CATWED", "categoryOfWeedKelp");
        yield return A(103, "JRSDTN", "jurisdiction");
        yield return A(111, "NATION", "nationality");
        yield return A(130, "RYRMGV", "referenceYearForMagneticVariation");
        yield return A(139, "SIGFRQ", "signalFrequency");
        yield return A(140, "SIGGEN", "signalGeneration");
        yield return A(150, "SURATH", "surveyAuthority");
        yield return A(153, "SURTYP", "surveyType");
        yield return A(171, "TOPSHP", "topmarkDaymarkShape");
        yield return A(173, "VALACM", "valueOfAnnualChangeInMagneticVariation");
        yield return A(176, "VALMAG", "valueOfMagneticVariation");
        yield return A(188, "CAT_TS", "categoryOfTidalStream");
    }

    private static S57FeatureRule F(ushort objl, string acronym, string? s101)
        => new() { Objl = objl, S57Acronym = acronym, DefaultS101Code = s101 };

    private static S57AttributeRule A(ushort attl, string acronym, string? s101)
        => new() { Attl = attl, S57Acronym = acronym, DefaultS101Code = s101 };
}
