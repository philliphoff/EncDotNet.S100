using EncDotNet.S100.Datasets.S101;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Tests;

/// <summary>
/// Tests for <see cref="S101ToS57Translator"/>, one or more per S-65 Annex C
/// general rule (§2), plus the meta-feature and structural rules Phase 1 covers.
/// </summary>
public class S101ToS57TranslatorTests
{
    // ── Builder ─────────────────────────────────────────────────────────

    // Builds an S-101 document in memory. Attribute rows are given as
    // (code, value, parent), where parent is the 1-based position of the
    // complex row the row nests in (PAIX), or 0 at the top level.
    private sealed class DocumentBuilder
    {
        private readonly Dictionary<ushort, string> _featureTypes = new();
        private readonly Dictionary<ushort, string> _attributes = new();
        private readonly Dictionary<ushort, string> _informationTypes = new();
        private readonly Dictionary<ushort, string> _informationAssociations = new();
        private readonly Dictionary<ushort, string> _roles = new();
        private readonly Dictionary<uint, S101PointRecord> _points = new();
        private readonly Dictionary<uint, S101MultiPointRecord> _multiPoints = new();
        private readonly Dictionary<uint, S101CurveSegmentRecord> _curves = new();
        private readonly Dictionary<uint, S101SurfaceRecord> _surfaces = new();
        private readonly Dictionary<uint, S101InformationRecord> _information = new();
        private readonly List<S101FeatureRecord> _features = [];

        public uint Point(uint id, int y, int x)
        {
            _points[id] = new S101PointRecord { RecordId = id, Y = y, X = x };
            return id;
        }

        public uint Curve(uint id, uint begin, uint end, params (int Y, int X)[] intermediates)
        {
            _curves[id] = new S101CurveSegmentRecord
            {
                RecordId = id,
                PointAssociations = [new(110, begin, 1), new(110, end, 2)],
                IntermediateCoordinates = intermediates,
            };
            return id;
        }

        public uint Surface(uint id, params uint[] exteriorCurves)
        {
            _surfaces[id] = new S101SurfaceRecord
            {
                RecordId = id,
                RingAssociations = exteriorCurves.Select(c => new S101RingAssociation(120, c, 1, 1)).ToList(),
            };
            return id;
        }

        public uint MultiPoint(uint id, params (int Y, int X, int Z)[] points)
        {
            _multiPoints[id] = new S101MultiPointRecord { RecordId = id, Points = points };
            return id;
        }

        public uint Information(uint id, string type, params (string Code, string Value, int Parent)[] rows)
        {
            _information[id] = new S101InformationRecord
            {
                RecordId = id,
                InformationTypeCode = Code(_informationTypes, type),
                Attributes = Rows(rows),
            };
            return id;
        }

        public S101FeatureRecord Feature(
            string type,
            (byte Rcnm, uint Id)? geometry,
            (string Code, string Value, int Parent)[] rows,
            uint fidn = 1,
            params uint[] information)
        {
            var feature = new S101FeatureRecord
            {
                RecordId = (uint)_features.Count + 1,
                FeatureTypeCode = Code(_featureTypes, type),
                ProducingAgency = 550,
                FeatureIdentificationNumber = fidn,
                FeatureIdentificationSubdivision = 1,
                Attributes = Rows(rows),
                SpatialAssociations = geometry is { } g ? [new S101SpatialAssociation(g.Rcnm, g.Id, 1)] : [],
                InformationAssociations = information
                    .Select(id => new S101InformationAssociation(
                        Code(_informationAssociations, "AdditionalInformation"), id, Code(_roles, "theInformation")))
                    .ToList(),
            };
            _features.Add(feature);
            return feature;
        }

        private List<S101Attribute> Rows((string Code, string Value, int Parent)[] rows)
            => rows.Select(r => new S101Attribute(Code(_attributes, r.Code), 1, r.Value, (ushort)r.Parent)).ToList();

