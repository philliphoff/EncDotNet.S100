using EncDotNet.S100.Datasets.Nis.Tests.TestSupport;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.Nis.Tests;

/// <summary>
/// Tests for <see cref="NisToS57Translator"/> on in-memory NIS features: the
/// NIS conventions, identities, relationships, soundings, chain-node topology
/// and coverage.
/// </summary>
public sealed class NisToS57TranslatorTests
{
    private static readonly S57ObjectCatalogue Catalogue = S57ObjectCatalogue.Iho;

    private static int Objl(string acronym)
        => (Catalogue.GetObjectClass(acronym) ?? throw new InvalidOperationException(acronym)).Code;

    private static int Attl(string acronym)
        => (Catalogue.GetAttribute(acronym) ?? throw new InvalidOperationException(acronym)).Code;

    private static S57Document Translate(IEnumerable<NisFeature> features, IEnumerable<NisRelationship>? relationships = null, NisToS57Options? options = null, NisToS57Diagnostics? diagnostics = null)
        => new NisToS57Translator(options ?? Make.Options()).Translate(new InMemoryNisSource(features, relationships), diagnostics);

    private static S57FeatureRecord Single(S57Document document, string acronym)
        => Assert.Single(document.FeatureRecords, f => (int)f.ObjectCode == Objl(acronym));

    private static string? Value(S57FeatureRecord feature, string acronym)
    {
        int code = Attl(acronym);
        foreach (var a in feature.Attributes.Concat(feature.NationalAttributes))
        {
            if (a.AttributeCode == code) return a.Value;
        }
        return null;
    }

    [Fact]
    public void Attributes_follow_the_nis_conventions()
    {
        var buoy = Make.Feature("BOYSPP", Make.Point(0.0005, 0.0005), "US000000000100001", Make.Attributes(
            ("BOYSHP", 2.0),
            ("COLOUR", "1, 11"),
            ("CATSPM", "-32767"),
            ("VALNMR", -32767.0),
            ("OBJNAM", "Buoy A"),
            ("NOBJNM", "Bouée A"),
            ("EDITOR", "someone"),
            ("NIS_EDITOR", "someone"),
            ("ZZZZZZ", "not an attribute"),
            ("P_QUAPOS", 4)));
        var diagnostics = new NisToS57Diagnostics();

        var document = Translate([buoy], diagnostics: diagnostics);

        var record = Single(document, "BOYSPP");
        Assert.Equal("2", Value(record, "BOYSHP"));
        Assert.Equal("1,11", Value(record, "COLOUR"));
        Assert.Equal(string.Empty, Value(record, "CATSPM"));
        Assert.Equal(string.Empty, Value(record, "VALNMR"));
        Assert.Equal("Buoy A", Value(record, "OBJNAM"));
        Assert.Contains(record.NationalAttributes, a => a.Value == "Bouée A");
        Assert.Equal(2, diagnostics.UnknownValues);
        Assert.Equal(1, diagnostics.UnknownAttributes["ZZZZZZ"]);
        Assert.False(diagnostics.UnknownAttributes.ContainsKey("EDITOR"));

        // P_QUAPOS becomes the QUAPOS of the isolated node.
        var node = Assert.Single(document.VectorRecords, v => v.RecordName.RecordNameCode == 110);
        Assert.Contains(node.Attributes, a => a.AttributeCode == Attl("QUAPOS") && a.Value == "4");
        Assert.Equal(S57GeometricPrimitive.Point, record.Primitive);
    }

    [Fact]
    public void Long_names_become_feature_object_identifiers()
    {
        var known = Make.Feature("LNDMRK", Make.Point(0.0001, 0.0001), "US119234008300003");
        var unknown = Make.Feature("LNDMRK", Make.Point(0.0002, 0.0002), "1B000000004200007");
        var missing = Make.Feature("LNDMRK", Make.Point(0.0003, 0.0003));
        var diagnostics = new NisToS57Diagnostics();

        var document = Translate([known, unknown, missing], options: new NisToS57Options { IssueDate = new DateOnly(2026, 11, 26), SynthesiseCoverage = false, ProducingAgency = 999 }, diagnostics: diagnostics);

        var names = document.FeatureRecords.Select(f => f.RecordName.LongName).ToList();
        Assert.Contains(new S57LongName(550, 1192340083, 3), names);
        Assert.Contains(new S57LongName(999, 42, 7), names);
        Assert.Equal(1, diagnostics.UnknownAgencies["1B"]);
        Assert.Equal(1, diagnostics.IdentitiesAssigned);
        Assert.Equal(3, names.Distinct().Count());
        Assert.Equal(999, document.DataSetIdentification!.ProducingAgency);
    }

