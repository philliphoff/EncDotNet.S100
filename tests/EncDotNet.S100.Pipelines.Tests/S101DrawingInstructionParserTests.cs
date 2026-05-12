using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests covering the wiring between the S-101 Lua-emitted instruction
/// string syntax and the unified <see cref="DrawingInstruction"/> hierarchy.
/// </summary>
public class S101DrawingInstructionParserTests
{
    [Fact]
    public void TextInstruction_PicksUpHorizontalAndVerticalAlignment()
    {
        const string s = "TextAlignHorizontal:End;TextAlignVertical:Top;FontColor:CHBLK;TextInstruction:LABEL";

        var parsed = DrawingInstructionParser.Parse("F1", s);

        var text = Assert.Single(parsed.OfType<TextInstruction>());
        Assert.Equal(TextHorizontalAlignment.End, text.HorizontalAlignment);
        Assert.Equal(TextVerticalAlignment.Top, text.VerticalAlignment);
        Assert.Equal("LABEL", text.Text);
    }

    [Fact]
    public void TextInstruction_PicksUpLocalOffsetAsMmOffset()
    {
        const string s = "LocalOffset:3.51,-3.51;TextAlignHorizontal:Center;TextInstruction:OBJNAM";

        var parsed = DrawingInstructionParser.Parse("F1", s);

        var text = Assert.Single(parsed.OfType<TextInstruction>());
        Assert.Equal(3.51, text.OffsetXmm);
        Assert.Equal(-3.51, text.OffsetYmm);
        Assert.Equal(TextHorizontalAlignment.Center, text.HorizontalAlignment);
    }

    [Fact]
    public void TextInstruction_AlignmentStateResetsBetweenInstructions()
    {
        // Two labels in one stream — the second deliberately specifies no
        // alignment, so it must not inherit "End" from the first.
        const string s =
            "TextAlignHorizontal:End;TextInstruction:FIRST;" +
            "TextInstruction:SECOND";

        var parsed = DrawingInstructionParser.Parse("F1", s);

        var texts = parsed.OfType<TextInstruction>().ToList();
        Assert.Equal(2, texts.Count);
        Assert.Equal(TextHorizontalAlignment.End, texts[0].HorizontalAlignment);
        Assert.Equal(TextHorizontalAlignment.Center, texts[1].HorizontalAlignment);
    }

    [Fact]
    public void TextInstruction_NoOffsetEmittedWhenLocalOffsetIsZero()
    {
        const string s = "TextAlignHorizontal:Center;FontColor:CHBLK;TextInstruction:NOOFFSET";

        var parsed = DrawingInstructionParser.Parse("F1", s);

        var text = Assert.Single(parsed.OfType<TextInstruction>());
        Assert.Null(text.OffsetXmm);
        Assert.Null(text.OffsetYmm);
    }

    [Fact]
    public void TextInstruction_FontColorWithTransparencyIsCaptured()
    {
        const string s = "FontColor:CHBLK,0.25;TextInstruction:LBL";

        var parsed = DrawingInstructionParser.Parse("F1", s);

        var text = Assert.Single(parsed.OfType<TextInstruction>());
        Assert.Equal("CHBLK", text.FontColor);
        Assert.Equal(0.25, text.FontTransparency);
    }

    [Fact]
    public void TextInstruction_FontBackgroundColorWithTransparencyIsCaptured()
    {
        const string s = "FontBackgroundColor:CHWHT,0.5;TextInstruction:LBL";

        var parsed = DrawingInstructionParser.Parse("F1", s);

        var text = Assert.Single(parsed.OfType<TextInstruction>());
        Assert.Equal("CHWHT", text.BackgroundColor);
        Assert.Equal(0.5, text.BackgroundTransparency);
    }