        private static ushort Code(Dictionary<ushort, string> catalogue, string name)
        {
            foreach (var (code, existing) in catalogue)
            {
                if (existing == name) return code;
            }
            var next = (ushort)(catalogue.Count + 1);
            catalogue[next] = name;
            return next;
        }

        public S101Document Build() => new()
        {
            Identification = new S101DatasetIdentification { DatasetName = "101TEST", DatasetReferenceDate = "2025-05-01", DatasetEdition = "3" },
            StructureInfo = new S101DatasetStructureInfo { CoordinateMultiplicationFactorX = 10_000_000, CoordinateMultiplicationFactorY = 10_000_000, CoordinateMultiplicationFactorZ = 10 },
            FeatureTypeCatalogue = _featureTypes,
            AttributeTypeCatalogue = _attributes,
            Points = _points,
            MultiPoints = _multiPoints,
            CurveSegments = _curves,
            CompositeCurves = new Dictionary<uint, S101CompositeCurveRecord>(),
            Surfaces = _surfaces,
            Features = _features,
            InformationTypes = _information,
            InformationTypeCatalogue = _informationTypes,
            InformationAssociationCatalogue = _informationAssociations,
            FeatureAssociationCatalogue = new Dictionary<ushort, string>(),
            RoleCatalogue = _roles,
        };
    }

    private static (string, string, int) R(string code, string value, int parent = 0) => (code, value, parent);

    // A one-feature document at a point.
    private static S101Document PointFeature(string type, params (string, string, int)[] rows)
    {
        var b = new DocumentBuilder();
        b.Feature(type, (110, b.Point(1, 400_000_000, -700_000_000)), rows);
        return b.Build();
    }

    private static S57FeatureRecord Single(S57Document document)
        => Assert.Single(document.FeatureRecords);

    private static string? Value(S57FeatureRecord feature, int attl)
        => feature.Attributes.Concat(feature.NationalAttributes)
            .Where(a => a.AttributeCode == attl)
            .Select(a => (string?)a.Value)
            .FirstOrDefault();

    private const int AttlColour = 75;
    private const int AttlCatslc = 60;
    private const int AttlConrad = 82;
    private const int AttlDrval1 = 87;
    private const int AttlDrval2 = 88;
    private const int AttlDatsta = 86;
    private const int AttlPersta = 119;
    private const int AttlSordat = 147;
    private const int AttlInform = 102;
    private const int AttlNinfom = 300;
    private const int AttlObjnam = 116;
    private const int AttlNobjnm = 301;
    private const int AttlTxtdsc = 158;
    private const int AttlNtxtds = 304;
    private const int AttlPicrep = 120;
    private const int AttlCatlit = 37;
    private const int AttlSectr1 = 136;
    private const int AttlLitchr = 107;
    private const int AttlTopshp = 171;
    private const int AttlCatcov = 18;
    private const int AttlSigseq = 143;

    // ── §2.4.1 Multiplicity ─────────────────────────────────────────────

    [Fact]
    public void Multiplicity_ListAttribute_JoinsValuesInOrder()
    {
        var document = PointFeature("LateralBuoy", R("colour", "3"), R("colour", "1"), R("colour", "3"));

        var feature = Single(new S101ToS57Translator().Translate(document));

        Assert.Equal((int)S57ObjectCode.BOYLAT, (int)feature.ObjectCode);
        Assert.Equal("3,1,3", Value(feature, AttlColour));
    }

    [Fact]
    public void Multiplicity_EnumeratedAttribute_KeepsFirstValue()
    {
        var document = PointFeature("ShorelineConstruction", R("categoryOfShorelineConstruction", "6"), R("categoryOfShorelineConstruction", "4"));
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var feature = Single(new S101ToS57Translator().Translate(document, diagnostics));

        Assert.Equal("6", Value(feature, AttlCatslc));
        Assert.Equal(1, diagnostics.TruncatedToFirst["CATSLC"]);
    }

