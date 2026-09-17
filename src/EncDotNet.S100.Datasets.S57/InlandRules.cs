namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Compiled-in seed data for the inland ENC (IENC) object classes and
/// attributes, layered onto <see cref="S57S101Mapping.Default"/> to form the
/// S-401 mapping (<see cref="S57S101Mapping.ForSpec"/>).
/// </summary>
/// <remarks>
/// <para>
/// OBJL / ATTL codes and acronyms are taken from the IEHG Inland ENC Feature
/// Catalogue, Edition 2.4 (corrigendum 2). Target S-401 codes are the S-401
/// Feature Catalogue (Edition 1.3.0) classes and attributes that declare the
/// IENC acronym as an alias, cross-checked against the IEHG "S-57 ENC to S-401
/// Conversion Guidance" (Edition 1.3.0, draft 2).
/// </para>
/// <para>
/// Most inland classes and attributes re-register a standard S-57 acronym in
/// lower case under a new code (e.g. <c>bridge</c> = 17011 for <c>BRIDGE</c> =
/// 11) because IENC adds inland attributes to them. The conversion guidance
/// states that its rules apply to those lower-case elements too, so such a
/// <em>twin</em> reuses the standard rule for its upper-case counterpart,
/// including any redirects, overrides and value remaps. For every twin the
/// standard rule's target agrees with the S-401 alias. Genuinely new inland
/// classes and attributes map to their S-401 alias target. Codes with no
/// S-401 home yet map to <c>null</c> and are reported as rule-dropped.
/// </para>
/// </remarks>
internal static class InlandRules
{
    /// <summary>
    /// The inland feature-class rules, with twins resolved against
    /// <paramref name="standard"/>.
    /// </summary>
    public static IEnumerable<S57FeatureRule> FeatureRules(S57S101Mapping standard)
    {
        ArgumentNullException.ThrowIfNull(standard);

        yield return Twin(standard, 17000, "achbrt", 3); // Anchor berth (twin of ACHBRT)
        yield return Twin(standard, 17001, "achare", 4); // Anchorage area (twin of ACHARE)
        yield return Twin(standard, 17003, "depare", 42); // Depth area (twin of DEPARE)
        yield return Twin(standard, 17004, "dismar", 44); // Distance mark (twin of DISMAR)
        yield return Twin(standard, 17005, "resare", 112); // Restricted area (twin of RESARE)
        yield return Twin(standard, 17007, "sistat", 123); // Signal station, traffic (twin of SISTAT)
        yield return Twin(standard, 17008, "sistaw", 124); // Signal station, warning (twin of SISTAW)
        yield return Twin(standard, 17010, "berths", 10); // Berth (twin of BERTHS)
        yield return Twin(standard, 17011, "bridge", 11); // Bridge (twin of BRIDGE)
        yield return Twin(standard, 17012, "cblohd", 21); // Cable, overhead (twin of CBLOHD)
        yield return Twin(standard, 17013, "feryrt", 53); // Ferry route (twin of FERYRT)
        yield return Twin(standard, 17014, "hrbare", 63); // Harbour area (administrative) (twin of HRBARE)
        yield return Twin(standard, 17015, "hrbfac", 64); // Harbour facility (twin of HRBFAC)
        yield return Twin(standard, 17016, "lokbsn", 79); // Lock basin (twin of LOKBSN)
        yield return Twin(standard, 17017, "rdocal", 104); // Radio calling-in point (twin of RDOCAL)
        yield return Twin(standard, 17018, "m_nsys", 306); // Navigational system of marks (twin of M_NSYS)
        yield return Twin(standard, 17019, "curent", 36); // Current, non-gravitational (twin of CURENT)
        yield return Twin(standard, 17020, "hulkes", 65); // Hulk (twin of HULKES)
        yield return Twin(standard, 17021, "ponton", 95); // Pontoon (twin of PONTON)
        yield return Twin(standard, 17022, "m_sdat", 309); // Sounding datum (twin of M_SDAT)
        yield return Twin(standard, 17023, "m_vdat", 312); // Vertical datum (twin of M_VDAT)
        yield return Twin(standard, 17024, "pipohd", 93); // Pipeline, overhead (twin of PIPOHD)
        yield return Twin(standard, 17025, "flodoc", 57); // Floating dock (twin of FLODOC)
        yield return Twin(standard, 17027, "chkpnt", 28); // Checkpoint (twin of CHKPNT)
        yield return Twin(standard, 17028, "bcnlat", 7); // Beacon, lateral (twin of BCNLAT)
        yield return Twin(standard, 17029, "boylat", 17); // Buoy, lateral (twin of BOYLAT)
        yield return Twin(standard, 17030, "cranes", 35); // Crane (twin of CRANES)
        yield return Twin(standard, 17031, "gatcon", 61); // Gate (twin of GATCON)
        yield return Twin(standard, 17032, "slcons", 122); // Shoreline Construction (twin of SLCONS)
        yield return Twin(standard, 17033, "uwtroc", 153); // Underwater rock / awash rock (twin of UWTROC)
        yield return Twin(standard, 17034, "convyr", 34); // Conveyor (twin of CONVYR)
        yield return Twin(standard, 17035, "daymar", 39); // Daymark (twin of DAYMAR)
        yield return F(17050, "notmrk", "NoticeMark"); // Notice mark
        yield return F(17051, "wtwaxs", "WaterwayAxis"); // Waterway axis
        yield return F(17052, "wtwprf", "WaterwayProfile"); // Waterway profile
        yield return F(17054, "bunsta", "BunkerStation"); // Bunker station
        yield return F(17055, "comare", "CommunicationArea"); // Communication area
        yield return F(17056, "hrbbsn", "HarbourBasin"); // Harbour basin
        yield return F(17058, "lkbspt", "LockBasinPart"); // Lock basin part
        yield return F(17059, "prtare", "PortArea"); // Port area
        yield return F(17062, "refdmp", "RefuseDump"); // Refuse dump
        yield return F(17064, "termnl", "Terminal"); // Terminal
        yield return F(17065, "trnbsn", "TurningBasin"); // Turning basin
        yield return F(17066, "wtware", "WaterwayArea"); // Waterway area
        yield return F(17067, "wtwgag", "WaterwayGauge"); // Waterway gauge
        // tisdge: no S-401 feature class; the translator emits a TimeScheduleInGeneral information record.
        yield return F(17068, "tisdge", null); // Time Schedule - in general
        yield return F(17069, "vehtrf", "VehicleTransfer"); // Vehicle transfer
        yield return F(17070, "excnst", "ExceptionalNavigationStructure"); // Exceptional navigation structure
        yield return F(18001, "lg_sdm", "MaximumPermittedShipDimensions"); // Maximum permitted ship dimensions
        yield return F(18002, "lg_vsp", "MaximumPermittedVesselSpeed"); // Maximum permitted vessel speed
        // c_brga: A collection object; S-401 expresses bridge-arch aggregation as a feature association, not converted yet.
        yield return F(18003, "c_brga", null); // Bridge Arch Aggregation
        yield return F(18004, "sensor", "Sensor"); // Sensor
        // NEWOBJ: NEWOBJ has no fixed meaning; S-401 has no equivalent.
        yield return F(18005, "NEWOBJ", null); // New Object
    }

