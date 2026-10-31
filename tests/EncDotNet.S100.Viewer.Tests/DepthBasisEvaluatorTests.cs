using EncDotNet.S100.Collections;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Services.Depth;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The depth basis indicator's decisions (#1191,
/// <c>docs/design/depth-basis-indicator.md</c> §2.2 and §6).
/// </summary>
public sealed class DepthBasisEvaluatorTests
{
    private const int Mllw = 12;
    private const int Lat = 23;

    private static readonly DateTimeOffset ViewTime = new(2026, 10, 31, 14, 20, 0, TimeSpan.Zero);
    private static readonly GeoBounds View = new(47.5, -122.5, 47.7, -122.3);
    private static readonly GeoBounds InView = new(47.55, -122.45, 47.65, -122.35);
    private static readonly GeoBounds Elsewhere = new(40, -70, 41, -69);

    private static DepthBasisDataset Chart(string name, GeoBounds? footprint = null, int? datum = Mllw, bool active = true, string spec = "S-101") =>
        new(name, spec, active, footprint ?? InView, datum);

    private static DepthBasisDataset Surface(string name, GeoBounds? footprint = null, int? datum = Mllw) =>
        new(name, "S-102", true, footprint ?? InView, datum);

    private static DepthBasisDataset WaterLevel(
        string name,
        GeoBounds? footprint = null,
        int? datum = Mllw,
        bool active = true,
        WaterLevelSourceKind kind = WaterLevelSourceKind.Grid) =>
        new(name, "S-104", active, footprint ?? InView, datum,
            new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc),
            kind);

    private static DepthBasis Evaluate(IWaterLevelAdjustmentState? adjustment, params DepthBasisDataset[] datasets) =>
        DepthBasisEvaluator.Evaluate(new DepthBasisInputs(datasets, View, ViewTime, adjustment ?? NoWaterLevelAdjustment.Instance));

    private static DepthBasis Evaluate(params DepthBasisDataset[] datasets) => Evaluate(null, datasets);

    [Fact]
    public void No_depth_datasets_hide_the_indicator()
    {
        Assert.Same(DepthBasis.Hidden, Evaluate());
        Assert.Same(DepthBasis.Hidden, Evaluate(WaterLevel("104US00_CBOFS")));
    }

    [Fact]
    public void Inactive_depth_datasets_hide_the_indicator()
    {
        Assert.Same(DepthBasis.Hidden, Evaluate(Chart("US5WA22M", active: false)));
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-401")]
    [InlineData("S-57")]
    [InlineData("S-102")]
    public void A_depth_bearing_dataset_shows_charted(string spec)
    {
        var basis = Evaluate(Chart("CELL", spec: spec));

        Assert.Equal(DepthBasisKind.Charted, basis.Kind);
        Assert.Equal(DepthBasisReason.None, basis.Reason);
        Assert.Null(basis.WaterLevelStatus);
        Assert.True(basis.CoversWholeView);
        Assert.Equal(ViewTime, basis.ViewTime);
        Assert.Equal("CELL", Assert.Single(basis.DepthSources).DatasetId);
        Assert.Empty(basis.WaterLevelSources);
        Assert.Null(basis.Datum);
    }

    [Fact]
    public void A_depth_dataset_outside_the_view_still_shows_charted_but_isnt_a_source()
    {
        var basis = Evaluate(Chart("US5WA22M", Elsewhere));

        Assert.Equal(DepthBasisKind.Charted, basis.Kind);
        Assert.Empty(basis.DepthSources);
    }

    [Fact]
    public void S104_outside_the_view_is_plain_charted()
    {
        var basis = Evaluate(Surface("102US00_SEATTLE"), WaterLevel("104US00_CBOFS", Elsewhere));

        Assert.Equal(DepthBasisKind.Charted, basis.Kind);
        Assert.Equal(DepthBasisReason.None, basis.Reason);
        Assert.Empty(basis.WaterLevelSources);
    }

    [Fact]
    public void S104_meeting_the_view_is_charted_not_applied()
    {
        var basis = Evaluate(Chart("US5WA22M"), WaterLevel("104US00_CBOFS"));

        Assert.Equal(DepthBasisKind.Charted, basis.Kind);
        Assert.Equal(DepthBasisReason.NotApplied, basis.Reason);
        Assert.Null(basis.WaterLevelStatus);
        var source = Assert.Single(basis.WaterLevelSources);
        Assert.Equal("104US00_CBOFS", source.DatasetId);
        Assert.Equal(WaterLevelSourceKind.Grid, source.Kind);
        Assert.Equal(new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc), source.Start);
        Assert.Equal(DatumAgreement.Same, basis.Datum);
    }

    [Fact]
    public void Inactive_S104_is_plain_charted()
    {
        var basis = Evaluate(Chart("US5WA22M"), WaterLevel("104US00_CBOFS", active: false));

        Assert.Equal(DepthBasisReason.None, basis.Reason);
        Assert.Empty(basis.WaterLevelSources);
    }

    [Fact]
    public void A_station_series_near_the_view_meets_it()
    {
        // A station just off the view's east edge, within the route check's 10 km.
        var station = new GeoBounds(47.6, -122.25, 47.6, -122.25);
        var basis = Evaluate(Chart("US5WA22M"), WaterLevel("104US00_STATIONS", station, kind: WaterLevelSourceKind.Station));

        Assert.Equal(DepthBasisReason.NotApplied, basis.Reason);
        Assert.Equal(WaterLevelSourceKind.Station, Assert.Single(basis.WaterLevelSources).Kind);
    }

    [Fact]
    public void A_station_series_far_from_the_view_does_not()
    {
        var station = new GeoBounds(47.6, -121.9, 47.6, -121.9);
        var basis = Evaluate(Chart("US5WA22M"), WaterLevel("104US00_STATIONS", station, kind: WaterLevelSourceKind.Station));

        Assert.Equal(DepthBasisReason.None, basis.Reason);
    }

    [Fact]
    public void An_unknown_footprint_counts_as_in_view()
    {
        var unknown = new DepthBasisDataset("104US00_CBOFS", "S-104", true, null, Mllw);
        var basis = Evaluate(Chart("US5WA22M"), unknown);

        Assert.Equal(DepthBasisReason.NotApplied, basis.Reason);
    }

    [Fact]
    public void Mixed_datums_are_all_listed_and_reported()
    {
        var basis = Evaluate(Chart("US5WA22M", datum: Mllw), Surface("102US00_SEATTLE", datum: Lat), WaterLevel("104US00_CBOFS", datum: Mllw));

        Assert.Equal([Lat, Mllw], basis.DepthSources.Select(s => s.VerticalDatumCode));
        Assert.Equal("102US00_SEATTLE", basis.DepthSources[0].DatasetId);
        Assert.Equal(DatumAgreement.Different, basis.Datum);
    }

    [Fact]
    public void An_undeclared_datum_is_unknown_agreement()
    {
        var basis = Evaluate(Chart("US5WA22M", datum: null), WaterLevel("104US00_CBOFS"));

        Assert.Null(Assert.Single(basis.DepthSources).VerticalDatumCode);
        Assert.Equal(DatumAgreement.Unknown, basis.Datum);
    }

    [Fact]
    public void Adjustment_applied_is_adjusted()
    {
        var step = new DateTime(2026, 10, 31, 14, 0, 0, DateTimeKind.Utc);
        var basis = Evaluate(
            new FakeAdjustment(new WaterLevelAdjustmentResult(WaterLevelStatus.Applied, step, Datum: DatumAgreement.Same)),
            Chart("US5WA22M"),
            WaterLevel("104US00_CBOFS"));

        Assert.Equal(DepthBasisKind.Adjusted, basis.Kind);
        Assert.Equal(DepthBasisReason.None, basis.Reason);
        Assert.Equal(WaterLevelStatus.Applied, basis.WaterLevelStatus);
        Assert.Equal(step, basis.WaterLevelStepTime);
        Assert.True(basis.CoversWholeView);
        Assert.Equal(DatumAgreement.Same, basis.Datum);
    }

    [Fact]
    public void Partial_coverage_does_not_cover_the_whole_view()
    {
        var basis = Evaluate(
            new FakeAdjustment(new WaterLevelAdjustmentResult(WaterLevelStatus.Applied, CoversWholeView: false)),
            Chart("US5WA22M"),
            WaterLevel("104US00_CBOFS"));

        Assert.Equal(DepthBasisKind.Adjusted, basis.Kind);
        Assert.False(basis.CoversWholeView);
    }

    [Theory]
    [InlineData(WaterLevelStatus.NoCoverage)]
    [InlineData(WaterLevelStatus.BeforeStart)]
    [InlineData(WaterLevelStatus.AfterEnd)]
    [InlineData(WaterLevelStatus.NoData)]
    [InlineData(WaterLevelStatus.DatumMismatch)]
    public void Adjustment_unavailable_is_charted_with_its_reason(WaterLevelStatus status)
    {
        var basis = Evaluate(
            new FakeAdjustment(new WaterLevelAdjustmentResult(status)),
            Chart("US5WA22M"),
            WaterLevel("104US00_CBOFS"));

        Assert.Equal(DepthBasisKind.Charted, basis.Kind);
        Assert.Equal(DepthBasisReason.WaterLevel, basis.Reason);
        Assert.Equal(status, basis.WaterLevelStatus);
        Assert.Null(basis.WaterLevelStepTime);
    }

    [Fact]
    public void Adjustment_turned_on_but_reporting_nothing_is_not_applied()
    {
        var basis = Evaluate(new FakeAdjustment(null), Chart("US5WA22M"), WaterLevel("104US00_CBOFS"));

        Assert.Equal(DepthBasisKind.Charted, basis.Kind);
        Assert.Equal(DepthBasisReason.NotApplied, basis.Reason);
    }

    [Fact]
    public void The_stub_adjustment_is_always_off()
    {
        Assert.False(NoWaterLevelAdjustment.Instance.IsEnabled);
        Assert.Null(NoWaterLevelAdjustment.Instance.Evaluate(View, ViewTime));
    }

    /// <summary>A chart adjustment that is on and reports a fixed result.</summary>
    internal sealed class FakeAdjustment(WaterLevelAdjustmentResult? result) : IWaterLevelAdjustmentState
    {
        public bool IsEnabled => true;

        public WaterLevelAdjustmentResult? Evaluate(GeoBounds? view, DateTimeOffset viewTime) => result;

        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