    // ── §2.4.3 Mandatory and conditional attributes ─────────────────────

    [Fact]
    public void MandatoryAttribute_WithoutSourceValue_IsWrittenEmpty()
    {
        var b = new DocumentBuilder();
        b.Point(1, 0, 0);
        b.Point(2, 0, 10);
        b.Point(3, 10, 10);
        b.Curve(1, 1, 2);
        b.Curve(2, 2, 3);
        b.Curve(3, 3, 1);
        b.Feature("DepthArea", (130, b.Surface(1, 1, 2, 3)), [R("depthRangeMinimumValue", "5")]);
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var feature = Single(new S101ToS57Translator().Translate(b.Build(), diagnostics));

        Assert.Equal("5", Value(feature, AttlDrval1));
        Assert.Equal(string.Empty, Value(feature, AttlDrval2));
        Assert.Equal(1, diagnostics.MandatoryAttributesNulled[new S57AttributeNulled("DEPARE", "DRVAL2")]);
    }

    // ── §2.4.2 / §2.4.11 Boolean → enumeration ──────────────────────────

    [Theory]
    [InlineData("true", "1")]
    [InlineData("1", "1")]
    [InlineData("false", "2")]
    [InlineData("0", "2")]
    public void Boolean_RadarConspicuous_BecomesConrad(string value, string conrad)
    {
        var feature = Single(new S101ToS57Translator().Translate(PointFeature("Landmark", R("radarConspicuous", value))));

        Assert.Equal(conrad, Value(feature, AttlConrad));
    }

    [Fact]
    public void Boolean_NonBooleanValue_IsReadAsFalseAndReported()
    {
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var feature = Single(new S101ToS57Translator().Translate(PointFeature("Landmark", R("radarConspicuous", "3")), diagnostics));

        Assert.Equal("2", Value(feature, AttlConrad));
        Assert.Equal(1, diagnostics.NonBooleanValues);
    }

    // ── §2.4.2 / §2.4.8 Truncated dates ─────────────────────────────────

    [Fact]
    public void TruncatedDate_LosesTrailingDashes()
    {
        var document = PointFeature("Landmark",
            R("reportedDate", "2019----"),
            R("fixedDateRange", ""), R("dateStart", "201905--", 2),
            R("periodicDateRange", ""), R("dateStart", "--0315", 4));

        var feature = Single(new S101ToS57Translator().Translate(document));

        Assert.Equal("2019", Value(feature, AttlSordat));
        Assert.Equal("201905", Value(feature, AttlDatsta));
        Assert.Equal("--0315", Value(feature, AttlPersta));
    }

    // ── §2.4.2 / §2.4.9 Time, URI and URN ───────────────────────────────

    [Fact]
    public void TimeUriAndUrnAttributes_AreNotConverted()
    {
        var document = PointFeature("Landmark",
            R("timeOfDayStart", "080000"),
            R("linkage", "https://example.org"),
            R("interoperabilityIdentifier", "urn:mrn:example"));
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var feature = Single(new S101ToS57Translator().Translate(document, diagnostics));

        Assert.Empty(feature.Attributes);
        Assert.Equal(1, diagnostics.DroppedAttributes[new S101AttributeDrop("timeOfDayStart", "2.4.9")]);
        Assert.Equal(1, diagnostics.DroppedAttributes[new S101AttributeDrop("linkage", "2.4.2")]);
        Assert.Equal(1, diagnostics.DroppedAttributes[new S101AttributeDrop("interoperabilityIdentifier", "2.4.2")]);
    }

    // ── §2.1 / §2.5.8.1 Text placement ──────────────────────────────────

