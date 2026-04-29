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
}
