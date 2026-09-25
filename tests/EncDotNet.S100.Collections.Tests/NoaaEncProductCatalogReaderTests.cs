using System.Xml;
using EncDotNet.S100.Collections.Noaa;

namespace EncDotNet.S100.Collections.Tests;

public class NoaaEncProductCatalogReaderTests
{
    private static readonly NoaaEncProductCatalog Catalog =
        NoaaEncProductCatalogReader.Read(TestPaths.Fixture("noaa-enc-prodcat.xml"));

    private static NoaaEncCell Cell(string name) => Catalog.Cells.Single(c => c.Name == name);

    [Fact]
    public void Reads_header_and_every_cell()
    {
        Assert.Equal("ENC Product Catalog", Catalog.Header.Title);
        Assert.Equal(550, Catalog.Header.AgencyCode);
        Assert.NotNull(Catalog.Header.ValidAt);
        Assert.Equal(
            ["US1EEZ1M", "US1GLBBA", "US1GLBDA", "US1GLBDS", "US2PACTX", "US3TC300", "US5MI62M"],
            Catalog.Cells.Select(c => c.Name));
    }

    [Fact]
    public void Reads_cell_fields()
    {
        var cell = Cell("US1EEZ1M");

        Assert.True(cell.IsCancelled);
        Assert.Equal("West Pacific; Wake Island, Mariana Islands, Guam", cell.LongName);
        Assert.Equal(3_000_000, cell.CompilationScale);
        Assert.Equal(["AK", "CA", "HI", "OR", "PO", "WA"], cell.States);
        Assert.Equal([14], cell.CoastGuardDistricts);
        Assert.Equal([30, 32, 34, 36, 40], cell.Regions);
        Assert.Equal(new Uri("https://www.charts.noaa.gov/ENCs/US1EEZ1M.zip"), cell.ZipUri);
        Assert.Equal(217_515, cell.ZipSize);
        Assert.NotNull(cell.ZipDateTime);
        Assert.Equal(10, cell.Edition);
        Assert.Equal(1, cell.Update);
        Assert.Equal(new DateOnly(2025, 12, 9), cell.UpdateApplicationDate);
        Assert.Equal(new DateOnly(2026, 8, 4), cell.IssueDate);
        Assert.Empty(cell.Panels);
    }

    [Fact]
    public void Collapses_repeated_whitespace_in_titles()
    {
        // The feed's own text has "South Pacific;  Cook Islands" style gaps.
        Assert.DoesNotContain("  ", string.Concat(Catalog.Cells.Select(c => c.LongName)));
    }

    [Fact]
    public void Reads_multiple_and_interior_panels()
    {
        var micronesia = Cell("US3TC300");
        Assert.Equal(2, micronesia.Panels.Count);
        Assert.All(micronesia.Panels, p => Assert.False(p.IsInterior));
        Assert.Equal(-219.52, micronesia.Panels[0].Vertices[0].Longitude);

        var michigan = Cell("US5MI62M");
        Assert.False(michigan.Panels[0].IsInterior);
        Assert.True(michigan.Panels[1].IsInterior);
    }

    [Fact]
    public void Rejects_a_document_that_is_not_a_catalogue()
    {
        using var stream = new MemoryStream("<RncProductCatalog/>"u8.ToArray());

        Assert.Throws<XmlException>(() => NoaaEncProductCatalogReader.Read(stream));
    }
}
