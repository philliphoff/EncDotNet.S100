using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="Part9DisplayListReader"/> covering the placement
/// metadata it must extract from S-100 Part 9 <c>textInstruction</c> elements
/// (alignment, mm offsets, and line offsets).
/// </summary>
public class Part9DisplayListReaderTests
{
    [Fact]
    public void ReadText_TextPointWithOffsetAndAlignment_PopulatesPlacementFields()
    {
        var doc = XDocument.Parse(
            """
            <displayList>
              <textInstruction>
                <featureReference>F1</featureReference>
                <viewingGroup>52270</viewingGroup>
                <displayPlane>OverRadar</displayPlane>
                <drawingPriority>24</drawingPriority>
                <textPoint horizontalAlignment="Start" verticalAlignment="Bottom" rotation="15">
                  <element>
                    <text>Shallow Water</text>
                    <bodySize>10</bodySize>
                    <foreground>AA44A8</foreground>
                  </element>
                  <offset>
                    <x>0.5</x>
                    <y>1.0</y>
                  </offset>
                </textPoint>
              </textInstruction>
            </displayList>
            """);

        var instructions = Part9DisplayListReader.Read(doc);
        var text = Assert.IsType<TextInstruction>(Assert.Single(instructions));

        Assert.Equal("Shallow Water", text.Text);
        Assert.Equal(10, text.FontSize);
        Assert.Equal("AA44A8", text.FontColor);
        Assert.Equal(15, text.Rotation);
        Assert.Equal(TextHorizontalAlignment.Start, text.HorizontalAlignment);
        Assert.Equal(TextVerticalAlignment.Bottom, text.VerticalAlignment);
        Assert.Equal(0.5, text.OffsetXmm);
        Assert.Equal(1.0, text.OffsetYmm);
        Assert.Null(text.LinePlacementPosition);
        Assert.Null(text.LineStartOffset);
    }

    [Fact]
    public void ReadText_ForegroundAndBackgroundTransparency_AreCaptured()
    {
        var doc = XDocument.Parse(
            """
            <displayList>
              <textInstruction>
                <featureReference>F1B</featureReference>
                <viewingGroup>52270</viewingGroup>
                <displayPlane>OverRadar</displayPlane>
                <drawingPriority>24</drawingPriority>
                <textPoint>
                  <element>
                    <text>UKCM</text>
                    <foreground transparency="0.25">AA44A8</foreground>
                    <background transparency="0.5">FFFFFF</background>
                  </element>
                </textPoint>
              </textInstruction>
            </displayList>
            """);

        var text = Assert.IsType<TextInstruction>(Assert.Single(Part9DisplayListReader.Read(doc)));

        Assert.Equal("AA44A8", text.FontColor);
        Assert.Equal(0.25, text.FontTransparency);
        Assert.Equal("FFFFFF", text.BackgroundColor);
        Assert.Equal(0.5, text.BackgroundTransparency);
    }

    [Fact]
    public void ReadText_TextLineWithRelativeOffsets_DerivesMidpointFraction()
    {
        var doc = XDocument.Parse(
            """
            <displayList>
              <textInstruction>
                <featureReference>F2</featureReference>
                <viewingGroup>52270</viewingGroup>
                <displayPlane>OverRadar</displayPlane>
                <drawingPriority>24</drawingPriority>
                <textLine horizontalAlignment="Center" verticalAlignment="Bottom">
                  <element><text>Report to VTS</text></element>
                  <startOffset>0.2</startOffset>
                  <endOffset>0.6</endOffset>
                  <placementMode>Relative</placementMode>
                </textLine>
              </textInstruction>
            </displayList>
            """);

        var instructions = Part9DisplayListReader.Read(doc);
        var text = Assert.IsType<TextInstruction>(Assert.Single(instructions));

        Assert.Equal(0.2, text.LineStartOffset);
        Assert.Equal(0.6, text.LineEndOffset);
        Assert.Equal(LinePlacementMode.Relative, text.LineOffsetMode);
        Assert.Equal(0.4, text.LinePlacementPosition);
    }

    [Fact]
    public void ReadText_OutOfRangeRelativeOffsets_FallsBackToMidpoint()
    {
        // The S-421 RouteWaypointLeg / RouteActionPoint XSL emits values of
        // 2.0 with mode "Relative" — outside the spec's [0,1] range.  The
        // reader should clamp to the polyline midpoint rather than throw or
        // pass through nonsense.
        var doc = XDocument.Parse(
            """
            <displayList>
              <textInstruction>
                <featureReference>F3</featureReference>
                <viewingGroup>52270</viewingGroup>
                <displayPlane>OverRadar</displayPlane>
                <drawingPriority>24</drawingPriority>
                <textPoint horizontalAlignment="Start" verticalAlignment="Bottom">
                  <element><text>Report to UKCM</text></element>
                  <startOffset>2.0</startOffset>
                  <endOffset>2.0</endOffset>
                  <placementMode>Relative</placementMode>
                </textPoint>
              </textInstruction>
            </displayList>
            """);

        var instructions = Part9DisplayListReader.Read(doc);
        var text = Assert.IsType<TextInstruction>(Assert.Single(instructions));

        Assert.Equal(0.5, text.LinePlacementPosition);
    }

    [Fact]
    public void ReadArea_TransparencyOnColorAttribute_IsCaptured()
    {
        // S-100 Part 9A allows transparency as an attribute on <color>; the
        // bundled S-122/S-124/S-128 portrayal catalogues all use this form
        // (e.g. <color transparency="0.30">CHGRN</color>). Regression test
        // for area fills appearing fully opaque.
        var doc = XDocument.Parse(
            """
            <displayList>
              <areaInstruction>
                <featureReference>F1</featureReference>
                <viewingGroup>31000</viewingGroup>
                <displayPlane>OVERRADAR</displayPlane>
                <drawingPriority>15</drawingPriority>
                <colorFill>
                  <color transparency="0.30">CHGRN</color>
                </colorFill>
              </areaInstruction>
            </displayList>
            """);

        var area = Assert.IsType<AreaInstruction>(Assert.Single(Part9DisplayListReader.Read(doc)));
        Assert.Equal("CHGRN", area.FillColor);
        Assert.Equal(0.30, area.Transparency);
    }

    [Fact]
    public void ReadArea_TransparencyAsChildElement_IsCaptured()
    {
        var doc = XDocument.Parse(
            """
            <displayList>
              <areaInstruction>
                <featureReference>F1</featureReference>
                <viewingGroup>31000</viewingGroup>
                <displayPlane>OVERRADAR</displayPlane>
                <drawingPriority>15</drawingPriority>
                <colorFill>
                  <color>CHGRN</color>
                  <transparency>0.50</transparency>
                </colorFill>
              </areaInstruction>
            </displayList>
            """);

        var area = Assert.IsType<AreaInstruction>(Assert.Single(Part9DisplayListReader.Read(doc)));
        Assert.Equal("CHGRN", area.FillColor);
        Assert.Equal(0.50, area.Transparency);
    }
}