    [Fact]
    public void TextInstruction_EmptyFontBackgroundColorClearsInheritedBackground()
    {
        // Per S-101 PortrayalModel.lua label replication, an empty value resets
        // the background to "none".
        const string s =
            "FontBackgroundColor:CHWHT,0.5;TextInstruction:HAS_BG;" +
            "FontBackgroundColor:;TextInstruction:NO_BG";

        var parsed = DrawingInstructionParser.Parse("F1", s);
        var texts = parsed.OfType<TextInstruction>().ToList();

        Assert.Equal(2, texts.Count);
        Assert.Equal("CHWHT", texts[0].BackgroundColor);
        Assert.Null(texts[1].BackgroundColor);
        Assert.Null(texts[1].BackgroundTransparency);
    }

    [Fact]
    public void PointInstruction_PicksUpLocalOffsetAndScaleAndRotation()
    {
        const string s =
            "LocalOffset:1.5,-2.0;ScaleFactor:1.5;Rotation:PortrayalCRS,45;PointInstruction:BOYLAT11";

        var parsed = DrawingInstructionParser.Parse("F1", s);

        var pt = Assert.Single(parsed.OfType<PointInstruction>());
        Assert.Equal(1.5, pt.LocalOffsetX);
        Assert.Equal(-2.0, pt.LocalOffsetY);
        Assert.Equal(1.5, pt.SymbolScale);
        Assert.Equal(45, pt.Rotation);
        Assert.Equal("BOYLAT11", pt.SymbolReference);
    }

    [Fact]
    public void PointInstruction_AugmentedPointGeographicCrsOverridesAnchor()
    {
        // S-101 SOUNDG03 emits AugmentedPoint:GeographicCRS,longitude,latitude
        // before the PointInstruction so each sounding of a MultiPoint feature
        // is anchored at its own coordinate.
        const string s =
            "AugmentedPoint:GeographicCRS,24.5,60.25;PointInstruction:SOUNDG02";

        var parsed = DrawingInstructionParser.Parse("F1", s);
        var pt = Assert.Single(parsed.OfType<PointInstruction>());

        Assert.NotNull(pt.CoordinateOverride);
        Assert.Equal(60.25, pt.CoordinateOverride!.Value.Latitude);
        Assert.Equal(24.5, pt.CoordinateOverride!.Value.Longitude);
    }

    [Fact]
    public void PointInstruction_AugmentedPointResetsBetweenInstructions()
    {
        // SOUNDG03 emits AugmentedPoint per sounding so each PointInstruction
        // gets its own anchor; without a fresh AugmentedPoint, the next
        // emit must NOT inherit the previous override.
        const string s =
            "AugmentedPoint:GeographicCRS,1,2;PointInstruction:A;" +
            "PointInstruction:B";

        var parsed = DrawingInstructionParser.Parse("F1", s).OfType<PointInstruction>().ToList();

        Assert.Equal(2, parsed.Count);
        Assert.NotNull(parsed[0].CoordinateOverride);
        Assert.Null(parsed[1].CoordinateOverride);
    }

    [Fact]
    public void ClearGeometry_ResetsPendingAugmentedAnchor()
    {
        const string s =
            "AugmentedPoint:GeographicCRS,1,2;ClearGeometry;PointInstruction:X";

        var parsed = DrawingInstructionParser.Parse("F1", s);
        var pt = Assert.Single(parsed.OfType<PointInstruction>());

        Assert.Null(pt.CoordinateOverride);
    }

    [Fact]
    public void DisplayPlane_OverRadar_ParsedCorrectly()
    {
        const string s = "ViewingGroup:12210;DrawingPriority:24;DisplayPlane:OverRadar;PointInstruction:BRIDGE01";

        var parsed = DrawingInstructionParser.Parse("F1", s);

        var pt = Assert.Single(parsed.OfType<PointInstruction>());
        Assert.Equal(DisplayPlane.OverRadar, pt.Plane);
    }

