using System.Linq;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Phase 1 tests for S-101 sequential-update metadata: the
/// <see cref="S101UpdateInstruction"/> vocabulary, the update fields on
/// <see cref="S101DatasetIdentification"/>, and the record/association
/// version + instruction fields read by <see cref="S101DocumentReader"/>
/// (S-100 Part 10a record fields RVER/RUIN and the per-element SAUI / FAUI /
/// IUIN / ATIN variants).
/// </summary>
public class S101UpdateMetadataTests
{
    [Theory]
    [InlineData(S101UpdateInstruction.None, 0)]
    [InlineData(S101UpdateInstruction.Insert, 1)]
    [InlineData(S101UpdateInstruction.Delete, 2)]
    [InlineData(S101UpdateInstruction.Modify, 3)]
    public void UpdateInstruction_ByteValues_MatchIso8211Encoding(S101UpdateInstruction instruction, int expected)
    {
        Assert.Equal(expected, (byte)instruction);
    }

    [Theory]
    [InlineData("1", false)]
    [InlineData("2", true)]
    [InlineData("", false)]
    public void DatasetIdentification_IsUpdate_DerivedFromApplicationProfile(string profile, bool expected)
    {
        var dsid = new S101DatasetIdentification { ApplicationProfile = profile };

        Assert.Equal(expected, dsid.IsUpdate);
    }

    [Fact]
    public void Records_DefaultUpdateInstruction_IsNone()
    {
        var point = new S101PointRecord
        {
            RecordId = 1,
            X = 0,
            Y = 0,
        };

        Assert.Equal(S101UpdateInstruction.None, point.UpdateInstruction);
        Assert.Equal(0, point.RecordVersion);
    }

    [Fact]
    public void Associations_DefaultUpdateInstruction_IsNone()
    {
        var spatial = new S101SpatialAssociation(110, 5, 1);
        var feature = new S101FeatureAssociation(1, 5, 0);
        var info = new S101InformationAssociation(1, 5, 0);
        var attribute = new S101Attribute(1, 1, "value");

        Assert.Equal(S101UpdateInstruction.None, spatial.UpdateInstruction);
        Assert.Equal(S101UpdateInstruction.None, feature.UpdateInstruction);
        Assert.Equal(S101UpdateInstruction.None, info.UpdateInstruction);
        Assert.Equal(S101UpdateInstruction.None, attribute.UpdateInstruction);
        Assert.Equal((ushort)0, attribute.ParentIndex);
    }

    [Fact]
    public void Associations_CarryUpdateInstruction_WhenProvided()
    {
        var spatial = new S101SpatialAssociation(110, 5, 1, S101UpdateInstruction.Delete);
        var attribute = new S101Attribute(1, 1, "value", 2, S101UpdateInstruction.Modify);

        Assert.Equal(S101UpdateInstruction.Delete, spatial.UpdateInstruction);
        Assert.Equal(S101UpdateInstruction.Modify, attribute.UpdateInstruction);
        Assert.Equal((ushort)2, attribute.ParentIndex);
    }

    [SkippableFact]
    public void ReadFromFile_BaseCell_HasBaseUpdateMetadata()
    {
        var path = FindDatasetFile(".000");
        Skip.If(path is null, "No S-101 base cell (.000) found under IC-ENC sample data.");

        var document = S101DocumentReader.ReadFromFile(path!);

        Assert.False(document.Identification.IsUpdate);
        Assert.Equal("1", document.Identification.ApplicationProfile);
        Assert.Equal(0, document.Identification.UpdateNumber);

        // Base cells encode every record as an Insert (RUIN = 1).
        Assert.All(document.Features, f => Assert.Equal(S101UpdateInstruction.Insert, f.UpdateInstruction));
    }

    [SkippableFact]
    public void ReadFromFile_UpdateCell_HasUpdateMetadata()
    {
        var path = FindDatasetFile(".001");
        Skip.If(path is null, "No S-101 update file (.001) found under IC-ENC sample data.");

        var document = S101DocumentReader.ReadFromFile(path!);

        Assert.True(document.Identification.IsUpdate);
        Assert.Equal("2", document.Identification.ApplicationProfile);
        Assert.Equal(1, document.Identification.UpdateNumber);

        // An update typically carries a mix of instructions (insert/delete/modify);
        // at minimum every parsed feature must carry a recorded instruction.
        Assert.All(document.Features, f => Assert.NotEqual(S101UpdateInstruction.None, f.UpdateInstruction));
    }

    [SkippableFact]
    public void OpenWithUpdates_AppliesSiblingUpdate()
    {
        var basePath = FindDatasetFile(".000");
        Skip.If(basePath is null, "No S-101 base cell (.000) found under IC-ENC sample data.");

        // Find an update (.001) that targets the same cell (same file stem).
        var stem = Path.GetFileNameWithoutExtension(basePath!);
        var updatePath = Directory
            .EnumerateFiles(RootOf(basePath!), stem + ".001", SearchOption.AllDirectories)
            .FirstOrDefault();
        Skip.If(updatePath is null, $"No matching .001 update found for base cell '{stem}'.");

        var dataset = S101Dataset.OpenWithUpdates(basePath!, new[] { updatePath! });

        Assert.NotNull(dataset.UpdateReport);
        Assert.Equal(0, dataset.UpdateReport!.BaseUpdateNumber);
        Assert.Equal(1, dataset.UpdateReport.AppliedThroughUpdateNumber);
    }

    private static string RootOf(string path)
    {
        var root = Environment.GetEnvironmentVariable("ICENC_ROOT");
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            return root;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultRoot = Path.Combine(home, "Downloads", "IC-ENC");
        return Directory.Exists(defaultRoot) ? defaultRoot : Path.GetDirectoryName(path)!;
    }

    private static string? FindDatasetFile(string extension)
    {
        var root = Environment.GetEnvironmentVariable("ICENC_ROOT");
        if (string.IsNullOrEmpty(root))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = Path.Combine(home, "Downloads", "IC-ENC");
        }

        if (!Directory.Exists(root))
            return null;

        return Directory
            .EnumerateFiles(root, "*" + extension, SearchOption.AllDirectories)
            .FirstOrDefault(p => Path.GetExtension(p)
                .Equals(extension, StringComparison.OrdinalIgnoreCase));
    }
}
