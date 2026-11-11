using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class PickTextCardViewModelTests
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

        var card = PickTextCardViewModel.FromReferencedText(attr);

        Assert.Equal("ANCHORING RESTRICTED", card.Title);
        Assert.Equal("Mariners are advised...", card.Body);
        Assert.True(card.HasText);
        Assert.Equal("101GB00N00549.TXT", card.FileName);
        Assert.Equal(attr.ExternalText, card.CopyText);
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

        var card = PickTextCardViewModel.FromReferencedText(attr);

        Assert.Equal("VESSEL REPORTING", card.Title);
        Assert.Equal(string.Empty, card.Body);
        Assert.False(card.HasText);
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

        var card = PickTextCardViewModel.FromReferencedText(attr);

        Assert.Equal("File reference", card.Title);
        Assert.Equal(string.Empty, card.Body);
    }

    [Fact]
    public void SplitHeadingAndBody_NormalizesCrlfAndTrimsBlankLines()
    {
        var (title, body) = PickTextCardViewModel.SplitHeadingAndBody(
            "\r\n\r\nTITLE\r\n\r\nLine one.\r\nLine two.\r\n\r\n");

        Assert.Equal("TITLE", title);
        Assert.Equal("Line one.\nLine two.", body);
    }

    [Theory]
    [InlineData("Line one.\nLine two.", true)]
    [InlineData("Line one.\r\nLine two.", true)]
    [InlineData("Short value", false)]
    public void IsLongValue_LineBreaksLift(string value, bool expected) =>
        Assert.Equal(expected, PickTextCardViewModel.IsLongValue(new PickAttribute { Code = "text", RawValue = value }));

    [Fact]
    public void IsLongValue_LiftsOnlyPastTheThreshold()
    {
        var atThreshold = new string('a', PickTextCardViewModel.LongValueThreshold);
        Assert.False(PickTextCardViewModel.IsLongValue(new PickAttribute { Code = "text", RawValue = atThreshold }));
        Assert.True(PickTextCardViewModel.IsLongValue(new PickAttribute { Code = "text", RawValue = atThreshold + "a" }));
    }

    [Fact]
    public void IsLongValue_IgnoresTypedAndComplexValues()
    {
        var longText = new string('a', 200);
        Assert.False(PickTextCardViewModel.IsLongValue(new PickAttribute { Code = "d", RawValue = longText, DateTimeValue = DateTime.UtcNow }));
        Assert.False(PickTextCardViewModel.IsLongValue(new PickAttribute { Code = "d", RawValue = longText, DepthMetresValue = 3 }));
        Assert.False(PickTextCardViewModel.IsLongValue(new PickAttribute
        {
            Code = "c",
            RawValue = longText,
            Children = [new PickAttribute { Code = "x", RawValue = "1" }],
        }));
    }

    [Fact]
    public void FromLongValue_HeadsWithTheAttributeNameAfterItsParent()
    {
        var parent = new PickAttribute { Code = "information", Name = "Information", RawValue = "" };
        var child = new PickAttribute { Code = "text", Name = "Text", RawValue = "First.\r\nSecond.\n" };

        var card = PickTextCardViewModel.FromLongValue(child, parent, notForNavigation: true);

        Assert.Equal("Information · Text", card.Title);
        Assert.False(card.HasFileName);
        Assert.Equal("First.\nSecond.", card.Body);
        Assert.Equal("First.\nSecond.", card.CopyText);
        Assert.Same(child, card.ExaminerAttribute);
        Assert.True(card.ExaminesAttribute);
        Assert.False(card.IsMonospace);
        Assert.True(card.NotForNavigation);
    }

    [Fact]
    public void LongText_ClipsAt20LinesPast22_ShowAllHolds_CopyIsWhole()
    {
        var text = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"Line {i}."));
        var card = PickTextCardViewModel.FromLongValue(new PickAttribute { Code = "text", RawValue = text });
        string? copied = null;
        card.CopyRequested += (_, t) => copied = t;

        Assert.True(card.IsClipped);
        Assert.Equal(30, card.LineCount);
        Assert.Equal(LongTextCardViewModel.ClipLines, card.DisplayedText.Split('\n').Length);

        card.ShowAllCommand.Execute(null);
        Assert.False(card.IsClipped);
        Assert.Equal(text, card.DisplayedText);

        card.CopyCommand.Execute(null);
        Assert.Equal(text, copied);
    }

    [Fact]
    public void LongText_At22LinesIsNotClipped()
    {
        var text = string.Join('\n', Enumerable.Range(1, LongTextCardViewModel.ClipThreshold).Select(i => $"Line {i}."));
        var card = PickTextCardViewModel.FromLongValue(new PickAttribute { Code = "text", RawValue = text });
        Assert.False(card.IsClipped);
    }

    [Fact]
    public void SingleLineReferencedFile_HidesTheTextBlock()
    {
        var card = PickTextCardViewModel.FromReferencedText(new PickAttribute
        {
            Code = "fileReference",
            RawValue = "ONE.TXT",
            ExternalText = "VESSEL REPORTING",
        });

        Assert.False(card.ShowsTextBlock);
        Assert.Equal("VESSEL REPORTING", card.CopyText);
    }

    [Theory]
    [InlineData("Anchorages exist:\n\n1. North of East Head.\n2. Thorney Channel.", false)]
    [InlineData("-6\t321,0.6\n-5.5\t323,0.6", false)]
    [InlineData("Hour   Dir  Rate\n-6     321  0.6\n-5     327  0.6", true)]
    [InlineData("One  slip here.\nThen prose.", false)]
    public void ReferencedText_IsMonospacedOnlyWhenLaidOutInSpacedColumns(string body, bool expected)
    {
        var card = PickTextCardViewModel.FromReferencedText(new PickAttribute
        {
            Code = "fileReference",
            RawValue = "T.TXT",
            ExternalText = "HEADING\n" + body,
        });

        Assert.Equal(expected, card.IsMonospace);
    }
}
