using EncDotNet.S100.Datasets.S57;
using EncDotNet.S57.ExchangeSets;

namespace EncDotNet.S100.Datasets.S57.Tests;

/// <summary>
/// Unit tests for <see cref="S57ExchangeSetCatalog"/>, which groups a parsed
/// <c>CATALOG.031</c>'s <c>CATD</c> entries into renderable base cells plus their
/// ordered sequential updates. Builds <see cref="S57Catalog"/> /
/// <see cref="S57CatalogEntry"/> in memory (both expose settable properties) so
/// no real ENC data or on-disk catalogue is required.
/// </summary>
public sealed class S57ExchangeSetCatalogTests
{
    private static char Sep => Path.DirectorySeparatorChar;

    private static S57Catalog Catalogue(params S57CatalogEntry[] entries) =>
        new() { Entries = entries };

    private static S57CatalogEntry Cell(
        string fileName,
        double? west = null,
        double? east = null,
        double? south = null,
        double? north = null) =>
        new()
        {
            FileName = fileName,
            WesternmostLongitude = west,
            EasternmostLongitude = east,
            SouthernmostLatitude = south,
            NorthernmostLatitude = north,
        };

    [Fact]
    public void SelectBaseCells_GroupsBaseAndOrdersUpdates()
    {
        var catalogue = Catalogue(
            Cell(@"ENC_ROOT\US5MA1BO\US5MA1BO.002"),
            Cell(@"ENC_ROOT\US5MA1BO\US5MA1BO.000"),
            Cell(@"ENC_ROOT\US5MA1BO\US5MA1BO.001"));

        var cells = S57ExchangeSetCatalog.SelectBaseCells(catalogue);

        var cell = Assert.Single(cells);
        Assert.Equal("US5MA1BO", cell.CellName);
        Assert.Equal($"ENC_ROOT{Sep}US5MA1BO{Sep}US5MA1BO.000", cell.RelativePath);
        Assert.Equal(
            new[]
            {
                $"ENC_ROOT{Sep}US5MA1BO{Sep}US5MA1BO.001",
                $"ENC_ROOT{Sep}US5MA1BO{Sep}US5MA1BO.002",
            },
            cell.UpdateRelativePaths);
    }

    [Fact]
    public void SelectBaseCells_SkipsNonDatasetEntries()
    {
        var catalogue = Catalogue(
            Cell("CATALOG.031"),
            Cell(@"ENC_ROOT\README.TXT"),
            Cell(@"ENC_ROOT\US5MA1BO\US5MA1BO.000"),
            Cell(@"ENC_ROOT\US5MA1BO\CERT.CRT"));

        var cells = S57ExchangeSetCatalog.SelectBaseCells(catalogue);

        var cell = Assert.Single(cells);
        Assert.Equal("US5MA1BO", cell.CellName);
        Assert.Empty(cell.UpdateRelativePaths);
    }

    [Fact]
    public void SelectBaseCells_SkipsOrphanUpdatesWithoutBase()
    {
        var catalogue = Catalogue(
            Cell(@"US5MA1BO\US5MA1BO.001"),
            Cell(@"US5MA1BO\US5MA1BO.002"));

        var cells = S57ExchangeSetCatalog.SelectBaseCells(catalogue);

        Assert.Empty(cells);
    }

    [Fact]
    public void SelectBaseCells_OrdersCellsByName()
    {
        var catalogue = Catalogue(
            Cell("ZZ5AAAAA.000"),
            Cell("AA5BBBBB.000"),
            Cell("MM5CCCCC.000"));

        var cells = S57ExchangeSetCatalog.SelectBaseCells(catalogue);

        Assert.Equal(
            new[] { "AA5BBBBB", "MM5CCCCC", "ZZ5AAAAA" },
            cells.Select(c => c.CellName).ToArray());
    }

    [Fact]
    public void SelectBaseCells_MapsBoundingBoxFromBaseEntry()
    {
        var catalogue = Catalogue(
            Cell("US5MA1BO.000", west: -71.0, east: -70.0, south: 42.0, north: 43.0));

        var cell = Assert.Single(S57ExchangeSetCatalog.SelectBaseCells(catalogue));

        Assert.NotNull(cell.BoundingBox);
        Assert.Equal(-71.0, cell.BoundingBox!.WestBoundLongitude);
        Assert.Equal(-70.0, cell.BoundingBox.EastBoundLongitude);
        Assert.Equal(42.0, cell.BoundingBox.SouthBoundLatitude);
        Assert.Equal(43.0, cell.BoundingBox.NorthBoundLatitude);
    }

    [Fact]
    public void SelectBaseCells_PartialBoundingBox_IsNull()
    {
        var catalogue = Catalogue(
            Cell("US5MA1BO.000", west: -71.0, east: -70.0, south: 42.0, north: null));

        var cell = Assert.Single(S57ExchangeSetCatalog.SelectBaseCells(catalogue));

        Assert.Null(cell.BoundingBox);
    }

    [Fact]
    public void UnionBoundingBox_UnionsCellExtents()
    {
        var catalogue = Catalogue(
            Cell("AA5AAAAA.000", west: -71.0, east: -70.0, south: 42.0, north: 43.0),
            Cell("BB5BBBBB.000", west: -72.0, east: -69.0, south: 41.0, north: 44.0));

        var union = S57ExchangeSetCatalog.UnionBoundingBox(
            S57ExchangeSetCatalog.SelectBaseCells(catalogue));

        Assert.NotNull(union);
        Assert.Equal(-72.0, union!.WestBoundLongitude);
        Assert.Equal(-69.0, union.EastBoundLongitude);
        Assert.Equal(41.0, union.SouthBoundLatitude);
        Assert.Equal(44.0, union.NorthBoundLatitude);
    }

    [Fact]
    public void UnionBoundingBox_NoExtents_ReturnsNull()
    {
        var catalogue = Catalogue(Cell("US5MA1BO.000"));

        var union = S57ExchangeSetCatalog.UnionBoundingBox(
            S57ExchangeSetCatalog.SelectBaseCells(catalogue));

        Assert.Null(union);
    }

    [Fact]
    public void SelectBaseCells_NullCatalogue_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => S57ExchangeSetCatalog.SelectBaseCells(null!));

    [Fact]
    public void ReadBaseCells_NullOrEmptyRoot_Throws() =>
        Assert.Throws<ArgumentException>(
            () => S57ExchangeSetCatalog.ReadBaseCells(string.Empty));

    [Fact]
    public void ResolveCataloguePath_MissingCatalogue_Throws()
    {
        var dir = Directory.CreateTempSubdirectory("s57cat");
        try
        {
            Assert.Throws<FileNotFoundException>(
                () => S57ExchangeSetCatalog.ResolveCataloguePath(dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveCataloguePath_FindsCatalogueInFolder()
    {
        var dir = Directory.CreateTempSubdirectory("s57cat");
        try
        {
            var catalogue = Path.Combine(dir.FullName, "CATALOG.031");
            File.WriteAllText(catalogue, "placeholder");

            Assert.Equal(
                Path.GetFullPath(catalogue),
                S57ExchangeSetCatalog.ResolveCataloguePath(dir.FullName));
            Assert.Equal(
                Path.GetFullPath(catalogue),
                S57ExchangeSetCatalog.ResolveCataloguePath(catalogue));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
