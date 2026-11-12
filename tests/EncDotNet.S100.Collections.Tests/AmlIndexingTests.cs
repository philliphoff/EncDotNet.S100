using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.TestSupport;
using EncDotNet.S57.ExchangeSets;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// The Library indexes NATO Additional Military Layers cells as <c>AML</c>,
/// with their AML facts in the item properties, whether they're loose or in
/// a CATALOG.031 exchange set (#1236).
/// </summary>
public class AmlIndexingTests
{
    private static readonly CollectionIndexer Indexer = CollectionIndexer.CreateDefault();

    [Fact]
    public async Task LooseAmlCell_IsIndexedAsAml_WithItsFacts()
    {
        using var temp = new TempDirectory();
        SyntheticAmlCell.Write(temp.Path, "UKC6UT00.000", new SyntheticAmlCell.Options { Comment = SyntheticAmlCell.UkhoComment("RESTRICTED") });

        var index = await Indexer.IndexAsync(new LocalFolderSource(Guid.NewGuid(), null, temp.Path), null, null, TestContext.Current.CancellationToken).AsTask();

        var item = Assert.Single(index.Items);
        AssertAml(item, "CLB", "RESTRICTED", "header");
        Assert.Null(item.UsageBand);
    }

    [Fact]
    public async Task ExchangeSetAmlCell_IsIndexedAsAml_WithTheMarkingFromItsName()
    {
        using var temp = new TempDirectory();
        const string name = "AMLDEU0R_NKLK0540_LBO-TrialData.000";
        var cell = SyntheticAmlCell.Write(temp.Path, name, new SyntheticAmlCell.Options { ProductSpecification = 57, Edition = "3.0", IntendedUsage = 100 });
        S57CatalogWriter.WriteToFile(Path.Combine(temp.Path, "CATALOG.031"), new S57Catalog
        {
            Entries =
            [
                new S57CatalogEntry { RecordId = 1, FileName = "CATALOG.031", Implementation = "ASC" },
                new S57CatalogEntry
                {
                    RecordId = 2,
                    FileName = name,
                    Implementation = "BIN",
                    SouthernmostLatitude = SyntheticAmlCell.South,
                    WesternmostLongitude = SyntheticAmlCell.West,
                    NorthernmostLatitude = SyntheticAmlCell.North,
                    EasternmostLongitude = SyntheticAmlCell.East,
                    CrcChecksum = S57CatalogWriter.ComputeCrc(File.ReadAllBytes(cell)),
                },
            ],
        });

        var index = await Indexer.IndexAsync(new LocalFolderSource(Guid.NewGuid(), null, temp.Path), null, null, TestContext.Current.CancellationToken).AsTask();

        var item = Assert.Single(index.Items);
        AssertAml(item, "LBO", "RESTRICTED", "cell-name");
        Assert.Equal("3.0", item.ProductSpecVersion);
    }

    private static void AssertAml(CollectionItem item, string product, string marking, string markingSource)
    {
        Assert.Equal(AmlDatasetInfo.SpecName, item.ProductSpec);
        Assert.Equal(product, item.Properties[AmlLibraryProperties.Product]);
        Assert.Equal(marking, item.Properties[AmlLibraryProperties.ProtectiveMarking]);
        Assert.Equal(markingSource, item.Properties[AmlLibraryProperties.MarkingSource]);

        var info = AmlLibraryProperties.Read(item.Properties, item.Name)!;
        Assert.Equal(product, info.ProductCode);
        Assert.Equal(marking, info.ProtectiveMarking);

        var tag = Assert.Single(LibraryItemText.Tags(new LibraryItemTextInput(item, null!, LibraryAvailability.Local, item)), t => t.Kind == LibraryTagKind.Military);
        Assert.Equal(info.TagText, tag.Text);
        Assert.StartsWith("Additional Military Layers · ", tag.ToolTip);
    }
}
