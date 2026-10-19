using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// S-124 warnings drawn only while in force at the view time (#993, S-98
/// Edition 2.0.0 §12.12), as the viewer hides them: at 15 Oct 2026 part 1 of
/// NAVAREA X 120/26 is in force, part 2 has ended (with its affected area and
/// text) and part 3 starts on 25 Oct; 121/26 cancelled itself on 12 Oct.
/// </summary>
public sealed class S124DrawingAtViewTimeTests
{
    private static readonly DateTimeOffset ViewTime = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private static string PathOf(string fileName) => Path.Combine(TestHelpers.DatasetsRoot, "S124", fileName);

    /// <summary>The parts the viewer hides at <see cref="ViewTime"/>, judged with both warnings.</summary>
    private static IReadOnlySet<string> Hidden(RenderHarness harness, string fileName, bool showNotYetInForce)
    {
        var parts = harness.Open(PathOf("navwarn_drawing_parts.gml"));
        var cancelled = harness.Open(PathOf("navwarn_drawing_cancelled.gml"));
        var set = new NavigationalWarningSet([parts.GetNavigationalWarningMessage()!, cancelled.GetNavigationalWarningMessage()!]);
        var processor = harness.Open(PathOf(fileName));
        return processor.GetNavigationalWarnings()
            .Where(p => !set.IsDrawnAt(p.Warning, ViewTime, showNotYetInForce))
            .Select(p => p.FeatureRef)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Theory]
    // In force only (setting off), or with the part not yet in force too (setting on).
    // Ink is the point symbol and, when on, the area outline; budgets as S124RenderingTests.
    [InlineData(false, 100, 0.5)]
    [InlineData(true, 400, 0.5)]
    public Task Parts_in_force_and_not_yet_in_force(bool showNotYetInForce, int minimumInkPixels, double maxDifferentInkFraction)
    {
        Assert.SkipUnless(File.Exists(PathOf("navwarn_drawing_parts.gml")), "S-124 drawing fixture not present");

        using var harness = new RenderHarness();
        var hidden = Hidden(harness, "navwarn_drawing_parts.gml", showNotYetInForce);
        Assert.Contains("ZZ.S124.NAVAREAX.120.26.2", hidden);
        Assert.Equal(!showNotYetInForce, hidden.Contains("ZZ.S124.NAVAREAX.120.26.3"));

        var bitmap = harness.Render(PathOf("navwarn_drawing_parts.gml"), new HarnessOptions
        {
            Width = 600,
            Height = 600,
            HiddenFeatures = hidden,
        });

        return TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels, maxDifferentInkFraction)
            .UseParameters(showNotYetInForce ? "with_not_yet_in_force" : "in_force_only");
    }

    [Fact]
    public void Every_part_draws_without_the_filter()
    {
        using var harness = new RenderHarness();
        var all = harness.Render(PathOf("navwarn_drawing_parts.gml"), new HarnessOptions { Width = 600, Height = 600 });
        var inForce = harness.Render(PathOf("navwarn_drawing_parts.gml"), new HarnessOptions
        {
            Width = 600,
            Height = 600,
            HiddenFeatures = Hidden(harness, "navwarn_drawing_parts.gml", showNotYetInForce: false),
        });

        Assert.True(
            TestHelpers.CountNonBackgroundPixels(all) > TestHelpers.CountNonBackgroundPixels(inForce),
            "Hiding the ended and later parts drew as much as drawing every part.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cancelled_warning_is_never_drawn(bool showNotYetInForce)
    {
        using var harness = new RenderHarness();
        var hidden = Hidden(harness, "navwarn_drawing_cancelled.gml", showNotYetInForce);
        Assert.NotEmpty(hidden);
        // Drawn without the filter, so the blank render below is the filter's doing.
        var unfiltered = harness.Render(PathOf("navwarn_drawing_cancelled.gml"), new HarnessOptions { Width = 300, Height = 300 });
        Assert.True(TestHelpers.CountNonBackgroundPixels(unfiltered) > 0);

        var bitmap = harness.Render(PathOf("navwarn_drawing_cancelled.gml"), new HarnessOptions
        {
            Width = 300,
            Height = 300,
            HiddenFeatures = hidden,
        });

        Assert.Equal(0, TestHelpers.CountNonBackgroundPixels(bitmap));
    }
}
