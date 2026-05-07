using System;
using System.IO;
using System.IO.Compression;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Unit coverage for <see cref="ExchangeSetDetection"/>, the helpers
/// the viewer's drag-and-drop handler uses to decide whether a
/// dropped folder or ZIP is an S-100 exchange set.
/// </summary>
public sealed class ExchangeSetDetectionTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), $"esd-{Guid.NewGuid():N}");

    public ExchangeSetDetectionTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best effort */ }
    }

    [Theory]
    [InlineData("foo.zip", true)]
    [InlineData("foo.ZIP", true)]
    [InlineData("/path/to/Foo.Zip", true)]
    [InlineData("foo.000", false)]
    [InlineData("foo", false)]
    [InlineData("", false)]
    public void IsZipPath_HandlesCommonCases(string path, bool expected)
    {
        Assert.Equal(expected, ExchangeSetDetection.IsZipPath(path));
    }

    [Fact]
    public void LooksLikeExchangeSetFolder_TrueWhenCatalogPresent()
    {
        var folder = Path.Combine(_tempRoot, "set");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "CATALOG.XML"), "<root/>");

        Assert.True(ExchangeSetDetection.LooksLikeExchangeSetFolder(folder));
    }

    [Fact]
    public void LooksLikeExchangeSetFolder_CaseInsensitiveOnFileName()
    {
        var folder = Path.Combine(_tempRoot, "ci");
        Directory.CreateDirectory(folder);
        // Name varies by case to make sure detection isn't relying on
        // a single canonical spelling.
        File.WriteAllText(Path.Combine(folder, "catalog.xml"), "<root/>");

        Assert.True(ExchangeSetDetection.LooksLikeExchangeSetFolder(folder));
    }

    [Fact]
    public void LooksLikeExchangeSetFolder_FalseWhenCatalogOnlyInSubfolder()
    {
        // A nested CATALOG.XML doesn't constitute an exchange set
        // root — the loader expects it at the top level.
        var folder = Path.Combine(_tempRoot, "nested");
        var sub = Path.Combine(folder, "inner");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "CATALOG.XML"), "<root/>");

        Assert.False(ExchangeSetDetection.LooksLikeExchangeSetFolder(folder));
    }

    [Fact]
    public void LooksLikeExchangeSetFolder_FalseForEmptyOrMissing()
    {
        var folder = Path.Combine(_tempRoot, "empty");
        Directory.CreateDirectory(folder);
        Assert.False(ExchangeSetDetection.LooksLikeExchangeSetFolder(folder));
        Assert.False(ExchangeSetDetection.LooksLikeExchangeSetFolder(
            Path.Combine(_tempRoot, "does-not-exist")));
        Assert.False(ExchangeSetDetection.LooksLikeExchangeSetFolder(""));
    }

    [Fact]
    public void LooksLikeExchangeSetZip_TrueWhenRootCatalogPresent()
    {
        var zip = Path.Combine(_tempRoot, "good.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("CATALOG.XML");
            archive.CreateEntry("S-101/A.000");
        }

        Assert.True(ExchangeSetDetection.LooksLikeExchangeSetZip(zip));
    }

    [Fact]
    public void LooksLikeExchangeSetZip_FalseWhenCatalogOnlyNested()
    {
        var zip = Path.Combine(_tempRoot, "nested.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("inner/CATALOG.XML");
            archive.CreateEntry("inner/S-101/A.000");
        }

        Assert.False(ExchangeSetDetection.LooksLikeExchangeSetZip(zip));
    }

    [Fact]
    public void LooksLikeExchangeSetZip_FalseForBareDataFile()
    {
        var zip = Path.Combine(_tempRoot, "bare.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("A.000");
            archive.CreateEntry("README.txt");
        }

        Assert.False(ExchangeSetDetection.LooksLikeExchangeSetZip(zip));
    }

    [Fact]
    public void LooksLikeExchangeSetZip_FalseForCorruptArchive()
    {
        var zip = Path.Combine(_tempRoot, "corrupt.zip");
        File.WriteAllText(zip, "not a real zip");

        // Should swallow the InvalidDataException and let the drop
        // fall through to the single-file loader.
        Assert.False(ExchangeSetDetection.LooksLikeExchangeSetZip(zip));
    }

    [Fact]
    public void LooksLikeExchangeSetZip_FalseForMissingFile()
    {
        Assert.False(ExchangeSetDetection.LooksLikeExchangeSetZip(
            Path.Combine(_tempRoot, "ghost.zip")));
        Assert.False(ExchangeSetDetection.LooksLikeExchangeSetZip(""));
    }
}
