using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for the product traits on <see cref="ProductRegistration"/> and the
/// <see cref="ProductRegistry"/> look-ups that replaced the spec-name switches
/// (issue #1061): the portrayal spec (S-57 → S-101), the portrayal basis
/// (S-104's application portrayal), <see cref="ProductRegistration.IsS100"/>
/// and the default S-98 planes, plus the <see cref="IDatasetProcessor.PortrayalSpec"/>
/// default member.
/// </summary>
public class ProductTraitsTests
{
    private static ProductRegistration Fake(string spec) => new()
    {
        Spec = spec,
        CreateFromPath = (_, _) => null!,
        CreateFromSource = (_, _) => null!,
    };

    [Theory]
    [InlineData("S-57", "S-101")]
    [InlineData("s-57", "S-101")]
    [InlineData("S-101", "S-101")]
    [InlineData("S-102", "S-102")]
    [InlineData("S-124", "S-124")]
    [InlineData("S-999", "S-999")]
    public void PortrayalSpecName_MapsS57ToS101_IdentityOtherwise(string product, string expected)
    {
        Assert.Equal(expected, ProductRegistry.Default.PortrayalSpecName(product));
    }

    [Fact]
    public void DisplayPlaneAuthorityProvider_CanBeCreatedByReflection()
    {
        // The perf runner's version probe builds the factory reflectively
        // (tools/EncDotNet.S100.PerfRunner/SharedInfrastructure.cs), so the
        // provider needs a real parameterless constructor.
        var provider = Assert.IsType<DisplayPlaneAuthorityProvider>(
            Activator.CreateInstance(typeof(DisplayPlaneAuthorityProvider)));

        Assert.Same(ProductRegistry.Default.DisplayPlanes, provider.Current);
    }

