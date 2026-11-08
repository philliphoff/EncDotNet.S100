using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Tests;

// Phase 2: the structural inverses of the forward translation, the Annex C
// class changes and enumeration folds, and the topology rules (§2.5.1.1, §3.5).
public partial class S101ToS57TranslatorTests
{
    private const int AttlCatbrg = 9;
    private const int AttlCatctr = 16;
    private const int AttlCatlmk = 35;
    private const int AttlCatmor = 40;
    private const int AttlCatscf = 65;
    private const int AttlCatspm = 66;
    private const int AttlCalsgn = 6;
    private const int AttlCscale = 80;
    private const int AttlNatsur = 113;
    private const int AttlNatqua = 114;
    private const int AttlVerclr = 181;
    private const int AttlSymins = 192;
    private const int AttlVallma = 175;

    private static List<S57FeatureRecord> OfClass(S57Document document, int objl)
        => document.FeatureRecords.Where(f => (int)f.ObjectCode == objl).ToList();

    private static (int, int, int) Foid(S57FeatureRecord f)
        => (f.RecordName.AgencyCode, f.RecordName.FeatureId, f.RecordName.FeatureSubdivision);

    // A square of curves 1–4 between points 1–4, side `size` from (y, x).
    private static uint[] Square(DocumentBuilder b, uint first, int y, int x, int size)
    {
        b.Point(first, y, x);
        b.Point(first + 1, y + size, x);
        b.Point(first + 2, y + size, x + size);
        b.Point(first + 3, y, x + size);
        b.Curve(first, first, first + 1);
        b.Curve(first + 1, first + 1, first + 2);
        b.Curve(first + 2, first + 2, first + 3);
        b.Curve(first + 3, first + 3, first);
        return [first, first + 1, first + 2, first + 3];
    }

    // ── Bridges (§6.6–§6.8) ─────────────────────────────────────────────

    [Fact]
    public void Bridge_WithOneSpan_BecomesOneBridgeWithTheBridgesIdentity()
    {
        var b = new DocumentBuilder();
        b.Point(1, 0, 0);
        b.Point(2, 0, 100);
        b.Curve(1, 1, 2);
        var bridge = b.Feature("Bridge", (120, 1u), [R("openingBridge", "false"), R("featureName", ""), R("name", "Old Bridge", 2), R("nameUsage", "1", 2)], fidn: 10);
        var span = b.Feature("SpanFixed", (120, 1u), [R("verticalClearanceFixed", ""), R("verticalClearanceValue", "12.5", 1)], fidn: 11);
        b.Associate(bridge, "BridgeAggregation", "theComponent", span);

        var document = new S101ToS57Translator().Translate(b.Build());

        var result = Assert.Single(document.FeatureRecords);
        Assert.Equal((int)S57ObjectCode.BRIDGE, (int)result.ObjectCode);
        Assert.Equal((550, 10, 1), Foid(result));
        Assert.Equal("1", Value(result, AttlCatbrg));
        Assert.Equal("12.5", Value(result, AttlVerclr));
        Assert.Equal("Old Bridge", Value(result, AttlObjnam));
    }