    [Fact]
    public void Relationships_become_feature_pointers()
    {
        var beacon = Make.Feature("BCNSPP", Make.Point(0.0001, 0.0001), "US000000000100001");
        var daymark = Make.Feature("DAYMAR", Make.Point(0.0001, 0.0001), "US000000000100002");
        var diagnostics = new NisToS57Diagnostics();

        var document = Translate(
            [beacon, daymark],
            [
                new NisRelationship("US000000000100001", "US000000000100002", NisRelationshipKind.Slave),
                new NisRelationship("US000000000100001", "US000000000199999", NisRelationshipKind.Slave),
            ],
            diagnostics: diagnostics);

        var record = Single(document, "BCNSPP");
        var pointer = Assert.Single(record.FeaturePointers);
        Assert.Equal(new S57LongName(550, 1, 2), pointer.Name);
        Assert.Equal(S57RelationshipIndicator.Slave, pointer.Relationship);
        Assert.Equal(1, diagnostics.RelationshipsWritten);
        Assert.Equal(1, diagnostics.RelationshipsUnresolved);
    }

    [Fact]
    public void Soundings_with_the_same_attributes_share_soundg_objects()
    {
        var quality = Make.Attributes(("QUASOU", "6"));
        NisFeature[] soundings =
        [
            Make.Feature("SOUNDG", Make.Point(0.0001, 0.0001, 7.3), attributes: quality),
            Make.Feature("SOUNDG", Make.Point(0.0002, 0.0001, 8.0), attributes: quality),
            Make.Feature("SOUNDG", Make.Point(0.0003, 0.0001, 9.1), attributes: quality),
            Make.Feature("SOUNDG", Make.Point(0.0004, 0.0001, 10.0), attributes: Make.Attributes(("QUASOU", "1"))),
        ];
        var diagnostics = new NisToS57Diagnostics();

        var document = Translate(soundings, options: new NisToS57Options { IssueDate = new DateOnly(2026, 11, 26), SynthesiseCoverage = false, MaxSoundingsPerObject = 2 }, diagnostics: diagnostics);

        var objects = document.FeatureRecords.Where(f => (int)f.ObjectCode == Objl("SOUNDG")).ToList();
        Assert.Equal(3, objects.Count);
        Assert.Equal(4, diagnostics.SoundingsWritten);
        var depths = document.VectorRecords.SelectMany(v => v.Soundings).Select(s => s.Depth).Order().ToList();
        Assert.Equal([73, 80, 91, 100], depths);
        Assert.Equal(3, diagnostics.SoundingObjects);
    }