    /// <summary>
    /// The inland attribute rules, with twins resolved against
    /// <paramref name="standard"/>.
    /// </summary>
    public static IEnumerable<S57AttributeRule> AttributeRules(S57S101Mapping standard)
    {
        ArgumentNullException.ThrowIfNull(standard);

        yield return TwinA(standard, 17000, "catach", 8); // Category of anchorage (twin of CATACH)
        yield return TwinA(standard, 17002, "catsit", 61); // Category of signal station, traffic (twin of CATSIT)
        yield return TwinA(standard, 17003, "catsiw", 62); // Category of signal station, warning (twin of CATSIW)
        yield return TwinA(standard, 17004, "restrn", 131); // Restriction (twin of RESTRN)
        yield return TwinA(standard, 17005, "verdat", 185); // Vertical datum (twin of VERDAT)
        yield return TwinA(standard, 17007, "catfry", 25); // Category of ferry (twin of CATFRY)
        yield return TwinA(standard, 17008, "cathaf", 30); // Category of harbour facility (twin of CATHAF)
        yield return TwinA(standard, 17009, "marsys", 109); // Marks navigational - System of (twin of MARSYS)
        yield return TwinA(standard, 17010, "catchp", 14); // Category of checkpoint (twin of CATCHP)
        yield return TwinA(standard, 17011, "catlam", 36); // Category of lateral mark (twin of CATLAM)
        yield return TwinA(standard, 17012, "catslc", 60); // Category of shoreline construction (twin of CATSLC)
        yield return A(17050, "addmrk", "additionalMark"); // Additional mark
        yield return A(17052, "catnmk", "categoryOfNoticeMark"); // Category of notice mark
        yield return A(17055, "clsdng", "classOfDangerousCargo"); // Class of dangerous cargo
        yield return A(17056, "dirimp", "directionOfImpact"); // Direction of impact
        yield return A(17057, "disbk1", "distanceFromNoticeMarkFirst"); // Distance from notice mark, first
        yield return A(17058, "disbk2", "distanceFromNoticeMarkSecond"); // Distance from notice mark, second
        yield return A(17059, "disipu", "distanceOfImpactUpstream"); // Distance of impact, upstream
        yield return A(17060, "disipd", "distanceOfImpactDownstream"); // Distance of impact, downstream
        yield return A(17061, "eleva1", "elevation1OfSurface"); // Elevation 1 of surface (m)
        yield return A(17062, "eleva2", "elevation2OfSurface"); // Elevation 2 of surface (m)
        yield return A(17063, "fnctnm", "functionOfNoticeMark"); // Function of notice mark
        yield return A(17064, "wtwdis", "waterwayDistance"); // Waterway distance
        yield return A(17065, "bunves", "bunkerVesselAvailability"); // Bunker vessel, availability
        yield return A(17066, "catbrt", "categoryOfBerth"); // Category of berth
        yield return A(17067, "catbun", "categoryOfBunkerStation"); // Category of bunker station
        yield return A(17068, "catccl", "categoryOfCEMTClass"); // Category of CEMT class
        yield return A(17069, "catcom", "categoryOfCommunication"); // Category of communication
        yield return A(17070, "cathbr", "categoryOfHarbourArea"); // Category of harbour area
        yield return A(17071, "catrfd", "categoryOfRefuseDump"); // Category of refuse dump
        // horcll: No S-401 alias; lock basin length is carried in a complex attribute, not converted yet.
        yield return A(17074, "horcll", null); // Horizontal clearance length
        // horclw: S-401 LockBasin does not bind horizontalClearanceWidth directly; lock basin dimensions are carried in a complex attribute, not converted yet.
        yield return A(17075, "horclw", null); // Horizontal clearance width
        yield return A(17076, "trshgd", "transshippingGoods"); // Transshipping goods
        yield return A(17077, "unlocd", "uNLocationCode"); // UN location code
        yield return A(17078, "catgag", "categoryOfWaterwayGauge"); // Category of waterway gauge
        yield return A(17080, "higwat", "valueAtRelevantHighWaterLevel"); // Value at relevant high water level
        yield return A(17081, "hignam", "nameOfRelevantHighWaterLevel"); // Name of relevant high water level
        yield return A(17082, "lowwat", "valueAtRelevantLowWaterLevel"); // Value at relevant low water level
        yield return A(17083, "lownam", "nameOfRelevantLowWaterLevel"); // Name of relevant low water level
        yield return A(17084, "meawat", "valueAtRelevantMeanWaterLevel"); // Value at relevant mean water level
        yield return A(17085, "meanam", "nameOfRelevantMeanWaterLevel"); // Name of relevant mean water level
        yield return A(17086, "othwat", "valueAtOtherLocallyRelevantWaterLevel"); // Value at other locally relevant water level
        yield return A(17087, "othnam", "nameOfOtherLocallyRelevantWaterLevel"); // Name of other locally relevant water level
        yield return A(17088, "reflev", "referenceGravitationalLevel"); // Reference gravitational level
        yield return A(17089, "sdrlev", "nameOfSoundingDatumReferenceLevel"); // Name of Sounding datum reference level
        yield return A(17090, "vcrlev", "nameOfVerticalRiverDatumReferenceLevel"); // Name of vertical river datum reference level
        yield return A(17091, "catvtr", "categoryOfVehicleTransfer"); // Category of vehicle transfer
        // cattab: bound on the S-401 TimeScheduleInGeneral information type, which the translator emits for tisdge.
        yield return A(17092, "cattab", "categoryOfTimeAndBehaviour"); // Category of time and behaviour
        // schref: bound on the S-401 TimeScheduleInGeneral information type, which the translator emits for tisdge.
        yield return A(17093, "schref", "timeScheduleReference"); // Time Schedule Reference
        // useshp: bound on the S-401 TimeScheduleInGeneral information type, which the translator emits for tisdge.
        yield return A(17094, "useshp", "useOfShip"); // Use of Ship
        yield return A(17095, "curvhw", "currentVelocityAtHighWaterLevel"); // Current velocity at high water level
        yield return A(17096, "curvlw", "currentVelocityAtLowWaterLevel"); // Current velocity at low water level
        yield return A(17097, "curvmw", "currentVelocityAtMeanWaterLevel"); // Current velocity at mean water level
        yield return A(17098, "curvow", "currentVelocityAtOtherWaterLevel"); // Current velocity at other water level
        // aptref: bound on the S-401 TimeScheduleInGeneral information type, which the translator emits for tisdge.
        yield return A(17099, "aptref", "averagePassingTimeReference"); // Average Passing Time Reference
        yield return A(17100, "catexs", "categoryOfExceptionalStructure"); // Category of exceptional structure
        yield return TwinA(standard, 17101, "catcbl", 11); // Category of cable (twin of CATCBL)
        yield return TwinA(standard, 17102, "cathlk", 31); // Category of hulk (twin of CATHLK)
        // hunits: the unit of wtwdis (IENC Encoding Guide 2.4.1); conversion guidance §2.1.4 (table 2.3).
        yield return A(17103, "hunits", "distanceUnitOfMeasurement") with { DefaultValueRemap = HunitsRemap }; // Height/length units
        yield return TwinA(standard, 17104, "watlev", 187); // Water level effect (twin of WATLEV)
        yield return A(17105, "bnkwtw", "bankOfTheWaterway"); // Bank of the waterway
        yield return TwinA(standard, 17106, "catrsc", 55); // Category of rescue station (twin of CATRSC)
        yield return A(18001, "lg_spd", "maximalPermittedSpeed"); // Maximal permitted speed
        yield return A(18002, "lg_spr", "speedReference"); // Speed reference
        yield return A(18003, "lg_bme", "maximalPermittedBeam"); // Maximal permitted beam
        yield return A(18004, "lg_lgs", "maximalPermittedLength"); // Maximal permitted length
        yield return A(18005, "lg_drt", "maximalPermittedDraught"); // Maximal permitted draught
        yield return A(18006, "lg_wdp", "maximalPermittedWaterDisplacement"); // Maximal permitted water displacement
        yield return A(18007, "lg_wdu", "waterDisplacementUnit"); // Water displacement unit
        yield return A(18008, "lg_rel", "relatedIssue"); // Related issue
        yield return A(18010, "lg_des", "descriptionOfLegalConditions"); // Description of legal conditions
        yield return A(18011, "lg_pbr", "publicationReference"); // Publication reference
        // lc_csi: No S-401 alias; ship-category ranges belong to the maximum-permitted-dimensions complex, not converted yet.
        yield return A(18012, "lc_csi", null); // Category of ship (including)
        yield return A(18013, "lc_cse", "categoryOfShipExcluding"); // Category of ship (excluding)
        yield return A(18014, "lc_asi", "assembliesOfShipIncluding"); // Assemblies of ship (including)
        yield return A(18015, "lc_ase", "assembliesOfShipExcluding"); // Assemblies of ship (excluding)
        yield return A(18016, "lc_cci", "categoryOfCargoIncluding"); // Category of cargo (including)
        yield return A(18017, "lc_cce", "categoryOfCargoExcluding"); // Category of cargo (excluding)
        yield return A(18018, "refgag", "referenceGauge"); // Reference Gauge
        yield return A(18019, "catsen", "categoryOfSensor"); // Category of sensor
        yield return A(18020, "fnctsn", "functionOfSensor"); // Function of sensor
        // CLSDEF: NEWOBJ definition; no S-401 equivalent.
        yield return A(18027, "CLSDEF", null); // Object class definition
        // CLSNAM: NEWOBJ name; no S-401 equivalent.
        yield return A(18028, "CLSNAM", null); // Object class name
        // SYMINS: NEWOBJ symbol instruction; no S-401 equivalent.
        yield return A(18029, "SYMINS", null); // Symbol instruction
        // catfrq: categoryOfFrequency is a sub-attribute of an S-401 complex attribute (shore power); not assembled yet.
        yield return A(18030, "catfrq", null); // Category of frequency
        // catvol: categoryOfVoltage is a sub-attribute of an S-401 complex attribute (shore power); not assembled yet.
        yield return A(18031, "catvol", null); // Category of voltage
        // amoamp: No S-401 alias.
        yield return A(18032, "amoamp", null); // Amount of amperage
        // allcon: allowedConsumption is a sub-attribute of an S-401 complex attribute (shore power); not assembled yet.
        yield return A(18033, "allcon", null); // Allowed consumption
        // catplg: categoryOfPlug is a sub-attribute of an S-401 complex attribute (shore power); not assembled yet.
        yield return A(18034, "catplg", null); // Category of plug
        // shrnum: numberOfShoreConnectors is a sub-attribute of an S-401 complex attribute (shore power); not assembled yet.
        yield return A(18035, "shrnum", null); // Number of shore connectors
        // shptyp: bound on the S-401 TimeScheduleInGeneral information type, which the translator emits for tisdge.
        yield return A(33066, "shptyp", "typeOfShip"); // Type of Ship
    }