    [Fact]
    public void Bridge_WithTwoSpans_BecomesABridgePerSpanTiedByCAggr()
    {
        var b = new DocumentBuilder();
        b.Point(1, 0, 0);
        b.Point(2, 0, 100);
        b.Point(3, 0, 200);
        b.Curve(1, 1, 2);
        b.Curve(2, 2, 3);
        var bridge = b.Feature("Bridge", null, [R("openingBridge", "true"), R("categoryOfOpeningBridge", "3")], fidn: 10);
        var fixedSpan = b.Feature("SpanFixed", (120, 1u), [R("verticalClearanceFixed", ""), R("verticalClearanceValue", "8", 1)], fidn: 11);
        var opening = b.Feature("SpanOpening", (120, 2u), [R("verticalClearanceClosed", ""), R("verticalClearanceValue", "4", 1)], fidn: 12);
        b.Associate(bridge, "BridgeAggregation", "theComponent", fixedSpan);
        b.Associate(bridge, "BridgeAggregation", "theComponent", opening);

        var document = new S101ToS57Translator().Translate(b.Build());

        var bridges = OfClass(document, (int)S57ObjectCode.BRIDGE);
        Assert.Equal(2, bridges.Count);
        Assert.Contains(bridges, f => Foid(f) == (550, 11, 1) && Value(f, AttlVerclr) == "8");
        Assert.Contains(bridges, f => Foid(f) == (550, 12, 1) && Value(f, 182) == "4");
        Assert.All(bridges, f => Assert.Equal("2,3", Value(f, AttlCatbrg)));

        var collection = Assert.Single(OfClass(document, 400));
        Assert.Equal((550, 10, 1), Foid(collection));
        Assert.Equal(2, collection.FeaturePointers.Count);
        Assert.All(collection.FeaturePointers, p => Assert.Equal(S57RelationshipIndicator.Peer, p.Relationship));
    }

    [Theory]
    [InlineData("26", (int)S57ObjectCode.BRIDGE, AttlCatbrg, "1")]
    [InlineData("27", (int)S57ObjectCode.DAMCON, 0, null)]
    [InlineData("22", (int)S57ObjectCode.CTRPNT, AttlCatctr, "1")]
    [InlineData("23", (int)S57ObjectCode.CTRPNT, AttlCatctr, "5")]
    public void Landmark_ByCategory_BecomesAnotherClass(string category, int objl, int attl, string? value)
    {
        var feature = Single(new S101ToS57Translator().Translate(
            PointFeature("Landmark", R("categoryOfLandmark", category), R("visualProminence", "1"))));

        Assert.Equal(objl, (int)feature.ObjectCode);
        Assert.Null(Value(feature, AttlCatlmk));
        if (attl != 0) Assert.Equal(value, Value(feature, attl));
    }

    // ── Mooring facilities (§8.4, §8.16, §20.8) ─────────────────────────

    [Fact]
    public void Bollard_BecomesMorfacCatmor3()
    {
        var feature = Single(new S101ToS57Translator().Translate(PointFeature("Bollard")));
        Assert.Equal((int)S57ObjectCode.MORFAC, (int)feature.ObjectCode);
        Assert.Equal("3", Value(feature, AttlCatmor));
    }

    [Fact]
    public void MooringBuoy_ForVisitors_BecomesSmcfac()
    {
        var feature = Single(new S101ToS57Translator().Translate(PointFeature("MooringBuoy", R("visitorsMooring", "true"))));
        Assert.Equal((int)S57ObjectCode.SMCFAC, (int)feature.ObjectCode);
        Assert.Equal("29", Value(feature, AttlCatscf));
    }

    [Fact]
    public void Pile_RadarConspicuousPoint_BecomesSpecialPurposeBeacon()
    {
        var feature = Single(new S101ToS57Translator().Translate(PointFeature("Pile", R("radarConspicuous", "true"))));
        Assert.Equal((int)S57ObjectCode.BCNSPP, (int)feature.ObjectCode);
        Assert.Equal("3", Value(feature, AttlConrad));
        Assert.Equal("52", Value(feature, AttlCatspm));
        Assert.Equal(string.Empty, Value(feature, AttlColour));
    }

    [Fact]
    public void Pile_Curve_BecomesMorfacCatmor5()
    {
        var b = new DocumentBuilder();
        b.Point(1, 0, 0);
        b.Point(2, 0, 100);
        b.Curve(1, 1, 2);
        b.Feature("Pile", (120, 1u), []);

        var feature = Single(new S101ToS57Translator().Translate(b.Build()));
        Assert.Equal((int)S57ObjectCode.MORFAC, (int)feature.ObjectCode);
        Assert.Equal("5", Value(feature, AttlCatmor));
    }

