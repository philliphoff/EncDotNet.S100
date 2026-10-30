using Avalonia.Controls;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// An S-101 complex attribute can hold further complex attributes (a tidal
/// stream panel's hourly values, #1156); the attribute table shows them at
/// every depth.
/// </summary>
public sealed class PickReportNestedAttributesViewTests
{
    private static PickAttribute Leaf(string code, string value) => new() { Code = code, RawValue = value };

    private static PickAttribute Complex(string code, params PickAttribute[] children) =>
        new() { Code = code, RawValue = string.Empty, Children = children };

    [AvaloniaFact]
    public void Nested_complex_attributes_show_every_leaf()
    {
        var panel = Complex(
            "tidalStreamPanelValues",
            Complex("tidalStreamValue", Leaf("speedMaximum", "1.5"), Leaf("orientationValue", "10")),
            Complex("tidalStreamValue", Leaf("speedMaximum", "2.25"), Leaf("orientationValue", "190")));
        var pick = new PickReportViewModel();
        pick.SetPicks([
            new PickHit
            {
                FeatureType = "TidalStreamPanelData",
                FeatureRef = "1",
                DatasetFileName = "101AA.000",
                ProductSpec = "S-101",
                Attributes = [panel],
            },
        ]);

        using var host = ViewHost.Show(new PickReportView { DataContext = pick }, width: 420, height: 900);
        var texts = host.All<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

        Assert.Contains("2.25", texts);
        Assert.Contains("190", texts);
        Assert.Equal(4, pick.AttributeCount);
    }
}
