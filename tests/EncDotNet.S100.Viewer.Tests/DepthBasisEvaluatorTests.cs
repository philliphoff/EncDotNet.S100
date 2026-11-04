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

    private static readonly DateTime Applied = new(2026, 10, 31, 14, 0, 0, DateTimeKind.Utc);

    private static ChartAdjustmentState State(
        DateTime? applied = null, bool undeclared = false, GeoBounds? footprint = null) =>
        new(
            WaterLevelAdjustmentMode.AtViewTime,
            applied ?? Applied,
            [new WaterLevelSourceInfo("104US00_CBOFS", WaterLevelSourceKind.Grid, Mllw)],
            [Area(footprint ?? new GeoBounds(47, -123, 48, -122))],
            undeclared);

    private static DataModel.CoverageArea Area(GeoBounds b) => new()
    {
        ExteriorRing =
        [
            new(b.South, b.West), new(b.South, b.East), new(b.North, b.East), new(b.North, b.West), new(b.South, b.West),
        ],
    };

    [Fact]
    public void Adjustment_applied_is_adjusted()
    {
        var basis = Evaluate(new FakeAdjustment(State()), Chart("US5WA22M"), WaterLevel("104US00_CBOFS"));

        Assert.Equal(DepthBasisKind.Adjusted, basis.Kind);
        Assert.Equal(DepthBasisReason.None, basis.Reason);
        Assert.Equal(WaterLevelStatus.Applied, basis.WaterLevelStatus);
        Assert.Equal(Applied, basis.WaterLevelStepTime);
        Assert.True(basis.CoversWholeView);
        Assert.Equal(DatumAgreement.Same, basis.Datum);
        Assert.Empty(basis.NotAdjustedSources);
    }

    [Fact]
    public void A_footprint_short_of_the_view_is_part_of_view()
    {
        var basis = Evaluate(
            new FakeAdjustment(State(footprint: new GeoBounds(47.6, -122.4, 48, -122))),
            Chart("US5WA22M"),
            WaterLevel("104US00_CBOFS"));

        Assert.Equal(DepthBasisKind.Adjusted, basis.Kind);
        Assert.False(basis.CoversWholeView);
    }

    [Fact]
    public void A_refused_source_is_listed_and_makes_it_part_of_view()
    {
        // S-164 WaterLevelAdjustment: S-102 on MLLW, S-104 on LAT, the S-101
        // cell declares nothing. With the override the cell is adjusted and
        // the surface isn't.
        var basis = Evaluate(
            new FakeAdjustment(State(undeclared: true)),
            Chart("10100AA164US4FL2A", datum: null),
            Surface("102US004FL2AI252277", datum: Mllw),
            WaterLevel("104US00_Florida", datum: Lat));

        Assert.Equal(DepthBasisKind.Adjusted, basis.Kind);
        Assert.False(basis.CoversWholeView);
        Assert.Equal(DatumAgreement.Unknown, basis.Datum);
        Assert.Equal("102US004FL2AI252277", Assert.Single(basis.NotAdjustedSources).DatasetId);
    }

    [Theory]
    [InlineData(Mllw, false, false)]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(Lat, true, true)]
    public void The_datum_rule_decides_whether_a_source_is_adjusted(int? depthDatum, bool undeclared, bool adjusted)
    {
        var basis = Evaluate(
            new FakeAdjustment(State(undeclared: undeclared)),
            Chart("US5WA22M", datum: depthDatum),
            WaterLevel("104US00_CBOFS", datum: depthDatum == Lat ? null : Lat));

        Assert.Equal(adjusted ? DepthBasisKind.Adjusted : DepthBasisKind.Charted, basis.Kind);
        if (!adjusted)
        {
            Assert.Equal(DepthBasisReason.WaterLevel, basis.Reason);
            Assert.Equal(WaterLevelStatus.DatumMismatch, basis.WaterLevelStatus);
        }
    }

    [Fact]
    public void No_water_level_in_view_is_no_coverage()
    {
        var basis = Evaluate(
            new FakeAdjustment(State(footprint: Elsewhere)),
            Chart("US5WA22M"),
            WaterLevel("104US00_CBOFS"));

        Assert.Equal(DepthBasisKind.Charted, basis.Kind);
        Assert.Equal(WaterLevelStatus.NoCoverage, basis.WaterLevelStatus);
    }

    [Theory]
    [InlineData(-1, WaterLevelStatus.BeforeStart)]
    [InlineData(+5, WaterLevelStatus.AfterEnd)]
    public void A_time_outside_the_series_is_charted(int days, WaterLevelStatus status)
    {
        var basis = Evaluate(
            new FakeAdjustment(State(applied: Applied.AddDays(days))),
            Chart("US5WA22M"),
            WaterLevel("104US00_CBOFS"));

        Assert.Equal(DepthBasisKind.Charted, basis.Kind);
        Assert.Equal(DepthBasisReason.WaterLevel, basis.Reason);
        Assert.Equal(status, basis.WaterLevelStatus);
    }

    [Fact]
    public void Adjustment_on_with_nothing_drawn_yet_is_not_applied()
    {
        var basis = Evaluate(new FakeAdjustment(null), Chart("US5WA22M"), WaterLevel("104US00_CBOFS"));

        Assert.Equal(DepthBasisKind.Charted, basis.Kind);
        Assert.Equal(DepthBasisReason.NotApplied, basis.Reason);
    }

    [Fact]
    public void The_stub_adjustment_is_always_off()
    {
        Assert.False(NoWaterLevelAdjustment.Instance.IsEnabled);
        Assert.Null(NoWaterLevelAdjustment.Instance.Current);
    }

    /// <summary>A chart adjustment that is on and reports a fixed state.</summary>
    internal sealed class FakeAdjustment(ChartAdjustmentState? state) : IWaterLevelAdjustmentState
    {
        public bool IsEnabled => true;

        public ChartAdjustmentState? Current => state;

        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