    [Fact]
    public void Areas_that_meet_share_one_edge()
    {
        // Two clockwise squares meeting along lon 0.001, both with a vertex
        // in the middle of the shared side.
        var water = Make.Feature("DEPARE", Make.Area([(0.001, 0), (0, 0), (0, 0.001), (0.001, 0.001), (0.001, 0.0005)]), "US000000000100001", Make.Attributes(("DRVAL1", 0.0), ("DRVAL2", 5.0)));
        var land = Make.Feature("LNDARE", Make.Area([(0.001, 0), (0.001, 0.0005), (0.001, 0.001), (0.002, 0.001), (0.002, 0)]), "US000000000100002");

        var document = Translate([water, land]);

        var edges = document.VectorRecords.Where(v => v.RecordName.RecordNameCode == 130).ToList();
        Assert.Equal(3, edges.Count);
        var waterEdges = Single(document, "DEPARE").SpatialPointers.Select(p => p.Name.RecordId).ToHashSet();
        var landEdges = Single(document, "LNDARE").SpatialPointers.Select(p => p.Name.RecordId).ToHashSet();
        Assert.Single(waterEdges.Intersect(landEdges));
        Assert.Equal(1, Single(document, "DEPARE").Group);
        Assert.DoesNotContain(S57TopologyCheck.Check(document), i => !i.Kind.StartsWith("coverage", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hole_is_an_interior_ring_shared_with_the_island()
    {
        var water = Make.Feature("DEPARE", Make.Area(Make.Square(0, 0, 0.003), Make.SquareHole(0.001, 0.001, 0.001)), "US000000000100001");
        var island = Make.Feature("LNDARE", Make.Area(Make.Square(0.001, 0.001, 0.001)), "US000000000100002");

        var document = Translate([water, island]);

        var waterPointers = Single(document, "DEPARE").SpatialPointers;
        Assert.Contains(waterPointers, p => p.Usage == S57UsageIndicator.Exterior);
        var interior = Assert.Single(waterPointers, p => p.Usage == S57UsageIndicator.Interior);
        var islandPointer = Assert.Single(Single(document, "LNDARE").SpatialPointers);
        Assert.Equal(S57UsageIndicator.Exterior, islandPointer.Usage);
        Assert.Equal(interior.Name.RecordId, islandPointer.Name.RecordId);
        Assert.NotEqual(interior.Orientation, islandPointer.Orientation);
        Assert.DoesNotContain(S57TopologyCheck.Check(document), i => !i.Kind.StartsWith("coverage", StringComparison.Ordinal));
    }

    [Fact]
    public void Coverage_tiles_the_extent_of_the_data()
    {
        var water = Make.Feature("DEPARE", Make.Area(Make.Square(0, 0, 0.001)), "US000000000100001");
        var landmark = Make.Feature("LNDMRK", Make.Point(0.003, 0.002), "US000000000100002");
        var diagnostics = new NisToS57Diagnostics();

        var document = Translate([water, landmark], options: Make.Options(coverage: true), diagnostics: diagnostics);

        var coverage = document.FeatureRecords.Where(f => (int)f.ObjectCode == Objl("M_COVR")).ToList();
        Assert.Equal(new string?[] { "1", "2" }, coverage.Select(c => Value(c, "CATCOV")).Order().ToArray());
        Assert.True(diagnostics.CoverageSynthesised);
        Assert.Equal(0, diagnostics.IdentitiesAssigned);
        Assert.Empty(S57TopologyCheck.Check(document));
    }

    [Fact]
    public void Existing_coverage_is_kept()
    {
        var coverage = Make.Feature("M_COVR", Make.Area(Make.Square(0, 0, 0.001)), "US000000000100001", Make.Attributes(("CATCOV", 1)));
        var water = Make.Feature("DEPARE", Make.Area(Make.Square(0, 0, 0.001)), "US000000000100002");
        var diagnostics = new NisToS57Diagnostics();

        var document = Translate([coverage, water], options: Make.Options(coverage: true), diagnostics: diagnostics);

        Assert.Single(document.FeatureRecords, f => (int)f.ObjectCode == Objl("M_COVR"));
        Assert.False(diagnostics.CoverageSynthesised);
    }

    [Fact]
    public void Lines_keep_their_direction_and_meta_objects_come_first()
    {
        var contour = Make.Feature("DEPCNT", Make.Line((0, 0), (0.001, 0), (0.002, 0.0005)), "US000000000100001", Make.Attributes(("VALDCO", 10.0)));
        var quality = Make.Feature("M_QUAL", Make.Area(Make.Square(-0.001, -0.001, 0.004)), "US000000000100002", Make.Attributes(("CATZOC", 4)));

        var document = Translate([contour, quality]);

        Assert.Equal(Objl("M_QUAL"), (int)document.FeatureRecords[0].ObjectCode);
        var line = Single(document, "DEPCNT");
        Assert.Equal(S57GeometricPrimitive.Line, line.Primitive);
        Assert.Equal("10", Value(line, "VALDCO"));
        Assert.All(line.SpatialPointers, p => Assert.Equal(S57Orientation.Forward, p.Orientation));
        Assert.Equal(255, (int)Single(document, "M_QUAL").Group);
    }

    [Fact]
    public void Unknown_object_classes_are_dropped_and_counted()
    {
        var known = Make.Feature("LNDMRK", Make.Point(0, 0), "US000000000100001");
        var unknown = Make.Feature("Representation", Make.Point(0, 0), "US000000000100002");
        var diagnostics = new NisToS57Diagnostics();

        var document = Translate([known, unknown], diagnostics: diagnostics);

        Assert.Single(document.FeatureRecords);
        Assert.Equal(1, diagnostics.DroppedObjectClasses["Representation"]);
    }

    [Fact]
    public void A_cell_must_be_chosen_when_there_are_several()
    {
        var a = Make.Feature("LNDMRK", Make.Point(0, 0), "US000000000100001", cell: "XX5AAAAA.000");
        var b = Make.Feature("LNDMRK", Make.Point(0, 0), "US000000000100002", cell: "XX5BBBBB.003");
        var source = new InMemoryNisSource([a, b]);

        Assert.Throws<InvalidOperationException>(() => new NisToS57Translator(Make.Options()).Translate(source));

        var document = new NisToS57Translator(new NisToS57Options { DatasetName = "xx5bbbbb.003", SynthesiseCoverage = false, IssueDate = new DateOnly(2026, 11, 26) }).Translate(source);
        Assert.Single(document.FeatureRecords);
        var identification = document.DataSetIdentification;
        Assert.NotNull(identification);
        Assert.Equal("XX5BBBBB.000", identification.DataSetName);
        Assert.Equal(5, identification.IntendedUsage);
        Assert.Equal("20261126", identification.IssueDate);
        Assert.Equal(20000, document.DataSetParameters!.CompilationScale);
    }

    [Fact]
    public void The_document_writes_and_reads_back()
    {
        NisFeature[] features =
        [
            Make.Feature("DEPARE", Make.Area(Make.Square(0, 0, 0.002)), "US000000000100001", Make.Attributes(("DRVAL1", 0.0), ("DRVAL2", 10.0))),
            Make.Feature("BOYLAT", Make.Point(0.001, 0.001), "US000000000100002", Make.Attributes(("BOYSHP", 2), ("CATLAM", 1))),
            Make.Feature("SOUNDG", Make.Point(0.0005, 0.0005, 4.2)),
        ];
        var document = Translate(features, options: Make.Options(coverage: true));

        var read = S57DocumentReader.Read(S57DocumentWriter.Write(document));

        Assert.Equal(document.FeatureRecords.Count, read.FeatureRecords.Count);
        Assert.Equal(document.VectorRecords.Count, read.VectorRecords.Count);
        Assert.Equal("XX5TEST1.000", read.DataSetIdentification!.DataSetName);
    }
}
