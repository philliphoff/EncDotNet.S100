using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// S-124 parts the host hides through <see cref="RenderContext.HiddenFeatures"/>
/// (#993): a part ending mid-warning hides alone, with the area and text only
/// it references; every part hidden hides the warning.
/// </summary>
public class S124HiddenPartsTests
{
    private const string Parts = "navwarn_drawing_parts.gml";
    private const string Cancelled = "navwarn_drawing_cancelled.gml";
    private const string InForcePart = "ZZ.S124.NAVAREAX.120.26.1";
    private const string EndedPart = "ZZ.S124.NAVAREAX.120.26.2";
    private const string LaterPart = "ZZ.S124.NAVAREAX.120.26.3";
    private const string EndedArea = "ZZ.S124.NAVAREAX.120.26.4";
    private const string EndedText = "ZZ.S124.NAVAREAX.120.26.5";

    /// <summary>The view time of the fixtures: part 1 in force, part 2 ended, part 3 not yet started, 121/26 cancelled.</summary>
    internal static readonly DateTimeOffset ViewTime = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private static S124DatasetProcessor CreateProcessor(string fileName)
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return new S124DatasetProcessor(
            Path.Combine(AppContext.BaseDirectory, "TestData", "S124", fileName),
            manager,
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
    }

    private static async Task<HashSet<string>> DrawnFeatures(S124DatasetProcessor processor, IReadOnlySet<string>? hidden)
    {
        var result = await processor.BuildVectorPortrayalAsync(new S124RenderContext { HiddenFeatures = hidden }, TestContext.Current.CancellationToken);
        return result.SubLayers.SelectMany(l => l.Instructions).Select(i => i.FeatureReference).ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlySet<string> Hidden(S124DatasetProcessor processor, NavigationalWarningSet set, bool showNotYetInForce) =>
        processor.GetNavigationalWarnings()
            .Where(p => !set.IsDrawnAt(p.Warning, ViewTime, showNotYetInForce))
            .Select(p => p.FeatureRef)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public async Task With_nothing_hidden_every_feature_is_drawn()
    {
        var drawn = await DrawnFeatures(CreateProcessor(Parts), hidden: null);

        Assert.Superset(new HashSet<string> { InForcePart, EndedPart, LaterPart, EndedArea, EndedText }, drawn);
    }

    [Fact]
    public async Task A_part_ending_mid_warning_hides_only_that_part_and_what_only_it_references()
    {
        var processor = CreateProcessor(Parts);
        var hidden = Hidden(processor, new NavigationalWarningSet([processor.GetNavigationalWarningMessage()!]), showNotYetInForce: true);
        Assert.Equal([EndedPart], hidden);

        var drawn = await DrawnFeatures(processor, hidden);

        Assert.Contains(InForcePart, drawn);
        Assert.Contains(LaterPart, drawn);
        Assert.DoesNotContain(EndedPart, drawn);
        Assert.DoesNotContain(EndedArea, drawn);
        Assert.DoesNotContain(EndedText, drawn);
    }

    [Fact]
    public async Task With_the_setting_off_a_part_not_yet_in_force_is_hidden_too()
    {
        var processor = CreateProcessor(Parts);
        var hidden = Hidden(processor, NavigationalWarningSet.Empty, showNotYetInForce: false);

        var drawn = await DrawnFeatures(processor, hidden);

        Assert.Contains(InForcePart, drawn);
        Assert.DoesNotContain(LaterPart, drawn);
        Assert.DoesNotContain(EndedPart, drawn);
    }

    [Fact]
    public async Task An_area_a_drawn_part_also_references_stays()
    {
        var processor = CreateProcessor("navwarn_with_xlinks.gml");

        // The only part is hidden: the whole warning goes.
        var none = await processor.BuildVectorPortrayalAsync(new S124RenderContext { HiddenFeatures = new HashSet<string> { "part1" } }, TestContext.Current.CancellationToken);
        Assert.Empty(none.SubLayers);
        Assert.Contains("no warning drawn", none.Info);

        // Hiding a feature that isn't a part changes nothing about the part's area.
        var drawn = await DrawnFeatures(processor, new HashSet<string> { "nope" });
        Assert.Contains("area1", drawn);
    }

    [Fact]
    public async Task A_cancelled_warning_is_hidden_whole()
    {
        var processor = CreateProcessor(Cancelled);
        var hidden = Hidden(processor, NavigationalWarningSet.Empty, showNotYetInForce: true);
        Assert.Single(hidden);

        var result = await processor.BuildVectorPortrayalAsync(new S124RenderContext { HiddenFeatures = hidden }, TestContext.Current.CancellationToken);

        Assert.Empty(result.SubLayers);
    }

    [Fact]
    public async Task Every_part_hidden_draws_no_area_or_text_either()
    {
        // The viewer's Navigational warnings switch off (#995) hides every part.
        var processor = CreateProcessor(Parts);
        var hidden = processor.GetNavigationalWarnings().Select(p => p.FeatureRef).ToHashSet(StringComparer.Ordinal);

        var result = await processor.BuildVectorPortrayalAsync(new S124RenderContext { HiddenFeatures = hidden }, TestContext.Current.CancellationToken);

        Assert.Empty(result.SubLayers);
        Assert.Empty(processor.HitTestFeatures(-16.50, 146.00, 500));
    }

    [Fact]
    public async Task Hidden_parts_are_not_picked()
    {
        var processor = CreateProcessor(Parts);
        Assert.Contains(processor.HitTestFeatures(-16.50, 146.00, 500), h => h.FeatureRef == InForcePart);

        await processor.BuildVectorPortrayalAsync(new S124RenderContext { HiddenFeatures = new HashSet<string> { InForcePart } }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(processor.HitTestFeatures(-16.50, 146.00, 500), h => h.FeatureRef == InForcePart);

        await processor.BuildVectorPortrayalAsync(new S124RenderContext(), TestContext.Current.CancellationToken);

        Assert.Contains(processor.HitTestFeatures(-16.50, 146.00, 500), h => h.FeatureRef == InForcePart);
    }
}
