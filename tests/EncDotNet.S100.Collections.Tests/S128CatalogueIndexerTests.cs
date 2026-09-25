using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Datasets.S128;

namespace EncDotNet.S100.Collections.Tests;

public class S128CatalogueIndexerTests
{
    [Fact]
    public async Task Indexes_every_product_entry_as_a_catalogue_only_item()
    {
        var path = TestPaths.Dataset("S128", "S128_TDS_sample.gml");
        var expected = S128Dataset.Open(path).Entries;

        var index = await CollectionIndexer.CreateDefault()
            .IndexAsync(new S128CatalogueSource(Guid.NewGuid(), null, path));

        Assert.NotEmpty(index.Items);
        Assert.Equal(expected.Count, index.Items.Count);
        Assert.All(index.Items, i => Assert.IsType<NoItemLocation>(i.Location));
        Assert.Contains(index.Items, i => i.Coverage is not null && i.Bounds is not null);
        Assert.StartsWith("s128-v1:", index.Fingerprint);
    }

    [Theory]
    [InlineData("S-57 Transfer Standard for Digital Hydrographic Data", "S-57")]
    [InlineData("S101", "S-101")]
    [InlineData("s-102", "S-102")]
    [InlineData("Paper chart", "Paper chart")]
    [InlineData(" ", null)]
    public void NormalizeSpecCode_reduces_free_form_names(string raw, string? expected)
    {
        Assert.Equal(expected, S128CatalogueIndexer.NormalizeSpecCode(raw));
    }

    [Fact]
    public async Task Missing_file_reports_an_error()
    {
        var index = await CollectionIndexer.CreateDefault()
            .IndexAsync(new S128CatalogueSource(Guid.NewGuid(), null, "/nonexistent/catalogue.gml"));

        Assert.Empty(index.Items);
        Assert.Null(index.Fingerprint);
        Assert.Contains(index.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Error);
    }
}
