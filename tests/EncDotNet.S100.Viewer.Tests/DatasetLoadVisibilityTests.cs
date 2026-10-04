using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// An S-128 catalogue bundled in an exchange set loads hidden: the IHO
/// portrayal fills each product's coverage 70 % opaque above the ENC, and
/// nested products stacked to near-opaque yellow over the whole chart.
/// A gridded S-104 surface loads hidden wherever it comes from (issue #483).
/// </summary>
public sealed class DatasetLoadVisibilityTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("s128-visibility-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void S128_catalogue_from_an_exchange_set_loads_hidden()
    {
        Assert.True(DatasetLoadVisibility.LoadsHidden(
            CreateS128Processor(), fromExchangeSet: true, wasKnownBySession: false));
    }

    [Fact]
    public void S128_catalogue_opened_on_its_own_is_shown()
    {
        Assert.False(DatasetLoadVisibility.LoadsHidden(
            CreateS128Processor(), fromExchangeSet: false, wasKnownBySession: false));
    }

    [Fact]
    public void S128_catalogue_reloaded_by_the_session_keeps_the_users_choice()
    {
        Assert.False(DatasetLoadVisibility.LoadsHidden(
            CreateS128Processor(), fromExchangeSet: true, wasKnownBySession: true));
    }

    [Fact]
    public void S104_gridded_surface_loads_hidden_even_when_opened_on_its_own()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "S104", "104US004SC1BO_20251217T12Z.h5");
        var processor = new S104DatasetProcessor(path, new ProjNetCrsTransformFactory());

        Assert.True(DatasetLoadVisibility.LoadsHidden(processor, fromExchangeSet: false, wasKnownBySession: false));
        Assert.False(DatasetLoadVisibility.LoadsHidden(processor, fromExchangeSet: false, wasKnownBySession: true));
    }

    private S128DatasetProcessor CreateS128Processor()
    {
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-128", Specification.CreatePortrayalCatalogueSource("S-128"));
        return new S128DatasetProcessor(
            SyntheticS128Catalogue.Write(_directory), catalogues, new DisplayPlaneAuthorityProvider());
    }
}