    // hunits → distanceUnitOfMeasurement (conversion guidance §2.1.4, table 2.3):
    // metres and kilometres keep their code; hectometres, statute miles and
    // nautical miles are renumbered; feet has no S-401 equivalent.
    private static readonly IReadOnlyDictionary<string, string?> HunitsRemap = new Dictionary<string, string?>
    {
        ["2"] = null, // feet
        ["4"] = "7", // hectometres
        ["5"] = "4", // statute miles
        ["6"] = "5", // nautical miles
    };

    private static S57FeatureRule F(ushort objl, string acronym, string? s401)
        => new() { Objl = objl, S57Acronym = acronym, DefaultS101Code = s401 };

    private static S57AttributeRule A(ushort attl, string acronym, string? s401)
        => new() { Attl = attl, S57Acronym = acronym, DefaultS101Code = s401 };

    private static S57FeatureRule Twin(S57S101Mapping standard, ushort objl, string acronym, ushort standardObjl)
        => standard.FeatureRules[standardObjl] with { Objl = objl, S57Acronym = acronym };

    private static S57AttributeRule TwinA(S57S101Mapping standard, ushort attl, string acronym, ushort standardAttl)
        => standard.AttributeRules[standardAttl] with { Attl = attl, S57Acronym = acronym };
}
