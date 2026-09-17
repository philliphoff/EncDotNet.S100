using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Translates a parsed S-57 <see cref="EncDotNet.S57.S57Document"/> (from the
/// <c>EncDotNet.S57</c> NuGet package) into the S-101 in-memory document model
/// so that the existing S-101 portrayal pipeline can drive rendering of S-57
/// ENC data.
/// </summary>
/// <remarks>
/// <para>
/// The translation is intentionally lossy and breadth-first:
/// <list type="bullet">
///   <item>S-57 object/attribute numeric codes are remapped to S-101 Feature
///   Catalogue acronyms via <see cref="S57S101Mapping"/>. Unmapped feature
///   classes are skipped.</item>
///   <item>S-57 isolated / connected nodes become S-101 Point records.</item>
///   <item>S-57 edges become S-101 Curve Segment records with begin / end
///   point associations and intermediate coordinates.</item>
///   <item>S-57 area features have their FSPT edge ring wrapped into a
///   single composite curve and referenced from a synthesised S-101 Surface
///   record.</item>
///   <item>S-57 multi-point soundings (<c>SOUNDG</c>) are translated into a single
///   S-101 <c>Sounding</c> feature backed by a multi-point spatial record
///   (RCNM = 115); the depth values live on the points within that record and
///   are read by the <c>SOUNDG03</c> portrayal rule as <c>point.ScaledZ</c>.</item>
///   <item>Listed-value remap (S-57 enum codes to S-101 enum codes) is not
///   yet performed; string attribute values pass through unchanged.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class S57ToS101Translator
{
    private const byte S101RcnmPoint = 110;
    private const byte S101RcnmMultiPoint = 115;
    private const byte S101RcnmCurveSegment = 120;
    private const byte S101RcnmCompositeCurve = 125;
    private const byte S101RcnmSurface = 130;

    private const byte OrientationForward = 1;
    private const byte OrientationReverse = 2;
    private const byte UsageExterior = 1;
    private const byte UsageInterior = 2;
    private const byte TopologyBegin = 1;
    private const byte TopologyEnd = 2;

    private const ushort SoundingObjl = 129;
    private const string SoundingS101Code = "Sounding";
    private const ushort LightsObjl = 75;  // LIGHTS (S-57 object class)

    // M_COVR (S-57 meta object, OBJL 302) with CATCOV = "no coverage
    // available" (2) has no S-101 equivalent: S-101 represents the absence of
    // data by the absence of a DataCoverage feature (the S-101 FC's
    // DataCoverage carries no categoryOfCoverage attribute). See the M_COVR
    // filter in the feature loop for why it must be dropped.
    private const ushort MCovrObjl = 302;
    private const string CatcovAcronym = "CATCOV";
    private const string CatcovNoCoverage = "2";

    // ── S-57 textual-info attribute codes (S-57 Appendix A Chapter 2) ──
    // Per IHO S-57→S-101 Conversion Guidance §2.3, these four attributes
    // do NOT pass through as simple S-101 attributes; instead they are
    // grouped into one or more instances of the S-101 `information`
    // complex attribute on the feature itself, with sub-attributes
    // text / fileReference / language as appropriate.
    private const ushort S57AttrInform = 102;   // INFORM  — free text (Eng.)
    private const ushort S57AttrTxtdsc = 158;   // TXTDSC  — text-file ref (Eng.)
    private const ushort S57AttrNinfom = 300;   // NINFOM  — free text (national)
    private const ushort S57AttrNtxtds = 304;   // NTXTDS  — text-file ref (national)

    // ── S-57 object-name attribute codes (S-57 Appendix A Chapter 2) ──
    // Per IHO S-57→S-101 Conversion Guidance, OBJNAM/NOBJNM do NOT pass
    // through as a simple attribute; they become one or more instances of
    // the S-101 `featureName` complex attribute (sub-attributes name /
    // language [/ nameUsage]). The FC declares OBJNAM and NOBJNM as the two
    // aliases of the `name` sub-attribute, mirroring the INFORM/NINFOM split.
    private const ushort S57AttrObjnam = 116;   // OBJNAM  — object name (Eng.)
    private const ushort S57AttrNobjnm = 301;   // NOBJNM  — object name (national)

    // S-101 attribute codes for the `information` complex attribute and
    // its sub-attributes (verified against the bundled FC).
    private const string S101AttrInformation = "information";
    private const string S101AttrText = "text";
    private const string S101AttrFileReference = "fileReference";
    private const string S101AttrLanguage = "language";

    // ── S-101 NauticalInformation info-type / association / role names ──
    // Per IHO S-57→S-101 Conversion Guidance §2.3 "fuller path": instead of
    // the inline `information` complex shortcut, the textual attributes
    // (INFORM/TXTDSC/NINFOM/NTXTDS) are carried by a standalone
    // `NauticalInformation` information type bound to the feature through an
    // `AdditionalInformation` information association whose single role is
    // `theInformation` (S-101 FC Ed 1.x; the association declares only the
    // information-end role). Resolved by name via the document catalogues by
    // the S-100 Part 9A portrayal (see `ProcessNauticalInformation`).
    private const string S101InfoTypeNauticalInformation = "NauticalInformation";
    private const string S101AssocAdditionalInformation = "AdditionalInformation";
    private const string S101RoleTheInformation = "theInformation";

    // ── S-57 collection object + S-101 RangeSystemAggregation names ─────
    // S-57 has generic collection objects C_AGGR (aggregation) / C_ASSO
    // (association); S-101 replaced them with a fixed set of purpose-specific
    // feature associations, each bound to a dedicated collection feature class.
    // A C_AGGR whose members are navigational tracks plus the navigation aids
    // that define them maps to a synthesised, geometry-less `RangeSystem`
    // feature (the `theCollection` head per the S-101 FC) linked to each member
    // by a `RangeSystemAggregation` association in the member's `theComponent`
    // role (S-101 FC Ed 1.x; RangeSystemAggregation def: "binding between
    // navigational tracks and the navigational aids that define the tracks").
    // C_ASSO has no generic S-101 home (its NOAA use is mostly land-area
    // partitions) and is intentionally not mapped.
    private const ushort CAggrObjl = 400;       // C_AGGR (S-57 Appendix A)
    private const string S101ClassRangeSystem = "RangeSystem";
    private const string S101AssocRangeSystemAggregation = "RangeSystemAggregation";
    private const string S101RoleTheComponent = "theComponent";

    // S-101 feature classes the FC permits as `theComponent` of a
    // `RangeSystemAggregation` (extracted from the bundled FeatureCatalogue.xml
    // feature bindings). A C_AGGR maps to a RangeSystem only when every member
    // resolves to one of these classes (or to another qualifying RangeSystem)
    // and at least one member is a navigational track.
    private static readonly HashSet<string> RangeSystemComponentClasses = new(StringComparer.Ordinal)
    {
        "Building", "CardinalBeacon", "Daymark", "Dolphin", "FortifiedStructure",
        "IsolatedDangerBeacon", "Landmark", "LateralBeacon", "LightAllAround",
        "LightSectored", "NavigationLine", "Pile", "RadarTransponderBeacon",
        "RangeSystem", "RecommendedRouteCentreline", "RecommendedTrack",
        "SafeWaterBeacon", "SiloTank", "SpecialPurposeGeneralBeacon",
    };

    // The subset of RangeSystem component classes that are navigational tracks;
    // a qualifying range system must contain at least one so that arbitrary
    // navaid clusters (e.g. two beacons) are not misclassified as range systems.
    private static readonly HashSet<string> RangeSystemTrackClasses = new(StringComparer.Ordinal)
    {
        "NavigationLine", "RecommendedTrack", "RecommendedRouteCentreline",
    };

    // S-101 attribute codes for the `featureName` complex attribute and its
    // sub-attributes (verified against the bundled FC; `name` is [1..1],
    // `language` is [1..1], `nameUsage` is [0..1] and has no S-57 source).
    private const string S101AttrFeatureName = "featureName";
    private const string S101AttrName = "name";

    // ── S-57 light-characteristic attribute codes (S-57 Appendix A) ──
    // On light features these do NOT pass through as simple attributes;
    // they become sub-attributes of the S-101 `rhythmOfLight` complex
    // attribute. LITCHR is the mandatory `lightCharacteristic` [1..1];
    // SIGGRP/SIGPER are the optional `signalGroup`/`signalPeriod`. SIGSEQ is
    // assembled as a nested `signalSequence` sub-complex; sector lights
    // (SECTR1/SECTR2) redirect to LightSectored with `sectorCharacteristics`.
    private const ushort S57AttrLitchr = 107;   // LITCHR  — light characteristic
    private const ushort S57AttrSiggrp = 141;   // SIGGRP  — signal group
    private const ushort S57AttrSigper = 142;   // SIGPER  — signal period

    // S-101 attribute codes for the `rhythmOfLight` complex attribute and
    // its (first-level) sub-attributes (verified against the bundled FC:
    // `lightCharacteristic` [1..1], `signalGroup` [0..*], `signalPeriod`
    // [0..1]).
    private const string S101AttrRhythmOfLight = "rhythmOfLight";
    private const string S101AttrLightCharacteristic = "lightCharacteristic";
    private const string S101AttrSignalGroup = "signalGroup";
    private const string S101AttrSignalPeriod = "signalPeriod";

    // S-57 SIGSEQ (Signal sequence, ATTL 143) maps to the S-101
    // `signalSequence` complex attribute. The S-57 value (S-57 Appendix B.1)
    // is a '+'-separated list of phase durations in seconds; a duration in
    // parentheses denotes an eclipse / silence phase. Each phase becomes one
    // `signalSequence` instance carrying `signalDuration` [1..1] (real,
    // seconds) and `signalStatus` [1..1] (1 = Lit/Sound for a bare duration,
    // 2 = Eclipsed/Silent for a parenthesised duration). On the light feature
    // classes that bind `rhythmOfLight` the sequence nests inside that complex
    // (it is the last sub-attribute in the FC's binding order); on FogSignal /
    // RadarTransponderBeacon `signalSequence` is bound at the top level.
    private const ushort S57AttrSigseq = 143;  // SIGSEQ — signal sequence
    private const string S101AttrSignalSequence = "signalSequence";
    private const string S101AttrSignalDuration = "signalDuration";
    private const string S101AttrSignalStatus = "signalStatus";
    private const string SignalStatusLit = "1";        // Lit / Sound
    private const string SignalStatusEclipsed = "2";   // Eclipsed / Silent

    // ── S-57 date-range attribute codes (S-57 Appendix A) ──
    // These pairs are not simple pass-through attributes; each pair becomes a
    // distinct S-101 date-range *complex* attribute whose `dateStart`/`dateEnd`
    // sub-attributes are of type S100_TruncatedDate. The destination complex is
    // determined by the S-57 pair, and is only emitted on a feature class that
    // actually binds it (see S101FeatureAttributeBindings), since the shared
    // sub-attributes would otherwise be non-conformant:
    //   DATSTA/DATEND → fixedDateRange     (dateStart [0..1], dateEnd [0..1])
    //   PERSTA/PEREND → periodicDateRange  (dateStart [1..1], dateEnd [1..1])
    //   SURSTA/SUREND → surveyDateRange    (dateStart [0..1], dateEnd [1..1])
    private const ushort S57AttrDatsta = 86;   // DATSTA — date start
    private const ushort S57AttrDatend = 85;   // DATEND — date end
    private const ushort S57AttrPersta = 119;  // PERSTA — periodic date start
    private const ushort S57AttrPerend = 118;  // PEREND — periodic date end
    private const ushort S57AttrSursta = 152;  // SURSTA — survey date start
    private const ushort S57AttrSurend = 151;  // SUREND — survey date end

    // S-101 date-range complex attribute codes and their shared sub-attribute
    // codes (verified against the bundled FC).
    private const string S101AttrFixedDateRange = "fixedDateRange";
    private const string S101AttrPeriodicDateRange = "periodicDateRange";
    private const string S101AttrSurveyDateRange = "surveyDateRange";
    private const string S101AttrDateStart = "dateStart";
    private const string S101AttrDateEnd = "dateEnd";

    // S-57 CATZOC (Category of zone of confidence in data, ATTL 72) maps to the
    // S-101 `zoneOfConfidence` complex attribute's `categoryOfZoneOfConfidenceInData`
    // sub-attribute (S-101 Conversion Guidance; verified against the
    // bundled FC). CATZOC is only bound to S-57 M_QUAL, which translates to the
    // S-101 QualityOfBathymetricData feature — the sole feature class binding
    // `zoneOfConfidence`. The enumeration values are identical in
    // S-57 and S-101 (1=A1, 2=A2, 3=B, 4=C, 5=D, 6=U), so no remapping is
    // needed; an out-of-range code drops the instance.
    //
    // The complex's `horizontalPositionUncertainty` and `verticalUncertainty`
    // sub-attributes are populated from the CATZOC-implied accuracy values of
    // the IHO CATZOC table (IHO S-4 §B-290 / S-57 Appendix A CATZOC), because
    // NOAA ENCs almost never carry POSACC/SOUACC on M_QUAL to derive them from
    // directly. Each is itself a complex of `uncertaintyFixed` [1..1] (the
    // fixed metre term `a`) and `uncertaintyVariableFactor` [0..1] (the
    // percentage-of-depth term `b`, for the `a + b% × d` accuracy model):
    //
    //   ZOC  horizontal (pos accuracy)   vertical (depth accuracy)
    //   A1   ±5 m + 5% depth             0.50 m + 1% depth
    //   A2   ±20 m                       1.00 m + 2% depth
    //   B    ±50 m                       1.00 m + 2% depth
    //   C    ±500 m                      2.00 m + 5% depth
    //   D    worse than C (unquantified) worse than C (unquantified)
    //   U    unassessed                  unassessed
    //
    // D and U have no quantified accuracy, so only `categoryOfZoneOfConfidenceInData`
    // is emitted for them. `fixedDateRange` has no CATZOC-side source and is
    // always left unpopulated.
    private const ushort S57AttrCatzoc = 72;   // CATZOC — category of ZOC in data
    private const string S101AttrZoneOfConfidence = "zoneOfConfidence";
    private const string S101AttrCategoryOfZocInData = "categoryOfZoneOfConfidenceInData";
    private const string S101AttrHorizontalPositionUncertainty = "horizontalPositionUncertainty";
    private const string S101AttrVerticalUncertainty = "verticalUncertainty";
    private const string S101AttrUncertaintyFixed = "uncertaintyFixed";
    private const string S101AttrUncertaintyVariableFactor = "uncertaintyVariableFactor";

    // CATZOC-implied uncertainty values keyed by the CATZOC / S-101
    // categoryOfZoneOfConfidenceInData enumerate code (1=A1 … 4=C). Codes 5 (D)
    // and 6 (U) are intentionally absent: their accuracy is unquantified /
    // unassessed. Values are the fixed metre term and (optional) percentage
    // term from the IHO CATZOC table above.
    private readonly record struct ZocUncertainty(
        string HorizontalFixed,
        string? HorizontalVariable,
        string VerticalFixed,
        string VerticalVariable);

    private static readonly IReadOnlyDictionary<string, ZocUncertainty> CatzocUncertainties =
        new Dictionary<string, ZocUncertainty>
        {
            ["1"] = new("5", "5", "0.5", "1"),   // A1
            ["2"] = new("20", null, "1", "2"),   // A2
            ["3"] = new("50", null, "1", "2"),   // B
            ["4"] = new("500", null, "2", "5"),  // C
        };

    // S-57 NATSUR (Nature of surface, ATTL 113) and NATQUA (Nature of surface,
    // qualifying terms, ATTL 114) — both list-valued — map onto the S-101
    // `surfaceCharacteristics` complex attribute's `natureOfSurface` and
    // `natureOfSurfaceQualifyingTerms` sub-attributes (S-101 Conversion
    // Guidance; verified against the bundled FC). `surfaceCharacteristics` is
    // bound only to SeabedArea (SBDARE), which — unlike Coastline, LandRegion,
    // etc. — does NOT bind a top-level `natureOfSurface`, so on SeabedArea the
    // NATSUR value has no conformant home except inside the complex. The two
    // S-57 lists are paired positionally into one repeating complex instance
    // per position: `natureOfSurface` [0..1] and `natureOfSurfaceQualifyingTerms`
    // [0..3] are both optional, so positions with only one of the two still
    // form a valid instance.
    private const ushort S57AttrNatsur = 113;  // NATSUR — nature of surface
    private const ushort S57AttrNatqua = 114;  // NATQUA — nature of surface, qualifying terms
    private const string S101AttrSurfaceCharacteristics = "surfaceCharacteristics";
    private const string S101AttrNatureOfSurface = "natureOfSurface";
    private const string S101AttrNatureOfSurfaceQualifyingTerms = "natureOfSurfaceQualifyingTerms";

    // S-57 sector-light geometry. A LIGHTS object carrying a sector arc
    // (SECTR1/SECTR2 present) is redirected to the S-101 LightSectored feature
    // (see DefaultRules), whose mandatory `sectorCharacteristics` [1..*] complex
    // the translator assembles here. Because each S-57 LIGHTS object encodes a
    // single sector, one LightSectored feature is emitted per S-57 sector-light,
    // carrying one `lightSector` (conformant, since `lightSector` is [1..*]);
    // co-located sectors of one physical light remain distinct features (S-57
    // encodes them as separate objects and the translator is one-to-one).
    // Nesting (verified against the bundled FC):
    //   sectorCharacteristics → lightCharacteristic [1..1], lightSector [1..*],
    //                           signalGroup [0..*], signalPeriod [0..1],
    //                           signalSequence [0..*]
    //   lightSector           → colour [1..*], lightVisibility [0..*],
    //                           sectorLimit [0..1], valueOfNominalRange [0..1], …
    //   sectorLimit           → sectorLimitOne [1..1], sectorLimitTwo [1..1]
    //   sectorLimitOne/Two    → sectorBearing [1..1], sectorLineLength [0..1]
    // S-57 → S-101 feeds: LITCHR→lightCharacteristic, SIGGRP→signalGroup,
    // SIGPER→signalPeriod, SIGSEQ→signalSequence, COLOUR→colour (list),
    // LITVIS→lightVisibility (list), VALNMR→valueOfNominalRange,
    // SECTR1→sectorLimitOne.sectorBearing, SECTR2→sectorLimitTwo.sectorBearing.
    private const ushort S57AttrSectr1 = 136;  // SECTR1 — sector limit one (bearing)
    private const ushort S57AttrSectr2 = 137;  // SECTR2 — sector limit two (bearing)
    private const ushort S57AttrColour = 75;   // COLOUR — colour (list)
    private const ushort S57AttrValnmr = 178;  // VALNMR — value of nominal range
    private const ushort S57AttrLitvis = 108;  // LITVIS — light visibility (list)
    private const string S101AttrSectorCharacteristics = "sectorCharacteristics";
    private const string S101AttrLightSector = "lightSector";
    private const string S101AttrSectorLimit = "sectorLimit";
    private const string S101AttrSectorLimitOne = "sectorLimitOne";
    private const string S101AttrSectorLimitTwo = "sectorLimitTwo";
    private const string S101AttrSectorBearing = "sectorBearing";
    private const string S101AttrColour = "colour";
    private const string S101AttrValueOfNominalRange = "valueOfNominalRange";
    private const string S101AttrLightVisibility = "lightVisibility";

    // S-57 TOPMAR (Topmark/daymark, OBJL 144) is a standalone object in S-57 but
    // in S-101 the topmark is modelled as the `topmark` complex attribute (alias
    // TOPMAR) carried by the parent buoy/beacon/light-float feature, reached via
    // the S-57 master/slave feature-to-feature relationship (the master structure
    // carries an FFPT to the TOPMAR slave). The complex binds `colour` [0..*]
    // (COLOUR), `colourPattern` [0..1] (COLPAT) and the mandatory
    // `topmarkDaymarkShape` [1..1] (TOPSHP); all three are straight code aliases,
    // so the S-57 values pass through unchanged (validated against the S-101
    // enumeration). A TOPMAR consumed this way emits no feature of its own.
    // (IHO S-57→S-101 Conversion Guidance: TOPMAR as parent attribute.)
    private const ushort TopmarObjl = 144;      // TOPMAR (S-57 object class)
    private const ushort S57AttrTopshp = 171;   // TOPSHP — topmark/daymark shape
    private const ushort S57AttrColpat = 76;    // COLPAT — colour pattern (single value)
    private const string S101AttrTopmark = "topmark";
    private const string S101AttrTopmarkDaymarkShape = "topmarkDaymarkShape";
    private const string S101AttrColourPattern = "colourPattern";

    // S-57 HORCLR (Horizontal clearance, ATTL 98) maps to the mandatory
    // `horizontalClearanceValue` [1..1] sub-attribute of one of two S-101
    // complex attributes (S-101 Conversion Guidance; verified against the
    // bundled FC): `horizontalClearanceOpen` — bound to Gate — or
    // `horizontalClearanceFixed` — bound to SpanFixed, SpanOpening, Tunnel,
    // ShorelineConstruction, StructureOverNavigableWater, Canal, DockArea and
    // LockBasin. Both complexes share the same two sub-attributes:
    // `horizontalClearanceValue` (real, [1..1]) and
    // `horizontalDistanceUncertainty` (real, [0..1]); S-57 carries no
    // per-clearance uncertainty source, so only the value is populated. The
    // correct complex is chosen per resolved feature by its FC binding; a
    // feature that binds neither has no conformant home for HORCLR, which then
    // falls through and is recorded unmapped. (On Bridge — which binds neither
    // — HORCLR is instead carried by the decomposed span; see below.)
    private const ushort S57AttrHorclr = 98;   // HORCLR — horizontal clearance
    private const string S101AttrHorizontalClearanceOpen = "horizontalClearanceOpen";
    private const string S101AttrHorizontalClearanceFixed = "horizontalClearanceFixed";
    private const string S101AttrHorizontalClearanceValue = "horizontalClearanceValue";

    // ── Class-bound simple attributes ───────────────────────────────────
    // Simple attributes that only mean something on the feature classes that
    // bind them, so they are dropped (and recorded as rule-dropped) elsewhere
    // rather than emitted unbound. Only the S-401 (inland) mapping targets
    // them: `distanceUnitOfMeasurement` (from IENC hunits) qualifies
    // `waterwayDistance`, and S-401 binds it exactly where it binds that;
    // the time-schedule attributes bind only on `TimeScheduleInGeneral`; the
    // usable lock / dock dimensions (IENC horcll / horclw) bind on the dock
    // and lock classes only.
    //
    // S-401 LockBasin binds `horizontalClearanceLength` but not
    // `horizontalClearanceWidth`; the IEHG S-57 ENC to S-401 Conversion
    // Guidance (clause 3.78) carries its width (horclw) in the
    // `horizontalClearanceFixed` complex instead. There horclw feeds the same
    // complex HORCLR does, and HORCLR takes precedence when both are present.
    private const string S101AttrHorizontalClearanceLength = "horizontalClearanceLength";
    private const string S101AttrHorizontalClearanceWidth = "horizontalClearanceWidth";
    private static readonly HashSet<string> ClassBoundSimpleAttributes = new(StringComparer.Ordinal)
    {
        "distanceUnitOfMeasurement",
        "categoryOfTimeAndBehaviour",
        "timeScheduleReference",
        "averagePassingTimeReference",
        "typeOfShip",
        "useOfShip",
        S101AttrHorizontalClearanceLength,
        S101AttrHorizontalClearanceWidth,
    };

    // ── IENC tisdge → S-401 TimeScheduleInGeneral ───────────────────────
    // An IENC time schedule (`tisdge`, IENC Encoding Guide 2.4.1 T.1.1) has no
    // geometry and is "always associated with respective geo object", through
    // a C_ASSO collection or a direct feature pointer. S-401 models it as the
    // `TimeScheduleInGeneral` information type, which a feature reaches
    // through `AdditionalInformation` (the conversion guidance has no tisdge
    // clause). Each tisdge becomes one such record, emitted with the first
    // linked feature whose class binds it, and every linked feature of such a
    // class gets an association to it. S-401 caps `AdditionalInformation` at
    // one per feature, but IENC encodes one tisdge per ship type or period
    // (and INFORM already uses the association), so every link is emitted
    // even where that exceeds the cap. A link to a class that cannot carry a
    // time schedule (e.g. Bridge, which only takes ServiceHours) is skipped; a
    // tisdge with no emitted link is recorded as a rule-dropped object. A
    // C_ASSO that links a tisdge is consumed by this; any other C_ASSO is
    // unaffected. A tisdge is recognised by its rule's acronym, so only a
    // mapping that registers it (the S-401 one) takes this path.
    private const string TimeScheduleAcronym = "tisdge";
    private const ushort CAssoObjl = 401;       // C_ASSO (S-57 Appendix A)
    private const string S101InfoTypeTimeSchedule = "TimeScheduleInGeneral";

    // ── IENC shore power → S-401 powerCharacteristics ───────────────────
    // The IENC power supply attributes of `bunsta` (catvol, catfrq, amoamp,
    // catplg, shrnum, allcon) are sub-attributes of the S-401
    // `powerCharacteristics` complex (IEHG S-57 ENC to S-401 Conversion
    // Guidance, clause 3.10), which only BunkerStation binds. They carry no
    // S-57 code here: the translator recognises them by the sub-attribute
    // their mapping rule targets, so only a mapping that targets them (the
    // S-401 one) takes this path. catvol and catfrq are IENC lists while
    // each sub-attribute is single-valued, so one instance is emitted per
    // voltage and frequency combination, each repeating the station-wide
    // values. On a class that does not bind the complex the attributes are
    // recorded as rule-dropped.
    private const string S101AttrPowerCharacteristics = "powerCharacteristics";
    private const string S101AttrCategoryOfVoltage = "categoryOfVoltage";
    private const string S101AttrCategoryOfFrequency = "categoryOfFrequency";

    // The sub-attributes in Feature Catalogue order.
    private static readonly string[] PowerCharacteristicsSubAttributes =
    [
        S101AttrCategoryOfVoltage,
        S101AttrCategoryOfFrequency,
        "amountOfAmperage",
        "categoryOfPlug",
        "numberOfShoreConnectors",
        "allowedConsumption",
    ];

    // ── S-57 BRIDGE → S-101 Bridge + SpanFixed / SpanOpening ────────────
    // S-65 Annex B (S-57 ENC to S-101 Conversion Guidance, Ed 1.2.0) clause
    // 4.8.10; S-101 DCEG 6.6–6.8 and 25.4. A BRIDGE over navigable water
    // becomes a `Bridge` plus a `SpanFixed` (non-opening) or `SpanOpening`
    // (CATBRG contains 2, 3, 4, 5 or 7) component sharing its geometry, the
    // two linked by a `BridgeAggregation` association carried by the Bridge in
    // the span's `theComponent` role (the same head-owned convention as
    // RangeSystemAggregation). The clearance attributes bind only on the spans
    // (the bundled FC's Bridge binds none of them), so they are diverted there:
    //   VERCLR → SpanFixed.verticalClearanceFixed.verticalClearanceValue
    //   VERCCL → SpanOpening.verticalClearanceClosed.verticalClearanceValue
    //   VERCOP → SpanOpening.verticalClearanceOpen.verticalClearanceValue
    //            (absent → verticalClearanceUnlimited = true; present, even
    //            with an empty value → verticalClearanceUnlimited = false)
    //   HORCLR → span horizontalClearanceFixed.horizontalClearanceValue
    //   HORACC → span horizontalClearanceFixed.horizontalDistanceUncertainty (§2.2.4.2)
    //   VERACC → span vertical clearance verticalUncertainty.uncertaintyFixed (§2.2.4.3)
    //   VERDAT → span verticalDatum (§2.1.2)
    // SCAMIN and DATSTA/DATEND are copied onto the span as well so that it is
    // shown / date-filtered together with its Bridge.
    //
    // "Over navigable water" is not decidable from a single S-57 object, so
    // the translator uses the attribute the span class makes mandatory: a
    // non-opening bridge yields a SpanFixed only when it carries VERCLR
    // (verticalClearanceFixed [1..1]); an opening bridge yields a SpanOpening
    // only when it carries VERCCL (verticalClearanceClosed [1..1]). The
    // attribute's presence is what counts: an S-57 empty (unknown) value
    // still yields the span, with the mandatory verticalClearanceValue
    // populated as empty (null), the S-65 Annex B convention for mandatory
    // attributes without a source value. A bridge without that clearance is
    // treated as not crossing navigable water and converts to a Bridge alone;
    // any clearance values it does carry have no conformant home and are
    // recorded as rule-dropped.
    //
    // A BRIDGE of point primitive converts to `Landmark` (Bridge permits no
    // point geometry) via the DefaultRules redirect; the translator supplies
    // the mandatory categoryOfLandmark = 26 (bridge) and, when CONVIS is
    // absent, visualProminence = 2 (not visually conspicuous) — the defaults
    // S-65 Annex B gives for the analogous point DAMCON → Landmark conversion
    // (clauses 4.8.5 / 4.8.15).
    //
    // A C_AGGR whose members are BRIDGE (curve/surface) objects plus any
    // PYLONS / PONTON objects becomes a single Bridge whose BridgeAggregation
    // links every emitted span, PylonBridgeSupport and Pontoon; the member
    // BRIDGE objects emit only their spans. See BuildBridgeAggregations.
    private const ushort BridgeObjl = 11;          // BRIDGE (S-57 object class)
    private const string BridgeAcronym = "BRIDGE";
    private const ushort S57AttrHoracc = 97;       // HORACC — horizontal accuracy
    private const ushort S57AttrVeracc = 180;      // VERACC — vertical accuracy
    private const ushort S57AttrVerclr = 181;      // VERCLR — vertical clearance
    private const ushort S57AttrVerccl = 182;      // VERCCL — vertical clearance, closed
    private const ushort S57AttrVercop = 183;      // VERCOP — vertical clearance, open
    private const ushort S57AttrVerdat = 185;      // VERDAT — vertical datum
    private const ushort S57AttrScamin = 133;      // SCAMIN — scale minimum
    private const string S101ClassBridge = "Bridge";
    private const string S101ClassSpanFixed = "SpanFixed";
    private const string S101ClassSpanOpening = "SpanOpening";
    private const string S101ClassLandmark = "Landmark";
    private const string S101AssocBridgeAggregation = "BridgeAggregation";
    private const string S101AttrVerticalClearanceFixed = "verticalClearanceFixed";
    private const string S101AttrVerticalClearanceClosed = "verticalClearanceClosed";
    private const string S101AttrVerticalClearanceOpen = "verticalClearanceOpen";
    private const string S101AttrVerticalClearanceValue = "verticalClearanceValue";
    private const string S101AttrVerticalClearanceUnlimited = "verticalClearanceUnlimited";
    private const string S101AttrVerticalClearanceSafe = "verticalClearanceSafe";
    private const string S101AttrHorizontalDistanceUncertainty = "horizontalDistanceUncertainty";
    private const string S101AttrVerticalDatum = "verticalDatum";
    private const string S101AttrScaleMinimum = "scaleMinimum";
    private const string S101AttrCategoryOfLandmark = "categoryOfLandmark";
    private const string S101AttrVisualProminence = "visualProminence";
    private const string CategoryOfLandmarkBridge = "26";
    private const string VisualProminenceNotConspicuous = "2";

    // ── Vertical clearances on features other than bridge spans ─────────
    // S-65 Annex B (Ed 1.2.0) §2.2.4.3 and the IEHG S-57 ENC to S-401
    // Conversion Guidance (Ed 1.3.0 draft 2) clauses 3.13 (Cable Overhead),
    // 3.26 (Conveyor), 3.27 (Crane) and 3.55 (Gate). Every S-101/S-401
    // vertical clearance is a complex whose `verticalClearanceValue` carries
    // the S-57 value, so the attribute rules target the complex (VERCLR →
    // verticalClearanceFixed, VERCCL → verticalClearanceClosed, VERCOP →
    // verticalClearanceOpen, VERCSA → verticalClearanceSafe; the S-401 Gate
    // rule sends VERCLR to verticalClearanceOpen) and the translator builds
    // the instance on a feature class that binds it. VERACC nests in each
    // instance with a known value as verticalUncertainty.uncertaintyFixed. A
    // clearance whose complex the feature does not bind is rule-dropped.
    private static readonly HashSet<string> VerticalClearanceComplexes = new(StringComparer.Ordinal)
    {
        S101AttrVerticalClearanceFixed,
        S101AttrVerticalClearanceClosed,
        S101AttrVerticalClearanceOpen,
        S101AttrVerticalClearanceSafe,
    };

    // CATBRG values classed as opening bridges in S-101 (opening bridge,
    // swing, lifting, bascule, draw). Pontoon (6) is opening only when
    // combined with 2 (S-65 Annex B §4.8.10).
    private static readonly HashSet<string> OpeningBridgeCategories = new(StringComparer.Ordinal)
    {
        "2", "3", "4", "5", "7",
    };

    // Non-bridge S-57-sourced classes the FC permits as `theComponent` of a
    // Bridge's `BridgeAggregation` (SpanFixed / SpanOpening are synthesised).
    private static readonly HashSet<string> BridgeAggregationMemberClasses = new(StringComparer.Ordinal)
    {
        "PylonBridgeSupport", "Pontoon",
    };

    // S-57 attributes a Bridge never carries itself: they are consumed by the
    // span (or recorded as dropped when no span is emitted).
    private static readonly HashSet<int> BridgeSpanAttributes =
    [
        S57AttrHorclr, S57AttrHoracc, S57AttrVeracc,
        S57AttrVerclr, S57AttrVerccl, S57AttrVercop, S57AttrVerdat,
    ];

    // S-57 SORDAT (Source date, ATTL 147) maps to the S-101 `reportedDate`
    // simple attribute (value type S100_TruncatedDate), which the bundled FC
    // binds directly on ~50 feature types. Unlike a normal attribute rule the
    // mapping must be gated on the resolved feature actually binding
    // `reportedDate`: SORDAT is a near-universal S-57 attribute that also
    // appears on features which do not carry `reportedDate` in S-101, where it
    // has no conformant home and is left unmapped. The S-57 date value
    // ("YYYYMMDD", possibly truncated) is carried verbatim, matching the
    // fidelity of the dateStart / dateEnd sub-attributes. (S-57 SORIND, ATTL
    // 148, carries a comma-separated source-indication string with no general
    // S-101 equivalent — the FC's `source` attribute binds only
    // UpdateInformation — and is intentionally left unmapped.)
    private const ushort S57AttrSordat = 147;  // SORDAT — source date
    private const string S101AttrReportedDate = "reportedDate";

    // S-57 VALLMA (Value of local magnetic anomaly, ATTL 175) maps to the S-101
    // `valueOfLocalMagneticAnomaly` complex attribute, which the bundled FC
    // binds on LocalMagneticAnomaly [1..2]. The complex carries a mandatory
    // `magneticAnomalyValue` [1..1] real sub-attribute (the VALLMA value, in
    // nanoteslas, carried verbatim) plus an optional `referenceDirection`
    // [0..1] enum that has no S-57 source and is left unpopulated. Flat
    // one-level complex, so no consumer change is required (like CATZOC /
    // horizontalClearance).
    private const ushort S57AttrVallma = 175;  // VALLMA — value of local magnetic anomaly
    private const string S101AttrValueOfLocalMagneticAnomaly = "valueOfLocalMagneticAnomaly";
    private const string S101AttrMagneticAnomalyValue = "magneticAnomalyValue";

    // S-57 RADWAL (Radar wave length, ATTL 126) maps to the S-101
    // `radarWaveLength` complex attribute, which the bundled FC binds on
    // RadarTransponderBeacon [0..2]. The complex has two mandatory [1..1]
    // sub-attributes: `waveLengthValue` (real, metres) and `radarBand` (text,
    // the band letter). The S-57 value is a list of "value-band" pairs (e.g.
    // "0.03-X" or "0.03-X,0.10-S"); each pair yields one complex instance.
    // Because both sub-attributes are mandatory, a pair that does not split
    // cleanly into a numeric value and a band token is dropped (and reported).
    private const ushort S57AttrRadwal = 126;  // RADWAL — radar wave length
    private const string S101AttrRadarWaveLength = "radarWaveLength";
    private const string S101AttrWaveLengthValue = "waveLengthValue";
    private const string S101AttrRadarBand = "radarBand";

    // S-57 CURVEL (Current velocity, ATTL 84) maps to the S-101 `speed`
    // complex attribute, which the bundled FC binds on CurrentNonGravitational
    // (CURENT) and TidalStreamFloodEbb (TS_FEB). The complex carries a
    // mandatory `speedMaximum` [1..1] real sub-attribute (the CURVEL value,
    // carried verbatim) plus an optional `speedMinimum` [0..1] that has no S-57
    // source and is left unpopulated. Flat one-level complex, so no consumer
    // change is required (like CATZOC / valueOfLocalMagneticAnomaly).
    private const ushort S57AttrCurvel = 84;   // CURVEL — current velocity
    private const string S101AttrSpeed = "speed";
    private const string S101AttrSpeedMaximum = "speedMaximum";

    // S-57 MLTYLT (Multiplicity of lights, ATTL 110) maps to the S-101
    // `multiplicityOfFeatures` complex attribute, which the bundled FC binds on
    // the light classes (LightAllAround, LightSectored, LightAirObstruction).
    // The complex carries a mandatory `multiplicityKnown` [1..1] boolean and an
    // optional `numberOfFeatures` [0..1] integer. The S-57 MLTYLT integer (the
    // number of lights exhibited) is carried verbatim into `numberOfFeatures`
    // with `multiplicityKnown` set true.
    private const ushort S57AttrMltylt = 110;  // MLTYLT — multiplicity of lights
    private const string S101AttrMultiplicityOfFeatures = "multiplicityOfFeatures";
    private const string S101AttrMultiplicityKnown = "multiplicityKnown";
    private const string S101AttrNumberOfFeatures = "numberOfFeatures";

    // S-57 CATBRG (Category of bridge, ATTL 9) is remodelled in S-101 into the
    // enumerations bridgeConstruction / bridgeFunction / categoryOfOpeningBridge
    // and the Boolean openingBridge, all bound on Bridge (S-65 Annex B § 4.8.10).
    // The per-value targets come from the mapping (see DefaultRules); the
    // translator assembles the list so that openingBridge is emitted once for
    // the whole CATBRG value and single-valued targets keep one occurrence.
    private const ushort S57AttrCatbrg = 9;    // CATBRG — category of bridge
    private const string S101AttrOpeningBridge = "openingBridge";
    private const string S101AttrCategoryOfOpeningBridge = "categoryOfOpeningBridge";

    // ISO 639-3 language code used for the English-language INFORM/TXTDSC
    // bucket. NINFOM/NTXTDS are emitted with an empty language string,
    // since S-57 carries no language tag and Data Producers are expected
    // to fill it in manually (Conversion Guidance §2.3).
    private const string LanguageEng = "eng";

    private readonly S57S101Mapping _mapping;
    private readonly S101AllowedEnumValues? _allowedEnumValues;
    private readonly S101FeatureAttributeBindings _featureBindings;
    private readonly S57TranslationTarget _target;

    /// <summary>Creates a translator using <see cref="S57S101Mapping.Default"/>.</summary>
    public S57ToS101Translator() : this(S57S101Mapping.Default, S101AllowedEnumValues.Default) { }

    /// <summary>Creates a translator using the supplied code mapping.</summary>
    public S57ToS101Translator(S57S101Mapping mapping)
        : this(mapping, S101AllowedEnumValues.Default) { }

    /// <summary>
    /// Creates a translator using the supplied code mapping and the given
    /// allowable-value lookup. Pass <c>null</c> for <paramref name="allowedEnumValues"/>
    /// to disable enumerate-value enforcement (useful in tests).
    /// </summary>
    public S57ToS101Translator(S57S101Mapping mapping, S101AllowedEnumValues? allowedEnumValues)
        : this(mapping, allowedEnumValues, S101FeatureAttributeBindings.Default) { }

    /// <summary>
    /// Creates a translator using the supplied code mapping, allowable-value
    /// lookup, and feature/attribute binding lookup. Pass <c>null</c> for
    /// <paramref name="allowedEnumValues"/> to disable enumerate-value
    /// enforcement (useful in tests). <paramref name="featureBindings"/> gates
    /// which feature classes may carry each assembled S-101 complex attribute.
    /// </summary>
    public S57ToS101Translator(
        S57S101Mapping mapping,
        S101AllowedEnumValues? allowedEnumValues,
        S101FeatureAttributeBindings featureBindings)
        : this(mapping, allowedEnumValues, featureBindings, S57TranslationTarget.S101) { }

    /// <summary>
    /// Creates a translator into <paramref name="target"/> using the supplied
    /// code mapping, allowable-value lookup, and feature/attribute binding lookup.
    /// The lookups should be built from <paramref name="target"/>'s Feature
    /// Catalogue; <see cref="ForTarget"/> wires the bundled ones.
    /// </summary>
    public S57ToS101Translator(
        S57S101Mapping mapping,
        S101AllowedEnumValues? allowedEnumValues,
        S101FeatureAttributeBindings featureBindings,
        S57TranslationTarget target)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(featureBindings);
        ArgumentNullException.ThrowIfNull(target);
        _mapping = mapping;
        _allowedEnumValues = allowedEnumValues;
        _featureBindings = featureBindings;
        _target = target;
    }

    /// <summary>
    /// Creates a translator into <paramref name="target"/> whose enumerate-value
    /// and complex-attribute checks use the bundled Feature Catalogue of
    /// <see cref="S57TranslationTarget.Spec"/>.
    /// </summary>
    /// <param name="target">The S-100 product to translate into.</param>
    /// <param name="mapping">The S-57 code mapping into <paramref name="target"/>'s feature catalogue.</param>
    /// <returns>A translator targeting <paramref name="target"/>.</returns>
    public static S57ToS101Translator ForTarget(S57TranslationTarget target, S57S101Mapping mapping)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new S57ToS101Translator(
            mapping,
            S101AllowedEnumValues.ForSpec(target.Spec),
            S101FeatureAttributeBindings.ForSpec(target.Spec),
            target);
    }

    /// <summary>
    /// Creates a translator into <paramref name="target"/> using the bundled
    /// mapping and Feature Catalogue lookups for
    /// <see cref="S57TranslationTarget.Spec"/> (see <see cref="S57S101Mapping.ForSpec"/>).
    /// </summary>
    /// <param name="target">The S-100 product to translate into.</param>
    /// <returns>A translator targeting <paramref name="target"/>.</returns>
    public static S57ToS101Translator ForTarget(S57TranslationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ForTarget(target, S57S101Mapping.ForSpec(target.Spec));
    }

    /// <summary>The S-100 product this translator targets.</summary>
    public S57TranslationTarget Target => _target;

    /// <summary>
    /// Translates an <see cref="S57Dataset"/> into an <see cref="S101Document"/>.
    /// </summary>
    public S101Document Translate(S57Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Translate(dataset.Document, diagnostics: null);
    }

    /// <summary>
    /// Translates an <see cref="S57Dataset"/> into an <see cref="S101Document"/>,
    /// recording what was dropped into <paramref name="diagnostics"/>.
    /// </summary>
    public S101Document Translate(S57Dataset dataset, S57TranslationDiagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Translate(dataset.Document, diagnostics);
    }

    /// <summary>
    /// Translates an <see cref="EncDotNet.S57.S57Document"/> into an
    /// <see cref="S101Document"/>.
    /// </summary>
    public S101Document Translate(EncDotNet.S57.S57Document s57)
        => Translate(s57, diagnostics: null);

    /// <summary>
    /// Translates an <see cref="EncDotNet.S57.S57Document"/> into an
    /// <see cref="S101Document"/>, optionally recording per-drop diagnostics.
    /// </summary>
    /// <param name="s57">The parsed S-57 document to translate.</param>
    /// <param name="diagnostics">
    /// Optional collector that accumulates the object classes, attributes, and
    /// enumerate values dropped during translation. Pass <c>null</c> (the
    /// default) to disable collection with zero overhead.
    /// </param>
    public S101Document Translate(EncDotNet.S57.S57Document s57, S57TranslationDiagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(s57);

        var ctx = new TranslationContext(s57, _mapping, _allowedEnumValues, _featureBindings, diagnostics);
        ctx.IndexVectorRecords();
        ctx.TranslateNodes();
        ctx.TranslateEdges();
        ctx.TranslateFeatures();

        // S57Document.CoordinateMultiplicationFactor / SoundingMultiplicationFactor
        // already supply the documented defaults (10_000_000 and 10).
        var cmf = (uint)s57.CoordinateMultiplicationFactor;
        var somf = (uint)s57.SoundingMultiplicationFactor;
        var dsid = s57.DataSetIdentification;

        return new S101Document
        {
            Identification = new S101DatasetIdentification
            {
                RecordName = 10,
                RecordId = 1,
                ProductSpecification = _target.Spec,
                ProductSpecificationEdition = _target.Edition,
                DatasetName = dsid?.DataSetName ?? "",
                DatasetTitle = dsid?.DataSetName ?? "",
                DatasetReferenceDate = dsid?.IssueDate ?? "",
                DatasetLanguage = "eng",
            },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = cmf,
                CoordinateMultiplicationFactorY = cmf,
                CoordinateMultiplicationFactorZ = somf,
            },
            FeatureTypeCatalogue = ctx.FeatureTypeCatalogue,
            AttributeTypeCatalogue = ctx.AttributeTypeCatalogue,
            Points = ctx.Points,
            MultiPoints = ctx.MultiPoints,
            CurveSegments = ctx.CurveSegments,
            CompositeCurves = ctx.CompositeCurves,
            Surfaces = ctx.Surfaces,
            Features = ctx.Features,
            InformationTypes = ctx.InformationTypes,
            InformationTypeCatalogue = ctx.InformationTypeCatalogue,
            InformationAssociationCatalogue = ctx.InformationAssociationCatalogue,
            FeatureAssociationCatalogue = ctx.FeatureAssociationCatalogue,
            RoleCatalogue = ctx.RoleCatalogue,
        };
    }

    // ── Translation context ─────────────────────────────────────────────

    private sealed class TranslationContext
    {
        private readonly EncDotNet.S57.S57Document _s57;
        private readonly S57S101Mapping _mapping;
        private readonly S101AllowedEnumValues? _allowedEnumValues;
        private readonly S101FeatureAttributeBindings _featureBindings;
        private readonly S57TranslationDiagnostics? _diagnostics;

        // Index of the document's flat VectorRecords list, keyed by
        // (RecordNameCode, RecordId) for fast lookup from spatial pointers.
        private readonly Dictionary<(int rcnm, int rcid), EncDotNet.S57.S57VectorRecord> _vectorIndex = new();

        // Mapping from S-57 (RCNM, RCID) to allocated S-101 IDs, per spatial kind.
        private readonly Dictionary<(int rcnm, int rcid), uint> _nodeIdMap = new();
        private readonly Dictionary<int, uint> _edgeIdMap = new();
        private uint _nextPointId = 1;
        private uint _nextMultiPointId = 1;
        private uint _nextCurveId = 1;
        private uint _nextCompositeId = 1;
        private uint _nextSurfaceId = 1;
        private uint _nextFeatureId = 1;
        private ushort _nextFeatureTypeCode = 1;
        private ushort _nextAttributeCode = 1;
        private uint _nextInformationId = 1;
        private ushort _nextInformationTypeCode = 1;
        private ushort _nextInformationAssociationCode = 1;
        private ushort _nextFeatureAssociationCode = 1;
        private ushort _nextRoleCode = 1;
        private readonly Dictionary<string, ushort> _featureTypeByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ushort> _attributeByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ushort> _informationTypeByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ushort> _informationAssociationByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ushort> _featureAssociationByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ushort> _roleByName = new(StringComparer.OrdinalIgnoreCase);

        // Maps an emitted feature's S-57 long name (LNAM: producing agency,
        // feature id, subdivision) to its allocated S-101 record id, so that
        // C_AGGR feature-to-feature pointers (which reference members by LNAM,
        // not RCNM/RCID) can be resolved to the S-101 features in a second pass.
        private readonly Dictionary<(int agency, long fid, int sub), uint> _recordIdByLnam = new();

        public Dictionary<uint, S101PointRecord> Points { get; } = new();
        public Dictionary<uint, S101MultiPointRecord> MultiPoints { get; } = new();
        public Dictionary<uint, S101CurveSegmentRecord> CurveSegments { get; } = new();
        public Dictionary<uint, S101CompositeCurveRecord> CompositeCurves { get; } = new();
        public Dictionary<uint, S101SurfaceRecord> Surfaces { get; } = new();
        public List<S101FeatureRecord> Features { get; } = new();
        public Dictionary<ushort, string> FeatureTypeCatalogue { get; } = new();
        public Dictionary<ushort, string> AttributeTypeCatalogue { get; } = new();
        public Dictionary<uint, S101InformationRecord> InformationTypes { get; } = new();
        public Dictionary<ushort, string> InformationTypeCatalogue { get; } = new();
        public Dictionary<ushort, string> InformationAssociationCatalogue { get; } = new();
        public Dictionary<ushort, string> FeatureAssociationCatalogue { get; } = new();
        public Dictionary<ushort, string> RoleCatalogue { get; } = new();

        public TranslationContext(
            EncDotNet.S57.S57Document s57,
            S57S101Mapping mapping,
            S101AllowedEnumValues? allowedEnumValues,
            S101FeatureAttributeBindings featureBindings,
            S57TranslationDiagnostics? diagnostics)
        {
            _s57 = s57;
            _mapping = mapping;
            _allowedEnumValues = allowedEnumValues;
            _featureBindings = featureBindings;
            _diagnostics = diagnostics;
        }

        public void IndexVectorRecords()
        {
            foreach (var vr in _s57.VectorRecords)
            {
                var key = (vr.RecordName.RecordNameCode, vr.RecordName.RecordId);
                _vectorIndex[key] = vr;
            }
        }

        // ── Spatial translation ─────────────────────────────────────────

        public void TranslateNodes()
        {
            foreach (var vr in _s57.VectorRecords)
            {
                var rcnm = vr.RecordName.RecordNameCode;
                if (rcnm != S57RecordNameCodes.IsolatedNode
                    && rcnm != S57RecordNameCodes.ConnectedNode)
                    continue;

                // Skip multi-point sounding nodes here; they're exploded into
                // features in TranslateFeatures().
                if (vr.Soundings.Count > 0) continue;
                if (vr.Coordinates2D.Count == 0) continue;

                var coord = vr.Coordinates2D[0];
                var id = _nextPointId++;
                Points[id] = new S101PointRecord { RecordId = id, Y = coord.Y, X = coord.X };
                _nodeIdMap[(rcnm, vr.RecordName.RecordId)] = id;
            }
        }

        public void TranslateEdges()
        {
            foreach (var vr in _s57.VectorRecords)
            {
                if (vr.RecordName.RecordNameCode != S57RecordNameCodes.Edge) continue;

                S101PointAssociation? begin = null;
                S101PointAssociation? end = null;
                foreach (var p in vr.VectorPointers)
                {
                    var topo = (int)p.Topology;
                    if (topo == TopologyBegin)
                    {
                        if (TryGetPointId(p.Name, out var pid))
                            begin = new S101PointAssociation(S101RcnmPoint, pid, TopologyBegin);
                    }
                    else if (topo == TopologyEnd)
                    {
                        if (TryGetPointId(p.Name, out var pid))
                            end = new S101PointAssociation(S101RcnmPoint, pid, TopologyEnd);
                    }
                }

                var ptas = new List<S101PointAssociation>();
                if (begin is not null) ptas.Add(begin.Value);
                if (end is not null) ptas.Add(end.Value);

                // Project package coords to (Y,X) tuples expected by the S-101 record.
                var intermediates = new List<(int Y, int X)>(vr.Coordinates2D.Count);
                foreach (var c in vr.Coordinates2D)
                    intermediates.Add((c.Y, c.X));

                var id = _nextCurveId++;
                CurveSegments[id] = new S101CurveSegmentRecord
                {
                    RecordId = id,
                    PointAssociations = ptas,
                    IntermediateCoordinates = intermediates,
                };
                _edgeIdMap[vr.RecordName.RecordId] = id;
            }
        }

        private bool TryGetPointId(S57RecordName name, out uint id)
            => _nodeIdMap.TryGetValue((name.RecordNameCode, name.RecordId), out id);

        // ── Feature translation ─────────────────────────────────────────

        public void TranslateFeatures()
        {
            // Sector-light merge: co-located S-57 LIGHTS objects that each carry
            // a single sector arc (SECTR1/SECTR2) model one physical multi-sector
            // light. Group them by the S-101 point they resolve to and fold the
            // absorbed members' sectors into the surviving LightSectored feature
            // as additional `sectorCharacteristics` instances (S-101 FC makes
            // `sectorCharacteristics` [1..*]). See BuildSectorMergeGroups.
            var featureRecords =
                _s57.FeatureRecords as IReadOnlyList<EncDotNet.S57.S57FeatureRecord>
                ?? _s57.FeatureRecords.ToList();
            var (absorbed, extraSectorsByIndex) = BuildSectorMergeGroups(featureRecords);

            // S-57 TOPMAR objects are folded into the `topmark` complex attribute
            // of their master buoy/beacon (the master's FFPT slave pointer), so
            // an absorbed TOPMAR emits no feature of its own. See
            // BuildTopmarkGroups.
            var (absorbedTopmarks, topmarkByMaster) = BuildTopmarkGroups(featureRecords);

            // C_AGGR collection objects are deferred to a second pass: their
            // members are referenced by LNAM and may be emitted after the
            // C_AGGR in document order, so the member record ids are only known
            // once every feature has been translated. See EmitRangeSystems.
            var pendingAggregations = new List<EncDotNet.S57.S57FeatureRecord>();

            // C_AGGR bridge collections (BRIDGE + PYLONS/PONTON members) are
            // identified up front: their member BRIDGE objects emit only their
            // spans, and a single aggregated Bridge is emitted after the loop.
            // See BuildBridgeAggregations / EmitBridgeAggregations.
            var bridgePlan = BuildBridgeAggregations(featureRecords);

            // tisdge time schedules and the features they apply to. See
            // BuildTimeSchedulePlan / TimeScheduleAssociations.
            var schedulePlan = BuildTimeSchedulePlan(featureRecords);

            for (int fi = 0; fi < featureRecords.Count; fi++)
            {
                var feat = featureRecords[fi];
                var objl = (ushort)(int)feat.ObjectCode;
                if (objl == SoundingObjl)
                {
                    if (_diagnostics is not null) _diagnostics.SoundingFeaturesRead++;
                    EmitSoundingMultiPoint(feat);
                    continue;
                }

                if (_diagnostics is not null) _diagnostics.FeatureRecordsRead++;

                // Members absorbed into a co-located sector-light merge emit no
                // feature of their own; their sector rides on the primary.
                if (absorbed.Contains(fi)) continue;

                // A TOPMAR absorbed into its master's `topmark` complex attribute
                // emits no feature of its own; it is counted here instead of as an
                // unmapped object class.
                if (absorbedTopmarks.Contains(fi))
                {
                    if (_diagnostics is not null) _diagnostics.TopmarksAbsorbed++;
                    continue;
                }

                // A time schedule is emitted with the first feature it applies
                // to; a C_ASSO that only links schedules emits nothing itself.
                if (schedulePlan.Schedules.Contains(fi) || schedulePlan.LinkingAssociations.Contains(fi))
                    continue;

                if (objl == CAggrObjl)
                {
                    // Defer to the RangeSystem second pass (members resolved by
                    // LNAM), unless it is a bridge collection, which is emitted
                    // as an aggregated Bridge instead.
                    if (!bridgePlan.GroupByAggregation.ContainsKey(fi))
                        pendingAggregations.Add(feat);
                    continue;
                }

                var acronymView = _mapping.BuildAcronymView(feat.Attributes);
                var resolved = _mapping.ResolveFeature(objl, acronymView, MapPrimitive(feat.Primitive));
                if (resolved is null)
                {
                    if (_diagnostics is not null)
                    {
                        if (_mapping.FeatureRules.ContainsKey(objl))
                            _diagnostics.RecordRuleDroppedObjectClass(objl);
                        else
                            _diagnostics.RecordUnmappedObjectClass(objl);
                    }
                    continue;
                }

                // Drop M_COVR meta-objects flagged "no coverage available"
                // (CATCOV = 2). S-101 has no such construct — a DataCoverage
                // feature always asserts coverage (its FC binds no
                // categoryOfCoverage attribute), so translating a no-coverage
                // M_COVR into DataCoverage would falsely claim data coverage
                // over the cell's no-data region. That in turn drives
                // cross-cell scale-band overlap suppression to blank the
                // coarser overlapping cell there, producing the mid-zoom
                // "drop-out" holes reported in issue #438. Coverage-available
                // M_COVR (CATCOV = 1, or absent) still converts normally.
                if (objl == MCovrObjl
                    && acronymView.TryGetValue(CatcovAcronym, out var catcov)
                    && catcov.Trim() == CatcovNoCoverage)
                {
                    _diagnostics?.RecordRuleDroppedObjectClass(objl);
                    continue;
                }

                bool isBridge = IsBridgeObjl(objl) && resolved.S101Code == S101ClassBridge;

                // A BRIDGE that is a member of a bridge C_AGGR contributes only
                // its span here; its Bridge-level attributes and geometry are
                // folded into the aggregated Bridge by EmitBridgeAggregations.
                if (isBridge && bridgePlan.GroupByBridgeMember.TryGetValue(fi, out var memberGroup))
                {
                    var memberSpan = BuildBridgeSpan(feat);
                    if (memberSpan is null) continue;
                    var memberSpatials = TranslateSpatialPointers(feat);
                    if (memberSpatials.Count == 0)
                    {
                        _diagnostics?.RecordFeatureWithoutGeometry(memberSpan.Value.SpanClass);
                        continue;
                    }
                    memberGroup.ComponentRecordIds.Add(EmitBridgeSpan(feat, memberSpan.Value, memberSpatials));
                    continue;
                }

                var spatials = TranslateSpatialPointers(feat);
                if (spatials.Count == 0)
                {
                    _diagnostics?.RecordFeatureWithoutGeometry(resolved.S101Code);
                    continue;
                }

                // Resolve geometry before translating attributes so that a
                // feature dropped for lack of geometry does not leave an orphan
                // NauticalInformation record behind (BuildNauticalInformation
                // allocates the record as a side effect).
                var typeCode = GetOrAssignFeatureTypeCode(resolved.S101Code);
                extraSectorsByIndex.TryGetValue(fi, out var extraSectors);
                topmarkByMaster.TryGetValue(fi, out var topmarkSource);
                var attributes = TranslateAttributes(
                    feat.Attributes, resolved, objl, out var infoAssociations, extraSectors, topmarkSource);
                if (schedulePlan.SchedulesByFeature.TryGetValue(fi, out var schedules))
                    infoAssociations = [.. infoAssociations, .. TimeScheduleAssociations(resolved.S101Code, schedules, schedulePlan)];

                // A Bridge over navigable water is decomposed into a span that
                // shares its geometry (S-65 Annex B §4.8.10).
                var span = isBridge ? BuildBridgeSpan(feat) : null;

                if (_diagnostics is not null) _diagnostics.FeaturesEmitted++;
                var recordId = _nextFeatureId++;
                _recordIdByLnam[Lnam(feat.RecordName)] = recordId;

                IReadOnlyList<S101FeatureAssociation> featureAssociations = [];
                S101FeatureRecord? spanRecord = null;
                if (span is not null)
                {
                    spanRecord = CreateBridgeSpanRecord(feat, span.Value, spatials);
                    featureAssociations = [BridgeComponentAssociation(spanRecord.RecordId)];
                }

                Features.Add(new S101FeatureRecord
                {
                    RecordId = recordId,
                    FeatureTypeCode = typeCode,
                    ProducingAgency = (ushort)feat.RecordName.AgencyCode,
                    FeatureIdentificationNumber = (uint)feat.RecordName.FeatureId,
                    FeatureIdentificationSubdivision = (ushort)feat.RecordName.FeatureSubdivision,
                    Attributes = attributes,
                    SpatialAssociations = spatials,
                    FeatureAssociations = featureAssociations,
                    InformationAssociations = infoAssociations,
                });

                if (spanRecord is not null)
                    Features.Add(spanRecord);
            }

            EmitBridgeAggregations(bridgePlan);
            RecordUnlinkedTimeSchedules(schedulePlan);
            EmitRangeSystems(pendingAggregations);
        }

        // ── Bridges (S-65 Annex B §4.8.10) ──────────────────────────────

        // Attribute payload of a span synthesised from an S-57 BRIDGE.
        private readonly record struct BridgeSpan(string SpanClass, IReadOnlyList<S101Attribute> Attributes);

        // A C_AGGR that collects the components of one bridge: its BRIDGE
        // members (by feature index) and its PYLONS / PONTON members, plus the
        // record ids of the span features emitted for the BRIDGE members.
        private sealed class BridgeAggregationGroup(
            EncDotNet.S57.S57FeatureRecord aggregation,
            IReadOnlyList<int> bridgeMemberIndices,
            IReadOnlyList<EncDotNet.S57.S57FeatureRecord> otherMembers)
        {
            public EncDotNet.S57.S57FeatureRecord Aggregation { get; } = aggregation;
            public IReadOnlyList<int> BridgeMemberIndices { get; } = bridgeMemberIndices;
            public IReadOnlyList<EncDotNet.S57.S57FeatureRecord> OtherMembers { get; } = otherMembers;
            public List<uint> ComponentRecordIds { get; } = [];
        }

        private sealed record BridgeAggregationPlan(
            IReadOnlyList<EncDotNet.S57.S57FeatureRecord> FeatureRecords,
            Dictionary<int, BridgeAggregationGroup> GroupByAggregation,
            Dictionary<int, BridgeAggregationGroup> GroupByBridgeMember);

        // Identifies S-57 C_AGGR collections that aggregate the components of a
        // single bridge (S-65 Annex B §4.8.10: the spans of a bridge over
        // navigable water are encoded as separate BRIDGE objects and, with any
        // bridge pylons or pontoons, aggregated by C_AGGR so that the converter
        // can create one Bridge feature). A C_AGGR qualifies when every member
        // resolves (by LNAM) to either a BRIDGE that maps to Bridge (i.e. is not
        // a point) or a PYLONS / PONTON that maps to a permitted
        // BridgeAggregation component, with at least one BRIDGE member. A BRIDGE
        // is claimed by at most one collection (first in document order wins).
        private BridgeAggregationPlan BuildBridgeAggregations(
            IReadOnlyList<EncDotNet.S57.S57FeatureRecord> featureRecords)
        {
            var byAggregation = new Dictionary<int, BridgeAggregationGroup>();
            var byMember = new Dictionary<int, BridgeAggregationGroup>();

            Dictionary<(int, long, int), int>? indexByLnam = null;
            for (int ai = 0; ai < featureRecords.Count; ai++)
            {
                var aggr = featureRecords[ai];
                if ((int)aggr.ObjectCode != CAggrObjl || aggr.FeaturePointers.Count == 0)
                    continue;

                if (indexByLnam is null)
                {
                    indexByLnam = new Dictionary<(int, long, int), int>();
                    for (int i = 0; i < featureRecords.Count; i++)
                        indexByLnam[Lnam(featureRecords[i].RecordName)] = i;
                }

                var bridges = new List<int>();
                var others = new List<EncDotNet.S57.S57FeatureRecord>();
                var seen = new HashSet<int>();
                bool qualifies = true;
                foreach (var fp in aggr.FeaturePointers)
                {
                    if (!indexByLnam.TryGetValue(Lnam(fp.Name), out var mi))
                    {
                        qualifies = false;
                        break;
                    }
                    if (!seen.Add(mi)) continue;

                    var member = featureRecords[mi];
                    var memberObjl = (ushort)(int)member.ObjectCode;
                    var resolved = _mapping.ResolveFeature(
                        memberObjl,
                        _mapping.BuildAcronymView(member.Attributes),
                        MapPrimitive(member.Primitive));
                    if (IsBridgeObjl(memberObjl)
                        && resolved?.S101Code == S101ClassBridge
                        && !byMember.ContainsKey(mi))
                    {
                        bridges.Add(mi);
                    }
                    else if (!IsBridgeObjl(memberObjl)
                        && resolved is not null
                        && BridgeAggregationMemberClasses.Contains(resolved.S101Code))
                    {
                        others.Add(member);
                    }
                    else
                    {
                        qualifies = false;
                        break;
                    }
                }

                if (!qualifies || bridges.Count == 0) continue;

                var group = new BridgeAggregationGroup(aggr, bridges, others);
                byAggregation[ai] = group;
                foreach (var mi in bridges)
                    byMember[mi] = group;
            }

            return new BridgeAggregationPlan(featureRecords, byAggregation, byMember);
        }

        // Emits one Bridge per qualifying bridge C_AGGR. Its Bridge-level
        // attributes come from a representative member BRIDGE — the first
        // opening one (S-101 requires the Bridge to be an opening bridge when
        // any span opens), else the first named one, else the first — with any
        // attribute carried by the C_AGGR itself taking precedence (S-65 Annex B
        // §4.8.10 places the bridge name on the C_AGGR); when neither carries a
        // name the first named member supplies it. The geometry is the
        // dissolved union of the member BRIDGE geometries when that forms a
        // single curve or surface, and no geometry otherwise (Bridge permits
        // noGeometry). The Bridge links every emitted span and every emitted
        // PylonBridgeSupport / Pontoon member as `theComponent`.
        private void EmitBridgeAggregations(BridgeAggregationPlan plan)
        {
            foreach (var group in plan.GroupByAggregation.Values)
            {
                var members = group.BridgeMemberIndices.Select(i => plan.FeatureRecords[i]).ToList();
                var representative =
                    members.FirstOrDefault(IsOpeningBridge)
                    ?? members.FirstOrDefault(HasObjectName)
                    ?? members[0];

                var aggrCodes = group.Aggregation.Attributes.Select(a => a.AttributeCode).ToHashSet();
                var merged = representative.Attributes
                    .Where(a => !aggrCodes.Contains(a.AttributeCode))
                    .ToList();
                if (!HasObjectName(group.Aggregation) && !HasObjectName(representative))
                {
                    var named = members.FirstOrDefault(HasObjectName);
                    if (named is not null)
                        merged.AddRange(named.Attributes.Where(a => a.AttributeCode is S57AttrObjnam or S57AttrNobjnm));
                }
                merged.AddRange(group.Aggregation.Attributes);

                var bridgeObjl = (ushort)(int)representative.ObjectCode;
                var resolved = _mapping.ResolveFeature(
                    bridgeObjl,
                    _mapping.BuildAcronymView(merged),
                    MapPrimitive(representative.Primitive));
                if (resolved is null) continue;

                var typeCode = GetOrAssignFeatureTypeCode(resolved.S101Code);
                var attributes = TranslateAttributes(merged, resolved, bridgeObjl, out var infoAssociations);
                var spatials = MergeBridgeGeometry(members);

                var componentIds = new List<uint>(group.ComponentRecordIds);
                foreach (var other in group.OtherMembers)
                {
                    if (_recordIdByLnam.TryGetValue(Lnam(other.RecordName), out var otherId))
                        componentIds.Add(otherId);
                }

                var recordId = _nextFeatureId++;
                _recordIdByLnam[Lnam(group.Aggregation.RecordName)] = recordId;
                if (_diagnostics is not null) _diagnostics.BridgeAggregationsEmitted++;
                Features.Add(new S101FeatureRecord
                {
                    RecordId = recordId,
                    FeatureTypeCode = typeCode,
                    ProducingAgency = (ushort)group.Aggregation.RecordName.AgencyCode,
                    FeatureIdentificationNumber = (uint)group.Aggregation.RecordName.FeatureId,
                    FeatureIdentificationSubdivision = (ushort)group.Aggregation.RecordName.FeatureSubdivision,
                    Attributes = attributes,
                    SpatialAssociations = spatials,
                    FeatureAssociations = componentIds.Select(BridgeComponentAssociation).ToList(),
                    InformationAssociations = infoAssociations,
                });
            }

            static bool HasObjectName(EncDotNet.S57.S57FeatureRecord f)
                => f.Attributes.Any(a => a.AttributeCode is S57AttrObjnam or S57AttrNobjnm
                    && !string.IsNullOrEmpty(a.Value));
        }

        // ── Time schedules (IENC tisdge) ────────────────────────────────

        private sealed record TimeSchedulePlan(
            IReadOnlyList<EncDotNet.S57.S57FeatureRecord> FeatureRecords,
            HashSet<int> Schedules,
            HashSet<int> LinkingAssociations,
            Dictionary<int, List<int>> SchedulesByFeature)
        {
            // Information record id per emitted schedule, by feature index.
            public Dictionary<int, uint> RecordIds { get; } = [];
        }

        // Finds the tisdge records and, per feature (by index), the schedules
        // it is linked to: through a C_ASSO that collects both, or through a
        // feature pointer in either direction. Collections are never linked
        // themselves.
        private TimeSchedulePlan BuildTimeSchedulePlan(
            IReadOnlyList<EncDotNet.S57.S57FeatureRecord> featureRecords)
        {
            var schedules = new HashSet<int>();
            var linking = new HashSet<int>();
            var byFeature = new Dictionary<int, List<int>>();
            for (int i = 0; i < featureRecords.Count; i++)
            {
                if (IsTimeScheduleObjl((ushort)(int)featureRecords[i].ObjectCode))
                    schedules.Add(i);
            }

            var plan = new TimeSchedulePlan(featureRecords, schedules, linking, byFeature);
            if (schedules.Count == 0)
                return plan;

            var indexByLnam = new Dictionary<(int, long, int), int>();
            for (int i = 0; i < featureRecords.Count; i++)
                indexByLnam[Lnam(featureRecords[i].RecordName)] = i;

            for (int i = 0; i < featureRecords.Count; i++)
            {
                var members = new List<int>();
                foreach (var fp in featureRecords[i].FeaturePointers)
                {
                    if (indexByLnam.TryGetValue(Lnam(fp.Name), out var mi) && mi != i)
                        members.Add(mi);
                }

                if ((ushort)(int)featureRecords[i].ObjectCode == CAssoObjl)
                {
                    var linkedSchedules = members.Where(schedules.Contains).ToList();
                    if (linkedSchedules.Count == 0)
                        continue;

                    linking.Add(i);
                    foreach (var member in members)
                    {
                        foreach (var schedule in linkedSchedules)
                            Link(member, schedule);
                    }
                }
                else if (schedules.Contains(i))
                {
                    foreach (var member in members)
                        Link(member, i);
                }
                else
                {
                    foreach (var member in members.Where(schedules.Contains))
                        Link(i, member);
                }
            }

            foreach (var list in byFeature.Values)
                list.Sort();
            return plan;

            void Link(int feature, int schedule)
            {
                if (schedules.Contains(feature) || IsCollectionObjl((ushort)(int)featureRecords[feature].ObjectCode))
                    return;
                if (!byFeature.TryGetValue(feature, out var list))
                    byFeature[feature] = list = [];
                if (!list.Contains(schedule))
                    list.Add(schedule);
            }
        }

        // The AdditionalInformation associations from a feature of class
        // featureCode to the schedules linked to it, emitting each schedule's
        // information record on first use. Schedules the class cannot carry
        // are skipped.
        private IEnumerable<S101InformationAssociation> TimeScheduleAssociations(
            string featureCode,
            List<int> schedules,
            TimeSchedulePlan plan)
        {
            if (!_featureBindings.BindsInformationType(featureCode, S101AssocAdditionalInformation, S101InfoTypeTimeSchedule))
                return [];

            var associations = new List<S101InformationAssociation>(schedules.Count);
            foreach (var schedule in schedules)
            {
                if (!plan.RecordIds.TryGetValue(schedule, out var recordId))
                    plan.RecordIds[schedule] = recordId = EmitTimeSchedule(plan.FeatureRecords[schedule]);
                associations.Add(new S101InformationAssociation(
                    GetOrAssignInformationAssociationCode(S101AssocAdditionalInformation),
                    recordId,
                    GetOrAssignRoleCode(S101RoleTheInformation)));
            }
            return associations;
        }

        // Emits the TimeScheduleInGeneral record for a tisdge. Its attributes
        // translate as a feature's would, keeping only those the information
        // type binds (all simple); the rest are recorded as rule-dropped.
        private uint EmitTimeSchedule(EncDotNet.S57.S57FeatureRecord schedule)
        {
            var objl = (ushort)(int)schedule.ObjectCode;
            var owner = new ResolvedFeature(
                S101InfoTypeTimeSchedule, new Dictionary<string, S57AttributeOverride>());
            var source = new List<EncDotNet.S57.S57AttributeValue>(schedule.Attributes.Count);
            foreach (var a in schedule.Attributes)
            {
                // Textual attributes would become a NauticalInformation record,
                // which an information type cannot reference; other mapped
                // attributes need a binding on the information type.
                var target = a.AttributeCode is >= 0 and <= ushort.MaxValue
                    ? _mapping.ResolveAttribute((ushort)a.AttributeCode, a.Value, owner)?.S101Code
                    : null;
                if (a.AttributeCode is S57AttrInform or S57AttrNinfom or S57AttrTxtdsc or S57AttrNtxtds
                    || (target is not null && !_featureBindings.Binds(S101InfoTypeTimeSchedule, target)))
                {
                    _diagnostics?.RecordRuleDroppedAttribute((ushort)a.AttributeCode);
                }
                else
                {
                    source.Add(a);
                }
            }

            // Every attribute left is simple and bound, except any the
            // translator derives itself (such as reportedDate); keep only
            // what the information type binds.
            var attributes = TranslateAttributes(source, owner, objl, out _)
                .Where(a => _featureBindings.Binds(S101InfoTypeTimeSchedule, AttributeTypeCatalogue[a.NumericCode]))
                .ToList();

            var recordId = _nextInformationId++;
            InformationTypes[recordId] = new S101InformationRecord
            {
                RecordId = recordId,
                InformationTypeCode = GetOrAssignInformationTypeCode(S101InfoTypeTimeSchedule),
                Attributes = attributes,
            };
            if (_diagnostics is not null) _diagnostics.TimeSchedulesEmitted++;
            return recordId;
        }

        // Records every tisdge that no emitted feature could carry.
        private void RecordUnlinkedTimeSchedules(TimeSchedulePlan plan)
        {
            foreach (var schedule in plan.Schedules)
            {
                if (!plan.RecordIds.ContainsKey(schedule))
                    _diagnostics?.RecordRuleDroppedObjectClass((ushort)(int)plan.FeatureRecords[schedule].ObjectCode);
            }
        }

        // True for an object class whose rule registers the IENC tisdge
        // (only the S-401 mapping does).
        private bool IsTimeScheduleObjl(ushort objl)
            => _mapping.FeatureRules.TryGetValue(objl, out var rule)
                && string.Equals(rule.S57Acronym, TimeScheduleAcronym, StringComparison.Ordinal);

        private static bool IsCollectionObjl(ushort objl)
            => objl is CAggrObjl or CAssoObjl;

        // True for S-57 BRIDGE and for any object class whose rule is a twin of
        // it (the IENC inland `bridge`, 17011, re-registers BRIDGE in lower case
        // and reuses its rule).
        private bool IsBridgeObjl(ushort objl)
            => objl == BridgeObjl
                || (_mapping.FeatureRules.TryGetValue(objl, out var rule)
                    && string.Equals(rule.S57Acronym, BridgeAcronym, StringComparison.OrdinalIgnoreCase));

        // True when the S-57 BRIDGE's CATBRG list contains an S-101 opening
        // bridge category (2, 3, 4, 5 or 7).
        private static bool IsOpeningBridge(EncDotNet.S57.S57FeatureRecord feat)
        {
            foreach (var a in feat.Attributes)
            {
                if (a.AttributeCode != S57AttrCatbrg) continue;
                foreach (var code in SplitEnumList(a.Value))
                {
                    if (OpeningBridgeCategories.Contains(code)) return true;
                }
            }
            return false;
        }

        private S101FeatureAssociation BridgeComponentAssociation(uint componentRecordId)
            => new(
                GetOrAssignFeatureAssociationCode(S101AssocBridgeAggregation),
                componentRecordId,
                GetOrAssignRoleCode(S101RoleTheComponent));

        // Emits a span feature record for an S-57 BRIDGE and returns its id.
        private uint EmitBridgeSpan(
            EncDotNet.S57.S57FeatureRecord feat,
            BridgeSpan span,
            IReadOnlyList<S101SpatialAssociation> spatials)
        {
            var record = CreateBridgeSpanRecord(feat, span, spatials);
            Features.Add(record);
            return record.RecordId;
        }

        // Allocates a span feature record that shares the BRIDGE's geometry and
        // S-57 feature identity. The caller adds it to Features.
        private S101FeatureRecord CreateBridgeSpanRecord(
            EncDotNet.S57.S57FeatureRecord feat,
            BridgeSpan span,
            IReadOnlyList<S101SpatialAssociation> spatials)
        {
            if (_diagnostics is not null) _diagnostics.BridgeSpansEmitted++;
            return new S101FeatureRecord
            {
                RecordId = _nextFeatureId++,
                FeatureTypeCode = GetOrAssignFeatureTypeCode(span.SpanClass),
                ProducingAgency = (ushort)feat.RecordName.AgencyCode,
                FeatureIdentificationNumber = (uint)feat.RecordName.FeatureId,
                FeatureIdentificationSubdivision = (ushort)feat.RecordName.FeatureSubdivision,
                Attributes = span.Attributes,
                SpatialAssociations = spatials,
                FeatureAssociations = [],
                InformationAssociations = [],
            };
        }

        // Builds the SpanFixed / SpanOpening attributes for an S-57 BRIDGE, or
        // returns null when the bridge lacks the clearance its span class makes
        // mandatory (VERCLR for SpanFixed, VERCCL for SpanOpening) — taken as
        // "not over navigable water" — in which case any clearance-related
        // attributes present are recorded as rule-dropped (Bridge binds none of
        // them). Sub-attributes follow the FC binding order: fixedDateRange,
        // horizontalClearanceFixed, vertical clearance complex(es),
        // verticalDatum, scaleMinimum.
        private BridgeSpan? BuildBridgeSpan(EncDotNet.S57.S57FeatureRecord feat)
        {
            string? verclr = null, verccl = null, vercop = null, horclr = null;
            string? horacc = null, veracc = null, verdat = null, scamin = null;
            string? datsta = null, datend = null;
            bool verclrPresent = false, vercclPresent = false, vercopPresent = false;
            foreach (var a in feat.Attributes)
            {
                var value = string.IsNullOrEmpty(a.Value) ? null : a.Value;
                switch (a.AttributeCode)
                {
                    case S57AttrVerclr: verclr = value; verclrPresent = true; break;
                    case S57AttrVerccl: verccl = value; vercclPresent = true; break;
                    case S57AttrVercop: vercop = value; vercopPresent = true; break;
                    case S57AttrHorclr: horclr = value; break;
                    case S57AttrHoracc: horacc = value; break;
                    case S57AttrVeracc: veracc = value; break;
                    case S57AttrVerdat: verdat = value; break;
                    case S57AttrScamin: scamin = value; break;
                    case S57AttrDatsta: datsta = value; break;
                    case S57AttrDatend: datend = value; break;
                }
            }

            bool opening = IsOpeningBridge(feat);
            if (!(opening ? vercclPresent : verclrPresent))
            {
                if (_diagnostics is not null)
                {
                    foreach (var a in feat.Attributes)
                    {
                        if (BridgeSpanAttributes.Contains(a.AttributeCode) && !string.IsNullOrEmpty(a.Value))
                            _diagnostics.RecordRuleDroppedAttribute((ushort)a.AttributeCode);
                    }
                }
                return null;
            }

            var spanClass = opening ? S101ClassSpanOpening : S101ClassSpanFixed;
            var builder = new List<S101Attribute>();

            // VERACC qualifies a known vertical clearance value only.
            bool hasVerticalValue = opening ? verccl is not null || vercop is not null : verclr is not null;
            if (veracc is not null && !hasVerticalValue)
                _diagnostics?.RecordRuleDroppedAttribute(S57AttrVeracc);

            if (datsta is not null || datend is not null)
                AppendDateRangeInstance(builder, S101AttrFixedDateRange, datsta, datend);

            if (horclr is not null)
            {
                AppendHorizontalClearanceInstance(builder, S101AttrHorizontalClearanceFixed, horclr);
                if (horacc is not null)
                    builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrHorizontalDistanceUncertainty), 1, horacc));
            }
            else if (horacc is not null)
            {
                // No horizontal clearance value for the accuracy to qualify.
                _diagnostics?.RecordRuleDroppedAttribute(S57AttrHoracc);
            }

            if (opening)
            {
                if (verclr is not null)
                {
                    // VERCLR has no home on an opening span (it binds the
                    // closed/open clearances only).
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrVerclr);
                }

                AppendVerticalClearanceInstance(
                    builder, S101AttrVerticalClearanceClosed, unlimited: null, verccl ?? string.Empty, veracc);
                // Absent VERCOP → unlimited open clearance; VERCOP populated
                // (even with an empty value) → limited (S-65 Annex B §4.8.10).
                AppendVerticalClearanceInstance(
                    builder, S101AttrVerticalClearanceOpen, unlimited: !vercopPresent, vercop,
                    vercop is not null ? veracc : null);
            }
            else
            {
                if (verccl is not null) _diagnostics?.RecordRuleDroppedAttribute(S57AttrVerccl);
                if (vercop is not null) _diagnostics?.RecordRuleDroppedAttribute(S57AttrVercop);
                AppendVerticalClearanceInstance(
                    builder, S101AttrVerticalClearanceFixed, unlimited: null, verclr ?? string.Empty, veracc);
            }

            if (verdat is not null)
            {
                if (_allowedEnumValues is null || _allowedEnumValues.IsAllowed(S101AttrVerticalDatum, verdat))
                    builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrVerticalDatum), 1, verdat));
                else
                    _diagnostics?.RecordDroppedEnumValue(S101AttrVerticalDatum, verdat);
            }

            if (scamin is not null)
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrScaleMinimum), 1, scamin));

            return new BridgeSpan(spanClass, builder);
        }

        // Emits a verticalClearanceFixed / verticalClearanceClosed /
        // verticalClearanceOpen complex instance: marker, then (open only) the
        // mandatory verticalClearanceUnlimited boolean, the clearance value
        // (null → omitted; empty → unknown) and, for a known value, an optional
        // nested verticalUncertainty { uncertaintyFixed } carrying VERACC — in
        // FC sub-attribute binding order.
        private void AppendVerticalClearanceInstance(
            List<S101Attribute> builder,
            string complexName,
            bool? unlimited,
            string? value,
            string? uncertainty)
        {
            builder.Add(new S101Attribute(GetOrAssignAttributeCode(complexName), 1, string.Empty));
            if (unlimited is not null)
            {
                builder.Add(new S101Attribute(
                    GetOrAssignAttributeCode(S101AttrVerticalClearanceUnlimited), 1, unlimited.Value ? "true" : "false"));
            }
            if (value is not null)
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrVerticalClearanceValue), 1, value));
            if (!string.IsNullOrEmpty(value) && uncertainty is not null)
            {
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrVerticalUncertainty), 1, string.Empty));
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrUncertaintyFixed), 1, uncertainty));
            }
        }

        // Supplies the mandatory Landmark attributes for a point BRIDGE
        // redirected to Landmark: categoryOfLandmark = 26 (bridge) and, when
        // CONVIS did not provide one, visualProminence = 2 (not visually
        // conspicuous).
        private void AppendPointBridgeLandmarkAttributes(List<S101Attribute> builder)
        {
            var visualCode = GetOrAssignAttributeCode(S101AttrVisualProminence);
            if (!builder.Any(a => a.NumericCode == visualCode))
                builder.Insert(0, new S101Attribute(visualCode, 1, VisualProminenceNotConspicuous));

            var categoryCode = GetOrAssignAttributeCode(S101AttrCategoryOfLandmark);
            if (!builder.Any(a => a.NumericCode == categoryCode))
                builder.Insert(0, new S101Attribute(categoryCode, 1, CategoryOfLandmarkBridge));
        }

        // Dissolves the geometries of the BRIDGE members of a bridge C_AGGR into
        // one Bridge geometry. A single member keeps its own geometry. Several
        // curve members are chained by shared nodes into one curve; several
        // surface members have their shared boundary edges removed and the
        // remaining edges chained into rings. When the members mix primitives,
        // or the result is not a single curve / single exterior ring, the
        // Bridge is emitted without geometry (S-101 Bridge permits noGeometry)
        // rather than as a flattened multi-part shape the renderers would join.
        private IReadOnlyList<S101SpatialAssociation> MergeBridgeGeometry(
            IReadOnlyList<EncDotNet.S57.S57FeatureRecord> members)
        {
            if (members.Count == 1)
                return TranslateSpatialPointers(members[0]);

            var primitive = (int)members[0].Primitive;
            if (members.Any(m => (int)m.Primitive != primitive))
                return [];

            var exterior = new List<S101CurveUsage>();
            var interior = new List<S101CurveUsage>();
            var seenCurves = new HashSet<uint>();
            foreach (var m in members)
            {
                foreach (var ptr in m.SpatialPointers)
                {
                    if (ptr.Name.RecordNameCode != S57RecordNameCodes.Edge) continue;
                    if (!_edgeIdMap.TryGetValue(ptr.Name.RecordId, out var cid)) continue;
                    var ornt = (int)ptr.Orientation == OrientationReverse ? OrientationReverse : OrientationForward;
                    var usage = new S101CurveUsage(S101RcnmCurveSegment, cid, ornt);
                    bool isInterior = primitive == 3 && (int)ptr.Usage == UsageInterior;
                    // Curves: keep each edge once. Surfaces: keep every
                    // exterior occurrence so shared boundaries can be removed.
                    if (primitive == 2 && !seenCurves.Add(cid)) continue;
                    (isInterior ? interior : exterior).Add(usage);
                }
            }

            if (primitive == 2)
            {
                var chains = ChainEdgesIntoRings(OrientChainSeed(exterior));
                if (chains.Count != 1) return [];
                return chains[0]
                    .Select(u => new S101SpatialAssociation(S101RcnmCurveSegment, u.RecordId, u.Orientation))
                    .ToList();
            }

            if (primitive == 3)
            {
                // An edge bounding two adjacent members is interior to the
                // union: drop every occurrence of it.
                var counts = exterior.GroupBy(u => u.RecordId).ToDictionary(g => g.Key, g => g.Count());
                var outline = exterior.Where(u => counts[u.RecordId] == 1).ToList();
                var outlineRings = ChainEdgesIntoRings(outline);
                if (outlineRings.Count != 1) return [];
                return BuildSurface(outlineRings, ChainEdgesIntoRings(interior));
            }

            return [];
        }

        // Reorders an open set of curve edges so that the chain seed starts at
        // a free end (a node touched by only one edge), oriented away from it,
        // letting ChainEdgesIntoRings (which only extends forward) walk the
        // whole path. A closed set is returned unchanged.
        private List<S101CurveUsage> OrientChainSeed(List<S101CurveUsage> edges)
        {
            var degree = new Dictionary<uint, int>();
            var nodes = new List<(uint? Begin, uint? End)>(edges.Count);
            foreach (var e in edges)
            {
                var n = (EdgeNode(e.RecordId, TopologyBegin), EdgeNode(e.RecordId, TopologyEnd));
                nodes.Add(n);
                if (n.Item1 is uint b) degree[b] = degree.GetValueOrDefault(b) + 1;
                if (n.Item2 is uint en) degree[en] = degree.GetValueOrDefault(en) + 1;
            }

            for (int i = 0; i < edges.Count; i++)
            {
                var (begin, end) = nodes[i];
                byte? orientation =
                    begin is uint b && degree[b] == 1 ? OrientationForward
                    : end is uint en && degree[en] == 1 ? OrientationReverse
                    : null;
                if (orientation is null) continue;

                var reordered = new List<S101CurveUsage>(edges.Count)
                {
                    new(S101RcnmCurveSegment, edges[i].RecordId, orientation.Value),
                };
                for (int j = 0; j < edges.Count; j++)
                {
                    if (j != i) reordered.Add(edges[j]);
                }
                return reordered;
            }

            return edges;
        }

        private static (int agency, long fid, int sub) Lnam(EncDotNet.S57.S57RecordName n)
            => ((int)n.AgencyCode, (long)n.FeatureId, (int)n.FeatureSubdivision);

        // Second pass over deferred S-57 C_AGGR collection objects. S-101 has no
        // generic aggregation; a C_AGGR whose members are navigational tracks
        // (NavigationLine / RecommendedTrack / RecommendedRouteCentreline) plus
        // the navigation aids that define them maps to a synthesised,
        // geometry-less `RangeSystem` feature — the FC's dedicated `theCollection`
        // head for a `RangeSystemAggregation` — linked to each member in the
        // member's `theComponent` role. C_AGGR members are referenced by LNAM
        // (the FFPT long name), so they are resolved against the LNAM→record-id
        // map built while translating features. A C_AGGR may itself be a member
        // of another C_AGGR (RangeSystem is a permitted component), so
        // qualification is recursive and record ids for qualifying aggregations
        // are allocated up front (phase 1) before the associations are wired
        // (phase 2) to keep nested references resolvable. C_AGGR groupings that
        // do not match the range-system pattern — and all C_ASSO — have no S-101
        // home and are counted as unmapped instead. (S-101 FC Ed 1.x.)
        private void EmitRangeSystems(List<EncDotNet.S57.S57FeatureRecord> aggregations)
        {
            if (aggregations.Count == 0) return;

            // RangeSystem is an S-101 construct; a target whose Feature Catalogue
            // does not define it (S-401) has no home for any C_AGGR.
            if (!_featureBindings.DefinesFeatureType(S101ClassRangeSystem))
            {
                foreach (var _ in aggregations)
                    _diagnostics?.RecordUnmappedObjectClass(CAggrObjl);
                return;
            }

            // Index every S-57 feature by LNAM so C_AGGR members (referenced by
            // long name, not RCNM/RCID) can be resolved to their object class.
            var featureByLnam = new Dictionary<(int, long, int), EncDotNet.S57.S57FeatureRecord>();
            foreach (var r in _s57.FeatureRecords)
                featureByLnam[Lnam(r.RecordName)] = r;

            var pendingByLnam = new Dictionary<(int, long, int), EncDotNet.S57.S57FeatureRecord>();
            foreach (var a in aggregations)
                pendingByLnam[Lnam(a.RecordName)] = a;

            var qualifyCache = new Dictionary<(int, long, int), bool>();

            // Resolves a member's S-101 class: a nested C_AGGR contributes
            // "RangeSystem" when it qualifies; any other feature contributes the
            // class it maps to (or null when unmapped/dropped).
            string? MemberClass((int, long, int) key, HashSet<(int, long, int)> visiting)
            {
                if (pendingByLnam.ContainsKey(key))
                    return Qualifies(key, visiting) ? S101ClassRangeSystem : null;
                if (!featureByLnam.TryGetValue(key, out var mf)) return null;
                if (!_recordIdByLnam.ContainsKey(key)) return null;
                var view = _mapping.BuildAcronymView(mf.Attributes);
                return _mapping.ResolveFeature(
                    (ushort)(int)mf.ObjectCode, view, MapPrimitive(mf.Primitive))?.S101Code;
            }

            bool Qualifies((int, long, int) key, HashSet<(int, long, int)> visiting)
            {
                if (qualifyCache.TryGetValue(key, out var cached)) return cached;
                if (!pendingByLnam.TryGetValue(key, out var aggr)) return false;
                if (!visiting.Add(key)) return false; // cycle guard

                bool hasTrack = false;
                bool allPermitted = aggr.FeaturePointers.Count > 0;
                foreach (var fp in aggr.FeaturePointers)
                {
                    var cls = MemberClass(Lnam(fp.Name), visiting);
                    if (cls is null || !RangeSystemComponentClasses.Contains(cls))
                    {
                        allPermitted = false;
                        break;
                    }
                    if (RangeSystemTrackClasses.Contains(cls)) hasTrack = true;
                }

                visiting.Remove(key);
                var result = allPermitted && hasTrack;
                qualifyCache[key] = result;
                return result;
            }

            // Phase 1: allocate a RangeSystem record id for every qualifying
            // C_AGGR and register it by LNAM so nested aggregations resolve.
            var qualifying = new List<(EncDotNet.S57.S57FeatureRecord Aggr, uint RecordId)>();
            foreach (var a in aggregations)
            {
                var key = Lnam(a.RecordName);
                if (!Qualifies(key, new HashSet<(int, long, int)>()))
                {
                    // Not a range system (e.g. a TSS aggregation or land grouping)
                    // — no S-101 home this round.
                    _diagnostics?.RecordUnmappedObjectClass(CAggrObjl);
                    continue;
                }
                var recordId = _nextFeatureId++;
                _recordIdByLnam[key] = recordId;
                qualifying.Add((a, recordId));
            }

            if (qualifying.Count == 0) return;

            var assocCode = GetOrAssignFeatureAssociationCode(S101AssocRangeSystemAggregation);
            var componentRole = GetOrAssignRoleCode(S101RoleTheComponent);
            var typeCode = GetOrAssignFeatureTypeCode(S101ClassRangeSystem);

            // Phase 2: emit each RangeSystem with a `theComponent` association to
            // every member that resolved to an emitted S-101 feature. Every
            // qualifying aggregation is emitted (never dropped) so that the record
            // ids registered in phase 1 always refer to a real record, keeping
            // nested-aggregation references intact.
            foreach (var (aggr, recordId) in qualifying)
            {
                var assocs = new List<S101FeatureAssociation>();
                foreach (var fp in aggr.FeaturePointers)
                {
                    if (_recordIdByLnam.TryGetValue(Lnam(fp.Name), out var memberId))
                        assocs.Add(new S101FeatureAssociation(assocCode, memberId, componentRole));
                }

                if (_diagnostics is not null) _diagnostics.RangeSystemsEmitted++;
                Features.Add(new S101FeatureRecord
                {
                    RecordId = recordId,
                    FeatureTypeCode = typeCode,
                    ProducingAgency = (ushort)aggr.RecordName.AgencyCode,
                    FeatureIdentificationNumber = (uint)aggr.RecordName.FeatureId,
                    FeatureIdentificationSubdivision = (ushort)aggr.RecordName.FeatureSubdivision,
                    Attributes = [],
                    SpatialAssociations = [],
                    FeatureAssociations = assocs,
                    InformationAssociations = [],
                });
            }
        }

        // Groups co-located sector-bearing S-57 LIGHTS features so that a single
        // S-101 LightSectored feature carries every arc as its own
        // `sectorCharacteristics` instance. The merge key is the S-101 point the
        // feature resolves to (a shared S-57 connected/isolated node), which is
        // how S-57 encodes the several sectors of one physical light. Returns the
        // set of feature indices that are absorbed (skipped by the main loop) and,
        // per surviving primary index, the extra sector inputs to append.
        //
        // Policy (corpus-derived from NOAA ENC): the lowest-document-order sector
        // light at a node is the primary; the rest are absorbed. Non-sector
        // attributes (height, status, name, …) come from the primary — in the
        // corpus these never diverge within a group (HEIGHT invariant), because
        // the group is one physical light. Each sector keeps its own light
        // characteristic, so groups whose members differ in LITCHR/SIGGRP/SIGPER
        // simply yield several `sectorCharacteristics` instances (all conformant).
        private (HashSet<int> Absorbed, Dictionary<int, List<SectorInput>> Extras) BuildSectorMergeGroups(
            IReadOnlyList<EncDotNet.S57.S57FeatureRecord> featureRecords)
        {
            var absorbed = new HashSet<int>();
            var extras = new Dictionary<int, List<SectorInput>>();

            // Bucket sector-bearing LightSectored feature indices by resolved point.
            var byPoint = new Dictionary<uint, List<int>>();
            for (int fi = 0; fi < featureRecords.Count; fi++)
            {
                var feat = featureRecords[fi];
                if ((int)feat.ObjectCode != LightsObjl) continue;

                bool hasSector = false;
                foreach (var a in feat.Attributes)
                {
                    if ((a.AttributeCode == S57AttrSectr1 || a.AttributeCode == S57AttrSectr2)
                        && !string.IsNullOrEmpty(a.Value))
                    {
                        hasSector = true;
                        break;
                    }
                }
                if (!hasSector) continue;

                var acronymView = _mapping.BuildAcronymView(feat.Attributes);
                var resolved = _mapping.ResolveFeature(
                    (ushort)(int)feat.ObjectCode, acronymView, MapPrimitive(feat.Primitive));
                if (resolved is null
                    || !_featureBindings.Binds(resolved.S101Code, S101AttrSectorCharacteristics))
                    continue;

                if (!TryResolvePointId(feat, out var pid)) continue;

                if (!byPoint.TryGetValue(pid, out var bucket))
                {
                    bucket = new List<int>();
                    byPoint[pid] = bucket;
                }
                bucket.Add(fi);
            }

            foreach (var bucket in byPoint.Values)
            {
                if (bucket.Count < 2) continue;

                // bucket is in ascending document order (indices appended in order).
                var primaryIndex = bucket[0];
                var list = new List<SectorInput>();
                for (int i = 1; i < bucket.Count; i++)
                {
                    var memberIndex = bucket[i];
                    absorbed.Add(memberIndex);

                    var si = ExtractSectorInput(featureRecords[memberIndex]);
                    if (si is null)
                        continue; // no LITCHR to anchor a sectorCharacteristics
                    if (_allowedEnumValues is not null
                        && !_allowedEnumValues.IsAllowed(S101AttrLightCharacteristic, si.LightCharacteristic))
                    {
                        // Parity with the single-feature path: an FC-rejected
                        // LITCHR drops the whole instance and its diverted inputs.
                        _diagnostics?.RecordDroppedEnumValue(S101AttrLightCharacteristic, si.LightCharacteristic);
                        RecordDivertedSectorAttributesDropped(
                            si.ColourList, si.LightVisibilityList, si.ValueOfNominalRange,
                            si.SectorBearingOne, si.SectorBearingTwo,
                            si.SignalGroup, si.SignalPeriod, si.SignalSequence);
                        continue;
                    }
                    list.Add(si);
                }

                if (_diagnostics is not null)
                    _diagnostics.SectorLightsMerged += bucket.Count - 1;
                extras[primaryIndex] = list;
            }

            return (absorbed, extras);
        }

        // Groups each S-57 master buoy/beacon feature with the TOPMAR slave it
        // references, so the TOPMAR's TOPSHP/COLOUR/COLPAT can be folded into the
        // master's `topmark` complex attribute (S-101 models the topmark as an
        // attribute of the parent, not a standalone feature). In S-57 the
        // relationship is a master/slave feature-to-feature pointer (FFPT) carried
        // by the master pointing to the TOPMAR (Relationship = Slave). Returns the
        // set of absorbed TOPMAR feature indices (which emit no feature of their
        // own) and, per master feature index, the TOPMAR record that supplies its
        // topmark. Only masters whose resolved S-101 class binds the `topmark`
        // complex are grouped; a TOPMAR referenced by no such master falls through
        // and is recorded as an unmapped object class as before. A master carries
        // at most one topmark, so the first slave TOPMAR wins.
        // (IHO S-57→S-101 Conversion Guidance: TOPMAR → parent attribute.)
        private (HashSet<int> Absorbed, Dictionary<int, EncDotNet.S57.S57FeatureRecord> ByMaster) BuildTopmarkGroups(
            IReadOnlyList<EncDotNet.S57.S57FeatureRecord> featureRecords)
        {
            var absorbed = new HashSet<int>();
            var byMaster = new Dictionary<int, EncDotNet.S57.S57FeatureRecord>();

            // Index every feature by LNAM so a master's FFPT (which references its
            // slave by long name) can be resolved to the pointed-to record + index.
            var indexByLnam = new Dictionary<(int, long, int), int>();
            for (int i = 0; i < featureRecords.Count; i++)
                indexByLnam[Lnam(featureRecords[i].RecordName)] = i;

            for (int mi = 0; mi < featureRecords.Count; mi++)
            {
                var master = featureRecords[mi];
                if ((int)master.ObjectCode == TopmarObjl) continue;
                if (master.FeaturePointers.Count == 0) continue;

                foreach (var fp in master.FeaturePointers)
                {
                    if (fp.Relationship != EncDotNet.S57.S57RelationshipIndicator.Slave)
                        continue;
                    if (!indexByLnam.TryGetValue(Lnam(fp.Name), out var slaveIndex))
                        continue;
                    var slave = featureRecords[slaveIndex];
                    if ((int)slave.ObjectCode != TopmarObjl)
                        continue;

                    // A TOPMAR can only be consumed by one master; if an earlier
                    // master already absorbed it, do not fold it again (which would
                    // duplicate the topmark attributes across features).
                    if (absorbed.Contains(slaveIndex))
                        continue;

                    // Only fold the topmark onto masters whose S-101 class binds
                    // the `topmark` complex; otherwise leave the TOPMAR unmapped.
                    var acronymView = _mapping.BuildAcronymView(master.Attributes);
                    var resolved = _mapping.ResolveFeature(
                        (ushort)(int)master.ObjectCode, acronymView, MapPrimitive(master.Primitive));
                    if (resolved is null
                        || !_featureBindings.Binds(resolved.S101Code, S101AttrTopmark))
                        continue;

                    // A master carries a single topmark — take the first slave.
                    byMaster[mi] = slave;
                    absorbed.Add(slaveIndex);
                    break;
                }
            }

            return (absorbed, byMaster);
        }

        // first spatial pointer to a connected/isolated node). Mirrors the lookup
        // in TranslatePointSpatial.
        private bool TryResolvePointId(EncDotNet.S57.S57FeatureRecord feat, out uint id)
        {
            foreach (var ptr in feat.SpatialPointers)
            {
                if (TryGetPointId(ptr.Name, out id)) return true;
            }
            id = 0;
            return false;
        }

        // Extracts the sector-light inputs (light characteristic, signal
        // group/period/sequence, colour/visibility/range lists and the two sector
        // bearings) from a single S-57 LIGHTS feature's raw attributes, for use by
        // the sector-light merge. Returns null when no LITCHR is present (LITCHR
        // anchors the mandatory `lightCharacteristic`, so without it no
        // `sectorCharacteristics` instance can be assembled).
        private static SectorInput? ExtractSectorInput(EncDotNet.S57.S57FeatureRecord feat)
        {
            string? litchr = null, colour = null, litvis = null, valnmr = null;
            string? sectr1 = null, sectr2 = null, siggrp = null, sigper = null, sigseq = null;
            foreach (var a in feat.Attributes)
            {
                if (string.IsNullOrEmpty(a.Value)) continue;
                switch (a.AttributeCode)
                {
                    case S57AttrLitchr: litchr = a.Value; break;
                    case S57AttrColour: colour = a.Value; break;
                    case S57AttrLitvis: litvis = a.Value; break;
                    case S57AttrValnmr: valnmr = a.Value; break;
                    case S57AttrSectr1: sectr1 = a.Value; break;
                    case S57AttrSectr2: sectr2 = a.Value; break;
                    case S57AttrSiggrp: siggrp = a.Value; break;
                    case S57AttrSigper: sigper = a.Value; break;
                    case S57AttrSigseq: sigseq = a.Value; break;
                }
            }
            if (litchr is null) return null;
            return new SectorInput(litchr, siggrp, sigper, sigseq, colour, litvis, valnmr, sectr1, sectr2);
        }


        /// <summary>
        /// S-57 SOUNDG (OBJL=129) features carry many depth measurements via SG3D
        /// triples on one or more vector records. The S-101 portrayal pipeline
        /// expects <c>Sounding</c> features whose <c>PrimitiveType</c> is
        /// <c>MultiPoint</c>; emit a single S-101 Sounding feature backed by an
        /// S-101 multi-point record (RCNM=115) so the SOUNDG03 rule can iterate
        /// the points and draw symbolised depth values.
        /// </summary>
        private void EmitSoundingMultiPoint(EncDotNet.S57.S57FeatureRecord feat)
        {
            var triples = new List<(int Y, int X, int Z)>();
            foreach (var ptr in feat.SpatialPointers)
            {
                if (!_vectorIndex.TryGetValue(
                        (ptr.Name.RecordNameCode, ptr.Name.RecordId),
                        out var vr))
                    continue;
                foreach (var s in vr.Soundings)
                    triples.Add((s.Y, s.X, s.Depth));
            }

            if (triples.Count == 0)
            {
                if (_diagnostics is not null) _diagnostics.SoundingFeaturesWithoutPoints++;
                return;
            }

            var mpid = _nextMultiPointId++;
            MultiPoints[mpid] = new S101MultiPointRecord
            {
                RecordId = mpid,
                Points = triples,
            };

            if (_diagnostics is not null)
            {
                _diagnostics.SoundingFeaturesEmitted++;
                _diagnostics.SoundingPointsEmitted += triples.Count;
            }

            var typeCode = GetOrAssignFeatureTypeCode(SoundingS101Code);
            Features.Add(new S101FeatureRecord
            {
                RecordId = _nextFeatureId++,
                FeatureTypeCode = typeCode,
                ProducingAgency = (ushort)feat.RecordName.AgencyCode,
                FeatureIdentificationNumber = (uint)feat.RecordName.FeatureId,
                FeatureIdentificationSubdivision = (ushort)feat.RecordName.FeatureSubdivision,
                // The S-101 Sounding feature carries no attributes — depth values
                // live on the individual points within the MultiPoint geometry,
                // which the SOUNDG03 portrayal rule reads as point.ScaledZ.
                Attributes = [],
                SpatialAssociations = [new S101SpatialAssociation(S101RcnmMultiPoint, mpid, OrientationForward)],
                FeatureAssociations = [],
                InformationAssociations = [],
            });
        }

        private IReadOnlyList<S101Attribute> TranslateAttributes(
            IReadOnlyList<EncDotNet.S57.S57AttributeValue> attrs,
            ResolvedFeature feature,
            ushort ownerObjl,
            out IReadOnlyList<S101InformationAssociation> informationAssociations,
            IReadOnlyList<SectorInput>? extraSectors = null,
            EncDotNet.S57.S57FeatureRecord? topmarkSource = null)
        {
            informationAssociations = [];
            if (attrs.Count == 0 && topmarkSource is null
                && !(IsBridgeObjl(ownerObjl) && feature.S101Code == S101ClassLandmark))
                return [];

            // Pre-pass: collect INFORM / NINFOM / TXTDSC / NTXTDS values so we
            // can emit them as one or more S-101 `information` complex-attribute
            // instances (Conversion Guidance §2.3), and OBJNAM / NOBJNM so we
            // can emit them as `featureName` complex-attribute instances when
            // the resolved feature class binds that complex in the S-101 FC.
            string? informText = null;
            string? ninfomText = null;
            string? txtdscFile = null;
            string? ntxtdsFile = null;
            string? objnamText = null;
            string? nobjnmText = null;
            bool bindsFeatureName = _featureBindings.Binds(feature.S101Code, S101AttrFeatureName);
            // rhythmOfLight sources — only assembled on feature classes that
            // bind the complex in the bundled S-101 Feature Catalogue.
            bool bindsRhythm = _featureBindings.Binds(feature.S101Code, S101AttrRhythmOfLight);
            string? litchrValue = null;
            string? siggrpValue = null;
            string? sigperValue = null;
            // signalSequence source — SIGSEQ. On the light feature classes it
            // nests inside `rhythmOfLight`; on FogSignal / RadarTransponderBeacon
            // it binds at the top level. Gated so it is only diverted from the
            // per-attribute pass-through where it has a conformant home.
            bool bindsSignalSequenceTop = _featureBindings.Binds(feature.S101Code, S101AttrSignalSequence);
            string? sigseqValue = null;
            // Date-range sources — each S-57 pair maps to a distinct S-101
            // date-range complex, gated on the resolved feature class actually
            // binding that complex (per the bundled FC).
            bool bindsFixedDate = _featureBindings.Binds(feature.S101Code, S101AttrFixedDateRange);
            bool bindsPeriodicDate = _featureBindings.Binds(feature.S101Code, S101AttrPeriodicDateRange);
            bool bindsSurveyDate = _featureBindings.Binds(feature.S101Code, S101AttrSurveyDateRange);
            string? datstaValue = null;
            string? datendValue = null;
            string? perstaValue = null;
            string? perendValue = null;
            string? surstaValue = null;
            string? surendValue = null;
            // zoneOfConfidence source — CATZOC, assembled on the (single) feature
            // class that binds the complex (QualityOfBathymetricData).
            bool bindsZoc = _featureBindings.Binds(feature.S101Code, S101AttrZoneOfConfidence);
            string? catzocValue = null;
            // topmark source — the TOPSHP/COLOUR/COLPAT of a master's slave TOPMAR
            // record (BuildTopmarkGroups), assembled into the `topmark` complex on
            // the buoy/beacon/light-float classes that bind it.
            bool bindsTopmark = topmarkSource is not null
                && _featureBindings.Binds(feature.S101Code, S101AttrTopmark);
            // surfaceCharacteristics source — NATSUR/NATQUA, assembled on the
            // (single) feature class that binds the complex (SeabedArea).
            bool bindsSurfaceChar = _featureBindings.Binds(feature.S101Code, S101AttrSurfaceCharacteristics);
            string? natsurList = null;
            string? natquaList = null;
            // sectorCharacteristics sources — assembled on the feature class
            // that binds the complex (LightSectored). LITCHR anchors the
            // mandatory `lightCharacteristic`; COLOUR/LITVIS are lists;
            // SECTR1/SECTR2 the sector bearings; VALNMR the nominal range;
            // SIGGRP/SIGPER/SIGSEQ nest at the sectorCharacteristics level.
            bool bindsSectorChar = _featureBindings.Binds(feature.S101Code, S101AttrSectorCharacteristics);
            string? sectrLitchr = null;
            string? sectrColour = null;
            string? sectrLitvis = null;
            string? sectrValnmr = null;
            string? sectrSectr1 = null;
            string? sectrSectr2 = null;
            string? sectrSiggrp = null;
            string? sectrSigper = null;
            string? sectrSigseq = null;
            // horizontalClearance source — HORCLR. The destination complex
            // depends on the resolved feature's FC binding: Gate binds
            // `horizontalClearanceOpen`; spans, tunnels, shoreline
            // constructions, canals and dock/lock areas bind
            // `horizontalClearanceFixed`. A feature that binds neither has no
            // conformant home for HORCLR.
            bool bindsHorClearanceOpen = _featureBindings.Binds(feature.S101Code, S101AttrHorizontalClearanceOpen);
            bool bindsHorClearanceFixed = _featureBindings.Binds(feature.S101Code, S101AttrHorizontalClearanceFixed);
            bool bindsHorClearance = bindsHorClearanceOpen || bindsHorClearanceFixed;
            string? horclrValue = null;
            // reportedDate source — SORDAT, emitted inline as a top-level simple
            // attribute on the (many) feature classes that bind `reportedDate`.
            bool bindsReportedDate = _featureBindings.Binds(feature.S101Code, S101AttrReportedDate);
            // valueOfLocalMagneticAnomaly source — VALLMA, assembled into the
            // complex on LocalMagneticAnomaly (the only feature that binds it).
            bool bindsValueOfLocalMagneticAnomaly = _featureBindings.Binds(feature.S101Code, S101AttrValueOfLocalMagneticAnomaly);
            string? vallmaValue = null;
            // radarWaveLength source — RADWAL, assembled into the complex on
            // RadarTransponderBeacon (the only feature that binds it).
            bool bindsRadarWaveLength = _featureBindings.Binds(feature.S101Code, S101AttrRadarWaveLength);
            string? radwalValue = null;
            // speed source — CURVEL, assembled into the `speed` complex on
            // CurrentNonGravitational / TidalStreamFloodEbb.
            bool bindsSpeed = _featureBindings.Binds(feature.S101Code, S101AttrSpeed);
            string? curvelValue = null;
            // multiplicityOfFeatures source — MLTYLT, assembled into the complex
            // on the light classes that bind it.
            bool bindsMultiplicityOfFeatures = _featureBindings.Binds(feature.S101Code, S101AttrMultiplicityOfFeatures);
            string? mltyltValue = null;
            // Bridge category source — CATBRG, assembled on the feature class
            // that binds `openingBridge` (Bridge).
            bool bindsOpeningBridge = _featureBindings.Binds(feature.S101Code, S101AttrOpeningBridge);
            string? catbrgValue = null;
            // powerCharacteristics sources — the shore-power attributes,
            // assembled on the class that binds the complex (BunkerStation).
            bool bindsPowerChar = _featureBindings.Binds(feature.S101Code, S101AttrPowerCharacteristics);
            Dictionary<string, string>? powerCharValues = null;
            // Bridge clearance / accuracy / vertical-datum attributes belong to
            // the decomposed span (BuildBridgeSpan), never to the Bridge itself.
            bool isBridge = IsBridgeObjl(ownerObjl) && feature.S101Code == S101ClassBridge;
            // Vertical clearance sources — any rule targeting a vertical
            // clearance complex, collected in the pass-through loop below; VERACC
            // qualifies them, on the feature classes that bind one.
            bool bindsVerticalClearance = VerticalClearanceComplexes.Any(
                c => _featureBindings.Binds(feature.S101Code, c));
            var verticalClearances = new List<(string Complex, string Value)>();
            string? veraccValue = null;
            foreach (var a in attrs)
            {
                switch (a.AttributeCode)
                {
                    case S57AttrInform: informText = a.Value; break;
                    case S57AttrNinfom: ninfomText = a.Value; break;
                    case S57AttrTxtdsc: txtdscFile = a.Value; break;
                    case S57AttrNtxtds: ntxtdsFile = a.Value; break;
                    case S57AttrObjnam: if (!string.IsNullOrEmpty(a.Value)) objnamText = a.Value; break;
                    case S57AttrNobjnm: if (!string.IsNullOrEmpty(a.Value)) nobjnmText = a.Value; break;
                    case S57AttrLitchr:
                        if (bindsRhythm && !string.IsNullOrEmpty(a.Value)) litchrValue = a.Value;
                        else if (bindsSectorChar && !string.IsNullOrEmpty(a.Value)) sectrLitchr = a.Value;
                        break;
                    case S57AttrSiggrp:
                        if (bindsRhythm && !string.IsNullOrEmpty(a.Value)) siggrpValue = a.Value;
                        else if (bindsSectorChar && !string.IsNullOrEmpty(a.Value)) sectrSiggrp = a.Value;
                        break;
                    case S57AttrSigper:
                        if (bindsRhythm && !string.IsNullOrEmpty(a.Value)) sigperValue = a.Value;
                        else if (bindsSectorChar && !string.IsNullOrEmpty(a.Value)) sectrSigper = a.Value;
                        break;
                    case S57AttrSigseq:
                        if ((bindsRhythm || bindsSignalSequenceTop) && !string.IsNullOrEmpty(a.Value)) sigseqValue = a.Value;
                        else if (bindsSectorChar && !string.IsNullOrEmpty(a.Value)) sectrSigseq = a.Value;
                        break;
                    case S57AttrColour: if (bindsSectorChar && !string.IsNullOrEmpty(a.Value)) sectrColour = a.Value; break;
                    case S57AttrLitvis: if (bindsSectorChar && !string.IsNullOrEmpty(a.Value)) sectrLitvis = a.Value; break;
                    case S57AttrValnmr: if (bindsSectorChar && !string.IsNullOrEmpty(a.Value)) sectrValnmr = a.Value; break;
                    case S57AttrSectr1: if (bindsSectorChar && !string.IsNullOrEmpty(a.Value)) sectrSectr1 = a.Value; break;
                    case S57AttrSectr2: if (bindsSectorChar && !string.IsNullOrEmpty(a.Value)) sectrSectr2 = a.Value; break;
                    case S57AttrDatsta: if (bindsFixedDate && !string.IsNullOrEmpty(a.Value)) datstaValue = a.Value; break;
                    case S57AttrDatend: if (bindsFixedDate && !string.IsNullOrEmpty(a.Value)) datendValue = a.Value; break;
                    case S57AttrPersta: if (bindsPeriodicDate && !string.IsNullOrEmpty(a.Value)) perstaValue = a.Value; break;
                    case S57AttrPerend: if (bindsPeriodicDate && !string.IsNullOrEmpty(a.Value)) perendValue = a.Value; break;
                    case S57AttrSursta: if (bindsSurveyDate && !string.IsNullOrEmpty(a.Value)) surstaValue = a.Value; break;
                    case S57AttrSurend: if (bindsSurveyDate && !string.IsNullOrEmpty(a.Value)) surendValue = a.Value; break;
                    case S57AttrCatzoc: if (bindsZoc && !string.IsNullOrEmpty(a.Value)) catzocValue = a.Value; break;
                    case S57AttrNatsur: if (bindsSurfaceChar && !string.IsNullOrEmpty(a.Value)) natsurList = a.Value; break;
                    case S57AttrNatqua: if (bindsSurfaceChar && !string.IsNullOrEmpty(a.Value)) natquaList = a.Value; break;
                    case S57AttrHorclr: if (bindsHorClearance && !string.IsNullOrEmpty(a.Value)) horclrValue = a.Value; break;
                    case S57AttrVallma: if (bindsValueOfLocalMagneticAnomaly && !string.IsNullOrEmpty(a.Value)) vallmaValue = a.Value; break;
                    case S57AttrRadwal: if (bindsRadarWaveLength && !string.IsNullOrEmpty(a.Value)) radwalValue = a.Value; break;
                    case S57AttrCurvel: if (bindsSpeed && !string.IsNullOrEmpty(a.Value)) curvelValue = a.Value; break;
                    case S57AttrMltylt: if (bindsMultiplicityOfFeatures && !string.IsNullOrEmpty(a.Value)) mltyltValue = a.Value; break;
                    case S57AttrCatbrg: if (bindsOpeningBridge && !string.IsNullOrEmpty(a.Value)) catbrgValue = a.Value; break;
                    case S57AttrVeracc: if (bindsVerticalClearance && !isBridge && !string.IsNullOrEmpty(a.Value)) veraccValue = a.Value; break;
                    default:
                        if (bindsPowerChar && !string.IsNullOrEmpty(a.Value)
                            && PowerCharacteristicsTarget(a.AttributeCode) is { } powerSub)
                        {
                            (powerCharValues ??= new(StringComparer.Ordinal))[powerSub] = a.Value;
                        }
                        break;
                }
            }

            var builder = new List<S101Attribute>();
            foreach (var a in attrs)
            {
                // Textual-info attributes are handled as complex attribute
                // groups below — skip the per-attribute pass-through.
                if (a.AttributeCode is S57AttrInform or S57AttrNinfom or S57AttrTxtdsc or S57AttrNtxtds)
                    continue;

                // On Bridge, the span-level attributes are carried by the
                // SpanFixed / SpanOpening component (or recorded as dropped by
                // BuildBridgeSpan when no span is emitted).
                if (isBridge && BridgeSpanAttributes.Contains(a.AttributeCode))
                    continue;

                // On feature classes that bind `featureName`, OBJNAM/NOBJNM
                // are assembled into that complex below; otherwise they have no
                // conformant home and fall through to be recorded as unmapped.
                if (bindsFeatureName && a.AttributeCode is (S57AttrObjnam or S57AttrNobjnm))
                    continue;

                // On rhythmOfLight-binding feature classes, LITCHR/SIGGRP/SIGPER
                // are assembled into the `rhythmOfLight` complex attribute below
                // rather than emitted as top-level simple attributes.
                if (bindsRhythm && a.AttributeCode is S57AttrLitchr or S57AttrSiggrp or S57AttrSigper)
                    continue;

                // SIGSEQ is assembled into the `signalSequence` complex —
                // nested inside `rhythmOfLight` on light features, or top-level
                // on FogSignal / RadarTransponderBeacon. On any other feature
                // it falls through and is recorded as unmapped (no conformant
                // home in S-101).
                if ((bindsRhythm || bindsSignalSequenceTop) && a.AttributeCode is S57AttrSigseq)
                    continue;

                // On feature classes that bind a date-range complex, the S-57
                // date pair is assembled into that complex below rather than
                // passed through. (When the feature does not bind the complex
                // the pair falls through and is recorded as unmapped, since the
                // date has no conformant home in S-101 on that feature.)
                if (bindsFixedDate && a.AttributeCode is S57AttrDatsta or S57AttrDatend)
                    continue;
                if (bindsPeriodicDate && a.AttributeCode is S57AttrPersta or S57AttrPerend)
                    continue;
                if (bindsSurveyDate && a.AttributeCode is S57AttrSursta or S57AttrSurend)
                    continue;

                // On QualityOfBathymetricData, CATZOC is assembled into the
                // `zoneOfConfidence` complex below rather than passed through.
                if (bindsZoc && a.AttributeCode is S57AttrCatzoc)
                    continue;

                // On SeabedArea, NATSUR/NATQUA are assembled into the
                // `surfaceCharacteristics` complex below. SeabedArea does not
                // bind a top-level `natureOfSurface`, so passing NATSUR through
                // would be non-conformant; NATQUA has no top-level home at all.
                if (bindsSurfaceChar && a.AttributeCode is S57AttrNatsur or S57AttrNatqua)
                    continue;

                // On LightSectored, the sector-geometry attributes are
                // assembled into the `sectorCharacteristics` complex below.
                // LightSectored binds none of them at the top level (colour,
                // lightCharacteristic, valueOfNominalRange, etc. all live inside
                // the complex per the FC), so passing them through would be
                // non-conformant.
                if (bindsSectorChar && a.AttributeCode is S57AttrLitchr or S57AttrColour
                        or S57AttrLitvis or S57AttrValnmr or S57AttrSectr1 or S57AttrSectr2
                        or S57AttrSiggrp or S57AttrSigper or S57AttrSigseq)
                    continue;

                // On features binding a horizontalClearance complex (Gate →
                // open; spans, tunnels, shoreline constructions, canals and
                // dock/lock areas → fixed), HORCLR is assembled into that
                // complex below rather than passed through — it is not a
                // top-level simple attribute on those features. (On a feature
                // binding neither, HORCLR falls through and is recorded
                // unmapped, having no conformant S-101 home there.)
                if (bindsHorClearance && a.AttributeCode is S57AttrHorclr)
                    continue;

                // On LocalMagneticAnomaly, VALLMA is assembled into the
                // `valueOfLocalMagneticAnomaly` complex below rather than passed
                // through (LocalMagneticAnomaly binds no top-level scalar for it).
                if (bindsValueOfLocalMagneticAnomaly && a.AttributeCode is S57AttrVallma)
                    continue;

                // On RadarTransponderBeacon, RADWAL is assembled into the
                // `radarWaveLength` complex below rather than passed through.
                if (bindsRadarWaveLength && a.AttributeCode is S57AttrRadwal)
                    continue;

                // On CurrentNonGravitational / TidalStreamFloodEbb, CURVEL is
                // assembled into the `speed` complex below rather than passed
                // through.
                if (bindsSpeed && a.AttributeCode is S57AttrCurvel)
                    continue;

                // On the light classes that bind it, MLTYLT is assembled into
                // the `multiplicityOfFeatures` complex below rather than passed
                // through.
                if (bindsMultiplicityOfFeatures && a.AttributeCode is S57AttrMltylt)
                    continue;

                // The shore-power attributes are assembled into the
                // `powerCharacteristics` complex below; they are only
                // sub-attributes, so elsewhere they have no conformant home.
                if (PowerCharacteristicsTarget(a.AttributeCode) is not null)
                {
                    if (!bindsPowerChar)
                        _diagnostics?.RecordRuleDroppedAttribute((ushort)a.AttributeCode);
                    continue;
                }

                // On Bridge, CATBRG is split across the bridge category
                // attributes below rather than passed through value by value.
                if (bindsOpeningBridge && a.AttributeCode is S57AttrCatbrg)
                    continue;

                // VERACC nests in the vertical clearance complex(es) assembled
                // below on the feature classes that bind one.
                if (bindsVerticalClearance && a.AttributeCode is S57AttrVeracc)
                    continue;

                // On feature classes that bind `reportedDate`, SORDAT is emitted
                // here as that top-level simple attribute (S100_TruncatedDate),
                // the S-57 date value carried verbatim. On a feature that does
                // not bind `reportedDate`, SORDAT falls through to the no-rule
                // path below and is recorded unmapped (no conformant S-101 home).
                if (bindsReportedDate && a.AttributeCode is S57AttrSordat)
                {
                    if (!string.IsNullOrEmpty(a.Value))
                        builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrReportedDate), 1, a.Value));
                    continue;
                }

                var attl = (ushort)a.AttributeCode;
                if (!_mapping.AttributeRules.ContainsKey(attl))
                {
                    _diagnostics?.RecordUnmappedAttribute(ownerObjl, attl);
                    continue;
                }

                var resolved = _mapping.ResolveAttribute(attl, a.Value, feature);
                if (resolved?.S101Code == S101AttrHorizontalClearanceWidth
                    && bindsHorClearanceFixed && !bindsHorClearanceOpen
                    && horclrValue is null && !string.IsNullOrEmpty(resolved.Value)
                    && !_featureBindings.Binds(feature.S101Code, S101AttrHorizontalClearanceWidth))
                {
                    horclrValue = resolved.Value;
                    continue;
                }

                if (resolved is null
                    || (ClassBoundSimpleAttributes.Contains(resolved.S101Code)
                        && !_featureBindings.Binds(feature.S101Code, resolved.S101Code)))
                {
                    _diagnostics?.RecordRuleDroppedAttribute(attl);
                    continue;
                }

                // A rule targeting a vertical clearance complex feeds that
                // complex's verticalClearanceValue (assembled below) when the
                // feature binds it; each complex binds at most once.
                if (VerticalClearanceComplexes.Contains(resolved.S101Code))
                {
                    if (_featureBindings.Binds(feature.S101Code, resolved.S101Code)
                        && !verticalClearances.Exists(c => c.Complex == resolved.S101Code))
                    {
                        verticalClearances.Add((resolved.S101Code, resolved.Value));
                    }
                    else
                    {
                        _diagnostics?.RecordRuleDroppedAttribute(attl);
                    }
                    continue;
                }

                // S-57 list-type attributes (e.g. COLOUR, NATSUR, CATLIT)
                // carry multiple enumerate codes as a comma-separated string
                // (e.g. "3,3"). The destination S-101 enumerate attribute
                // encodes each value as a separate occurrence, so split the
                // list and emit each code independently — otherwise a single
                // invalid (or simply multi-valued) code would cause the whole
                // attribute to be dropped. Splitting is a structural
                // S-57→S-101 correctness requirement independent of FC
                // validation, so it must still happen when enum enforcement is
                // disabled (_allowedEnumValues is null). When the FC is
                // available we use its authoritative enumerate flag; otherwise
                // we fall back to a structural check: enumerate codes are
                // integer tokens and never contain commas, so a comma-separated
                // value whose tokens are all integers is a list enum, whereas
                // free text (such as OBJNAM, which may legitimately contain
                // commas) is not.
                if (a.Value.Contains(',')
                    && (_allowedEnumValues is not null
                            ? _allowedEnumValues.IsEnumerated(resolved.S101Code)
                            : IsIntegerList(a.Value)))
                {
                    ushort index = 1;
                    foreach (var rawCode in a.Value.Split(','))
                    {
                        var code = rawCode.Trim();
                        if (code.Length == 0)
                            continue;

                        var sub = _mapping.ResolveAttribute(attl, code, feature);
                        if (sub is null)
                        {
                            _diagnostics?.RecordRuleDroppedAttribute(attl);
                            continue;
                        }

                        // Skip FC allowable-value checks when enforcement is
                        // disabled; the split itself is still required.
                        if (_allowedEnumValues is not null
                            && !_allowedEnumValues.IsAllowed(sub.S101Code, sub.Value))
                        {
                            _diagnostics?.RecordDroppedEnumValue(sub.S101Code, sub.Value);
                            continue;
                        }

                        builder.Add(new S101Attribute(GetOrAssignAttributeCode(sub.S101Code), index++, sub.Value));
                    }
                    continue;
                }

                // Drop S-57 enum values that aren't in the S-101 FC's allowable
                // listed values (per IHO S-57→S-101 Conversion Guidance, Jan 2021).
                if (_allowedEnumValues is not null
                    && !_allowedEnumValues.IsAllowed(resolved.S101Code, resolved.Value))
                {
                    _diagnostics?.RecordDroppedEnumValue(resolved.S101Code, resolved.Value);
                    continue;
                }

                var numeric = GetOrAssignAttributeCode(resolved.S101Code);
                builder.Add(new S101Attribute(numeric, 1, resolved.Value));
            }

            // Emit the textual attributes via the "fuller path" (IHO S-57→S-101
            // Conversion Guidance §2.3): rather than an inline `information`
            // complex on the feature, build a standalone `NauticalInformation`
            // information type carrying the `information` complex instance(s) and
            // bind it to the feature with an `AdditionalInformation` association.
            // English (INFORM/TXTDSC) and national (NINFOM/NTXTDS) text share one
            // NauticalInformation record (the FC binds `information` [0..*]).
            var infoAssociation = BuildNauticalInformationAssociation(
                informText, txtdscFile, ninfomText, ntxtdsFile);
            if (infoAssociation is not null)
                informationAssociations = [infoAssociation.Value];

            // Append `featureName` complex-attribute instances only on feature
            // classes that bind the complex. OBJNAM carries the English name;
            // NOBJNM the national-language name (emitted with an empty language
            // string, as S-57 carries no language tag — mirrors the
            // INFORM/NINFOM handling above).
            if (bindsFeatureName)
            {
                if (objnamText is not null)
                    AppendFeatureNameInstance(builder, name: objnamText, language: LanguageEng);
                if (nobjnmText is not null)
                    AppendFeatureNameInstance(builder, name: nobjnmText, language: string.Empty);
            }

            // Append the `rhythmOfLight` complex-attribute instance. The FC
            // makes `lightCharacteristic` [1..1] mandatory, so an instance is
            // only emitted when a valid LITCHR value is present; SIGGRP/SIGPER
            // are included as the optional `signalGroup`/`signalPeriod`
            // sub-attributes and SIGSEQ as the nested `signalSequence`
            // sub-complex. An out-of-range LITCHR code is dropped (and
            // reported), which also drops the instance since the mandatory
            // sub-attribute would be missing; a SIGSEQ carried by a light with
            // no valid LITCHR therefore has nowhere to nest and is dropped.
            if (bindsRhythm && litchrValue is not null)
            {
                if (_allowedEnumValues is null
                    || _allowedEnumValues.IsAllowed(S101AttrLightCharacteristic, litchrValue))
                {
                    AppendRhythmOfLightInstance(builder, litchrValue, siggrpValue, sigperValue, sigseqValue);
                }
                else
                {
                    _diagnostics?.RecordDroppedEnumValue(S101AttrLightCharacteristic, litchrValue);
                    if (sigseqValue is not null)
                        _diagnostics?.RecordRuleDroppedAttribute(S57AttrSigseq);
                }
            }
            else if (bindsRhythm && sigseqValue is not null)
            {
                // SIGSEQ present on a rhythmOfLight-binding light but no LITCHR
                // to anchor the parent complex — the sequence cannot be nested.
                _diagnostics?.RecordRuleDroppedAttribute(S57AttrSigseq);
            }

            // Append the date-range complex-attribute instances. Each is a
            // marker followed by the S100_TruncatedDate `dateStart`/`dateEnd`
            // sub-attributes (S-57 dates pass through unchanged; both S-57 and
            // S-101 use CCYYMMDD). Mandatory sub-attributes (per the FC's
            // multiplicity) must be present or the instance is dropped and
            // recorded, rather than emitting a non-conformant partial complex.
            //
            //   fixedDateRange    — dateStart [0..1], dateEnd [0..1]: emit if
            //                       either endpoint is present.
            if (bindsFixedDate && (datstaValue is not null || datendValue is not null))
                AppendDateRangeInstance(builder, S101AttrFixedDateRange, datstaValue, datendValue);

            //   periodicDateRange — dateStart [1..1], dateEnd [1..1]: both
            //                       endpoints are mandatory.
            if (bindsPeriodicDate)
            {
                if (perstaValue is not null && perendValue is not null)
                    AppendDateRangeInstance(builder, S101AttrPeriodicDateRange, perstaValue, perendValue);
                else if (perstaValue is not null)
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrPersta);
                else if (perendValue is not null)
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrPerend);
            }

            //   surveyDateRange   — dateStart [0..1], dateEnd [1..1]: dateEnd is
            //                       mandatory, dateStart optional.
            if (bindsSurveyDate)
            {
                if (surendValue is not null)
                    AppendDateRangeInstance(builder, S101AttrSurveyDateRange, surstaValue, surendValue);
                else if (surstaValue is not null)
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrSursta);
            }

            // Append the `zoneOfConfidence` complex-attribute instance. The only
            // CATZOC-sourced sub-attribute, `categoryOfZoneOfConfidenceInData`,
            // is mandatory in practice (nothing else is populated), so an
            // out-of-range CATZOC code drops the instance and is reported.
            if (bindsZoc && catzocValue is not null)
            {
                if (_allowedEnumValues is null
                    || _allowedEnumValues.IsAllowed(S101AttrCategoryOfZocInData, catzocValue))
                {
                    AppendZoneOfConfidenceInstance(builder, catzocValue);
                }
                else
                {
                    _diagnostics?.RecordDroppedEnumValue(S101AttrCategoryOfZocInData, catzocValue);
                }
            }

            // Append the `topmark` complex-attribute instance from the master's
            // slave TOPMAR (BuildTopmarkGroups). The FC makes `topmarkDaymarkShape`
            // [1..1] mandatory, so the instance is only emitted when a valid TOPSHP
            // is present; COLOUR feeds the `colour` [0..*] list and COLPAT the
            // optional `colourPattern`. (IHO S-57→S-101 Conversion Guidance:
            // TOPMAR → parent attribute.)
            if (bindsTopmark && topmarkSource is not null)
                AppendTopmarkInstance(builder, topmarkSource);


            // is a real that carries the HORCLR value verbatim (matching the
            // fidelity of the other real sub-attributes such as
            // valueOfNominalRange); `horizontalDistanceUncertainty` has no S-57
            // source and is left unpopulated.
            if (bindsHorClearance && horclrValue is not null)
            {
                var complexName = bindsHorClearanceOpen
                    ? S101AttrHorizontalClearanceOpen
                    : S101AttrHorizontalClearanceFixed;
                AppendHorizontalClearanceInstance(builder, complexName, horclrValue);
            }

            // Append the vertical clearance complex instances. A present S-57
            // value, even an empty (unknown) one, yields the instance, as for
            // bridge spans: the mandatory verticalClearanceValue is populated
            // as empty (null), except on verticalClearanceOpen, whose value is
            // optional and whose mandatory verticalClearanceUnlimited is false
            // because a clearance was given.
            foreach (var (complex, value) in verticalClearances)
            {
                bool open = complex == S101AttrVerticalClearanceOpen;
                AppendVerticalClearanceInstance(
                    builder,
                    complex,
                    unlimited: open ? false : null,
                    open && value.Length == 0 ? null : value,
                    veraccValue);
            }
            if (veraccValue is not null && !verticalClearances.Exists(c => c.Value.Length > 0))
            {
                // No known clearance value for the accuracy to qualify.
                _diagnostics?.RecordRuleDroppedAttribute(S57AttrVeracc);
            }
            if (powerCharValues is not null)
                AppendPowerCharacteristicsInstances(builder, powerCharValues);

            // Append the `valueOfLocalMagneticAnomaly` complex-attribute
            // instance. Its mandatory sub-attribute `magneticAnomalyValue` is a
            // real that carries the VALLMA value verbatim; `referenceDirection`
            // has no S-57 source and is left unpopulated.
            if (bindsValueOfLocalMagneticAnomaly && vallmaValue is not null)
            {
                AppendValueOfLocalMagneticAnomalyInstance(builder, vallmaValue);
            }

            // Append one `radarWaveLength` complex-attribute instance per S-57
            // "value-band" pair in the (list-typed) RADWAL value. Both
            // sub-attributes are mandatory, so a pair that does not split into a
            // numeric wavelength and a band token is dropped and reported.
            if (bindsRadarWaveLength && radwalValue is not null)
            {
                AppendRadarWaveLengthInstances(builder, radwalValue);
            }

            // Append the `speed` complex-attribute instance. Its mandatory
            // sub-attribute `speedMaximum` is a real carrying the CURVEL value
            // verbatim; the optional `speedMinimum` has no S-57 source and is
            // left unpopulated.
            if (bindsSpeed && curvelValue is not null)
            {
                AppendSpeedInstance(builder, curvelValue);
            }

            // Append the `multiplicityOfFeatures` complex-attribute instance.
            // MLTYLT (the number of lights) feeds the optional `numberOfFeatures`
            // integer; the mandatory `multiplicityKnown` boolean is set true.
            if (bindsMultiplicityOfFeatures && mltyltValue is not null)
            {
                AppendMultiplicityOfFeaturesInstance(builder, mltyltValue);
            }

            // Append the bridge category attributes assembled from CATBRG.
            if (bindsOpeningBridge && catbrgValue is not null)
            {
                AppendBridgeCategoryAttributes(builder, ownerObjl, feature, catbrgValue);
            }

            // Append `surfaceCharacteristics` complex-attribute instances. The
            // S-57 NATSUR and NATQUA lists are paired positionally: position i
            // yields one instance carrying `natureOfSurface` = NATSUR[i] (when
            // present and permitted) and `natureOfSurfaceQualifyingTerms` =
            // NATQUA[i] (when present and permitted). Both sub-attributes are
            // optional, so a position with only one populated still forms a
            // valid instance; a position where both are dropped emits nothing.
            if (bindsSurfaceChar && (natsurList is not null || natquaList is not null))
                AppendSurfaceCharacteristicsInstances(builder, natsurList, natquaList);

            // Append top-level `signalSequence` complex-attribute instances on
            // feature classes that bind it directly (FogSignal,
            // RadarTransponderBeacon). On the light feature classes SIGSEQ is
            // instead nested inside `rhythmOfLight` (emitted above), so the
            // `!bindsRhythm` guard prevents any double emission.
            if (bindsSignalSequenceTop && !bindsRhythm && sigseqValue is not null)
                AppendSignalSequenceInstances(builder, sigseqValue);

            // Append the `sectorCharacteristics` complex-attribute instance on
            // LightSectored. The FC makes `lightCharacteristic` [1..1] and
            // `lightSector` [1..*] mandatory; an instance is only emitted when a
            // valid LITCHR anchors the characteristic (an out-of-range LITCHR is
            // dropped and reported, which also drops the instance). One
            // `lightSector` is assembled from the sector's colour/visibility/
            // range and the SECTR1/SECTR2 bearings.
            if (bindsSectorChar
                && sectrLitchr is not null
                && (_allowedEnumValues is null
                    || _allowedEnumValues.IsAllowed(S101AttrLightCharacteristic, sectrLitchr)))
            {
                AppendSectorCharacteristicsInstance(
                    builder, sectrLitchr, sectrSiggrp, sectrSigper, sectrSigseq,
                    sectrColour, sectrLitvis, sectrValnmr, sectrSectr1, sectrSectr2);
            }
            else if (bindsSectorChar)
            {
                // No `sectorCharacteristics` instance is emitted (LITCHR missing
                // or FC-rejected), so the sector-input attributes diverted from
                // the per-attribute pass-through would otherwise vanish silently.
                // Record them so corpus audits still see the data loss. A
                // FC-rejected LITCHR is reported as an enum drop; a missing
                // LITCHR has nothing to record.
                if (sectrLitchr is not null)
                    _diagnostics?.RecordDroppedEnumValue(S101AttrLightCharacteristic, sectrLitchr);
                RecordDivertedSectorAttributesDropped(
                    sectrColour, sectrLitvis, sectrValnmr, sectrSectr1, sectrSectr2,
                    sectrSiggrp, sectrSigper, sectrSigseq);
            }

            // Sector-light merge: co-located sector lights absorbed into this
            // primary each contribute one more `sectorCharacteristics` instance
            // (already LITCHR-validated in BuildSectorMergeGroups). The FC allows
            // `sectorCharacteristics` [1..*], so the surviving LightSectored
            // feature carries every arc of the physical light.
            if (bindsSectorChar && extraSectors is not null)
            {
                foreach (var s in extraSectors)
                {
                    AppendSectorCharacteristicsInstance(
                        builder, s.LightCharacteristic, s.SignalGroup, s.SignalPeriod, s.SignalSequence,
                        s.ColourList, s.LightVisibilityList, s.ValueOfNominalRange,
                        s.SectorBearingOne, s.SectorBearingTwo);
                }
            }

            // A point BRIDGE redirected to Landmark needs the Landmark's
            // mandatory category and visual prominence (S-65 Annex B §4.8.10).
            if (IsBridgeObjl(ownerObjl) && feature.S101Code == S101ClassLandmark)
                AppendPointBridgeLandmarkAttributes(builder);

            return builder;
        }

        private void AppendInformationInstance(
            List<S101Attribute> builder,
            string? text,
            string? fileReference,
            string language)
        {
            var infoCode = GetOrAssignAttributeCode(S101AttrInformation);
            // Marker entry — Index=1, value=empty — followed by sub-attributes.
            builder.Add(new S101Attribute(infoCode, 1, string.Empty));
            if (text is not null)
            {
                var textCode = GetOrAssignAttributeCode(S101AttrText);
                builder.Add(new S101Attribute(textCode, 1, text));
            }
            if (fileReference is not null)
            {
                var fileRefCode = GetOrAssignAttributeCode(S101AttrFileReference);
                builder.Add(new S101Attribute(fileRefCode, 1, fileReference));
            }
            var langCode = GetOrAssignAttributeCode(S101AttrLanguage);
            builder.Add(new S101Attribute(langCode, 1, language));
        }

        // Builds a `NauticalInformation` information-type record for a feature's
        // textual attributes and returns the `AdditionalInformation` /
        // `theInformation` association that binds it to the feature (IHO
        // S-57→S-101 Conversion Guidance §2.3 "fuller path"). The record carries
        // the same `information` complex the inline shortcut used — one instance
        // for the English text (INFORM/TXTDSC, language `eng`) and one for the
        // national text (NINFOM/NTXTDS, empty language) — so the S-100 Part 9A
        // portrayal (`ProcessNauticalInformation`, which reads the association)
        // is unchanged. Returns <see langword="null"/> when the feature has no
        // textual attributes, in which case no record or association is emitted.
        private S101InformationAssociation? BuildNauticalInformationAssociation(
            string? informText, string? txtdscFile, string? ninfomText, string? ntxtdsFile)
        {
            bool hasEnglish = informText is not null || txtdscFile is not null;
            bool hasNational = ninfomText is not null || ntxtdsFile is not null;
            if (!hasEnglish && !hasNational) return null;

            var infoAttributes = new List<S101Attribute>();
            if (hasEnglish)
                AppendInformationInstance(infoAttributes, text: informText, fileReference: txtdscFile, language: LanguageEng);
            if (hasNational)
                AppendInformationInstance(infoAttributes, text: ninfomText, fileReference: ntxtdsFile, language: string.Empty);

            var infoId = _nextInformationId++;
            InformationTypes[infoId] = new S101InformationRecord
            {
                RecordId = infoId,
                InformationTypeCode = GetOrAssignInformationTypeCode(S101InfoTypeNauticalInformation),
                Attributes = infoAttributes,
            };

            if (_diagnostics is not null) _diagnostics.NauticalInformationTypesEmitted++;

            return new S101InformationAssociation(
                GetOrAssignInformationAssociationCode(S101AssocAdditionalInformation),
                infoId,
                GetOrAssignRoleCode(S101RoleTheInformation));
        }
        private void AppendFeatureNameInstance(
            List<S101Attribute> builder,
            string name,
            string language)
        {
            var featureNameCode = GetOrAssignAttributeCode(S101AttrFeatureName);
            // Marker entry — Index=1, value=empty — followed by sub-attributes.
            builder.Add(new S101Attribute(featureNameCode, 1, string.Empty));
            var nameCode = GetOrAssignAttributeCode(S101AttrName);
            builder.Add(new S101Attribute(nameCode, 1, name));
            var langCode = GetOrAssignAttributeCode(S101AttrLanguage);
            builder.Add(new S101Attribute(langCode, 1, language));
        }

        // Emits a `rhythmOfLight` complex-attribute instance (marker +
        // lightCharacteristic + optional signalGroup/signalPeriod), using the
        // same flat marker + contiguous-sub-attribute convention as the other
        // complex attributes. When SIGSEQ supplies a signalSequence, it is
        // appended as nested sub-complex instances after signalGroup/signalPeriod
        // because the FC declares signalSequence as rhythmOfLight's last
        // sub-attribute.
        private void AppendRhythmOfLightInstance(
            List<S101Attribute> builder,
            string lightCharacteristic,
            string? signalGroup,
            string? signalPeriod,
            string? signalSequence)
        {
            var rhythmCode = GetOrAssignAttributeCode(S101AttrRhythmOfLight);
            // Marker entry — Index=1, value=empty — followed by sub-attributes.
            builder.Add(new S101Attribute(rhythmCode, 1, string.Empty));
            var litCharCode = GetOrAssignAttributeCode(S101AttrLightCharacteristic);
            builder.Add(new S101Attribute(litCharCode, 1, lightCharacteristic));
            if (signalGroup is not null)
            {
                var sigGrpCode = GetOrAssignAttributeCode(S101AttrSignalGroup);
                builder.Add(new S101Attribute(sigGrpCode, 1, signalGroup));
            }
            if (signalPeriod is not null)
            {
                var sigPerCode = GetOrAssignAttributeCode(S101AttrSignalPeriod);
                builder.Add(new S101Attribute(sigPerCode, 1, signalPeriod));
            }
            // Nested `signalSequence` sub-complex instances. Per the FC binding
            // order `signalSequence` is the last sub-attribute of
            // `rhythmOfLight`, so the nested markers/sub-attributes are appended
            // after signalGroup/signalPeriod. The S-101 data provider's scope
            // resolver treats these as nested (rather than sibling) complexes
            // because the FC declares `signalSequence` a sub-attribute of
            // `rhythmOfLight`.
            if (signalSequence is not null)
                AppendSignalSequenceInstances(builder, signalSequence);
        }

        // Emits a date-range complex-attribute instance (marker + optional
        // dateStart + optional dateEnd), using the same flat marker +
        // contiguous-sub-attribute convention as the other complex attributes.
        // The three date-range complexes (fixedDateRange / periodicDateRange /
        // surveyDateRange) share the `dateStart` / `dateEnd` sub-attribute
        // codes; the S-101 data provider delimits one instance from the next
        // by the complex marker, so emitting each instance as a contiguous run
        // keeps them distinct.
        private void AppendDateRangeInstance(
            List<S101Attribute> builder,
            string complexCode,
            string? dateStart,
            string? dateEnd)
        {
            var rangeCode = GetOrAssignAttributeCode(complexCode);
            // Marker entry — Index=1, value=empty — followed by sub-attributes.
            builder.Add(new S101Attribute(rangeCode, 1, string.Empty));
            if (dateStart is not null)
            {
                var startCode = GetOrAssignAttributeCode(S101AttrDateStart);
                builder.Add(new S101Attribute(startCode, 1, dateStart));
            }
            if (dateEnd is not null)
            {
                var endCode = GetOrAssignAttributeCode(S101AttrDateEnd);
                builder.Add(new S101Attribute(endCode, 1, dateEnd));
            }
        }

        // Emits a `zoneOfConfidence` complex-attribute instance (marker +
        // categoryOfZoneOfConfidenceInData, followed by the CATZOC-implied
        // horizontalPositionUncertainty / verticalUncertainty sub-complexes for
        // ZOC A1–C) using the same flat marker / pre-order sub-attribute
        // convention as the other nested complex attributes. The FC binding
        // order is categoryOfZoneOfConfidenceInData, fixedDateRange (no source,
        // omitted), horizontalPositionUncertainty, verticalUncertainty; each
        // uncertainty is itself a complex of uncertaintyFixed [1..1] +
        // uncertaintyVariableFactor [0..1]. ZOC D/U have no quantified accuracy
        // (absent from CatzocUncertainties) so only the category is emitted.
        private void AppendZoneOfConfidenceInstance(
            List<S101Attribute> builder,
            string categoryOfZoneOfConfidenceInData)
        {
            var zocCode = GetOrAssignAttributeCode(S101AttrZoneOfConfidence);
            // Marker entry — Index=1, value=empty — followed by the sub-attribute.
            builder.Add(new S101Attribute(zocCode, 1, string.Empty));
            var catCode = GetOrAssignAttributeCode(S101AttrCategoryOfZocInData);
            builder.Add(new S101Attribute(catCode, 1, categoryOfZoneOfConfidenceInData));

            if (!CatzocUncertainties.TryGetValue(categoryOfZoneOfConfidenceInData, out var u))
                return;

            // horizontalPositionUncertainty → uncertaintyFixed [+ uncertaintyVariableFactor].
            builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrHorizontalPositionUncertainty), 1, string.Empty));
            builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrUncertaintyFixed), 1, u.HorizontalFixed));
            if (u.HorizontalVariable is not null)
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrUncertaintyVariableFactor), 1, u.HorizontalVariable));

            // verticalUncertainty → uncertaintyFixed + uncertaintyVariableFactor.
            builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrVerticalUncertainty), 1, string.Empty));
            builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrUncertaintyFixed), 1, u.VerticalFixed));
            builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrUncertaintyVariableFactor), 1, u.VerticalVariable));
        }

        // Emits a `topmark` complex-attribute instance from the master's slave
        // TOPMAR record, using the same flat marker / pre-order sub-attribute
        // convention as the other nested complex attributes. The FC binding order
        // is colour [0..*], colourPattern [0..1], topmarkDaymarkShape [1..1],
        // shapeInformation [0..*]. `topmarkDaymarkShape` (TOPSHP) is mandatory, so
        // a missing or FC-rejected shape drops the whole instance (reported);
        // COLOUR is a list-valued enumerate split into individual `colour`
        // occurrences, and COLPAT feeds the optional `colourPattern`. All three
        // are straight S-57→S-101 code aliases, so values pass through unchanged
        // (validated against the S-101 enumeration). (S-101 FC Ed 1.x; IHO
        // S-57→S-101 Conversion Guidance.)
        private void AppendTopmarkInstance(
            List<S101Attribute> builder,
            EncDotNet.S57.S57FeatureRecord topmarkSource)
        {
            string? topshp = null;
            string? colourList = null;
            string? colpat = null;
            foreach (var a in topmarkSource.Attributes)
            {
                switch (a.AttributeCode)
                {
                    case S57AttrTopshp: if (!string.IsNullOrEmpty(a.Value)) topshp = a.Value; break;
                    case S57AttrColour: if (!string.IsNullOrEmpty(a.Value)) colourList = a.Value; break;
                    case S57AttrColpat: if (!string.IsNullOrEmpty(a.Value)) colpat = a.Value; break;
                }
            }

            // topmarkDaymarkShape [1..1] is mandatory: without a valid shape the
            // instance is non-conformant, so drop it entirely (and report the
            // loss so corpus audits see it) rather than emit a partial complex.
            if (topshp is null)
            {
                _diagnostics?.RecordRuleDroppedAttribute(S57AttrTopshp);
                return;
            }
            if (_allowedEnumValues is not null
                && !_allowedEnumValues.IsAllowed(S101AttrTopmarkDaymarkShape, topshp))
            {
                _diagnostics?.RecordDroppedEnumValue(S101AttrTopmarkDaymarkShape, topshp);
                return;
            }

            // Marker entry — Index=1, value=empty — followed by the sub-attributes
            // in FC binding order.
            builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrTopmark), 1, string.Empty));

            ushort colourIndex = 1;
            foreach (var colour in SplitEnumList(colourList))
            {
                if (_allowedEnumValues is not null
                    && !_allowedEnumValues.IsAllowed(S101AttrColour, colour))
                {
                    _diagnostics?.RecordDroppedEnumValue(S101AttrColour, colour);
                    continue;
                }
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrColour), colourIndex++, colour));
            }

            if (colpat is not null)
            {
                if (_allowedEnumValues is null
                    || _allowedEnumValues.IsAllowed(S101AttrColourPattern, colpat))
                    builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrColourPattern), 1, colpat));
                else
                    _diagnostics?.RecordDroppedEnumValue(S101AttrColourPattern, colpat);
            }

            builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrTopmarkDaymarkShape), 1, topshp));
        }

        // Emits a `horizontalClearanceOpen` / `horizontalClearanceFixed`
        // complex-attribute instance (marker + its mandatory
        // `horizontalClearanceValue` real sub-attribute) using the same marker
        // / contiguous-sub-attribute convention as the other complex
        // attributes. The S-101 data provider delimits the instance from any
        // following complex marker.
        private void AppendHorizontalClearanceInstance(
            List<S101Attribute> builder,
            string complexName,
            string horizontalClearanceValue)
        {
            var complexCode = GetOrAssignAttributeCode(complexName);
            // Marker entry — Index=1, value=empty — followed by the sub-attribute.
            builder.Add(new S101Attribute(complexCode, 1, string.Empty));
            var valueCode = GetOrAssignAttributeCode(S101AttrHorizontalClearanceValue);
            builder.Add(new S101Attribute(valueCode, 1, horizontalClearanceValue));
        }

        // Emits a `valueOfLocalMagneticAnomaly` complex-attribute instance
        // (marker + its mandatory `magneticAnomalyValue` real sub-attribute),
        // using the same marker / contiguous-sub-attribute convention as the
        // other complex attributes. The optional `referenceDirection` enum has
        // no S-57 source and is omitted.
        private void AppendValueOfLocalMagneticAnomalyInstance(
            List<S101Attribute> builder,
            string magneticAnomalyValue)
        {
            var complexCode = GetOrAssignAttributeCode(S101AttrValueOfLocalMagneticAnomaly);
            // Marker entry — Index=1, value=empty — followed by the sub-attribute.
            builder.Add(new S101Attribute(complexCode, 1, string.Empty));
            var valueCode = GetOrAssignAttributeCode(S101AttrMagneticAnomalyValue);
            builder.Add(new S101Attribute(valueCode, 1, magneticAnomalyValue));
        }

        // Emits zero or more `radarWaveLength` complex-attribute instances from
        // the S-57 RADWAL value, which is a comma-separated list of
        // "wavelength-band" pairs (e.g. "0.03-X" or "0.03-X,0.10-S"). Each pair
        // yields one instance carrying the mandatory `waveLengthValue` (real,
        // the numeric part) and `radarBand` (text, the band token). Because
        // both sub-attributes are mandatory, a pair that lacks either part is
        // dropped and reported. At most two instances are emitted because the
        // S-101 Feature Catalogue binds `radarWaveLength` with multiplicity
        // upper=2 (e.g. on RadarTransponderBeacon); additional pairs are
        // dropped and reported to keep the output FC-conformant.
        private const int RadarWaveLengthMaxInstances = 2;

        private void AppendRadarWaveLengthInstances(
            List<S101Attribute> builder,
            string radwalValue)
        {
            var emitted = 0;
            foreach (var pair in radwalValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var sep = pair.IndexOf('-');
                if (sep <= 0 || sep >= pair.Length - 1)
                {
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrRadwal);
                    continue;
                }

                var value = pair[..sep].Trim();
                var band = pair[(sep + 1)..].Trim();
                if (value.Length == 0 || band.Length == 0)
                {
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrRadwal);
                    continue;
                }

                if (emitted == RadarWaveLengthMaxInstances)
                {
                    // Exceeds the FC upper bound (2); drop the surplus pair.
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrRadwal);
                    continue;
                }

                var complexCode = GetOrAssignAttributeCode(S101AttrRadarWaveLength);
                // Marker entry — Index=1, value=empty — followed by the sub-attributes.
                builder.Add(new S101Attribute(complexCode, 1, string.Empty));
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrWaveLengthValue), 1, value));
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrRadarBand), 1, band));
                emitted++;
            }
        }

        // Emits a `speed` complex-attribute instance (marker + its mandatory
        // `speedMaximum` real sub-attribute) using the same marker /
        // contiguous-sub-attribute convention as the other complex attributes.
        // The optional `speedMinimum` has no S-57 source and is omitted.
        private void AppendSpeedInstance(
            List<S101Attribute> builder,
            string speedMaximum)
        {
            var complexCode = GetOrAssignAttributeCode(S101AttrSpeed);
            // Marker entry — Index=1, value=empty — followed by the sub-attribute.
            builder.Add(new S101Attribute(complexCode, 1, string.Empty));
            var maxCode = GetOrAssignAttributeCode(S101AttrSpeedMaximum);
            builder.Add(new S101Attribute(maxCode, 1, speedMaximum));
        }

        // Emits a `multiplicityOfFeatures` complex-attribute instance (marker +
        // the mandatory `multiplicityKnown` boolean + the optional
        // `numberOfFeatures` integer). MLTYLT supplies the count, so
        // `multiplicityKnown` is emitted true and `numberOfFeatures` carries the
        // MLTYLT value verbatim.
        private void AppendMultiplicityOfFeaturesInstance(
            List<S101Attribute> builder,
            string numberOfFeatures)
        {
            var complexCode = GetOrAssignAttributeCode(S101AttrMultiplicityOfFeatures);
            // Marker entry — Index=1, value=empty — followed by the sub-attributes.
            builder.Add(new S101Attribute(complexCode, 1, string.Empty));
            var knownCode = GetOrAssignAttributeCode(S101AttrMultiplicityKnown);
            builder.Add(new S101Attribute(knownCode, 1, "true"));
            var numberCode = GetOrAssignAttributeCode(S101AttrNumberOfFeatures);
            builder.Add(new S101Attribute(numberCode, 1, numberOfFeatures));
        }

        // Emits the S-101 bridge category attributes for an S-57 CATBRG list
        // (S-65 Annex B § 4.8.10). Each listed value is resolved through the
        // mapping to bridgeConstruction, bridgeFunction, categoryOfOpeningBridge
        // or openingBridge. openingBridge is emitted once: true when any value
        // denotes an opening bridge (CATBRG 2, or a categoryOfOpeningBridge
        // value, which S-101 only allows on an opening bridge), otherwise false.
        // A single-valued target (bridgeConstruction, categoryOfOpeningBridge)
        // keeps its first value; later ones are dropped and reported.
        private void AppendBridgeCategoryAttributes(
            List<S101Attribute> builder,
            ushort ownerObjl,
            ResolvedFeature feature,
            string catbrg)
        {
            if (!_mapping.AttributeRules.ContainsKey(S57AttrCatbrg))
            {
                _diagnostics?.RecordUnmappedAttribute(ownerObjl, S57AttrCatbrg);
                return;
            }

            bool? openingBridge = null;
            var occurrences = new List<(string Code, List<string> Values)>();
            foreach (var token in catbrg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var resolved = _mapping.ResolveAttribute(S57AttrCatbrg, token, feature);
                if (resolved is null)
                {
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrCatbrg);
                    continue;
                }

                if (_allowedEnumValues is not null
                    && !_allowedEnumValues.IsAllowed(resolved.S101Code, resolved.Value))
                {
                    _diagnostics?.RecordDroppedEnumValue(resolved.S101Code, resolved.Value);
                    continue;
                }

                if (resolved.S101Code == S101AttrOpeningBridge)
                {
                    if (!bool.TryParse(resolved.Value, out var isOpening))
                    {
                        _diagnostics?.RecordRuleDroppedAttribute(S57AttrCatbrg);
                        continue;
                    }
                    openingBridge = openingBridge == true || isOpening;
                    continue;
                }

                openingBridge = openingBridge == true
                    || resolved.S101Code == S101AttrCategoryOfOpeningBridge;

                var index = occurrences.FindIndex(o => o.Code == resolved.S101Code);
                if (index < 0)
                {
                    occurrences.Add((resolved.S101Code, [resolved.Value]));
                    continue;
                }

                var values = occurrences[index].Values;
                if (values.Contains(resolved.Value))
                    continue;
                if (_featureBindings.IsSingleValued(feature.S101Code, resolved.S101Code))
                {
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrCatbrg);
                    continue;
                }
                values.Add(resolved.Value);
            }

            foreach (var (code, values) in occurrences)
            {
                var numeric = GetOrAssignAttributeCode(code);
                ushort index = 1;
                foreach (var value in values)
                    builder.Add(new S101Attribute(numeric, index++, value));
            }

            if (openingBridge is { } opening)
            {
                builder.Add(new S101Attribute(
                    GetOrAssignAttributeCode(S101AttrOpeningBridge), 1, opening ? "true" : "false"));
            }
        }

        // The powerCharacteristics sub-attribute an S-57 attribute's mapping
        // rule targets, or null when it targets none.
        private string? PowerCharacteristicsTarget(int attributeCode)
        {
            if (attributeCode is < 0 or > ushort.MaxValue
                || !_mapping.AttributeRules.TryGetValue((ushort)attributeCode, out var rule)
                || rule.DefaultS101Code is not { } code)
            {
                return null;
            }

            return Array.IndexOf(PowerCharacteristicsSubAttributes, code) >= 0 ? code : null;
        }

        // Emits one `powerCharacteristics` instance per combination of the
        // listed voltages and frequencies (a single instance when neither is
        // listed), each carrying the remaining single-valued sub-attributes.
        // Voltage and frequency codes the catalogue does not allow are dropped
        // and reported.
        private void AppendPowerCharacteristicsInstances(
            List<S101Attribute> builder,
            Dictionary<string, string> values)
        {
            var voltages = AllowedEnumTokens(S101AttrCategoryOfVoltage, values.GetValueOrDefault(S101AttrCategoryOfVoltage));
            var frequencies = AllowedEnumTokens(S101AttrCategoryOfFrequency, values.GetValueOrDefault(S101AttrCategoryOfFrequency));
            if (voltages.Count == 0) voltages.Add(null);
            if (frequencies.Count == 0) frequencies.Add(null);

            var complexCode = GetOrAssignAttributeCode(S101AttrPowerCharacteristics);
            foreach (var voltage in voltages)
            {
                foreach (var frequency in frequencies)
                {
                    var subs = new List<S101Attribute>();
                    foreach (var sub in PowerCharacteristicsSubAttributes)
                    {
                        var value = sub switch
                        {
                            S101AttrCategoryOfVoltage => voltage,
                            S101AttrCategoryOfFrequency => frequency,
                            _ => values.GetValueOrDefault(sub),
                        };
                        if (value is not null)
                            subs.Add(new S101Attribute(GetOrAssignAttributeCode(sub), 1, value));
                    }

                    if (subs.Count == 0)
                        continue;

                    // Marker entry — Index=1, value=empty — followed by sub-attributes.
                    builder.Add(new S101Attribute(complexCode, 1, string.Empty));
                    builder.AddRange(subs);
                }
            }
        }

        // The tokens of an S-57 list-valued enumerate that the target
        // catalogue allows for attributeCode; the others are reported.
        private List<string?> AllowedEnumTokens(string attributeCode, string? list)
        {
            var allowed = new List<string?>();
            foreach (var code in SplitEnumList(list))
            {
                if (_allowedEnumValues is not null && !_allowedEnumValues.IsAllowed(attributeCode, code))
                    _diagnostics?.RecordDroppedEnumValue(attributeCode, code);
                else
                    allowed.Add(code);
            }
            return allowed;
        }

        // Emits zero or more `surfaceCharacteristics` complex-attribute
        // instances by pairing the S-57 NATSUR and NATQUA lists positionally.
        // Each is a flat marker + contiguous-sub-attribute run (the same
        // convention as the other complex attributes); the S-101 data provider
        // delimits one repeating instance from the next by the complex marker.
        // Out-of-range enumerate codes are dropped (and reported) individually,
        // and a position whose sub-attributes are all dropped emits no instance.
        private void AppendSurfaceCharacteristicsInstances(
            List<S101Attribute> builder,
            string? natsurList,
            string? natquaList)
        {
            var surfaces = SplitEnumList(natsurList);
            var quals = SplitEnumList(natquaList);
            int count = Math.Max(surfaces.Count, quals.Count);
            for (int i = 0; i < count; i++)
            {
                string? surface = i < surfaces.Count ? surfaces[i] : null;
                string? qual = i < quals.Count ? quals[i] : null;

                if (surface is not null
                    && _allowedEnumValues is not null
                    && !_allowedEnumValues.IsAllowed(S101AttrNatureOfSurface, surface))
                {
                    _diagnostics?.RecordDroppedEnumValue(S101AttrNatureOfSurface, surface);
                    surface = null;
                }
                if (qual is not null
                    && _allowedEnumValues is not null
                    && !_allowedEnumValues.IsAllowed(S101AttrNatureOfSurfaceQualifyingTerms, qual))
                {
                    _diagnostics?.RecordDroppedEnumValue(S101AttrNatureOfSurfaceQualifyingTerms, qual);
                    qual = null;
                }

                if (surface is null && qual is null)
                    continue;

                var scCode = GetOrAssignAttributeCode(S101AttrSurfaceCharacteristics);
                // Marker entry — Index=1, value=empty — followed by sub-attributes.
                builder.Add(new S101Attribute(scCode, 1, string.Empty));
                if (surface is not null)
                    builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrNatureOfSurface), 1, surface));
                if (qual is not null)
                    builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrNatureOfSurfaceQualifyingTerms), 1, qual));
            }
        }

        // Splits an S-57 list-valued enumerate attribute (comma-separated
        // integer codes) into its individual non-empty tokens.
        private static List<string> SplitEnumList(string? list)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(list))
                return result;
            foreach (var token in list.Split(','))
            {
                var code = token.Trim();
                if (code.Length > 0)
                    result.Add(code);
            }
            return result;
        }

        // Structural discriminator used when FC enum enforcement is disabled
        // (_allowedEnumValues is null) to decide whether a comma-separated
        // value is a list-valued enumerate (which must be split) rather than
        // free text. S-57 enumerate codes are integer tokens, so a value whose
        // non-empty tokens are all integers is treated as a list enum; text
        // attributes (e.g. OBJNAM) that legitimately contain commas are not.
        private static bool IsIntegerList(string value)
        {
            var any = false;
            foreach (var token in value.Split(','))
            {
                var code = token.Trim();
                if (code.Length == 0)
                    continue;
                if (!int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    return false;
                any = true;
            }
            return any;
        }

        // Emits zero or more `signalSequence` complex-attribute instances by
        // parsing the S-57 SIGSEQ string (S-57 Appendix B.1). Each parsed
        // phase becomes a flat marker + contiguous sub-attribute run (marker +
        // signalDuration + signalStatus), the same convention as the other
        // complex attributes; when appended after a `rhythmOfLight` instance's
        // simple sub-attributes these form nested sub-complexes of that
        // instance. Phases that do not parse as a real duration are dropped
        // (and reported).
        private void AppendSignalSequenceInstances(List<S101Attribute> builder, string sigseq)
        {
            foreach (var (duration, status) in ParseSignalSequence(sigseq))
            {
                var seqCode = GetOrAssignAttributeCode(S101AttrSignalSequence);
                // Marker entry — Index=1, value=empty — followed by sub-attributes.
                builder.Add(new S101Attribute(seqCode, 1, string.Empty));
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSignalDuration), 1, duration));
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSignalStatus), 1, status));
            }
        }

        // Parses an S-57 SIGSEQ value into (signalDuration, signalStatus)
        // pairs. The value is a '+'-separated list of phase durations in
        // seconds (e.g. "02.0+(02.0)+02.0+(24.0)"); a duration enclosed in
        // parentheses is an eclipse / silence phase (signalStatus = 2), an
        // unparenthesised one is a lit / sound phase (signalStatus = 1). The
        // duration is normalised to an invariant-culture real. Tokens that do
        // not parse as a real are skipped and recorded as a rule-dropped
        // attribute.
        private IEnumerable<(string Duration, string Status)> ParseSignalSequence(string sigseq)
        {
            foreach (var rawToken in sigseq.Split('+'))
            {
                var token = rawToken.Trim();
                if (token.Length == 0)
                    continue;

                var status = SignalStatusLit;
                if (token.StartsWith('(') && token.EndsWith(')'))
                {
                    status = SignalStatusEclipsed;
                    token = token[1..^1].Trim();
                }

                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    _diagnostics?.RecordRuleDroppedAttribute(S57AttrSigseq);
                    continue;
                }

                yield return (seconds.ToString(CultureInfo.InvariantCulture), status);
            }
        }

        // Emits a single `sectorCharacteristics` complex-attribute instance for
        // a sectored light (LightSectored), using the same flat marker +
        // contiguous-sub-attribute convention as the other complex attributes
        // but nested up to three levels deep (the S-101 data provider's scope
        // resolver descends the FC-declared nesting so the three-level path
        // `sectorCharacteristics;lightSector;sectorLimit;sectorLimitOne` is
        // navigable). The pre-order layout is:
        //   sectorCharacteristics
        //     lightCharacteristic  (LITCHR, mandatory [1..1])
        //     signalGroup          (SIGGRP, optional)
        //     signalPeriod         (SIGPER, optional)
        //     lightSector          (mandatory [1..*]; one emitted per S-57 sector)
        //       colour             (COLOUR list, enum, [1..*])
        //       valueOfNominalRange(VALNMR, real, [0..1])
        //       lightVisibility    (LITVIS list, enum, [0..*])
        //       sectorLimit        ([0..1]; emitted when both bearings present)
        //         sectorLimitOne   (sectorBearing = SECTR1)
        //         sectorLimitTwo   (sectorBearing = SECTR2)
        //     signalSequence…      (SIGSEQ phases, nested at this level)
        // Out-of-range enumerate codes (colour / lightVisibility) are dropped
        // and reported individually; the caller has already validated LITCHR.
        // Immutable inputs for one S-101 `sectorCharacteristics` instance,
        // extracted from a single S-57 LIGHTS feature. Used by the sector-light
        // merge to carry an absorbed co-located light's arc onto the surviving
        // LightSectored primary.
        private sealed record SectorInput(
            string LightCharacteristic,
            string? SignalGroup,
            string? SignalPeriod,
            string? SignalSequence,
            string? ColourList,
            string? LightVisibilityList,
            string? ValueOfNominalRange,
            string? SectorBearingOne,
            string? SectorBearingTwo);

        // If no valid colour remains, the whole instance is rolled back because
        // lightSector/colour are mandatory in the S-101 Feature Catalogue.
        private void AppendSectorCharacteristicsInstance(
            List<S101Attribute> builder,
            string lightCharacteristic,
            string? signalGroup,
            string? signalPeriod,
            string? signalSequence,
            string? colourList,
            string? lightVisibilityList,
            string? valueOfNominalRange,
            string? sectorBearingOne,
            string? sectorBearingTwo)
        {
            int instanceStart = builder.Count;

            builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSectorCharacteristics), 1, string.Empty));
            builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrLightCharacteristic), 1, lightCharacteristic));
            if (signalGroup is not null)
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSignalGroup), 1, signalGroup));
            if (signalPeriod is not null)
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSignalPeriod), 1, signalPeriod));

            // lightSector marker + its sub-attributes.
            builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrLightSector), 1, string.Empty));
            bool anyColour = false;
            foreach (var colour in SplitEnumList(colourList))
            {
                if (_allowedEnumValues is not null
                    && !_allowedEnumValues.IsAllowed(S101AttrColour, colour))
                {
                    _diagnostics?.RecordDroppedEnumValue(S101AttrColour, colour);
                    continue;
                }
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrColour), 1, colour));
                anyColour = true;
            }

            // lightSector requires colour [1..*] and sectorCharacteristics requires
            // lightSector [1..*] (S-101 FC). Without at least one valid colour the
            // whole subtree is non-conformant, so roll back the entire instance
            // rather than emit a partial sectorCharacteristics/lightSector.
            if (!anyColour)
            {
                builder.RemoveRange(instanceStart, builder.Count - instanceStart);
                _diagnostics?.RecordRuleDroppedAttribute(S57AttrColour);
                return;
            }

            if (valueOfNominalRange is not null)
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrValueOfNominalRange), 1, valueOfNominalRange));
            foreach (var visibility in SplitEnumList(lightVisibilityList))
            {
                if (_allowedEnumValues is not null
                    && !_allowedEnumValues.IsAllowed(S101AttrLightVisibility, visibility))
                {
                    _diagnostics?.RecordDroppedEnumValue(S101AttrLightVisibility, visibility);
                    continue;
                }
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrLightVisibility), 1, visibility));
            }

            // sectorLimit → sectorLimitOne / sectorLimitTwo → sectorBearing. Both
            // sectorLimitOne and sectorLimitTwo are mandatory [1..1] in the FC, so
            // the sectorLimit subtree (itself [0..1]) is only emitted when both
            // bearings are present; a lone bearing omits the whole subtree and is
            // recorded as a rule-dropped attribute so corpus audits see the loss.
            if (sectorBearingOne is not null && sectorBearingTwo is not null)
            {
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSectorLimit), 1, string.Empty));
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSectorLimitOne), 1, string.Empty));
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSectorBearing), 1, sectorBearingOne));
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSectorLimitTwo), 1, string.Empty));
                builder.Add(new S101Attribute(GetOrAssignAttributeCode(S101AttrSectorBearing), 1, sectorBearingTwo));
            }
            else if (sectorBearingOne is not null)
            {
                _diagnostics?.RecordRuleDroppedAttribute(S57AttrSectr1);
            }
            else if (sectorBearingTwo is not null)
            {
                _diagnostics?.RecordRuleDroppedAttribute(S57AttrSectr2);
            }

            // Nested `signalSequence` sub-complexes at the sectorCharacteristics
            // level (after the lightSector subtree, so the lightSector scope
            // terminates at the first signalSequence marker).
            if (signalSequence is not null)
                AppendSignalSequenceInstances(builder, signalSequence);
        }

        // Records each present sector-input S-57 attribute as rule-dropped. Used
        // when a LightSectored feature diverts these attributes from the
        // per-attribute pass-through but no `sectorCharacteristics` instance is
        // emitted (missing / FC-rejected LITCHR), so corpus audits still see the
        // data loss.
        private void RecordDivertedSectorAttributesDropped(
            string? colour, string? litvis, string? valnmr,
            string? sectr1, string? sectr2,
            string? siggrp, string? sigper, string? sigseq)
        {
            if (_diagnostics is null)
                return;
            if (colour is not null) _diagnostics.RecordRuleDroppedAttribute(S57AttrColour);
            if (litvis is not null) _diagnostics.RecordRuleDroppedAttribute(S57AttrLitvis);
            if (valnmr is not null) _diagnostics.RecordRuleDroppedAttribute(S57AttrValnmr);
            if (sectr1 is not null) _diagnostics.RecordRuleDroppedAttribute(S57AttrSectr1);
            if (sectr2 is not null) _diagnostics.RecordRuleDroppedAttribute(S57AttrSectr2);
            if (siggrp is not null) _diagnostics.RecordRuleDroppedAttribute(S57AttrSiggrp);
            if (sigper is not null) _diagnostics.RecordRuleDroppedAttribute(S57AttrSigper);
            if (sigseq is not null) _diagnostics.RecordRuleDroppedAttribute(S57AttrSigseq);
        }

        private IReadOnlyList<S101SpatialAssociation> TranslateSpatialPointers(EncDotNet.S57.S57FeatureRecord feat)
        {
            // S57GeometricPrimitive struct overlays an int (1=Point, 2=Line,
            // 3=Area, 255=None) so casting to int recovers the wire value.
            var prim = (int)feat.Primitive;
            return prim switch
            {
                1 => TranslatePointSpatial(feat),
                2 => TranslateLineSpatial(feat),
                3 => TranslateAreaSpatial(feat),
                _ => [],
            };
        }

        // Maps the S-57 wire primitive (1=Point, 2=Line, 3=Area, else None) to
        // the mapping layer's S57GeometryPrimitive, used to drive
        // geometry-conditional feature-class redirects (e.g. MORFAC).
        private static S57GeometryPrimitive MapPrimitive(EncDotNet.S57.S57GeometricPrimitive primitive) =>
            (int)primitive switch
            {
                1 => S57GeometryPrimitive.Point,
                2 => S57GeometryPrimitive.Curve,
                3 => S57GeometryPrimitive.Surface,
                _ => S57GeometryPrimitive.None,
            };

        private IReadOnlyList<S101SpatialAssociation> TranslatePointSpatial(EncDotNet.S57.S57FeatureRecord feat)
        {
            // Point features reference a single isolated/connected node.
            foreach (var ptr in feat.SpatialPointers)
            {
                if (TryGetPointId(ptr.Name, out var pid))
                {
                    return [new S101SpatialAssociation(S101RcnmPoint, pid, OrientationForward)];
                }
            }
            return [];
        }

        private IReadOnlyList<S101SpatialAssociation> TranslateLineSpatial(EncDotNet.S57.S57FeatureRecord feat)
        {
            // Line features reference one or more edges in traversal order.
            var builder = new List<S101SpatialAssociation>();
            foreach (var ptr in feat.SpatialPointers)
            {
                if (ptr.Name.RecordNameCode != S57RecordNameCodes.Edge) continue;
                if (!_edgeIdMap.TryGetValue(ptr.Name.RecordId, out var cid)) continue;
                var ornt = (int)ptr.Orientation == OrientationReverse ? OrientationReverse : OrientationForward;
                builder.Add(new S101SpatialAssociation(S101RcnmCurveSegment, cid, ornt));
            }
            return builder;
        }

        private IReadOnlyList<S101SpatialAssociation> TranslateAreaSpatial(EncDotNet.S57.S57FeatureRecord feat)
        {
            // Area features reference their boundary edges via FSPT, tagged by
            // USAG (1 = exterior, 2 = interior). S-57 lists every interior edge
            // consecutively, so grouping by USAG alone merges all holes into a
            // single boundary; flattening that merged ring to coordinates then
            // jumps from one hole to the next and renders as long "spike"
            // artifacts. Instead we collect the edges per usage and chain them
            // into contiguous rings by shared node identity (S-57 Appendix B.1
            // area topology; S-100 Part 10a surface ring topology), so each hole
            // becomes its own interior ring.
            var exteriorEdges = new List<S101CurveUsage>();
            var interiorEdges = new List<S101CurveUsage>();

            foreach (var ptr in feat.SpatialPointers)
            {
                if (ptr.Name.RecordNameCode != S57RecordNameCodes.Edge) continue;
                if (!_edgeIdMap.TryGetValue(ptr.Name.RecordId, out var cid)) continue;
                var ornt = (int)ptr.Orientation == OrientationReverse ? OrientationReverse : OrientationForward;
                var usage = new S101CurveUsage(S101RcnmCurveSegment, cid, ornt);

                // USAG 2 is interior; USAG 1 (exterior) and 3 (exterior
                // truncated at the cell boundary) both bound the exterior.
                if ((int)ptr.Usage == UsageInterior)
                    interiorEdges.Add(usage);
                else
                    exteriorEdges.Add(usage);
            }

            if (exteriorEdges.Count == 0) return [];

            return BuildSurface(ChainEdgesIntoRings(exteriorEdges), ChainEdgesIntoRings(interiorEdges));
        }

        // Allocates the composite-curve records for already-chained exterior and
        // interior rings plus the surface record that references them, and
        // returns the surface spatial association.
        private IReadOnlyList<S101SpatialAssociation> BuildSurface(
            List<List<S101CurveUsage>> exteriorRings,
            List<List<S101CurveUsage>> interiorRings)
        {
            var rings = new List<S101RingAssociation>();

            foreach (var ringEdges in exteriorRings)
            {
                var extId = _nextCompositeId++;
                CompositeCurves[extId] = new S101CompositeCurveRecord
                {
                    RecordId = extId,
                    CurveComponents = ringEdges,
                };
                rings.Add(new S101RingAssociation(
                    S101RcnmCompositeCurve, extId, OrientationForward, UsageExterior));
            }

            foreach (var ringEdges in interiorRings)
            {
                var intId = _nextCompositeId++;
                CompositeCurves[intId] = new S101CompositeCurveRecord
                {
                    RecordId = intId,
                    CurveComponents = ringEdges,
                };
                rings.Add(new S101RingAssociation(
                    S101RcnmCompositeCurve, intId, OrientationForward, UsageInterior));
            }

            var sid = _nextSurfaceId++;
            Surfaces[sid] = new S101SurfaceRecord
            {
                RecordId = sid,
                RingAssociations = rings,
            };

            return [new S101SpatialAssociation(S101RcnmSurface, sid, OrientationForward)];
        }

        /// <summary>
        /// Chains a set of area boundary edges into contiguous rings using shared
        /// begin/end node identity, reversing individual edges as required so that
        /// each edge connects head-to-tail. Edges that do not connect to the current
        /// chain start a new ring.
        /// </summary>
        /// <remarks>
        /// S-57 does not guarantee that the edges bounding a single ring are listed
        /// in traversal order, nor does it separate the multiple interior boundaries
        /// (holes) of an area. Chaining by node identity reconstructs the individual
        /// rings, preventing the long cross-hole "spike" segments that a naive
        /// concatenation produces (S-57 Appendix B.1 area topology; S-100 Part 10a
        /// §4 surface ring topology).
        /// </remarks>
        /// <param name="edges">The edge references (with FSPT orientation) to chain.</param>
        /// <returns>One list of ordered, correctly oriented edges per contiguous ring.</returns>
        private List<List<S101CurveUsage>> ChainEdgesIntoRings(List<S101CurveUsage> edges)
        {
            var rings = new List<List<S101CurveUsage>>();
            var curveNodes = new Dictionary<uint, (uint? Begin, uint? End)>();
            var edgeNodes = new (uint? Begin, uint? End)[edges.Count];
            var incidentEdgesByNode = new Dictionary<uint, List<int>>();
            var used = new bool[edges.Count];

            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (!curveNodes.TryGetValue(edge.RecordId, out var nodes))
                {
                    nodes = (
                        EdgeNode(edge.RecordId, TopologyBegin),
                        EdgeNode(edge.RecordId, TopologyEnd));
                    curveNodes.Add(edge.RecordId, nodes);
                }

                edgeNodes[i] = nodes;

                if (nodes.Begin is uint begin)
                {
                    AddIncidentEdge(begin, i);
                }

                if (nodes.End is uint end && end != nodes.Begin)
                {
                    AddIncidentEdge(end, i);
                }
            }

            for (var seedIndex = 0; seedIndex < edges.Count; seedIndex++)
            {
                if (used[seedIndex])
                {
                    continue;
                }

                // Seed a new ring with the next available edge in its FSPT orientation.
                var seed = edges[seedIndex];
                used[seedIndex] = true;

                var seedOrientation = seed.Orientation == OrientationReverse
                    ? OrientationReverse
                    : OrientationForward;
                var ring = new List<S101CurveUsage>
                {
                    new(S101RcnmCurveSegment, seed.RecordId, seedOrientation),
                };

                uint? startNode = seedOrientation == OrientationReverse
                    ? edgeNodes[seedIndex].End
                    : edgeNodes[seedIndex].Begin;
                uint? endNode = seedOrientation == OrientationReverse
                    ? edgeNodes[seedIndex].Begin
                    : edgeNodes[seedIndex].End;

                // Extend the chain from its trailing node until the ring closes
                // (returns to its start node) or no connecting edge remains.
                var extended = true;
                while (extended && endNode is not null && endNode != startNode)
                {
                    extended = false;
                    if (!incidentEdgesByNode.TryGetValue(endNode.Value, out var candidates))
                    {
                        break;
                    }

                    foreach (var edgeIndex in candidates)
                    {
                        if (used[edgeIndex])
                        {
                            continue;
                        }

                        var edge = edges[edgeIndex];
                        var (begin, end) = edgeNodes[edgeIndex];
                        if (begin is not null && begin == endNode)
                        {
                            ring.Add(new S101CurveUsage(S101RcnmCurveSegment, edge.RecordId, OrientationForward));
                            endNode = end;
                            used[edgeIndex] = true;
                            extended = true;
                            break;
                        }

                        if (end is not null && end == endNode)
                        {
                            ring.Add(new S101CurveUsage(S101RcnmCurveSegment, edge.RecordId, OrientationReverse));
                            endNode = begin;
                            used[edgeIndex] = true;
                            extended = true;
                            break;
                        }
                    }
                }

                rings.Add(ring);
            }

            return rings;

            void AddIncidentEdge(uint nodeId, int edgeIndex)
            {
                if (!incidentEdgesByNode.TryGetValue(nodeId, out var edgeIndices))
                {
                    edgeIndices = [];
                    incidentEdgesByNode.Add(nodeId, edgeIndices);
                }

                edgeIndices.Add(edgeIndex);
            }
        }

        /// <summary>
        /// Returns the record id of the begin (<paramref name="topology"/> = 1) or
        /// end (2) node of a translated curve segment, or <see langword="null"/> when
        /// the segment or the requested node association is unavailable.
        /// </summary>
        private uint? EdgeNode(uint curveId, byte topology)
        {
            if (!CurveSegments.TryGetValue(curveId, out var segment)) return null;
            foreach (var pta in segment.PointAssociations)
            {
                if (pta.Topology == topology) return pta.RecordId;
            }

            return null;
        }

        // ── Catalogue interning ─────────────────────────────────────────

        private ushort GetOrAssignFeatureTypeCode(string s101Code)
        {
            if (_featureTypeByName.TryGetValue(s101Code, out var existing)) return existing;
            var code = _nextFeatureTypeCode++;
            _featureTypeByName[s101Code] = code;
            FeatureTypeCatalogue[code] = s101Code;
            return code;
        }

        private ushort GetOrAssignAttributeCode(string s101Code)
        {
            if (_attributeByName.TryGetValue(s101Code, out var existing)) return existing;
            var code = _nextAttributeCode++;
            _attributeByName[s101Code] = code;
            AttributeTypeCatalogue[code] = s101Code;
            return code;
        }

        private ushort GetOrAssignInformationTypeCode(string s101Code)
        {
            if (_informationTypeByName.TryGetValue(s101Code, out var existing)) return existing;
            var code = _nextInformationTypeCode++;
            _informationTypeByName[s101Code] = code;
            InformationTypeCatalogue[code] = s101Code;
            return code;
        }

        private ushort GetOrAssignInformationAssociationCode(string s101Code)
        {
            if (_informationAssociationByName.TryGetValue(s101Code, out var existing)) return existing;
            var code = _nextInformationAssociationCode++;
            _informationAssociationByName[s101Code] = code;
            InformationAssociationCatalogue[code] = s101Code;
            return code;
        }

        private ushort GetOrAssignFeatureAssociationCode(string s101Code)
        {
            if (_featureAssociationByName.TryGetValue(s101Code, out var existing)) return existing;
            var code = _nextFeatureAssociationCode++;
            _featureAssociationByName[s101Code] = code;
            FeatureAssociationCatalogue[code] = s101Code;
            return code;
        }

        private ushort GetOrAssignRoleCode(string s101Code)
        {
            if (_roleByName.TryGetValue(s101Code, out var existing)) return existing;
            var code = _nextRoleCode++;
            _roleByName[s101Code] = code;
            RoleCatalogue[code] = s101Code;
            return code;
        }
    }
}
