using System.IO.Compression;

namespace EncDotNet.S100.ExchangeSets.Tests;

/// <summary>
/// Tests for <see cref="ExchangeSetRoot"/>: a catalogue at the top level, or
/// else under <c>S100_ROOT</c> (S-100 Part 17).
/// </summary>
public sealed class ExchangeSetRootTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"s100-root-{Guid.NewGuid():N}");

    public ExchangeSetRootTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(new[] { "CATALOG.XML", "S-101/A.000" }, "", "CATALOG.XML")]
    [InlineData(new[] { "S100_ROOT/CATALOG.XML", "S100_ROOT/S-101/A.000" }, "S100_ROOT/", "CATALOG.XML")]
    [InlineData(new[] { "s100_root/catalogue.xml" }, "s100_root/", "catalogue.xml")]
    [InlineData(new[] { "S100_ROOT/CATALOGUE.XML", "S100_ROOT/CATALOG.XML" }, "S100_ROOT/", "CATALOG.XML")]
    [InlineData(new[] { "CATALOG.XML", "S100_ROOT/CATALOG.XML" }, "", "CATALOG.XML")]
    public void FindInZip_FindsTheCatalogue(string[] entries, string basePath, string catalogueName) =>
        Assert.Equal(new ExchangeSetRootLocation(basePath, catalogueName), ExchangeSetRoot.FindInZip(entries));

    [Theory]
    [InlineData("inner/CATALOG.XML")]
    [InlineData("S100_ROOT/inner/CATALOG.XML")]
    [InlineData("S-101/A.000")]
    public void FindInZip_IgnoresOtherFolders(string entry) =>
        Assert.Null(ExchangeSetRoot.FindInZip([entry]));

    [Fact]
    public void FindInZip_OpensTheArchive()
    {
        var zip = Path.Combine(_root, "set.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("S100_ROOT/CATALOG.XML");
        }

        Assert.Equal(new ExchangeSetRootLocation("S100_ROOT/", "CATALOG.XML"), ExchangeSetRoot.FindInZip(zip));
        Assert.Null(ExchangeSetRoot.FindInZip(Path.Combine(_root, "missing.zip")));
    }

    [Fact]
    public void FindInZip_ReturnsNullForACorruptArchive()
    {
        var zip = Path.Combine(_root, "corrupt.zip");
        File.WriteAllText(zip, "not a zip");

        Assert.Null(ExchangeSetRoot.FindInZip(zip));
    }

    [Fact]
    public void FindInFolder_PrefersTheTopLevel()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "S100_ROOT")).FullName;
        File.WriteAllText(Path.Combine(nested, "CATALOG.XML"), "<root/>");

        Assert.Equal(new ExchangeSetRootLocation(nested, "CATALOG.XML"), ExchangeSetRoot.FindInFolder(_root));

        File.WriteAllText(Path.Combine(_root, "catalogue.xml"), "<root/>");
        Assert.Equal(new ExchangeSetRootLocation(_root, "catalogue.xml"), ExchangeSetRoot.FindInFolder(_root));
    }

    [Fact]
    public void FindInFolder_IgnoresOtherFolders()
    {
        var inner = Directory.CreateDirectory(Path.Combine(_root, "inner")).FullName;
        File.WriteAllText(Path.Combine(inner, "CATALOG.XML"), "<root/>");

        Assert.Null(ExchangeSetRoot.FindInFolder(_root));
        Assert.Null(ExchangeSetRoot.FindInFolder(Path.Combine(_root, "missing")));
    }
}
