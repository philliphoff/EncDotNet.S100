using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.TestSupport;
using PureHDF;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1061: a product that isn't S-100 is one registration. A fake GRIB2
/// product (<see cref="FakeWeatherProduct"/>) registered with a
/// <see cref="ProductRegistration.MatchBytes"/> matcher is detected from its
/// leading bytes (with no useful extension), constructed, rendered, projected
/// into the catalog and given its default plane — with no change to the
/// factory, the projector or the authority. Also pins the detection changes:
/// an HDF5 file no product claims is unknown, not S-102.
/// </summary>
public class NonS100ProductRegistrationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("non-s100-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ProductRegistry RegistryWithFake()
    {
        var registry = S100Products.CreateDefaultRegistry();
        registry.Register(FakeWeatherProduct.Registration());
        return registry;
    }

    [Theory]
    [InlineData("gfs.t06z.pgrb2.0p25.f000-filter")]
    [InlineData("filter_gfs_0p25.pl")]
    [InlineData("wind.grib2")]
    [InlineData("WIND.GRB")]
    public void Detect_ClaimsTheFileByItsLeadingBytes(string fileName)
    {
        var path = FakeWeatherProduct.Write(_dir, fileName);

        Assert.Equal("GRIB2", DatasetPipelineFactory.DetectProductSpec(path, RegistryWithFake()));
    }

    [Fact]
    public void Detect_WithoutTheRegistration_IsUnknown()
    {
        var path = FakeWeatherProduct.Write(_dir);

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path, S100Products.CreateDefaultRegistry()));
    }

    [Fact]
    public void Detect_BytesThatDontMatch_IsUnknown()
    {
        var path = Path.Combine(_dir, "grib1.grb");
        var bytes = new byte[16];
        "GRIB"u8.CopyTo(bytes);
        bytes[7] = 1; // GRIB edition 1
        File.WriteAllBytes(path, bytes);

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path, RegistryWithFake()));
    }

    [Fact]
    public void Detect_FromAnAssetSource_ClaimsTheFileByItsLeadingBytes()
    {
        FakeWeatherProduct.Write(_dir);
        using var source = FileSystemAssetSource.Create(_dir);

        Assert.Equal(
            "GRIB2",
            DatasetPipelineFactory.DetectProductSpecFromSource(
                source, "gfs.t06z.pgrb2.0p25.f000-filter", RegistryWithFake()));
    }

    [Fact]
    public void MatchBytes_SeesTheLeadingBytesAndTheExtension()
    {
        var path = FakeWeatherProduct.Write(_dir, "wind.grib2");
        DatasetHeader? seen = null;
        var registry = new ProductRegistry();
        registry.Register(FakeWeatherProduct.Registration() with
        {
            MatchBytes = header =>
            {
                seen = header;
                return false;
            },
        });

        DatasetPipelineFactory.DetectProductSpec(path, registry);

        Assert.NotNull(seen);
        Assert.Equal(".grib2", seen.Value.Extension);
        Assert.Equal(64, seen.Value.LeadingBytes.Length);
        Assert.True(seen.Value.StartsWithAscii("GRIB"));
        Assert.False(seen.Value.IsHdf5);
    }

    [Fact]
    public void MatchBytes_ThatThrows_DoesNotBreakDetection()
    {
        var path = FakeWeatherProduct.Write(_dir);
        var registry = new ProductRegistry();
        registry.Register(FakeWeatherProduct.Registration() with
        {
            Spec = "NetCDF",
            MatchBytes = static _ => throw new InvalidOperationException("faulty matcher"),
        });
        registry.Register(FakeWeatherProduct.Registration());

        Assert.Equal("GRIB2", DatasetPipelineFactory.DetectProductSpec(path, registry));
    }

    [Fact]
    public void Factory_ConstructsTheRegisteredProcessor()
    {
        var path = FakeWeatherProduct.Write(_dir);

        var processor = CreateFactory(RegistryWithFake()).CreateProcessor(path);

        var fake = Assert.IsType<FakeWeatherProduct.Processor>(processor);
        Assert.Equal("GRIB2", fake.Spec.Name);
        Assert.Equal(new SpecVersion(2, 0, 0), fake.Spec.Edition);
    }

    [Fact]
    public async Task Processor_Renders()
    {
        var path = FakeWeatherProduct.Write(_dir);
        var processor = (IVectorPortrayalSource)CreateFactory(RegistryWithFake()).CreateProcessor(path);

        var result = await processor.BuildVectorPortrayalAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        using var bitmap = new HeadlessCompositor(new ProjNetCrsTransformFactory()).Render(
            [HeadlessCompositeInput.ForVector(result)],
            new HeadlessCompositeOptions { Width = 64, Height = 64 });

        Assert.Equal(SKColor.Parse(FakeWeatherProduct.FillHex), bitmap.GetPixel(32, 32));
    }

    [Fact]
    public void Projector_OpensTheRegisteredPayload()
    {
        var path = FakeWeatherProduct.Write(_dir);
        using var stream = File.OpenRead(path);

        var loaded = LoadedDatasetProjector.Project(
            new DatasetId("fake"), "GRIB2", stream, registry: RegistryWithFake());

        Assert.NotNull(loaded);
        Assert.Equal(FakeWeatherProduct.SpecRef, loaded.Spec);
        Assert.Equal(FakeWeatherProduct.Bounds, loaded.Bounds);
        Assert.IsType<FakeWeatherProduct.Data>(loaded.Data);
    }

    [Fact]
    public void Projector_WithoutTheRegistration_ReturnsNull()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        Assert.Null(LoadedDatasetProjector.Project(
            new DatasetId("fake"), "GRIB2", stream, registry: S100Products.CreateDefaultRegistry()));
    }

    [Fact]
    public void Traits_ComeFromTheRegistration()
    {
        var registry = RegistryWithFake();

        Assert.False(registry.IsS100("GRIB2"));
        Assert.Equal("GRIB2 Ed 2", registry.PortrayalOf("grib2").AsApplication?.Basis);
        Assert.Equal(S98DisplayPlane.OnDemandSurface, registry.DisplayPlanes.GetDefaultPlane("GRIB2", "wind"));
        Assert.Equal("GRIB2", registry.PortrayalSpecName("GRIB2"));
    }

    [Fact]
    public void Hdf5_FileNoProductClaims_IsUnknownNotS102()
    {
        // An HDF5 file with no S-100 productSpecification: it used to be
        // routed to S-102; now nobody claims it. (One with a CF Conventions
        // attribute is NetCDF-4, issue #1071; see NetCdfProcessorTests.)
        var path = Path.Combine(_dir, "wave.h5");
        new H5File { Attributes = new() { ["title"] = "not a product" } }.Write(path);

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path));
        using var source = FileSystemAssetSource.Create(_dir);
        Assert.Null(DatasetPipelineFactory.DetectProductSpecFromSource(source, "wave.h5"));
    }

    [Fact]
    public void Hdf5_FileWithCfConventions_IsNetCdf()
    {
        var path = Path.Combine(_dir, "wave.h5");
        new H5File { Attributes = new() { ["Conventions"] = "CF-1.8" } }.Write(path);

        Assert.Equal("NetCDF", DatasetPipelineFactory.DetectProductSpec(path));
        using var source = FileSystemAssetSource.Create(_dir);
        Assert.Equal("NetCDF", DatasetPipelineFactory.DetectProductSpecFromSource(source, "wave.h5"));
    }

    [Fact]
    public void Hdf5_RootAttributes_ReachTheMatcher()
    {
        // A NetCDF-4 product can claim an HDF5-extension file by its CF
        // Conventions attribute (design note §3.2), through MatchHdf5.
        var path = Path.Combine(_dir, "wave.h5");
        new H5File { Attributes = new() { ["Conventions"] = "CF-1.8" } }.Write(path);
        var registry = S100Products.CreateDefaultRegistry();
        registry.Register(FakeWeatherProduct.Registration() with
        {
            Spec = "NetCDF",
            MatchBytes = null,
            MatchHdf5 = static root => root.ProductSpecification.Length == 0
                && root.ReadStringAttribute("Conventions")?.StartsWith("CF-", StringComparison.Ordinal) == true,
        });

        Assert.Equal("NetCDF", DatasetPipelineFactory.DetectProductSpec(path, registry));
    }

    [Fact]
    public void Hdf5_WithAnUnknownExtension_IsSniffedByItsSignature()
    {
        var path = Path.Combine(_dir, "102TEST");
        new H5File { Attributes = new() { ["productSpecification"] = "INT.IHO.S-102.2.2" } }.Write(path);

        Assert.Equal("S-102", DatasetPipelineFactory.DetectProductSpec(path));
    }

    [Theory]
    [InlineData("INT.IHO.S-102.2.2", "S-102")]
    [InlineData("INT.IHO.S-104.2.0", "S-104")]
    [InlineData("INT.IHO.S-111.2.0", "S-111")]
    [InlineData("S-104", "S-104")]
    [InlineData("INT.IHO.S-999.1.0", null)]
    public void Hdf5_ProductSpecification_SelectsTheProduct(string productSpecification, string? expected)
    {
        var path = Path.Combine(_dir, "coverage.h5");
        new H5File { Attributes = new() { ["productSpecification"] = productSpecification } }.Write(path);

        Assert.Equal(expected, DatasetPipelineFactory.DetectProductSpec(path));
    }

    [Fact]
    public void Iso8211UpdateFile_IsNotSniffedAsADataset()
    {
        // Only the HDF5 signature is sniffed, so an ISO 8211 update (.001) is
        // still not reported as a product of its own.
        var path = SyntheticIso8211Cell.Write(_dir, "101AA00DS.001", "INT.IHO.S-101.1.0.2");

        Assert.Null(DatasetPipelineFactory.DetectProductSpec(path));
    }

    private static DatasetPipelineFactory CreateFactory(ProductRegistry registry) =>
        new(
            new PortrayalCatalogueManager(),
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(),
            new DisplayPlaneAuthorityProvider(registry),
            productRegistry: registry);
}