    [Fact]
    public void TextPlacement_IsDropped()
    {
        var b = new DocumentBuilder();
        b.Feature("TextPlacement", (110, b.Point(1, 0, 0)), [R("text", "Harbour")]);
        b.Feature("Landmark", (110, b.Point(2, 0, 0)), [], fidn: 2);
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var result = new S101ToS57Translator().Translate(b.Build(), diagnostics);

        Assert.Equal((int)S57ObjectCode.LNDMRK, (int)Assert.Single(result.FeatureRecords).ObjectCode);
        Assert.Equal(1, diagnostics.DroppedFeatures[new S101FeatureDrop("TextPlacement", "2.5.8.1")]);
        Assert.Equal(2, diagnostics.FeaturesRead);
        Assert.Equal(1, diagnostics.FeaturesWritten);
    }

    // ── §2.5.8 / §27.132 Feature names ──────────────────────────────────

    private static (string, string, int)[] Names(params (string Name, string Language, string? Usage)[] names)
    {
        var rows = new List<(string, string, int)>();
        foreach (var (name, language, usage) in names)
        {
            var parent = rows.Count + 1;
            rows.Add(R("featureName", ""));
            rows.Add(R("name", name, parent));
            rows.Add(R("language", language, parent));
            if (usage is not null) rows.Add(R("nameUsage", usage, parent));
        }
        return [.. rows];
    }

    [Fact]
    public void FeatureName_ByNameUsage_BecomesObjnamAndNobjnm()
    {
        var document = PointFeature("Landmark", Names(("Tour Eiffel", "fra", "2"), ("Eiffel Tower", "eng", "1")));

        var feature = Single(new S101ToS57Translator().Translate(document));

        Assert.Equal("Eiffel Tower", Value(feature, AttlObjnam));
        Assert.Equal("Tour Eiffel", Value(feature, AttlNobjnm));
        Assert.Contains(feature.NationalAttributes, a => a.AttributeCode == AttlNobjnm);
        Assert.DoesNotContain(feature.Attributes, a => a.AttributeCode == AttlNobjnm);
    }

    [Fact]
    public void FeatureName_SeveralAlternates_PrefersConfiguredLanguage()
    {
        var document = PointFeature("Landmark", Names(("Name", "eng", "1"), ("Nom", "fra", "2"), ("Name DE", "deu", "2")));

        var feature = Single(new S101ToS57Translator(new S101ToS57TranslationOptions { NationalLanguage = "deu" }).Translate(document));

        Assert.Equal("Name DE", Value(feature, AttlNobjnm));
    }

    [Fact]
    public void FeatureName_WithoutNameUsage_IsDroppedWhenConfigured()
    {
        var document = PointFeature("Landmark", Names(("Eiffel Tower", "eng", null)));
        var diagnostics = new S101ToS57TranslationDiagnostics();
        var options = new S101ToS57TranslationOptions { NameWithoutUsage = S101NameWithoutUsage.Drop };

        var feature = Single(new S101ToS57Translator(options).Translate(document, diagnostics));

        Assert.Null(Value(feature, AttlObjnam));
        Assert.Equal(1, diagnostics.DroppedAttributes[new S101AttributeDrop("featureName", "2.5.8")]);
    }

    [Fact]
    public void FeatureName_WithoutNameUsage_ByLanguage_ChoosesEnglishFirst()
    {
        var document = PointFeature("Landmark", Names(("Tour Eiffel", "", null), ("Eiffel Tower", "eng", null)));

        var feature = Single(new S101ToS57Translator().Translate(document));

        Assert.Equal("Tour Eiffel", Value(feature, AttlObjnam));
        Assert.Equal("Eiffel Tower", Value(feature, AttlNobjnm));
    }

    // ── §2.4.12 Support files ───────────────────────────────────────────

    [Fact]
    public void FileReference_ByLanguage_BecomesTxtdscOrNtxtds()
    {
        var document = PointFeature("Landmark",
            R("information", ""), R("fileReference", "US123.TXT", 1), R("language", "eng", 1),
            R("information", ""), R("fileReference", "FR123.TXT", 4), R("language", "fra", 4));

        var feature = Single(new S101ToS57Translator().Translate(document));

        Assert.Equal("US123.TXT", Value(feature, AttlTxtdsc));
        Assert.Equal("FR123.TXT", Value(feature, AttlNtxtds));
    }

