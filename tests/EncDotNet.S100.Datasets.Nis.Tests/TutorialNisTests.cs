using EncDotNet.S100.Datasets.Nis.FileGdb;
using EncDotNet.S100.Datasets.Nis.Tests.TestSupport;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.Nis.Tests;

/// <summary>
/// Tests on Esri's sample NIS (TUTORIAL_NIS.gdb), downloaded by
/// tools/nis-fixtures/fetch-tutorial-nis.sh and skipped when it's absent.
/// The expected counts were checked against GDAL's OpenFileGDB driver.
/// </summary>
public sealed class TutorialNisTests
{
    [Fact]
    public void Reads_the_feature_classes_and_cells()
    {
        TutorialNis.SkipUnlessPresent();
        var source = FileGdbNisSource.Open(TutorialNis.Path);

        Assert.Equal(["US5CA98M.005", "US5NY1DM.010"], source.GetDatasetNames());
        var features = source.GetFeatures(new NisQuery { IncludeDeleted = true }).ToList();
        Assert.Equal(12318, features.Count);
        Assert.Equal(158, features.Count(f => f.FeatureClass == "AidsToNavigationP"));
        Assert.Equal(5885, features.Count(f => f.ObjectClass == "SOUNDG"));
        Assert.Equal(2, features.Count(f => f.ObjectClass == "C_AGGR"));
        Assert.Equal(65, source.GetRelationships().Count());
    }

    [Fact]
    public void Decodes_subtypes_values_and_geometry()
    {
        TutorialNis.SkipUnlessPresent();
        var source = FileGdbNisSource.Open(TutorialNis.Path);
        var features = source.GetFeatures(NisQuery.All).ToList();

        var buoy = Assert.Single(features, f => f.LongName == "US119234008300003");
        Assert.Equal("BOYSPP", buoy.ObjectClass);
        Assert.Equal("US5CA98M.005", buoy.DatasetName);
        Assert.Equal(40000, buoy.CompilationScale);
        Assert.Equal("Lake Tahoe Buoy D", buoy.Attributes["OBJNAM"]);
        Assert.Equal("1,11", buoy.Attributes["COLOUR"]);
        Assert.Equal(NisGeometryKind.Point, buoy.Geometry!.Kind);

        var sounding = features.First(f => f.ObjectClass == "SOUNDG");
        var position = Assert.Single(Assert.Single(sounding.Geometry!.Parts));
        Assert.NotNull(position.Z);
        Assert.Equal((double)sounding.Attributes["DEPTH"], position.Z!.Value, 3);

        Assert.All(features.Where(f => f.FeatureClass == "MetaDataA"), f => Assert.StartsWith("M_", f.ObjectClass));
    }

    [Theory]
    [InlineData("US5CA98M.005")]
    [InlineData("US5NY1DM.010")]
    public void Converts_each_cell_to_s57_with_clean_topology(string cell)
    {
        TutorialNis.SkipUnlessPresent();
        var source = FileGdbNisSource.Open(TutorialNis.Path);
        var diagnostics = new NisToS57Diagnostics();

        var document = new NisToS57Translator(new NisToS57Options { DatasetName = cell, IssueDate = new DateOnly(2026, 11, 26) }).Translate(source, diagnostics);

        Assert.Empty(S57TopologyCheck.Check(document));
        Assert.Empty(diagnostics.DroppedObjectClasses);
        Assert.Empty(diagnostics.UnknownAttributes);
        Assert.Empty(diagnostics.UnknownAgencies);
        Assert.Equal(0, diagnostics.RelationshipsUnresolved);
        Assert.True(diagnostics.CoverageSynthesised);
        Assert.Equal(550, document.DataSetIdentification!.ProducingAgency);
        Assert.True(document.DataSetParameters!.CompilationScale > 0);

        var read = S57DocumentReader.Read(S57DocumentWriter.Write(document));
        Assert.Equal(document.FeatureRecords.Count, read.FeatureRecords.Count);
        Assert.Equal(NisToS57Translator.CellNameOf(cell) + ".000", read.DataSetIdentification!.DataSetName);
    }
}
