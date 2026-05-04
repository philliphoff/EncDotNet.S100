using System.Collections.Generic;
using System.ComponentModel;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

public class DatasetEntryDisplayStateTests
{
    [Fact]
    public void Defaults_AreVisibleAndFullyOpaque()
    {
        var entry = new DatasetEntry("/tmp/x.000", "S-101");

        Assert.True(entry.IsVisible);
        Assert.Equal(1.0, entry.Opacity);
        Assert.Equal(1.0, entry.RowOpacity);
    }

    [Fact]
    public void TogglingVisibility_RaisesPropertyChanged_AndDimsRow()
    {
        var entry = new DatasetEntry("/tmp/x.000", "S-101");
        var observed = new List<string?>();
        entry.PropertyChanged += (_, e) => observed.Add(e.PropertyName);

        entry.IsVisible = false;

        Assert.False(entry.IsVisible);
        Assert.Equal(0.5, entry.RowOpacity);
        Assert.Contains(nameof(DatasetEntry.IsVisible), observed);
        Assert.Contains(nameof(DatasetEntry.RowOpacity), observed);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.42, 0.42)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 1.0)]
    public void Opacity_ClampsToUnitInterval(double input, double expected)
    {
        var entry = new DatasetEntry("/tmp/x.000", "S-101")
        {
            Opacity = input,
        };

        Assert.Equal(expected, entry.Opacity);
    }

    [Fact]
    public void OpacityChange_RaisesPropertyChanged()
    {
        var entry = new DatasetEntry("/tmp/x.000", "S-101");
        var observed = new List<string?>();
        entry.PropertyChanged += (_, e) => observed.Add(e.PropertyName);

        entry.Opacity = 0.3;

        Assert.Contains(nameof(DatasetEntry.Opacity), observed);
    }
}
