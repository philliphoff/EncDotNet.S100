using System.Reflection;
using System.Runtime.CompilerServices;
using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using PureHDF;
using static EncDotNet.S100.TestSupport.NetCdfFixtures;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1092: an HDF5 file is claimed only by the product its root
/// <c>productSpecification</c> attribute (S-100 Part 10c Table 10c-6,
/// mandatory) declares, and is never assumed to be S-102. When no product
/// claims it, <see cref="DatasetPipelineFactory.DescribeUnrecognizedDataset(string)"/>
/// says why: no attribute, an unsupported product, or not readable as HDF5.
/// </summary>
public sealed class DatasetPipelineFactoryHdf5UndeclaredTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hdf5-undeclared-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    /// <summary>
    /// Writes a minimal S-102-shaped file: a <c>BathymetryCoverage</c> group
    /// with one 2×2 instance, and the given root attributes.
    /// </summary>
    private string WriteS102Shaped(string name, Dictionary<string, object> rootAttributes)
    {
        var path = PathFor(name);
        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = 47.0,
                ["gridOriginLongitude"] = -122.0,
                ["gridSpacingLatitudinal"] = 0.001,
                ["gridSpacingLongitudinal"] = 0.001,
                ["numPointsLatitudinal"] = 2,
                ["numPointsLongitudinal"] = 2,
            },
            ["Group_001"] = new H5Group { ["values"] = Enumerable.Range(0, 4).Select(i => new BathyRow { Depth = 10 + i, Uncertainty = 0.5f }).ToArray() },
        };
        rootAttributes.TryAdd("horizontalCRS", 4326);
        new H5File
        {
            Attributes = rootAttributes,
            ["BathymetryCoverage"] = new H5Group { ["BathymetryCoverage.01"] = instance },
        }.Write(path, new H5WriteOptions(FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name));
        return path;
    }

    private static string DatasetsRoot([CallerFilePath] string callerFilePath = "") =>
        Path.Combine(Path.GetDirectoryName(callerFilePath)!, "..", "datasets");

    // -----------------------------------------------------------------
    // Declared S-102: claimed
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("INT.IHO.S-102.3.0.0")]
    [InlineData("INT.IHO.S-102.2.2")]
    public void A_declared_S102_file_is_S102(string productSpecification)
    {
        var path = WriteS102Shaped("declared.h5", new() { ["productSpecification"] = productSpecification });

        Assert.Equal("S-102", DatasetPipelineFactory.DetectProductSpec(path));
        Assert.Null(DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
    }

    [Fact]
    public void The_committed_NOAA_S102_tile_is_S102()
    {
        var path = Path.Combine(DatasetsRoot(), "S102", "102US004MI1CI262227.h5");
        Assert.SkipUnless(File.Exists(path), $"Fixture not found: {path}");

        Assert.Equal("S-102", DatasetPipelineFactory.DetectProductSpec(path));
        Assert.Null(DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
    }

    // -----------------------------------------------------------------
    // Undeclared: unknown, and says so
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("undeclared.h5")]
    [InlineData("undeclared.hdf5")]
    [InlineData("undeclared")]
    public void An_undeclared_S102_shaped_file_is_unknown_and_says_it_declares_no_product(string name)
    {
        var path = WriteS102Shaped(name, new());

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path));
        Assert.Equal(
            "This HDF5 file doesn't say which S-100 product it is (no productSpecification attribute).",
            DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
    }

    [Fact]
    public void An_undeclared_file_is_unknown_from_an_asset_source_too()
    {
        WriteS102Shaped("undeclared.h5", new());
        using var source = FileSystemAssetSource.Create(_dir);

        Assert.Null(DatasetPipelineFactory.DetectProductSpecFromSource(source, "undeclared.h5"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_productSpecification_counts_as_none(string productSpecification)
    {
        var path = WriteS102Shaped("blank.h5", new() { ["productSpecification"] = productSpecification });

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path));
        Assert.Equal(DatasetPipelineFactory.Hdf5NoProductSpecificationMessage, DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
    }

    [Fact]
    public void A_productSpecification_that_is_not_a_string_counts_as_none()
    {
        var path = WriteS102Shaped("numeric.h5", new() { ["productSpecification"] = 102 });

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path));
        Assert.Equal(DatasetPipelineFactory.Hdf5NoProductSpecificationMessage, DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
    }

    [Fact]
    public void Opening_an_undeclared_file_fails_with_the_reason()
    {
        var path = WriteS102Shaped("undeclared.h5", new());

        var error = Assert.Throws<NotSupportedException>(() => CreateFactory().CreateProcessor(path));
        Assert.Equal(
            "Unrecognized dataset file: undeclared.h5. " + DatasetPipelineFactory.Hdf5NoProductSpecificationMessage,
            error.Message);
    }

    // -----------------------------------------------------------------
    // Declares a product nobody reads
    // -----------------------------------------------------------------

    [Fact]
    public void A_file_declaring_an_unsupported_product_quotes_it()
    {
        var path = WriteS102Shaped("s412.h5", new() { ["productSpecification"] = "INT.IHO.S-412.1.0" });

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path));
        Assert.Equal(
            "This HDF5 file declares productSpecification 'INT.IHO.S-412.1.0', which isn't a supported product.",
            DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
    }

    [Fact]
    public void A_product_the_registry_lacks_is_unsupported()
    {
        var path = WriteS102Shaped("s104.h5", new() { ["productSpecification"] = "INT.IHO.S-104.2.0" });
        var s102Only = new ProductRegistry().AddProduct(S100Products.S102);

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path, s102Only));
        Assert.Equal(
            "This HDF5 file declares productSpecification 'INT.IHO.S-104.2.0', which isn't a supported product.",
            DatasetPipelineFactory.DescribeUnrecognizedDataset(path, s102Only));
    }

    // -----------------------------------------------------------------
    // Not readable as HDF5: encrypted or damaged
    // -----------------------------------------------------------------

    [Fact]
    public void An_h5_file_with_no_HDF5_signature_may_be_encrypted()
    {
        var path = PathFor("encrypted.h5");
        File.WriteAllBytes(path, [.. Enumerable.Range(0, 4096).Select(i => (byte)(i * 37 + 11))]);

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path));
        Assert.Equal(
            "This file is named .h5 but isn't readable as HDF5. It may be encrypted (S-100 Part 15), which opens only from its exchange set with a permit, or it isn't HDF5.",
            DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
    }

    [Fact]
    public void The_committed_encrypted_S102_dataset_is_reported_as_possibly_encrypted()
    {
        // The Protected-Mixed exchange set's S-102 file is S-100 Part 15
        // ciphertext; its catalogue, not the file, says it is S-102.
        var path = Path.Combine(DatasetsRoot(), "ExchangeSets", "Protected-Mixed", "S-102", "DATASET_FILES", "102ZZ00SPIT.h5");
        Assert.SkipUnless(File.Exists(path), $"Fixture not found: {path}");

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path));
        Assert.Contains("may be encrypted (S-100 Part 15)", DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
    }

    [Fact]
    public void A_truncated_HDF5_file_may_be_damaged()
    {
        var whole = File.ReadAllBytes(WriteS102Shaped("whole.h5", new() { ["productSpecification"] = "INT.IHO.S-102.3.0.0" }));
        var path = PathFor("truncated.h5");
        File.WriteAllBytes(path, whole[..16]);

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path));
        Assert.Equal(
            "This HDF5 file can't be read; it may be damaged or truncated.",
            DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
    }

    // -----------------------------------------------------------------
    // NetCDF-4 is HDF5 too
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("waves.h5")]
    [InlineData("waves.nc")]
    public void A_CF_NetCDF4_file_is_NetCDF_not_S102(string name)
    {
        var path = WriteWaveNetCdf4(PathFor(name));

        Assert.Equal("NetCDF", DatasetPipelineFactory.DetectProductSpec(path));
        Assert.Null(DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
    }

    [Fact]
    public void A_NetCDF4_file_without_CF_Conventions_named_h5_is_unknown()
    {
        var path = WriteWaveNetCdf4(PathFor("plain.h5"), globalAttributes: new Dictionary<string, object> { ["Conventions"] = "COARDS" });

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path));
        Assert.Equal(DatasetPipelineFactory.Hdf5NoProductSpecificationMessage, DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
    }

    // -----------------------------------------------------------------
    // Nothing more to say
    // -----------------------------------------------------------------

    [Fact]
    public void A_file_that_is_not_HDF5_has_no_specific_reason()
    {
        var path = PathFor("mystery.dat");
        File.WriteAllText(path, "not a dataset");

        Assert.Null(DatasetPipelineFactory.DescribeUnrecognizedDataset(path));
        Assert.Null(DatasetPipelineFactory.DescribeUnrecognizedDataset(PathFor("missing.h5")));
        Assert.Null(DatasetPipelineFactory.DescribeUnrecognizedDataset(_dir));
    }

    private static DatasetPipelineFactory CreateFactory() => new(
        new PortrayalCatalogueManager(),
        new MoonSharpLuaEngine(),
        new ProjNetCrsTransformFactory(),
        new FeatureCatalogueManager(),
        new DisplayPlaneAuthorityProvider());

    private struct BathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }
}
