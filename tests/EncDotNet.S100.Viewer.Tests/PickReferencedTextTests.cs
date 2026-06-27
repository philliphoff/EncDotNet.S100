using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class PickReferencedTextTests
{
    [Fact]
    public void FromAttribute_PromotesFirstLineToTitleAndKeepsBody()
    {
        var attr = new PickAttribute
        {
            Code = "fileReference",
            RawValue = "101GB00N00549.TXT",
            ExternalText = "ANCHORING RESTRICTED\n\nMariners are advised...",
            Children = [],
        };

        var card = PickReferencedText.FromAttribute(attr);

        Assert.Equal("ANCHORING RESTRICTED", card.Title);
        Assert.Equal("Mariners are advised...", card.Body);
        Assert.True(card.HasBody);
        Assert.Equal("101GB00N00549.TXT", card.FileName);
        Assert.Equal(attr.ExternalText, card.ClipboardText);
    }

    [Fact]
    public void FromAttribute_SingleLineFileHasNoBody()
    {
        var attr = new PickAttribute
        {
            Code = "fileReference",
            RawValue = "ONE.TXT",
            ExternalText = "VESSEL REPORTING",
            Children = [],
        };

        var card = PickReferencedText.FromAttribute(attr);

        Assert.Equal("VESSEL REPORTING", card.Title);
        Assert.Equal(string.Empty, card.Body);
        Assert.False(card.HasBody);
    }

    [Fact]
    public void FromAttribute_EmptyTextFallsBackToDisplayName()
    {
        var attr = new PickAttribute
        {
            Code = "fileReference",
            Name = "File reference",
            RawValue = "EMPTY.TXT",
            ExternalText = "   \n  ",
            Children = [],
        };

        var card = PickReferencedText.FromAttribute(attr);

        Assert.Equal("File reference", card.Title);
        Assert.Equal(string.Empty, card.Body);
    }

    [Fact]
    public void SplitHeadingAndBody_NormalizesCrlfAndTrimsBlankLines()
    {
        var (title, body) = PickReferencedText.SplitHeadingAndBody(
            "\r\n\r\nTITLE\r\n\r\nLine one.\r\nLine two.\r\n\r\n");

        Assert.Equal("TITLE", title);
        Assert.Equal("Line one.\nLine two.", body);
    }
}
