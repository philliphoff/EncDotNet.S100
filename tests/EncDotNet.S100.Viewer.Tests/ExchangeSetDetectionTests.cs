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
    public void LooksLikeExchangeSetFolder_AcceptsS411CatalogueSpelling()
    {
        // JCOMM/IHO S-411 sample sets name the catalogue "catalogue.xml"
        // rather than the canonical CATALOG.XML; the folder must still
        // route to the exchange-set loader.
        var folder = Path.Combine(_tempRoot, "s411");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "catalogue.xml"), "<root/>");

        Assert.True(ExchangeSetDetection.LooksLikeExchangeSetFolder(folder));
        Assert.Equal(
            "catalogue.xml",
            ExchangeSetDetection.ResolveFolderCatalogueName(folder));
    }

    [Fact]
    public void ResolveFolderCatalogueName_PreservesCanonicalName()
    {
        var folder = Path.Combine(_tempRoot, "canon");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "CATALOG.XML"), "<root/>");

        Assert.Equal(
            "CATALOG.XML",
            ExchangeSetDetection.ResolveFolderCatalogueName(folder));
    }

    [Fact]
    public void ResolveFolderCatalogueName_PrefersCanonicalName()
    {
        var folder = Path.Combine(_tempRoot, "canon-preferred");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "catalogue.xml"), "<root/>");
        File.WriteAllText(Path.Combine(folder, "CATALOG.XML"), "<root/>");

        Assert.Equal(
            "CATALOG.XML",
            ExchangeSetDetection.ResolveFolderCatalogueName(folder));
    }

    [Fact]
    public void ResolveFolderCatalogueName_NullWhenAbsent()
    {
        var folder = Path.Combine(_tempRoot, "none");
        Directory.CreateDirectory(folder);
        Assert.Null(ExchangeSetDetection.ResolveFolderCatalogueName(folder));
        Assert.Null(ExchangeSetDetection.ResolveFolderCatalogueName(
            Path.Combine(_tempRoot, "ghost")));
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
    public void LooksLikeExchangeSetZip_AcceptsS411CatalogueSpelling()
    {
        var zip = Path.Combine(_tempRoot, "s411.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("catalogue.xml");
            archive.CreateEntry("data/S411.gml");
        }

        Assert.True(ExchangeSetDetection.LooksLikeExchangeSetZip(zip));
        Assert.Equal(
            "catalogue.xml",
            ExchangeSetDetection.ResolveZipCatalogueEntry(zip));
    }

    [Fact]
    public void ResolveZipCatalogueEntry_PrefersCanonicalName()
    {
        var zip = Path.Combine(_tempRoot, "canon-preferred.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("catalogue.xml");
            archive.CreateEntry("CATALOG.XML");
            archive.CreateEntry("data/S411.gml");
        }

        Assert.Equal(
            "CATALOG.XML",
            ExchangeSetDetection.ResolveZipCatalogueEntry(zip));
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

    [Fact]
    public void LooksLikeS57ExchangeSetFolder_TrueWhenCatalog031Present()
    {
        var folder = Path.Combine(_tempRoot, "s57set");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "CATALOG.031"), "binary");

        Assert.True(ExchangeSetDetection.LooksLikeS57ExchangeSetFolder(folder));
        Assert.True(ExchangeSetDetection.LooksLikeS57ExchangeSet(folder));
    }

    [Fact]
    public void LooksLikeS57ExchangeSetFolder_CaseInsensitive()
    {
        var folder = Path.Combine(_tempRoot, "s57ci");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "catalog.031"), "binary");

        Assert.True(ExchangeSetDetection.LooksLikeS57ExchangeSetFolder(folder));
    }

    [Fact]
    public void IsS57CataloguePath_TrueForDroppedCatalogue031()
    {
        var folder = Path.Combine(_tempRoot, "s57drop");
        Directory.CreateDirectory(folder);
        var catalogue = Path.Combine(folder, "CATALOG.031");
        File.WriteAllText(catalogue, "binary");

        Assert.True(ExchangeSetDetection.IsS57CataloguePath(catalogue));
        Assert.True(ExchangeSetDetection.LooksLikeS57ExchangeSet(catalogue));
    }

    [Fact]
    public void S57Detection_FalseForS100Set()
    {
        var folder = Path.Combine(_tempRoot, "s100set");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "CATALOG.XML"), "<root/>");

        Assert.False(ExchangeSetDetection.LooksLikeS57ExchangeSetFolder(folder));
        Assert.False(ExchangeSetDetection.LooksLikeS57ExchangeSet(folder));
        // The S-100 detector must still recognise it (regression guard).
        Assert.True(ExchangeSetDetection.LooksLikeExchangeSetFolder(folder));
    }

    [Fact]
    public void S57Detection_FalseForEmptyOrMissing()
    {
        var folder = Path.Combine(_tempRoot, "s57empty");
        Directory.CreateDirectory(folder);
        Assert.False(ExchangeSetDetection.LooksLikeS57ExchangeSetFolder(folder));
        Assert.False(ExchangeSetDetection.LooksLikeS57ExchangeSet(
            Path.Combine(_tempRoot, "missing")));
        Assert.False(ExchangeSetDetection.IsS57CataloguePath(""));
    }

    [Fact]
    public void ResolveS57Root_ResolvesFolderAndFile()
    {
        var folder = Path.Combine(_tempRoot, "s57resolve");
        Directory.CreateDirectory(folder);
        var catalogue = Path.Combine(folder, "CATALOG.031");
        File.WriteAllText(catalogue, "binary");

        Assert.Equal(
            Path.GetFullPath(folder),
            ExchangeSetDetection.ResolveS57Root(folder));
        Assert.Equal(
            Path.GetFullPath(folder),
            ExchangeSetDetection.ResolveS57Root(catalogue));
    }

    [Fact]
    public void ResolveS57Root_ThrowsWhenNotAnS57Set()
    {
        Assert.Throws<FileNotFoundException>(
            () => ExchangeSetDetection.ResolveS57Root(
                Path.Combine(_tempRoot, "nope.000")));
    }

    // ── Loose-cell folder detection (issue #449) ──────────────────────

    [Fact]
    public void EnumerateLooseBaseCells_ReturnsTopLevelBaseCells()
    {
        var folder = Path.Combine(_tempRoot, "loose");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "US5WA01M.000"), "base");
        File.WriteAllText(Path.Combine(folder, "US5WA01M.001"), "update");
        File.WriteAllText(Path.Combine(folder, "US5WA02M.000"), "base2");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "ignore");

        var cells = ExchangeSetDetection.EnumerateLooseBaseCells(folder);

        Assert.Equal(2, cells.Count);
        Assert.Contains(cells, c => Path.GetFileName(c) == "US5WA01M.000");
        Assert.Contains(cells, c => Path.GetFileName(c) == "US5WA02M.000");
        Assert.DoesNotContain(cells, c => Path.GetFileName(c) == "US5WA01M.001");
    }

    [Fact]
    public void EnumerateLooseBaseCells_CaseInsensitiveExtension()
    {
        var folder = Path.Combine(_tempRoot, "loose-ci");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "CELL.000"), "base");

        Assert.Single(ExchangeSetDetection.EnumerateLooseBaseCells(folder));
    }

    [Fact]
    public void EnumerateLooseBaseCells_EmptyForMissingFolder()
    {
        Assert.Empty(ExchangeSetDetection.EnumerateLooseBaseCells(
            Path.Combine(_tempRoot, "missing")));
        Assert.Empty(ExchangeSetDetection.EnumerateLooseBaseCells(""));
    }

    [Fact]
    public void LooksLikeLooseCellFolder_TrueForCatalogueLessCellFolder()
    {
        var folder = Path.Combine(_tempRoot, "cells");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "US5WA01M.000"), "base");
        File.WriteAllText(Path.Combine(folder, "US5WA01M.001"), "update");

        Assert.True(ExchangeSetDetection.LooksLikeLooseCellFolder(folder));
    }

    [Fact]
    public void LooksLikeLooseCellFolder_FalseWhenS100CataloguePresent()
    {
        var folder = Path.Combine(_tempRoot, "cells-with-s100");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "US5WA01M.000"), "base");
        File.WriteAllText(Path.Combine(folder, "CATALOG.XML"), "<root/>");

        // A catalogue is present, so this must route to the exchange-set
        // loader, not the loose-cell path.
        Assert.False(ExchangeSetDetection.LooksLikeLooseCellFolder(folder));
        Assert.True(ExchangeSetDetection.LooksLikeExchangeSetFolder(folder));
    }

    [Fact]
    public void LooksLikeLooseCellFolder_FalseWhenS57CataloguePresent()
    {
        var folder = Path.Combine(_tempRoot, "cells-with-s57");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "US5WA01M.000"), "base");
        File.WriteAllText(Path.Combine(folder, "CATALOG.031"), "binary");

        Assert.False(ExchangeSetDetection.LooksLikeLooseCellFolder(folder));
        Assert.True(ExchangeSetDetection.LooksLikeS57ExchangeSetFolder(folder));
    }

    [Fact]
    public void LooksLikeLooseCellFolder_FalseForFolderWithoutBaseCells()
    {
        var folder = Path.Combine(_tempRoot, "no-cells");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "readme.txt"), "text");
        File.WriteAllText(Path.Combine(folder, "US5WA01M.001"), "orphan update");

        Assert.False(ExchangeSetDetection.LooksLikeLooseCellFolder(folder));
    }

    [Fact]
    public void LooksLikeLooseCellFolder_FalseForMissingOrEmptyPath()
    {
        Assert.False(ExchangeSetDetection.LooksLikeLooseCellFolder(
            Path.Combine(_tempRoot, "missing")));
        Assert.False(ExchangeSetDetection.LooksLikeLooseCellFolder(""));
    }
}