    [Fact]
    public void DisplayPlane_ConcatenatedInstructions_PreservePlane()
    {
        // Simulates table.concat of multiple AddInstructions calls — some OverRadar, some UnderRadar
        const string s =
            "ViewingGroup:12210;DrawingPriority:6;DisplayPlane:UnderRadar;LineInstruction:LITARE01;" +
            "ViewingGroup:12210;DrawingPriority:24;DisplayPlane:OverRadar;PointInstruction:BRIDGE01";

        var parsed = DrawingInstructionParser.Parse("F1", s);

        Assert.Equal(2, parsed.Count);
        var line = parsed.OfType<LineInstruction>().Single();
        var point = parsed.OfType<PointInstruction>().Single();
        Assert.Equal(DisplayPlane.UnderRadar, line.Plane);
        Assert.Equal(DisplayPlane.OverRadar, point.Plane);
    }

    [Fact]
    public void DisplayPlane_DefaultsToUnderRadar_WhenNotSpecified()
    {
        const string s = "ViewingGroup:10000;DrawingPriority:1;PointInstruction:DEFAULT";

        var parsed = DrawingInstructionParser.Parse("F1", s);

        var pt = Assert.Single(parsed.OfType<PointInstruction>());
        Assert.Equal(DisplayPlane.UnderRadar, pt.Plane);
    }

    // ── Augmented line geometry tests ──

    [Fact]
    public void AugmentedRay_GeographicCRS_ProducesLineWithCoordinatesOverride()
    {
        // AugmentedRay without AugmentedPath — auto-resolves at LineInstruction.
        const string s =
            "ViewingGroup:27070;DrawingPriority:24;" +
            "AugmentedRay:GeographicCRS,45.0,GeographicCRS,18520;" +
            "LineInstruction:_simple_";

        var anchor = (Latitude: 60.0, Longitude: 25.0);
        var parsed = DrawingInstructionParser.Parse("F1", s, anchor);

        var line = Assert.Single(parsed.OfType<LineInstruction>());
        Assert.NotNull(line.CoordinatesOverride);
        Assert.True(line.CoordinatesOverride!.Count >= 2);

        // First point should be the anchor.
        Assert.Equal(60.0, line.CoordinatesOverride[0].Latitude, precision: 5);
        Assert.Equal(25.0, line.CoordinatesOverride[0].Longitude, precision: 5);
    }

    [Fact]
    public void ArcByRadius_FullCircle_ProducesClosedCoordinatesOverride()
    {
        // All-around light pattern: ArcByRadius with 360° sweep.
        const string s =
            "ViewingGroup:27070;DrawingPriority:24;" +
            "ArcByRadius:0,0,26,0,360;" +
            "AugmentedPath:LocalCRS,LocalCRS,LocalCRS;" +
            "LineInstruction:_simple_";

        var anchor = (Latitude: 51.5, Longitude: -0.1);
        var parsed = DrawingInstructionParser.Parse("F1", s, anchor);

        var line = Assert.Single(parsed.OfType<LineInstruction>());
        Assert.NotNull(line.CoordinatesOverride);
        Assert.True(line.CoordinatesOverride!.Count >= 3);

        // Full circle: first and last points should coincide.
        Assert.Equal(
            line.CoordinatesOverride[0].Latitude,
            line.CoordinatesOverride[^1].Latitude,
            precision: 8);
        Assert.Equal(
            line.CoordinatesOverride[0].Longitude,
            line.CoordinatesOverride[^1].Longitude,
            precision: 8);
    }