    [Fact]
    public void PictorialRepresentation_KeepsFirstOnly()
    {
        var diagnostics = new S101ToS57TranslationDiagnostics();
        var document = PointFeature("Landmark", R("pictorialRepresentation", "A.JPG"), R("pictorialRepresentation", "B.JPG"));

        var feature = Single(new S101ToS57Translator().Translate(document, diagnostics));

        Assert.Equal("A.JPG", Value(feature, AttlPicrep));
        Assert.Equal(1, diagnostics.TruncatedToFirst["PICREP"]);
    }

    // ── §2.4.6 Textual information ──────────────────────────────────────

    private static S101Document TextFeature()
    {
        var b = new DocumentBuilder();
        var info = b.Information(1, "NauticalInformation",
            R("information", ""), R("text", "Second", 1), R("language", "eng", 1),
            R("information", ""), R("text", "Deuxième", 4), R("language", "fra", 4));
        b.Feature("Landmark", (110, b.Point(1, 0, 0)),
            [R("information", ""), R("text", "First", 1), R("language", "eng", 1)],
            information: info);
        return b.Build();
    }

    [Fact]
    public void Text_FoldsIntoInformAndNinfom_WithSeparator()
    {
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var feature = Single(new S101ToS57Translator().Translate(TextFeature(), diagnostics));

        Assert.Equal("First;Second", Value(feature, AttlInform));
        Assert.Equal("Deuxième", Value(feature, AttlNinfom));
        Assert.Equal(1, diagnostics.FoldedIntoInform["information"]);
        Assert.Equal(2, diagnostics.FoldedIntoInform["NauticalInformation"]);
    }

    [Fact]
    public void Text_SeparatorIsConfigurable()
    {
        var options = new S101ToS57TranslationOptions { InformSeparator = " | " };

        var feature = Single(new S101ToS57Translator(options).Translate(TextFeature()));

        Assert.Equal("First | Second", Value(feature, AttlInform));
    }

    [Theory]
    [InlineData(S101InformLengthPolicy.Keep, "First;Second")]
    [InlineData(S101InformLengthPolicy.Truncate, "First;Se")]
    public void Text_OverMaximumLength_IsReportedAndOptionallyCut(S101InformLengthPolicy policy, string expected)
    {
        var options = new S101ToS57TranslationOptions { MaxInformLength = 8, InformLengthPolicy = policy };
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var feature = Single(new S101ToS57Translator(options).Translate(TextFeature(), diagnostics));

        Assert.Equal(expected, Value(feature, AttlInform));
        Assert.Equal(1, diagnostics.InformOverLength); // INFORM; NINFOM "Deuxième" fits
    }

    // ── §2.1.1 / §27.131 Multiplicity of features ───────────────────────

    [Theory]
    [InlineData("true", "3", "3 features")]
    [InlineData("false", null, "Multiple features")]
    public void MultiplicityOfFeatures_FoldsIntoInform(string known, string? number, string inform)
    {
        var rows = new List<(string, string, int)> { R("multiplicityOfFeatures", ""), R("multiplicityKnown", known, 1) };
        if (number is not null) rows.Add(R("numberOfFeatures", number, 1));

        var feature = Single(new S101ToS57Translator().Translate(PointFeature("Landmark", [.. rows])));

        Assert.Equal(inform, Value(feature, AttlInform));
    }

    [Fact]
    public void MultiplicityOfFeatures_IsOptional()
    {
        var options = new S101ToS57TranslationOptions { IncludeOptionalInform = false };
        var document = PointFeature("Landmark", R("multiplicityOfFeatures", ""), R("multiplicityKnown", "false", 1));

        var feature = Single(new S101ToS57Translator(options).Translate(document));

        Assert.Null(Value(feature, AttlInform));
    }

