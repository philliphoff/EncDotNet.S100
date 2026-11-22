using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="CatalogueSpecDetection.Detect"/> tells a catalogue's kind and
/// product from its content (design note <c>catalogue-panels.md</c> D5, A1,
/// A2): feature catalogue XML, portrayal catalogue folders and ZIP packages,
/// and a reason for anything it refuses.
/// </summary>
public sealed class CatalogueSpecDetectionTests : IDisposable
{
    private readonly CatalogueFixtures _files = new();

    public void Dispose() => _files.Dispose();

    private static DetectedCatalogue Found(CatalogueDetectionResult result)
    {
        Assert.True(result.Catalogue is not null, result.Reason);
        Assert.Null(result.Reason);
        return result.Catalogue!;
    }

    private static string Refused(CatalogueDetectionResult result)
    {
        Assert.Null(result.Catalogue);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        return result.Reason!;
    }

    [Fact]
    public void A_feature_catalogue_is_found_by_its_productId_not_assumed_to_be_S101()
    {
        var path = _files.FeatureFile("9.9.9");

        var catalogue = Found(CatalogueSpecDetection.Detect(path));

        Assert.Equal(CatalogueKind.Feature, catalogue.Kind);
        Assert.Equal("S-102", catalogue.Spec);
        Assert.Equal("9.9.9", catalogue.Metadata.Version);
        Assert.Equal("2024-12-05", catalogue.Metadata.VersionDate);
        Assert.Equal(Path.GetFullPath(path), catalogue.Path);
    }

    [Fact]
    public void A_portrayal_catalogue_folder_is_found_by_its_productId()
    {
        var folder = _files.PortrayalFolder("3.1.0-draft");

        var catalogue = Found(CatalogueSpecDetection.Detect(folder));

        Assert.Equal(CatalogueKind.Portrayal, catalogue.Kind);
        Assert.Equal("S-102", catalogue.Spec);
        Assert.Equal("3.1.0-draft", catalogue.Metadata.Version);
        Assert.Null(catalogue.ZipBasePath);
    }

    [Fact]
    public void A_folder_holding_one_catalogue_folder_and_the_xml_file_itself_both_find_it()
    {
        var inner = _files.PortrayalFolder("3.1.0", name: Path.Combine("download", "PortrayalCatalog"));

        var fromParent = Found(CatalogueSpecDetection.Detect(Path.GetDirectoryName(inner)!));
        var fromXml = Found(CatalogueSpecDetection.Detect(Path.Combine(inner, "portrayal_catalogue.xml")));

        Assert.Equal(Path.GetFullPath(inner), fromParent.SourcePath);
        Assert.Equal(Path.GetFullPath(inner), fromXml.SourcePath);
        Assert.Equal("S-102", fromXml.Spec);
    }

    [Fact]
    public async Task A_zip_package_is_found_and_its_source_reads_from_the_folder_inside()
    {
        var zip = _files.PortrayalPackage("3.2.0");

        var catalogue = Found(CatalogueSpecDetection.Detect(zip));

        Assert.Equal(CatalogueKind.Portrayal, catalogue.Kind);
        Assert.Equal("S-102", catalogue.Spec);
        Assert.Equal("3.2.0", catalogue.Metadata.Version);
        Assert.Equal("pkg-src-3.2.0/", catalogue.ZipBasePath);
        using var source = catalogue.OpenSource();
        await using var stream = await source.OpenAsync("portrayal_catalogue.xml", TestContext.Current.CancellationToken);
        Assert.True(stream.ReadByte() >= 0);
    }

    [Fact]
    public void A_missing_path_is_refused()
    {
        Refused(CatalogueSpecDetection.Detect(Path.Combine(_files.Root, "nope")));
        Refused(CatalogueSpecDetection.Detect(""));
    }

    [Fact]
    public void A_folder_without_portrayal_catalogue_xml_says_so()
    {
        var folder = Path.Combine(_files.Root, "empty");
        Directory.CreateDirectory(folder);

        Assert.Contains("portrayal_catalogue.xml", Refused(CatalogueSpecDetection.Detect(folder)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_zip_without_portrayal_catalogue_xml_says_so()
    {
        var folder = Path.Combine(_files.Root, "notpc");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "readme.txt"), "hello");
        var zip = Path.Combine(_files.Root, "notpc.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, zip);

        Assert.Contains("package", Refused(CatalogueSpecDetection.Detect(zip)), StringComparison.Ordinal);
    }

    [Fact]
    public void Text_that_isnt_xml_is_refused()
    {
        Refused(CatalogueSpecDetection.Detect(_files.File_("garbage.xml", "not xml at all")));
    }

    [Fact]
    public void Xml_of_another_kind_names_its_root_element()
    {
        var reason = Refused(CatalogueSpecDetection.Detect(_files.File_("other.xml", "<?xml version=\"1.0\"?><Dataset />")));

        Assert.Contains("Dataset", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_feature_catalogue_that_doesnt_say_its_product_is_refused()
    {
        var path = _files.FeatureFile("1.0.0", withProductId: false);

        Assert.Contains("productId", Refused(CatalogueSpecDetection.Detect(path)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-102")]
    [InlineData("S-411")]
    public void Built_in_feature_catalogue_metadata_has_a_version(string spec)
    {
        var metadata = CatalogueSpecDetection.ReadBuiltInFeatureCatalogue(spec);

        Assert.NotNull(metadata);
        Assert.False(string.IsNullOrEmpty(metadata.Version));
    }

    [Fact]
    public void Built_in_portrayal_catalogue_metadata_has_a_version_and_unknown_specs_have_none()
    {
        Assert.Equal(CatalogueFixtures.BuiltInVersion, CatalogueSpecDetection.ReadBuiltInPortrayalCatalogue("S-102")?.Version);
        Assert.Null(CatalogueSpecDetection.ReadBuiltInFeatureCatalogue("S-999"));
        Assert.Null(CatalogueSpecDetection.ReadBuiltInPortrayalCatalogue("S-999"));
    }
}
