using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Datasets.Cap.Tests;

/// <summary>Issue #1069: the code-defined CAP drawing, hatched and outlined by severity and labelled with the event.</summary>
public sealed class CapStyleTests
{
    [Fact]
    public void Each_area_is_hatched_outlined_and_labelled()
    {
        var area = CapWarningSet.Build(CapReader.Read(CapFixtures.Fixture(CapFixtures.GeoJson))).Areas[0];

        var instructions = CapStyle.BuildInstructions([area]);

        Assert.Equal(3, instructions.Count);
        Assert.All(instructions, i =>
        {
            Assert.Equal(area.Id, i.FeatureReference);
            Assert.Equal(CapStyle.ViewingGroup, i.ViewingGroup);
            Assert.Equal(10 + (int)CapSeverity.Severe, i.DrawingPriority);
        });

        var fill = Assert.IsType<AreaInstruction>(instructions[0]);
        Assert.Equal("CAPHATCHSEV", fill.AreaFillReference);
        var line = Assert.IsType<LineInstruction>(instructions[1]);
        Assert.Equal("CAPSEV", line.LineColor);
        Assert.Equal(0.7, line.LineWidth);
        Assert.Equal(0, line.DashIntervalLength);
        var label = Assert.IsType<TextInstruction>(instructions[2]);
        Assert.Equal("Special Marine Warning", label.Text);
        Assert.Equal(CapStyle.LabelScaleMinimum, label.ScaleMinimum);
        Assert.Null(fill.ScaleMinimum);
        Assert.Equal(CapStyle.HaloToken, label.HaloColor);
    }

    [Theory]
    [InlineData(CapSeverity.Extreme, "CAPEXT", "CAPHATCHEXT", 2.0)]
    [InlineData(CapSeverity.Severe, "CAPSEV", "CAPHATCHSEV", 2.5)]
    [InlineData(CapSeverity.Moderate, "CAPMOD", "CAPHATCHMOD", 4.0)]
    [InlineData(CapSeverity.Minor, "CAPMIN", "CAPHATCHMIN", 6.0)]
    [InlineData(CapSeverity.Unknown, "CAPUNK", "CAPHATCHUNK", 6.0)]
    public void Each_severity_has_its_ink_and_hatching(CapSeverity severity, string token, string fillName, double spacing)
    {
        Assert.Equal(token, CapStyle.ColorToken(severity));
        Assert.Equal(fillName, CapStyle.AreaFillName(severity));

        var fill = CapStyle.ResolveAreaFill(fillName);
        Assert.NotNull(fill);
        Assert.Equal(fillName, fill.PatternSymbol);
        Assert.Equal((spacing, 0, 0, spacing), (fill.V1X, fill.V1Y, fill.V2X, fill.V2Y));

        var svg = CapStyle.Symbols.GetSymbol(fillName);
        Assert.NotNull(svg);
        Assert.Contains($"class=\"s{token}\"", svg);
        Assert.Contains($"width=\"{spacing:g}mm\"", svg);

        foreach (var palette in new[] { PaletteType.Day, PaletteType.Dusk, PaletteType.Night })
            Assert.True(CapStyle.Palette.GetColorPalette(palette).Colors.ContainsKey(token));
    }

    [Fact]
    public void An_unknown_severity_is_dashed_and_more_severe_areas_draw_above()
    {
        var set = CapWarningSet.Build(CapReader.Read(CapFixtures.Fixture(CapFixtures.Circle)));
        var unknown = set.Areas[1] with { Info = set.Areas[1].Info with { Severity = CapSeverity.Unknown } };

        var instructions = CapStyle.BuildInstructions([set.Areas[0], unknown]);

        var dashed = instructions.OfType<LineInstruction>().Single(l => l.FeatureReference == unknown.Id);
        Assert.True(dashed.DashIntervalLength > 0);
        Assert.True(
            instructions.First(i => i.FeatureReference == set.Areas[0].Id).DrawingPriority
            > instructions.First(i => i.FeatureReference == unknown.Id).DrawingPriority);
    }

    [Fact]
    public void Areas_with_no_outline_draw_nothing()
    {
        var area = CapWarningSet.Build(CapReader.Read(CapFixtures.Fixture(CapFixtures.GaleWarning))).Areas[0];

        Assert.Empty(CapStyle.BuildInstructions([area with { Surfaces = [] }]));
    }

    [Fact]
    public void Night_colours_stay_dim()
    {
        foreach (var (token, colors) in CapStyle.Palette.Tokens)
        {
            if (token == CapStyle.HaloToken)
                continue;
            var c = colors.Night;
            double Linear(byte v) => v / 255.0 <= 0.04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4);
            var luminance = 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
            Assert.True(luminance < 0.3, $"{token} night luminance {luminance:0.00}");
        }
    }

    [Fact]
    public void The_cache_scope_names_the_style_and_version() =>
        Assert.StartsWith($"app:CAP@{CapStyle.Version}|toolkit:", CapStyle.ContentScope);
}