    // ── §3 Meta features ────────────────────────────────────────────────

    [Fact]
    public void DataCoverage_BecomesCoverageAvailable()
    {
        var b = new DocumentBuilder();
        b.Point(1, 0, 0);
        b.Point(2, 0, 10);
        b.Point(3, 10, 10);
        b.Curve(1, 1, 2);
        b.Curve(2, 2, 3);
        b.Curve(3, 3, 1);
        b.Feature("DataCoverage", (130, b.Surface(1, 1, 2, 3)), [R("optimumDisplayScale", "22000")]);
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var feature = Single(new S101ToS57Translator().Translate(b.Build(), diagnostics));

        Assert.Equal((int)S57ObjectCode.M_COVR, (int)feature.ObjectCode);
        Assert.Equal("1", Value(feature, AttlCatcov));
        Assert.Equal(255, (int)feature.Group);
        Assert.Equal(1, diagnostics.DroppedAttributes[new S101AttributeDrop("optimumDisplayScale", "3.5")]);
    }

    [Fact]
    public void SoundingDatum_Commonest_GoesToDspmAndOthersToMSdat()
    {
        var b = new DocumentBuilder();
        b.Feature("SoundingDatum", (110, b.Point(1, 0, 0)), [R("verticalDatum", "12")], fidn: 1);
        b.Feature("SoundingDatum", (110, b.Point(2, 0, 0)), [R("verticalDatum", "12")], fidn: 2);
        b.Feature("SoundingDatum", (110, b.Point(3, 0, 0)), [R("verticalDatum", "3")], fidn: 3);

        var result = new S101ToS57Translator().Translate(b.Build());

        Assert.Equal(12, result.DataSetParameters!.SoundingDatum);
        var mSdat = Assert.Single(result.FeatureRecords);
        Assert.Equal((int)S57ObjectCode.M_SDAT, (int)mSdat.ObjectCode);
        Assert.Equal(3, mSdat.RecordName.FeatureId);
    }

    [Fact]
    public void UpdateInformation_IsDropped()
    {
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var result = new S101ToS57Translator().Translate(PointFeature("UpdateInformation"), diagnostics);

        Assert.Empty(result.FeatureRecords);
        Assert.Equal(1, diagnostics.DroppedFeatures[new S101FeatureDrop("UpdateInformation", "3.12")]);
    }

    // ── §18.1, §19.3 Aids to navigation ─────────────────────────────────

    [Fact]
    public void LightSectored_BecomesOneLightsPerSector()
    {
        var rows = new List<(string, string, int)> { R("height", "12") };
        void Sector(string colour, string one, string two, bool directional = false)
        {
            var sc = rows.Count + 1;
            rows.Add(R("sectorCharacteristics", ""));
            rows.Add(R("lightCharacteristic", "2", sc));
            var ls = rows.Count + 1;
            rows.Add(R("lightSector", "", sc));
            rows.Add(R("colour", colour, ls));
            var limit = rows.Count + 1;
            rows.Add(R("sectorLimit", "", ls));
            rows.Add(R("sectorLimitOne", "", limit));
            rows.Add(R("sectorBearing", one, limit + 1));
            rows.Add(R("sectorLimitTwo", "", limit));
            rows.Add(R("sectorBearing", two, limit + 3));
            if (directional)
                rows.Add(R("directionalCharacter", "", ls));
        }
        Sector("3", "10", "90");
        Sector("4", "90", "180", directional: true);
        var diagnostics = new S101ToS57TranslationDiagnostics();

        var result = new S101ToS57Translator().Translate(PointFeature("LightSectored", [.. rows]), diagnostics);

        Assert.Equal(2, result.FeatureRecords.Count);
        Assert.All(result.FeatureRecords, f => Assert.Equal((int)S57ObjectCode.LIGHTS, (int)f.ObjectCode));
        Assert.Equal(["10", "90"], result.FeatureRecords.Select(f => Value(f, AttlSectr1)));
        Assert.Equal(["3", "4"], result.FeatureRecords.Select(f => Value(f, AttlColour)));
        Assert.All(result.FeatureRecords, f => Assert.Equal("2", Value(f, AttlLitchr)));
        Assert.Null(Value(result.FeatureRecords[0], AttlCatlit));
        Assert.Equal("1", Value(result.FeatureRecords[1], AttlCatlit));
        Assert.NotEqual(result.FeatureRecords[0].RecordName.FeatureSubdivision, result.FeatureRecords[1].RecordName.FeatureSubdivision);
        Assert.Equal(1, diagnostics.FeaturesSynthesised["LIGHTS"]);
    }