    // ── Seabed (§12.1) ──────────────────────────────────────────────────

    [Fact]
    public void SurfaceCharacteristics_WithUnderlyingLayer_UseSlash()
    {
        var feature = Single(new S101ToS57Translator().Translate(PointFeature("SeabedArea",
            R("surfaceCharacteristics", ""), R("natureOfSurface", "4", 1), R("natureOfSurfaceQualifyingTerms", "1", 1),
            R("surfaceCharacteristics", ""), R("natureOfSurface", "1", 4),
            R("surfaceCharacteristics", ""), R("natureOfSurface", "9", 6), R("underlyingLayer", "true", 6))));

        Assert.Equal("4,1/9", Value(feature, AttlNatsur));
        Assert.Equal("1,/", Value(feature, AttlNatqua));
    }

    // ── Aggregations and relationships (§15.6, §18.2, §21.2) ────────────

    [Fact]
    public void RangeSystem_BecomesCAggrOfItsMembers()
    {
        var b = new DocumentBuilder();
        b.Point(1, 0, 0);
        b.Point(2, 0, 100);
        b.Curve(1, 1, 2);
        var line = b.Feature("NavigationLine", (120, 1u), [R("categoryOfNavigationLine", "3")], fidn: 2);
        var light = b.Feature("LightAllAround", (110, 1u), [], fidn: 3);
        var range = b.Feature("RangeSystem", null, [], fidn: 4);
        b.Associate(range, "RangeSystemAggregation", "theComponent", line);
        b.Associate(range, "RangeSystemAggregation", "theComponent", light);

        var document = new S101ToS57Translator().Translate(b.Build());

        var collection = Assert.Single(OfClass(document, 400));
        Assert.Equal((550, 4, 1), Foid(collection));
        Assert.Equal(S57GeometricPrimitive.None, collection.Primitive);
        Assert.Equal(
            [new S57LongName(550, 2, 1), new S57LongName(550, 3, 1)],
            collection.FeaturePointers.Select(p => p.Name).OrderBy(n => n.FeatureId).ToArray());
    }

    [Fact]
    public void StructureEquipment_BecomesMasterAndSlave()
    {
        var b = new DocumentBuilder();
        b.Point(1, 0, 0);
        var buoy = b.Feature("LateralBuoy", (110, 1u), [R("categoryOfLateralMark", "1")], fidn: 2);
        var light = b.Feature("LightAllAround", (110, 1u), [], fidn: 3);
        b.Associate(buoy, "StructureEquipment", "theEquipment", light);

        var document = new S101ToS57Translator().Translate(b.Build());

        var master = Assert.Single(OfClass(document, (int)S57ObjectCode.BOYLAT));
        var pointer = Assert.Single(master.FeaturePointers);
        Assert.Equal(new S57LongName(550, 3, 1), pointer.Name);
        Assert.Equal(S57RelationshipIndicator.Slave, pointer.Relationship);
    }

    [Fact]
    public void PhysicalAisAid_BecomesTextOnItsStructure()
    {
        var b = new DocumentBuilder();
        b.Point(1, 0, 0);
        var buoy = b.Feature("LateralBuoy", (110, 1u), [], fidn: 2);
        var ais = b.Feature("PhysicalAISAidToNavigation", (110, 1u), [], fidn: 3);
        b.Associate(buoy, "StructureEquipment", "theEquipment", ais);

        var feature = Single(new S101ToS57Translator().Translate(b.Build()));
        Assert.Equal("Automatic Identification System (AIS) aid to navigation", Value(feature, AttlInform));
    }

