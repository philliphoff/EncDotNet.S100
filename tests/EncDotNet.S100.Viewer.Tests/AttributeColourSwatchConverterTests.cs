using System.Globalization;
using Avalonia.Media;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Tests;

public class AttributeColourSwatchConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static PickAttribute Attr(string code, string value, string? name = null) =>
        new() { Code = code, Name = name, RawValue = value, DisplayValue = null, Children = [] };

    private static object? Brush(PickAttribute a) =>
        AttributeColourSwatchConverter.Instance.Convert(a, typeof(IBrush), null, Culture);

    private static object? Visible(PickAttribute a) =>
        AttributeColourSwatchConverter.Instance.Convert(a, typeof(bool), "visible", Culture);

    [Fact]
    public void ColourAttribute_KnownColour_ProducesBrushAndVisible()
    {
        var attr = Attr("colour", "Red");
        var brush = Assert.IsType<SolidColorBrush>(Brush(attr));
        Assert.Equal(Color.FromRgb(0xD0, 0x21, 0x21), brush.Color);
        Assert.True((bool)Visible(attr)!);
    }

    [Fact]
    public void ColourAttribute_CompoundValue_UsesFirstRecognisedColour()
    {
        var attr = Attr("colour", "Red;White");
        var brush = Assert.IsType<SolidColorBrush>(Brush(attr));
        Assert.Equal(Color.FromRgb(0xD0, 0x21, 0x21), brush.Color);
    }

    [Fact]
    public void NonColourAttribute_EvenIfValueIsColourWord_IsNotSwatched()
    {
        var attr = Attr("category", "Red");
        Assert.Same(Brushes.Transparent, Brush(attr));
        Assert.False((bool)Visible(attr)!);
    }

    [Fact]
    public void ColourAttribute_UnknownColourWord_IsNotVisible()
    {
        var attr = Attr("colour", "Mauve");
        Assert.False((bool)Visible(attr)!);
    }

    [Fact]
    public void ColourDetected_ViaName_WhenCodeIsOpaque()
    {
        var attr = Attr("COLOUR", "Green", name: "Colour");
        Assert.True((bool)Visible(attr)!);
    }
}
