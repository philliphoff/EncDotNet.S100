using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.TropicalCyclones;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1298 (vector weather S4): the Weather section's cyclone switches
/// (V26), a stale storm's label (V29), and a final advisory (§7), on Hurricane
/// Ian's advisory 20, issued 03:00 UTC 28 Sep 2022.
/// </summary>
public sealed class TropicalCycloneDisplayTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tc-display-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TropicalCycloneDatasetProcessor Ian() => new(TropicalCycloneFixtures.IanForecast);

    private static RenderContext With(params (string Key, bool Visible)[] switches) => new()
    {
        EcdisDisplay = new EcdisDisplaySettings { LayerVisibility = switches.ToDictionary(s => s.Key, s => s.Visible) },
    };

    private static async Task<DrawingInstruction[]> InstructionsAsync(TropicalCycloneDatasetProcessor processor, RenderContext context) =>
        [.. (await processor.BuildVectorPortrayalAsync(context, Ct)).SubLayers.SelectMany(l => l.Instructions)];

    [Fact]
    public void The_switches_are_on_by_default()
    {
        Assert.Equal(["weather.tc.radii", "weather.tc.pastTrack", "weather.tc.forecastTimes"], TropicalCycloneStyle.SwitchKeys);
        Assert.All(TropicalCycloneStyle.SwitchKeys, key => Assert.True(TropicalCycloneStyle.IsShown(null, key)));
        Assert.All(TropicalCycloneStyle.SwitchKeys, key => Assert.True(TropicalCycloneStyle.IsShown(new EcdisDisplaySettings(), key)));
    }

    [Fact]
    public async Task Wind_radii_off_leaves_out_the_radii_and_keeps_the_cone()
    {
        var all = await InstructionsAsync(Ian(), new RenderContext());
        var without = await InstructionsAsync(Ian(), With((TropicalCycloneStyle.WindRadiiKey, false)));

        Assert.Contains(all, i => i.FeatureReference.EndsWith(" kt", StringComparison.Ordinal));
        Assert.DoesNotContain(without, i => i.FeatureReference.EndsWith(" kt", StringComparison.Ordinal));
        Assert.Contains(without, i => i.FeatureReference == "AL092022 cone days 1-3");
        Assert.Contains(without, i => i.FeatureReference == "AL092022 track");
    }

    [Fact]
    public async Task Past_track_off_leaves_out_the_line_and_best_track_points()
    {
        var all = await InstructionsAsync(Ian(), new RenderContext());
        var without = await InstructionsAsync(Ian(), With((TropicalCycloneStyle.PastTrackKey, false)));

        Assert.Contains(all, i => i.FeatureReference == "AL092022 past track");
        Assert.Contains(all, i => i.FeatureReference.StartsWith("AL092022 past ", StringComparison.Ordinal) && i.FeatureReference != "AL092022 past track");
        Assert.DoesNotContain(without, i => i.FeatureReference.StartsWith("AL092022 past", StringComparison.Ordinal));
        Assert.Contains(without, i => i.FeatureReference == "AL092022");
    }

    [Fact]
    public async Task Forecast_times_off_keeps_the_points_without_their_labels()
    {
        var all = await InstructionsAsync(Ian(), new RenderContext());
        var without = await InstructionsAsync(Ian(), With((TropicalCycloneStyle.ForecastTimesKey, false)));

        static bool IsForecastPoint(DrawingInstruction i) => i.FeatureReference.StartsWith("AL092022 +", StringComparison.Ordinal);
        Assert.Contains(all, i => IsForecastPoint(i) && i is TextInstruction);
        Assert.DoesNotContain(without, i => IsForecastPoint(i) && i is TextInstruction);
        Assert.Equal(
            all.Count(i => IsForecastPoint(i) && i is PointInstruction),
            without.Count(i => IsForecastPoint(i) && i is PointInstruction));
        // The storm keeps its label.
        Assert.Equal(2, without.OfType<TextInstruction>().Count(t => t.FeatureReference == "AL092022"));
    }

    [Fact]
    public async Task A_stale_storm_says_its_age_on_its_second_line_and_isnt_faded()
    {
        var result = await Ian().BuildVectorPortrayalAsync(new RenderContext { TimeStepAge = TimeSpan.FromHours(9) }, Ct);

        var texts = result.SubLayers.SelectMany(l => l.Instructions).OfType<TextInstruction>().Where(t => t.FeatureReference == "AL092022").ToArray();
        Assert.Equal("Adv 20 · 9 h old · newer advisory likely", texts[1].Text);
        Assert.All(result.SubLayers, l => Assert.Equal(1.0, l.Opacity));
    }

    [Fact]
    public async Task A_stale_storms_card_says_its_age_under_the_notice_until_its_current_again()
    {
        var processor = Ian();
        await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStepAge = TimeSpan.FromHours(9) }, Ct);

        var stale = processor.GetFeatureInfo("AL092022")!.Attributes;
        Assert.True(stale[0].IsNavigationNotice);
        Assert.True(stale[1].IsStaleNotice);
        Assert.Equal("Advisory 20 is 9 h old. NHC has probably issued a newer one.", stale[1].RawValue);

        await processor.BuildVectorPortrayalAsync(new RenderContext(), Ct);
        Assert.DoesNotContain(processor.GetFeatureInfo("AL092022")!.Attributes, a => a.IsStaleNotice);
    }

    [Fact]
    public async Task A_final_advisory_draws_the_past_track_and_a_post_tropical_storm_only()
    {
        var forecast = TropicalCycloneFixtures.CopyIan(_folder);
        File.WriteAllText(Path.Combine(_folder, NhcAdvisoryFiles.FinalMarkerName("AL092022")), "final");

        Assert.Equal("al092022_final.txt", NhcAdvisoryFiles.FinalMarkerName("AL092022"));
        Assert.True(NhcAdvisoryFiles.Find(forecast)!.IsFinal);
        Assert.True(NhcAdvisoryFiles.Find(_folder)!.IsFinal);
        var processor = new TropicalCycloneDatasetProcessor(forecast);
        Assert.True(processor.Advisory.IsFinal);

        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var instructions = await InstructionsAsync(processor, new RenderContext());
            var references = instructions.Select(i => i.FeatureReference).ToHashSet();

            Assert.Contains("AL092022", references);
            Assert.Contains("AL092022 past track", references);
            Assert.DoesNotContain(references, r => r.Contains("cone", StringComparison.Ordinal) || r.EndsWith(" kt", StringComparison.Ordinal)
                || r.StartsWith("AL092022 +", StringComparison.Ordinal) || r == "AL092022 track");
            var storm = instructions.Where(i => i.FeatureReference == "AL092022").ToArray();
            Assert.Contains(storm.OfType<PointInstruction>(), p => p.SymbolReference == "WXTCPTN");
            Assert.Equal("Final advisory issued Wed 03:00", storm.OfType<TextInstruction>().ElementAt(1).Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        var rows = processor.GetFeatureInfo("AL092022")!.Attributes.ToDictionary(a => a.Code);
        Assert.Equal(new DateTime(2022, 9, 28, 3, 0, 0, DateTimeKind.Utc), rows["finalAdvisory"].DateTimeValue);
        Assert.DoesNotContain("nextAdvisory", rows.Keys);
        Assert.DoesNotContain("cone", rows.Keys);
        Assert.DoesNotContain("radius34", rows.Keys);
    }

    [Fact]
    public void Without_the_marker_an_advisory_isnt_final()
    {
        var forecast = TropicalCycloneFixtures.CopyIan(_folder);

        Assert.False(NhcAdvisoryFiles.Find(forecast)!.IsFinal);
        Assert.False(new TropicalCycloneDatasetProcessor(forecast).Advisory.IsFinal);
    }
}
