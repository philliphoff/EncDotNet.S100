using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests covering the PR-L3 in-memory Active flag contract on
/// <see cref="LoadedDatasetInfo"/> and via the
/// <see cref="IDatasetLoaderService"/> abstraction.
/// </summary>
public class ActiveFlagTests
{
    [Fact]
    public void LoadedDatasetInfo_Active_RoundTripsTrue()
    {
        var info = new LoadedDatasetInfo("a.000", "S-101", Active: true);
        Assert.True(info.Active);
    }

    [Fact]
    public void LoadedDatasetInfo_Active_RoundTripsFalse()
    {
        var info = new LoadedDatasetInfo("b.h5", "S-102", Active: false);
        Assert.False(info.Active);
    }

    [Fact]
    public void LoaderStub_GetActive_DefaultsToTrue_ForUnknownId()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();
        Assert.True(loader.GetActive("never-seen-before"));
    }

    [Fact]
    public void LoaderStub_SetActive_FiresActiveChanged()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();
        string? observed = null;
        loader.ActiveChanged += id => observed = id;

        loader.SetActive("a.000", false);

        Assert.Equal("a.000", observed);
        Assert.False(loader.GetActive("a.000"));
    }
}