    [Fact]
    public void VirtualAisAid_BecomesNewobjWithItsSymbol()
    {
        var feature = Single(new S101ToS57Translator().Translate(
            PointFeature("VirtualAISAidToNavigation", R("virtualAISAidToNavigationType", "10"))));
        Assert.Equal(163, (int)feature.ObjectCode);
        Assert.Equal("SY(BRTHNO01);SY(BOYSAW12);TX('V-AIS',3,2,2,'15110',2,0,CHMGD,11)", Value(feature, AttlSymins));
        Assert.Equal("Virtual AtoN, Safe Water; A Virtual object marking safe water.", Value(feature, AttlInform));
    }

    // ── Information types (§24) ─────────────────────────────────────────

    [Fact]
    public void ContactDetails_GiveCallSign_AndServiceHoursAreDropped()
    {
        var b = new DocumentBuilder();
        var contact = b.Information(1, "ContactDetails", R("callSign", "KXYZ"), R("contactInstructions", "Call ahead"));
        var hours = b.Information(2, "ServiceHours", R("scheduleByDayOfWeek", ""));
        b.Feature("RadioStation", (110, b.Point(1, 0, 0)), [], 1, contact, hours);
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var feature = Single(new S101ToS57Translator().Translate(b.Build(), diagnostics));

        Assert.Equal("KXYZ", Value(feature, AttlCalsgn));
        Assert.Equal("Contact instructions: Call ahead", Value(feature, AttlInform));
        Assert.Equal(1, diagnostics.DroppedInformationTypes[new S101FeatureDrop("ServiceHours", "24.2")]);
    }

    // ── Enumeration differences (Table A-2, §27) ────────────────────────

    [Fact]
    public void EnumerationValue_S57Lacks_FoldsIntoInform()
    {
        var feature = Single(new S101ToS57Translator().Translate(PointFeature("Hulk", R("categoryOfHulk", "6"))));
        Assert.Null(Value(feature, 31));
        Assert.Equal("Casino", Value(feature, AttlInform));
    }

    [Fact]
    public void EnumerationValue_Remapped()
    {
        var feature = Single(new S101ToS57Translator().Translate(PointFeature("PipelineSubmarineOnLand", R("categoryOfPipelinePipe", "7"))));
        Assert.Equal("5", Value(feature, 47));
    }

    [Fact]
    public void Measurement_FoldsIntoInformAsText()
    {
        var feature = Single(new S101ToS57Translator().Translate(PointFeature("Berth", R("maximumPermittedDraught", "12.50"))));
        Assert.Equal("Maximum draught permitted = 12.5 metres", Value(feature, AttlInform));
    }

    [Fact]
    public void MagneticAnomaly_DegreesBecomeMinutes()
    {
        var feature = Single(new S101ToS57Translator().Translate(PointFeature("LocalMagneticAnomaly",
            R("valueOfLocalMagneticAnomaly", ""), R("magneticAnomalyValue", "1.5", 1))));
        Assert.Equal("90", Value(feature, AttlVallma));
    }

    [Fact]
    public void AttributeAnnexCDoesNotConvertOnTheClass_IsDropped()
    {
        var diagnostics = new S101ToS57TranslationDiagnostics();
        var feature = Single(new S101ToS57Translator().Translate(PointFeature("Daymark", R("radarConspicuous", "true")), diagnostics));
        Assert.Null(Value(feature, AttlConrad));
        Assert.Equal(1, diagnostics.DroppedAttributes[new S101AttributeDrop("radarConspicuous", "20.14")]);
    }

    // ── Geometric primitives (Table 2-1) ────────────────────────────────

    [Fact]
    public void SurfaceStation_BecomesPointInside()
    {
        var b = new DocumentBuilder();
        b.Feature("CoastGuardStation", (130, b.Surface(1, Square(b, 1, 0, 0, 1000))), []);
        var document = new S101ToS57Translator().Translate(b.Build());
        var feature = Single(document);

        Assert.Equal(S57GeometricPrimitive.Point, feature.Primitive);
        var node = Assert.Single(document.VectorRecords);
        Assert.Equal(110, node.RecordName.RecordNameCode);
        Assert.Equal(new S57Coordinate2D { X = 500, Y = 500 }, node.Coordinates2D[0]);
    }

