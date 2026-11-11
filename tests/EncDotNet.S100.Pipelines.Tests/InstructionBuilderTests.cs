using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="InstructionBuilder"/>, the drawing instructions of a
/// portrayal defined in code (#1066).
/// </summary>
public sealed class InstructionBuilderTests
{
    private static readonly InstructionBuilder Builder = new()
    {
        ViewingGroup = 31050,
        DrawingPriority = 15,
        Plane = DisplayPlane.OverRadar,
        ScaleMinimum = 90_000,
        ScaleMaximum = 5_000,
    };

    [Fact]
    public void Every_kind_carries_the_viewing_group_priority_plane_and_scale_limits()
    {
        DrawingInstruction[] instructions =
        [
            Builder.Point("f1", "SYM01"),
            Builder.Text("f1", "label", new TextInstructionStyle()),
            Builder.Line("f1", "LINK", 0.32),
            Builder.Area("f1", "AFILL"),
        ];

        Assert.All(instructions, instruction =>
        {
            Assert.Equal("f1", instruction.FeatureReference);
            Assert.Equal(31050, instruction.ViewingGroup);
            Assert.Empty(instruction.AdditionalViewingGroups);
            Assert.Equal(15, instruction.DrawingPriority);
            Assert.Equal(DisplayPlane.OverRadar, instruction.Plane);
            Assert.Equal(90_000, instruction.ScaleMinimum);
            Assert.Equal(5_000, instruction.ScaleMaximum);
        });
    }

    [Fact]
    public void Defaults_match_a_bare_instruction()
    {
        var built = new InstructionBuilder().Point("f", "SYM");
        var bare = new PointInstruction { FeatureReference = "f", SymbolReference = "SYM" };

        Assert.Equal(
            Vector.Caching.DrawingInstructionSerializer.Serialize([bare]),
            Vector.Caching.DrawingInstructionSerializer.Serialize([built]));
    }

    [Fact]
    public void A_with_expression_derives_a_variant_and_leaves_the_original()
    {
        var labels = Builder with { ScaleMinimum = null };

        Assert.Null(labels.Text("f", "x", new TextInstructionStyle()).ScaleMinimum);
        Assert.Equal(90_000, Builder.Point("f", "SYM").ScaleMinimum);
        Assert.Equal(31050, labels.Point("f", "SYM").ViewingGroup);
    }

    [Fact]
    public void Point_carries_the_symbol_and_rotation()
    {
        var point = Builder.Point("f", "ARROW", rotation: 45, rotationCrs: SymbolRotationCrs.Geographic);

        Assert.Equal("ARROW", point.SymbolReference);
        Assert.Equal(45, point.Rotation);
        Assert.Equal(SymbolRotationCrs.Geographic, point.RotationCrs);
        Assert.Null(Builder.Point("f", "ARROW").Rotation);
    }

    [Fact]
    public void Text_carries_the_style()
    {
        var style = new TextInstructionStyle
        {
            FontSize = 11,
            FontColor = "TINK",
            FontWeight = TextFontWeight.Bold,
            FontProportion = TextFontProportion.MonoSpaced,
            HaloColor = "THALO",
            HaloWidth = 0.96,
            HorizontalAlignment = TextHorizontalAlignment.Start,
            VerticalAlignment = TextVerticalAlignment.Top,
            OffsetX = 4.16,
            OffsetY = -1,
        };

        var text = Builder.Text("f", "+1.42", style);

        Assert.Equal("+1.42", text.Text);
        Assert.Equal(11, text.FontSize);
        Assert.Equal("TINK", text.FontColor);
        Assert.Equal(TextFontWeight.Bold, text.FontWeight);
        Assert.Equal(TextFontProportion.MonoSpaced, text.FontProportion);
        Assert.Equal("THALO", text.HaloColor);
        Assert.Equal(0.96, text.HaloWidth);
        Assert.Equal(TextHorizontalAlignment.Start, text.HorizontalAlignment);
        Assert.Equal(TextVerticalAlignment.Top, text.VerticalAlignment);
        Assert.Equal(4.16, text.OffsetX);
        Assert.Equal(-1, text.OffsetY);
    }

    [Fact]
    public void Text_style_defaults_match_a_bare_text_instruction()
    {
        var built = new InstructionBuilder().Text("f", "x", new TextInstructionStyle());
        var bare = new TextInstruction { FeatureReference = "f", Text = "x" };

        Assert.Equal(
            Vector.Caching.DrawingInstructionSerializer.Serialize([bare]),
            Vector.Caching.DrawingInstructionSerializer.Serialize([built]));
    }

    [Fact]
    public void Line_is_a_simple_line_solid_or_dashed()
    {
        var solid = Builder.Line("f", "LINK", 0.32);
        Assert.Equal(LineInstruction.SimpleLineStyleReference, solid.LineStyleReference);
        Assert.Equal("LINK", solid.LineColor);
        Assert.Equal(0.32, solid.LineWidth);
        Assert.Null(solid.Dashes);
        Assert.Equal(0, solid.DashIntervalLength);

        var dashed = Builder.Line("f", "LINK", 0.32, dashLength: 1.9, gapLength: 0.96);
        // A 1.9 mm dash at the start of each 2.86 mm interval (#1141).
        Assert.Equal(new[] { (Offset: 0.0, Length: 1.9) }, dashed.Dashes!);
        Assert.Equal(2.86, dashed.DashIntervalLength, 6);
    }

    [Fact]
    public void Area_is_a_colour_fill()
    {
        var area = Builder.Area("f", "AFILL", transparency: 0.75);

        Assert.Equal("AFILL", area.FillColor);
        Assert.Equal(0.75, area.Transparency);
        Assert.Null(area.AreaFillReference);
        Assert.Throws<ArgumentOutOfRangeException>(() => Builder.Area("f", "AFILL", transparency: 1.5));
    }

    [Fact]
    public void Missing_references_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Builder.Point("", "SYM"));
        Assert.Throws<ArgumentException>(() => Builder.Point("f", ""));
        Assert.Throws<ArgumentException>(() => Builder.Line("f", "", 0.32));
        Assert.Throws<ArgumentOutOfRangeException>(() => Builder.Line("f", "LINK", -1));
    }
}