    [Fact]
    public void Topmark_BecomesSlaveTopmar()
    {
        var document = PointFeature("LateralBuoy",
            R("colour", "4"),
            R("topmark", ""), R("colour", "4", 2), R("topmarkDaymarkShape", "3", 2));

        var result = new S101ToS57Translator().Translate(document);

        Assert.Equal(2, result.FeatureRecords.Count);
        var buoy = result.FeatureRecords[0];
        var topmark = result.FeatureRecords[1];
        Assert.Equal((int)S57ObjectCode.TOPMAR, (int)topmark.ObjectCode);
        Assert.Equal("3", Value(topmark, AttlTopshp));
        Assert.Equal("4", Value(topmark, AttlColour));
        Assert.Equal(buoy.SpatialPointers[0].Name, topmark.SpatialPointers[0].Name);
        var pointer = Assert.Single(buoy.FeaturePointers);
        Assert.Equal(S57RelationshipIndicator.Slave, pointer.Relationship);
        Assert.Equal(topmark.LongName, pointer.Name);
    }

    [Fact]
    public void SignalSequence_JoinsWithPlusAndBracketsEclipses()
    {
        var document = PointFeature("LightAllAround",
            R("rhythmOfLight", ""), R("lightCharacteristic", "2", 1),
            R("signalSequence", "", 1), R("signalDuration", "0.8", 3), R("signalStatus", "1", 3),
            R("signalSequence", "", 1), R("signalDuration", "2.2", 6), R("signalStatus", "2", 6));

        var feature = Single(new S101ToS57Translator().Translate(document));

        Assert.Equal("00.8+(02.2)", Value(feature, AttlSigseq));
    }

    // ── Geometry ────────────────────────────────────────────────────────

    [Fact]
    public void Curve_BecomesEdgeBetweenConnectedNodes()
    {
        var b = new DocumentBuilder();
        b.Point(1, 0, 0);
        b.Point(2, 0, 100);
        b.Feature("Coastline", (120, b.Curve(7, 1, 2, (5, 50))), []);

        var result = new S101ToS57Translator().Translate(b.Build());

        var feature = Single(result);
        Assert.Equal(S57GeometricPrimitive.Line, feature.Primitive);
        var pointer = Assert.Single(feature.SpatialPointers);
        Assert.Equal((130, 7), (pointer.Name.RecordNameCode, pointer.Name.RecordId));
        var edge = Assert.Single(result.VectorRecords, v => v.RecordName.RecordNameCode == 130);
        Assert.Equal([(1, 1), (2, 2)], edge.VectorPointers.Select(p => (p.Name.RecordId, (int)p.Topology)));
        Assert.Equal(2, result.VectorRecords.Count(v => v.RecordName.RecordNameCode == 120));
        Assert.Equal((50, 5), (edge.Coordinates2D[0].X, edge.Coordinates2D[0].Y));
    }