    [Fact]
    public void CurveCollisionRegulationsLimit_BecomesNarrowArea()
    {
        var b = new DocumentBuilder();
        b.Point(1, 400_000_000, -700_000_000);
        b.Point(2, 400_000_000, -699_000_000);
        b.Curve(1, 1, 2);
        b.Feature("CollisionRegulationsLimit", (120, 1u), []);

        var document = new S101ToS57Translator().Translate(b.Build());

        var feature = Single(document);
        Assert.Equal((int)S57ObjectCode.CTNARE, (int)feature.ObjectCode);
        Assert.Equal(S57GeometricPrimitive.Area, feature.Primitive);
        Assert.Equal("Collision regulations limit", Value(feature, AttlInform));
        Assert.Empty(S57TopologyCheck.Check(document));
        // 0.3 mm at 1:50 000 is 15 m, about 1 350 units of 1e-7° of latitude.
        var ys = document.VectorRecords.SelectMany(v => v.Coordinates2D).Select(c => c.Y).ToList();
        Assert.InRange(ys.Max() - ys.Min(), 1_250, 1_450);
    }

    // ── Skin of the earth (§2.5.1.1) ────────────────────────────────────

    [Fact]
    public void Pontoon_InsideDepthArea_IsCutOut()
    {
        var b = new DocumentBuilder();
        b.Feature("DepthArea", (130, b.Surface(1, Square(b, 1, 0, 0, 1000))), [R("depthRangeMinimumValue", "0"), R("depthRangeMaximumValue", "5")]);
        b.Feature("Pontoon", (130, b.Surface(2, Square(b, 11, 400, 400, 100))), [], fidn: 2);
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var document = new S101ToS57Translator().Translate(b.Build(), diagnostics);

        var depth = Assert.Single(OfClass(document, (int)S57ObjectCode.DEPARE));
        Assert.Contains(depth.SpatialPointers, p => p.Usage == S57UsageIndicator.Interior);
        // The hole is the pontoon's edges, shared.
        var pontoon = Assert.Single(OfClass(document, (int)S57ObjectCode.PONTON));
        Assert.Equal(
            pontoon.SpatialPointers.Select(p => p.Name.RecordId).Order(),
            depth.SpatialPointers.Where(p => p.Usage == S57UsageIndicator.Interior).Select(p => p.Name.RecordId).Order());
        Assert.Empty(S57TopologyCheck.Check(document));
        Assert.Equal(1, diagnostics.SkinOfTheEarthCuts["DEPARE"]);
    }

    [Fact]
    public void FloatingDock_AcrossDepthAndLandAreas_SplitsTheSharedEdge()
    {
        var b = new DocumentBuilder();
        // A depth area west of x = 1000 and a land area east of it, sharing
        // the curve along x = 1000 (curve 2 of the first square).
        var depth = Square(b, 1, 0, 0, 1000);
        b.Point(5, 1000, 2000);
        b.Point(6, 0, 2000);
        b.Curve(5, 3, 5);
        b.Curve(6, 5, 6);
        b.Curve(7, 6, 4);
        b.Feature("DepthArea", (130, b.Surface(1, depth)), [R("depthRangeMinimumValue", "0"), R("depthRangeMaximumValue", "5")]);
        b.Feature("LandArea", (130, b.Surface(2, 3, 5, 6, 7)), [], fidn: 2);
        b.Feature("FloatingDock", (130, b.Surface(3, Square(b, 11, 400, 900, 200))), [], fidn: 3);

        var document = new S101ToS57Translator().Translate(b.Build());

        Assert.Single(OfClass(document, (int)S57ObjectCode.DEPARE));
        Assert.Single(OfClass(document, (int)S57ObjectCode.LNDARE));
        Assert.Empty(S57TopologyCheck.Check(document));
    }

