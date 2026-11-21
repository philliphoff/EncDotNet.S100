using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S57.Aml;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// The AML groups of a Library item's details (#1370, decision 6), from
/// synthetic AML metadata.
/// </summary>
public class AmlLibraryDetailsTests
{
    private static CollectionItem Item(string name, AmlDatasetInfo info)
    {
        var properties = new Dictionary<string, string> { ["band"] = "x" };
        AmlLibraryProperties.Write(info, properties);
        return new CollectionItem
        {
            Key = name,
            ProductSpec = AmlDatasetInfo.SpecName,
            Name = name,
            Location = NoItemLocation.Instance,
            Properties = properties,
        };
    }

    private static AmlDatasetInfo Info(string name, AmlProduct product, string? code, string? marking, AmlMarkingSource source, int? band)
    {
        AmlCellName.TryParse(name, out var fromName);
        return new AmlDatasetInfo
        {
            DataSetName = name,
            Product = product,
            ProductSpecification = (int)product,
            ProductCode = code,
            ProductName = AmlProducts.Name(code) ?? AmlProducts.IntegratedName,
            Version = "1.0",
            ProtectiveMarking = marking,
            MarkingSource = source,
            Caveat = "None",
            ScaleBand = band,
            FromName = fromName,
        };
    }

    [Fact]
    public void Where_the_name_and_the_header_disagree_the_header_wins_and_says_so()
    {
        // The name says CLB, band 6, Restricted; the header says LBO, band 5, NATO RESTRICTED.
        var item = Item("NOC6RA3B.000", Info("NOC6RA3B.000", AmlProduct.LBO, "LBO", "NATO RESTRICTED", AmlMarkingSource.Header, 5));

        var groups = LibraryItemText.AmlDetails(item);

        Assert.Equal(["Additional Military Layers", "From the cell name"], groups.Select(g => g.Title));
        Assert.Contains(groups[0].Fields, f => f is { Label: "Product", Value: "LBO · Large Bottom Objects" });
        Assert.Contains(groups[0].Fields, f => f is { Label: "Caveat", Value: "None" });
        Assert.Equal(
            [
                ("Producer", "NO"),
                ("Product", "C · CLB · the header says LBO, which wins"),
                ("Scale band", "6 · the header says 5, which wins"),
                ("Protective marking", "R · RESTRICTED · the header says NATO RESTRICTED, which wins"),
            ],
            groups[1].Fields.Select(f => (f.Label, f.Value)));
    }

    [Fact]
    public void A_marking_only_the_name_gives_is_said_to_come_from_it()
    {
        var item = Item("UKC6R114.000", Info("UKC6R114.000", AmlProduct.CLB, "CLB", "RESTRICTED", AmlMarkingSource.CellName, 6));

        var groups = LibraryItemText.AmlDetails(item);

        Assert.Contains(groups[0].Fields, f => f is { Label: "Protective marking", Value: "RESTRICTED (from cell name)" });
        Assert.Contains(groups[1].Fields, f => f is { Label: "Protective marking", Value: "R · RESTRICTED · used, as the cell gives none" });
    }

    [Fact]
    public void An_AML_30_name_is_not_decoded_and_other_items_have_no_AML_groups()
    {
        const string name = "AMLDEU7U_NKKK4824_CLB_SB7_TrialData.000";
        var item = Item(name, Info(name, AmlProduct.Integrated, "CLB", "UNCLASSIFIED", AmlMarkingSource.CellName, 7));

        Assert.Equal(["Additional Military Layers"], LibraryItemText.AmlDetails(item).Select(g => g.Title));
        Assert.Empty(LibraryItemText.AmlDetails(new CollectionItem
        {
            Key = "GB4X0000",
            ProductSpec = "S-57",
            Name = "GB4X0000",
            Location = NoItemLocation.Instance,
        }));
    }

    [Fact]
    public void Details_lead_with_the_AML_groups_and_drop_the_raw_AML_properties()
    {
        var item = Item("UKC6U114.000", Info("UKC6U114.000", AmlProduct.CLB, "CLB", "UNCLASSIFIED", AmlMarkingSource.Header, 6));

        var details = LibraryItemText.Details(new LibraryItemTextInput(item, null!, LibraryAvailability.Local, item));

        Assert.Equal("Additional Military Layers", details[0].Title);
        var source = details.Single(g => g.Title == "Source");
        Assert.Single(source.Fields);  // the unrelated "band" property only
    }
}
