using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// S-412 warnings follow the render context's time step (#841): the processor
/// reports the instants the warnings in force change, draws and picks only
/// those in force at the step, and draws them all with no step.
/// </summary>
public class S412TimeStepTests
{
    // In synthetic_all_warnings.gml at 2026-11-08 06:00 UTC: WIND1, WAVE1 and
    // ICE1 are in force; VIS1 and TS1 have ended.
    private static readonly DateTime MidWarning = new(2026, 11, 8, 6, 0, 0, DateTimeKind.Utc);

    private static S412DatasetProcessor CreateProcessor()
    {
        var manager = new PortrayalCatalogueManager();
        manager.SetSource("S-412", Specification.CreatePortrayalCatalogueSource("S-412"));

        return new S412DatasetProcessor(
            Path.Combine(AppContext.BaseDirectory, "TestData", "S412", "synthetic_all_warnings.gml"),
            manager,
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
    }

    private static async Task<HashSet<string>> DrawnFeatures(S412DatasetProcessor processor, DateTime? timeStep)
    {
        var result = await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = timeStep }, TestContext.Current.CancellationToken);
        return result.SubLayers.SelectMany(l => l.Instructions).Select(i => i.FeatureReference).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void Steps_AreTheInstantsTheWarningsInForceChange()
    {
        var processor = CreateProcessor();

        Assert.IsAssignableFrom<ITimeAwareDatasetProcessor>(processor);
        Assert.Equal(new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc), processor.AvailableTimes[0]);
        Assert.Equal(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), processor.AvailableTimes[^1]);
        Assert.Equal(7, processor.AvailableTimes.Count);
        Assert.Equal(TimeStepSelection.AtOrBefore, processor.TimeSemantics.Selection);
        Assert.Equal(TimeStepKind.Forecast, processor.TimeSemantics.Kind);
        Assert.Equal(new DateTime(2026, 11, 7, 5, 30, 0, DateTimeKind.Utc), processor.IssueTime);
        Assert.Equal(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc), processor.NextIssueTime);
    }

    [Fact]
    public void Metadata_CoversTheWarnings()
    {
        var coverage = CreateProcessor().Metadata.TimeCoverage;

        Assert.Equal(new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc), coverage?.Start);
        Assert.Equal(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), coverage?.End);
    }

    [Fact]
    public async Task WithoutTimeStep_EveryWarningIsDrawn()
    {
        var drawn = await DrawnFeatures(CreateProcessor(), timeStep: null);

        Assert.Equal(new HashSet<string> { "WIND1", "WAVE1", "VIS1", "TS1", "ICE1" }, drawn);
    }

    [Fact]
    public async Task AtTimeStep_OnlyWarningsInForceAreDrawnAndPicked()
    {
        var processor = CreateProcessor();

        var drawn = await DrawnFeatures(processor, MidWarning);

        Assert.Equal(new HashSet<string> { "WIND1", "WAVE1", "ICE1" }, drawn);
        // The thunderstorm area (around 51.5 N, 25 W) ended at midnight.
        Assert.DoesNotContain(processor.HitTestFeatures(51.5, -25.0, 1000), h => h.FeatureRef == "TS1");
        Assert.Contains(processor.HitTestFeatures(61.0, -23.0, 1000), h => h.FeatureRef == "ICE1");

        await DrawnFeatures(processor, timeStep: null);
        Assert.Contains(processor.HitTestFeatures(51.5, -25.0, 1000), h => h.FeatureRef == "TS1");
    }

    [Fact]
    public async Task AfterEveryWarningEnds_NothingIsDrawn()
    {
        var processor = CreateProcessor();

        var result = await processor.BuildVectorPortrayalAsync(
            new RenderContext { TimeStep = processor.AvailableTimes[^1] }, TestContext.Current.CancellationToken);

        Assert.Empty(result.SubLayers);
        Assert.Contains("no warning in force", result.Info, StringComparison.Ordinal);
        Assert.Empty(processor.HitTestFeatures(61.0, -23.0, 1000));
    }

    [Fact]
    public async Task HeadlessRender_AtTimeStep_IsBlankWhenNothingIsInForce()
    {
        var processor = CreateProcessor();

        using var bitmap = await processor.RenderHeadlessAsync(
            64, 64, new RenderContext { TimeStep = processor.AvailableTimes[^1] }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(bitmap.Pixels, p => Assert.Equal((byte)255, p.Red));
    }

    [Fact]
    public void Validate_RunsTheS412RulePack()
    {
        // The synthetic dataset is clean, so the report is empty rather than
        // null (null would mean no rule pack).
        var report = CreateProcessor().Validate();

        Assert.NotNull(report);
        Assert.Empty(report.Findings);
    }
}