    // ── Coverage (§3.5) ─────────────────────────────────────────────────

    [Fact]
    public void Coverage_TilesItsBoundingBox_AndScalesThatDifferGetMCscl()
    {
        var b = new DocumentBuilder();
        // Two adjoining coverages, at 1:22 000 and 1:45 000, and a point
        // further east that widens the bounding box.
        var west = Square(b, 1, 0, 0, 1000);
        b.Point(5, 1000, 2000);
        b.Point(6, 0, 2000);
        b.Curve(5, 3, 5);
        b.Curve(6, 5, 6);
        b.Curve(7, 6, 4);
        b.Feature("DataCoverage", (130, b.Surface(1, west)), [R("optimumDisplayScale", "22000")]);
        b.Feature("DataCoverage", (130, b.Surface(2, 3, 5, 6, 7)), [R("optimumDisplayScale", "45000")], fidn: 2);
        b.Feature("Landmark", (110, b.Point(20, 500, 3000)), [R("categoryOfLandmark", "17")], fidn: 3);

        var document = new S101ToS57Translator().Translate(b.Build());

        var coverage = OfClass(document, (int)S57ObjectCode.M_COVR);
        Assert.Single(coverage, f => Value(f, AttlCatcov) == "1");
        Assert.Single(coverage, f => Value(f, AttlCatcov) == "2");
        var scale = Assert.Single(OfClass(document, (int)S57ObjectCode.M_CSCL));
        Assert.Equal("45000", Value(scale, AttlCscale));
        Assert.Equal(22000, document.DataSetParameters!.CompilationScale);
        Assert.Empty(S57TopologyCheck.Check(document));
    }

    [Fact]
    public void Coverage_SliverAgainstItsBoundingBox_StaysUncovered()
    {
        // #1291: the coverage's southern edge drops one unit across its
        // width, so the bounding box leaves a sliver one unit tall beneath
        // it. The sliver has no grid point inside it; it must not be taken
        // for covered, or M_COVR grows past the skin of the earth.
        var b = new DocumentBuilder();
        b.Point(1, 1, 0);
        b.Point(2, 1000, 0);
        b.Point(3, 1000, 1000);
        b.Point(4, 0, 1000);
        b.Curve(1, 1, 2);
        b.Curve(2, 2, 3);
        b.Curve(3, 3, 4);
        b.Curve(4, 4, 1);
        b.Feature("DataCoverage", (130, b.Surface(1, 1, 2, 3, 4)), []);
        b.Feature("DepthArea", (130, b.Surface(2, 1, 2, 3, 4)), [R("depthRangeMinimumValue", "0"), R("depthRangeMaximumValue", "5")], fidn: 2);

        var document = new S101ToS57Translator().Translate(b.Build());

        Assert.Single(OfClass(document, (int)S57ObjectCode.M_COVR), f => Value(f, AttlCatcov) == "2");
        Assert.Empty(S57TopologyCheck.Check(document));
    }

    [Fact]
    public void TopologyCheck_ReportsOverlapsAndOpenRings()
    {
        var b = new DocumentBuilder();
        b.Feature("LandArea", (130, b.Surface(1, Square(b, 1, 0, 0, 1000))), []);
        b.Feature("LandArea", (130, b.Surface(2, Square(b, 11, 500, 500, 1000))), [], fidn: 2);
        b.Point(30, 0, 5000);
        b.Point(31, 100, 5000);
        b.Curve(30, 30, 31);
        b.Feature("LandArea", (130, b.Surface(3, 30)), [], fidn: 3);

        var issues = S57TopologyCheck.Check(new S101ToS57Translator().Translate(b.Build()));

        Assert.Contains(issues, i => i.Kind == "skin-overlap");
        Assert.Contains(issues, i => i.Kind == "open-ring");
    }
}