    [Fact]
    public void ArcByRadius_PartialArc_ProducesExpectedSweep()
    {
        // Sector arc: 90° sweep from bearing 45°.
        const string s =
            "ViewingGroup:27070;DrawingPriority:24;" +
            "ArcByRadius:0,0,20,45,90;" +
            "AugmentedPath:LocalCRS,LocalCRS,LocalCRS;" +
            "LineInstruction:_simple_";

        var anchor = (Latitude: 0.0, Longitude: 0.0);
        var parsed = DrawingInstructionParser.Parse("F1", s, anchor);

        var line = Assert.Single(parsed.OfType<LineInstruction>());
        Assert.NotNull(line.CoordinatesOverride);
        Assert.True(line.CoordinatesOverride!.Count >= 3);

        // First and last should NOT coincide (partial arc).
        var first = line.CoordinatesOverride[0];
        var last = line.CoordinatesOverride[^1];
        var dist = Math.Sqrt(
            Math.Pow(first.Latitude - last.Latitude, 2) +
            Math.Pow(first.Longitude - last.Longitude, 2));
        Assert.True(dist > 0.0001);
    }

    [Fact]
    public void SectorLight_TwoRaysAndArc_ProducesMultipleLineInstructions()
    {
        // Simulates the LightSectored.lua pattern:
        //   Ray leg 1 → LineInstruction
        //   Ray leg 2 → LineInstruction
        //   ArcByRadius → AugmentedPath → LineInstruction (×2 for outline + fill)
        //   ClearGeometry
        const string s =
            "ViewingGroup:27070;DrawingPriority:24;" +
            "AugmentedRay:GeographicCRS,200,GeographicCRS,18520;" +
            "LineInstruction:_simple_;" +
            "AugmentedRay:GeographicCRS,260,GeographicCRS,18520;" +
            "LineInstruction:_simple_;" +
            "ArcByRadius:0,0,25,200,60;" +
            "AugmentedPath:LocalCRS,GeographicCRS,LocalCRS;" +
            "LineInstruction:_simple_;" +
            "LineInstruction:_simple_;" +
            "ClearGeometry";

        var anchor = (Latitude: 55.0, Longitude: 12.0);
        var parsed = DrawingInstructionParser.Parse("F1", s, anchor);

        var lines = parsed.OfType<LineInstruction>().ToList();
        Assert.Equal(4, lines.Count);

        // Legs 1 & 2: each should have their own ray coordinates.
        Assert.NotNull(lines[0].CoordinatesOverride);
        Assert.NotNull(lines[1].CoordinatesOverride);

        // Arc lines 3 & 4: should share the same augmented coordinates
        // (same AugmentedPath resolved once, persists until ClearGeometry).
        Assert.NotNull(lines[2].CoordinatesOverride);
        Assert.NotNull(lines[3].CoordinatesOverride);
        Assert.Equal(lines[2].CoordinatesOverride!.Count, lines[3].CoordinatesOverride!.Count);
    }

    [Fact]
    public void ClearGeometry_ResetsAugmentedLineState()
    {
        // After ClearGeometry, a subsequent LineInstruction should NOT have
        // any augmented coordinates.
        const string s =
            "ViewingGroup:27070;DrawingPriority:24;" +
            "ArcByRadius:0,0,20,0,360;" +
            "AugmentedPath:LocalCRS,LocalCRS,LocalCRS;" +
            "LineInstruction:_simple_;" +
            "ClearGeometry;" +
            "LineInstruction:_simple_";

        var anchor = (Latitude: 10.0, Longitude: 20.0);
        var parsed = DrawingInstructionParser.Parse("F1", s, anchor);

        var lines = parsed.OfType<LineInstruction>().ToList();
        Assert.Equal(2, lines.Count);

        Assert.NotNull(lines[0].CoordinatesOverride);
        Assert.Null(lines[1].CoordinatesOverride);
    }

    [Fact]
    public void AugmentedRay_WithoutFeatureAnchor_ProducesNoOverride()
    {
        // When no anchor is available, augmented line geometry is skipped.
        const string s =
            "ViewingGroup:27070;DrawingPriority:24;" +
            "AugmentedRay:GeographicCRS,45.0,GeographicCRS,18520;" +
            "LineInstruction:_simple_";

        var parsed = DrawingInstructionParser.Parse("F1", s);

        var line = Assert.Single(parsed.OfType<LineInstruction>());
        Assert.Null(line.CoordinatesOverride);
    }
}