    [Fact]
    public void PortrayalSpecName_NullProduct_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ProductRegistry.Default.PortrayalSpecName(null!));
    }

    [Fact]
    public void PortrayalSpecFor_S57_ReturnsS101()
    {
        var portrayal = ProductRegistry.Default.PortrayalSpecFor(new SpecRef("S-57", default));
        Assert.Equal("S-101", portrayal.Name);
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-102")]
    [InlineData("S-124")]
    public void PortrayalSpecFor_NonS57_IsIdentity(string product)
    {
        var spec = new SpecRef(product, default);
        Assert.Equal(spec.Name, ProductRegistry.Default.PortrayalSpecFor(spec).Name);
    }

    [Fact]
    public void PortrayalSpec_ComesFromTheRegistration()
    {
        // A host's own translated product names its portrayal spec on the
        // registration; nothing else needs to know.
        var registry = new ProductRegistry();
        registry.Register(Fake("S-999") with { PortrayalSpec = "S-101" });

        Assert.Equal("S-101", registry.PortrayalSpecName("S-999"));
        Assert.Equal("S-102", registry.PortrayalSpecName("S-102"));
    }

    [Fact]
    public void ProcessorPortrayalSpec_Default_IsTheProcessorsSpec()
    {
        // The default member no longer maps names; the S-57 processor overrides
        // it (S57DatasetProcessor.PortrayalSpec), so a stub keeps its own spec.
        IDatasetProcessor s102 = new StubProcessor(new SpecRef("S-102", default));
        IDatasetProcessor s57 = new StubProcessor(new SpecRef("S-57", default));

        Assert.Equal("S-102", s102.PortrayalSpec.Name);
        Assert.Equal("S-57", s57.PortrayalSpec.Name);
    }

    [Fact]
    public void PortrayalOf_S104_IsTheApplicationPortrayal()
    {
        var portrayal = ProductRegistry.Default.PortrayalOf("S-104").AsApplication;

        Assert.NotNull(portrayal);
        Assert.Same(S100Products.S104Portrayal, ProductRegistry.Default.PortrayalOf("S-104"));
        Assert.Equal("S-104 Ed 2.0.0", portrayal.Basis);
        Assert.Equal("SoundCharts portrayal: S-104 Ed 2.0.0 defines no symbology", portrayal.Note);
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-102")]
    [InlineData("S-111")]
    [InlineData("S-57")]
    [InlineData("S-999")]
    [InlineData(null)]
    public void PortrayalOf_CatalogueProducts_IsCatalogue(string? product)
    {
        var portrayal = ProductRegistry.Default.PortrayalOf(product);

        Assert.Same(PortrayalBasis.Catalogue, portrayal);
        Assert.Null(portrayal.AsApplication);
    }

    [Fact]
    public void BuiltInProducts_AreAllS100()
    {
        Assert.All(S100Products.All, r => Assert.True(r.IsS100, r.Spec));
        Assert.All(S100Products.All, r => Assert.True(ProductRegistry.Default.IsS100(r.Spec), r.Spec));
    }

    [Theory]
    [InlineData("GRIB2")]
    [InlineData("netcdf")]
    [InlineData("CAP")]
    public void IsS100_UnregisteredFormatName_IsFalse(string spec)
    {
        Assert.False(new ProductRegistry().IsS100(spec));
    }

    [Fact]
    public void IsS100_ComesFromTheRegistration()
    {
        var registry = new ProductRegistry();
        registry.Register(Fake("grib2") with { IsS100 = false });

        Assert.False(registry.IsS100("GRIB2"));
        Assert.True(registry.IsS100("S-999"));
        // The format name is canonicalized on the way in.
        Assert.Contains("GRIB2", registry.RegisteredSpecs);
    }

    [Fact]
    public void Registration_Defaults_DescribeAnS100CatalogueProduct()
    {
        var registration = Fake("S-999");

        Assert.True(registration.IsS100);
        Assert.Same(PortrayalBasis.Catalogue, registration.Portrayal);
        Assert.Null(registration.PortrayalSpec);
        Assert.Null(registration.ReadMetadata);
        Assert.Null(registration.OpenData);
        Assert.Null(registration.PickReportNotice);
        Assert.Null(registration.MatchBytes);
        Assert.Empty(registration.DefaultPlanes);
        Assert.Null(registration.DefaultPlaneFor("area"));
    }

    [Fact]
    public void DefaultPlanes_ComeFromTheRegistration()
    {
        var registry = new ProductRegistry();
        registry.Register(Fake("GRIB2") with
        {
            DefaultPlanes = new Dictionary<string, S98DisplayPlane>
            {
                ["Wind"] = S98DisplayPlane.DynamicArrows,
                [ProductRegistration.AnyLayerKind] = S98DisplayPlane.OnDemandSurface,
            },
        });

        // Layer kinds match case-insensitively, even with the default comparer.
        Assert.Equal(S98DisplayPlane.DynamicArrows, registry.DisplayPlanes.GetDefaultPlane("GRIB2", "wind"));
        Assert.Equal(S98DisplayPlane.OnDemandSurface, registry.DisplayPlanes.GetDefaultPlane("GRIB2", "waves"));
        Assert.Equal(S98DisplayPlane.OnDemandSurface, registry.DisplayPlanes.GetDefaultPlane("GRIB2"));
        // An unregistered product lands on the catch-all overlay plane.
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, registry.DisplayPlanes.GetDefaultPlane("S-101", "area"));
    }

    [Fact]
    public void DisplayPlaneAuthorityProvider_ServesTheRegistrysPlanes()
    {
        var registry = new ProductRegistry();
        registry.Register(Fake("GRIB2") with
        {
            DefaultPlanes = new Dictionary<string, S98DisplayPlane>
            {
                [ProductRegistration.AnyLayerKind] = S98DisplayPlane.OnDemandSurface,
            },
        });

        Assert.Equal(
            S98DisplayPlane.OnDemandSurface,
            new DisplayPlaneAuthorityProvider(registry).Current.GetDefaultPlane("GRIB2"));
        Assert.Equal(
            S98DisplayPlane.Bathymetry,
            new DisplayPlaneAuthorityProvider().Current.GetDefaultPlane("S-102"));
    }

    [Fact]
    public void InteroperabilityAuthority_WithoutATable_PutsEveryProductOnTheOverlayPlane()
    {
        // A renderer with no product registry in reach (Renderers.Mapsui's
        // fallback) knows no planes; sub-layers carry their own.
        var authority = new InteroperabilityAuthority();

        Assert.Equal(S98DisplayPlane.OtherChartOverlays, authority.GetDefaultPlane("S-101", "area"));
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, authority.GetDefaultPlane("S-102"));
    }

    [Fact]
    public void Unregister_RemovesTheProduct()
    {
        var registry = new ProductRegistry();
        registry.Register(Fake("GRIB2"));

        Assert.True(registry.Unregister("grib2"));
        Assert.False(registry.IsRegistered("GRIB2"));
        Assert.False(registry.Unregister("GRIB2"));
    }

    private sealed class StubProcessor(SpecRef spec) : IDatasetProcessor
    {
        public SpecRef Spec { get; } = spec;

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
    }
}