    [Fact]
    public void Sounding_BecomesIsolatedNodeWithSoundings()
    {
        var b = new DocumentBuilder();
        b.Point(1, 0, 0);
        b.Feature("Sounding", (115, b.MultiPoint(1, (10, 20, 55), (11, 21, 60))), []);

        var result = new S101ToS57Translator().Translate(b.Build());

        var feature = Single(result);
        Assert.Equal((int)S57ObjectCode.SOUNDG, (int)feature.ObjectCode);
        var node = Assert.Single(result.VectorRecords);
        Assert.Equal(110, node.RecordName.RecordNameCode);
        Assert.Equal([55, 60], node.Soundings.Select(s => s.Depth));
        Assert.Equal(10, result.DataSetParameters!.SoundingMultiplicationFactor);
    }

    // ── Rules ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Coastline", "COALNE", "5.3")]
    [InlineData("Bollard", "MORFAC", "8.16")]
    [InlineData("LightSectored", "LIGHTS", "19.3")]
    [InlineData("DataCoverage", "M_COVR", "3.5")]
    public void Rules_InvertForwardMappingAndCiteAnnexC(string s101, string s57, string clause)
    {
        var rule = S101ToS57Rules.Default.ResolveFeature(s101);

        Assert.NotNull(rule);
        Assert.Equal(s57, rule.S57Acronym);
        Assert.Equal(clause, rule.Clause);
    }

    [Fact]
    public void Rules_InvertPerClassValueRemap()
    {
        // Forward: COALNE CATCOA 3 (sandy shore) → Coastline natureOfSurface 4 (sand).
        var rule = S101ToS57Rules.Default.ResolveAttribute("Coastline", "natureOfSurface");

        Assert.NotNull(rule);
        Assert.Equal("CATCOA", rule.S57Acronym);
        Assert.Equal("3", rule.ValueRemap["4"]);
    }

    [Fact]
    public void RoundTrip_SimpleCell_IsUnchanged()
    {
        var s57 = new S57Document
        {
            DataSetIdentification = new S57DataSetIdentification { DataSetName = "RT.000", EditionNumber = "1", UpdateNumber = "0", IssueDate = "20240101" },
            DataSetParameters = new S57DataSetParameters { CoordinateMultiplicationFactor = 10_000_000, SoundingMultiplicationFactor = 10 },
            VectorRecords =
            [
                new S57VectorRecord
                {
                    RecordName = new S57RecordName { RecordNameCode = 110, RecordId = 1 },
                    VectorPointers = [], Soundings = [], Attributes = [],
                    Coordinates2D = [new S57Coordinate2D { X = 10, Y = 20 }],
                },
            ],
            FeatureRecords =
            [
                new S57FeatureRecord
                {
                    RecordName = new S57RecordName { RecordNameCode = 100, RecordId = 1, AgencyCode = 550, FeatureId = 9, FeatureSubdivision = 1 },
                    Primitive = S57GeometricPrimitive.Point,
                    ObjectCode = S57ObjectCode.LNDMRK,
                    Attributes =
                    [
                        new S57AttributeValue { AttributeCode = 35, Value = "17" },   // CATLMK
                        new S57AttributeValue { AttributeCode = 83, Value = "1" },    // CONVIS
                        new S57AttributeValue { AttributeCode = 116, Value = "Mast" }, // OBJNAM
                    ],
                    NationalAttributes = [],
                    SpatialPointers = [new S57SpatialPointer { Name = new S57RecordName { RecordNameCode = 110, RecordId = 1 } }],
                    FeaturePointers = [],
                },
            ],
        };

        var back = new S101ToS57Translator().Translate(new S57ToS101Translator().Translate(s57));

        var feature = Single(back);
        Assert.Equal(S57ObjectCode.LNDMRK, feature.ObjectCode);
        Assert.Equal((550, 9, 1), (feature.RecordName.AgencyCode, feature.RecordName.FeatureId, feature.RecordName.FeatureSubdivision));
        Assert.Equal(
            [(35, "17"), (83, "1"), (116, "Mast")],
            feature.Attributes.Select(a => (a.AttributeCode, a.Value)));
    }
}
