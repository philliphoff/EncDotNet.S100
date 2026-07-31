using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Viewer.Services.Depth;

namespace EncDotNet.S100.Viewer.Tests;

public class DepthAssimilationServiceTests
{
    private readonly DepthAssimilationService _service = new();

    private static S104TimeSeries Series(params (DateTime Time, double? Height)[] points)
    {
        var list = points
            .Select(p => new S104TimeSeriesPoint(p.Time, p.Height, 1))
            .ToList();
        return new S104TimeSeries
        {
            Row = 0,
            Col = 0,
            CellLatitude = 51.9,
            CellLongitude = 4.4,
            Points = list,
        };
    }

    private static readonly DateTime T0 = new(2024, 7, 23, 0, 0, 0, DateTimeKind.Utc);

    private static BaseDepthResult Bathy(double depth, double? unc = 0.3, int? datum = 10) =>
        new(depth, BaseDepthSource.Bathymetry, unc, datum, SoundingDistanceMeters: null);

    private static BaseDepthResult Vector(double depth) =>
        new(depth, BaseDepthSource.DepthArea, null, null, null);

    [Fact]
    public void Assimilate_returns_null_when_no_base_depth()
    {
        Assert.Null(_service.Assimilate(null, []));
    }

    [Fact]
    public void Assimilate_composes_tide_adjusted_depth()
    {
        var result = _service.Assimilate(
            Bathy(12.0),
            [new S104TideCandidate("ds1", 0.01, T0, 10, Series((T0, 1.5), (T0.AddMinutes(10), 1.0)))]);

        Assert.NotNull(result);
        Assert.Equal(2, result!.DepthOverTime.Count);
        Assert.Equal(13.5, result.DepthOverTime[0].DepthMeters);
        Assert.Equal(13.0, result.DepthOverTime[1].DepthMeters);
        Assert.Equal("ds1", result.Tide!.DatasetId);
        Assert.Equal(0.3, result.UncertaintyMeters);
    }

    [Fact]
    public void Assimilate_maps_nodata_tide_to_null_depth()
    {
        var result = _service.Assimilate(
            Bathy(12.0),
            [new S104TideCandidate("ds1", 0.01, T0, 10, Series((T0, null), (T0.AddMinutes(10), 2.0)))]);

        Assert.NotNull(result);
        Assert.Null(result!.DepthOverTime[0].DepthMeters);
        Assert.Equal(14.0, result.DepthOverTime[1].DepthMeters);
    }

    [Fact]
    public void Assimilate_partial_state_when_no_tide_overlap()
    {
        var result = _service.Assimilate(Bathy(12.0), []);

        Assert.NotNull(result);
        Assert.Null(result!.Tide);
        Assert.Empty(result.DepthOverTime);
        Assert.Equal(0.3, result.UncertaintyMeters);
        Assert.False(result.DatumsNotReconciled);
    }

    [Fact]
    public void Assimilate_partial_state_when_all_candidates_out_of_bounds()
    {
        var result = _service.Assimilate(
            Bathy(12.0),
            [new S104TideCandidate("ds1", 0.01, T0, 10, Series: null)]);

        Assert.NotNull(result);
        Assert.Null(result!.Tide);
        Assert.Empty(result.DepthOverTime);
    }

    [Fact]
    public void Assimilate_falls_back_to_static_when_all_tide_steps_are_nodata()
    {
        // The grid overlaps (Series is non-null) but every time-step at the
        // picked cell is NODATA — e.g. a land-masked cell. The result must be
        // the static base-only state, not a tide selection with an all-null
        // curve (which would render "DEPTH NOW n/a" despite a valid base).
        var result = _service.Assimilate(
            Bathy(0.9),
            [new S104TideCandidate("ds1", 0.01, T0, 10, Series((T0, null), (T0.AddMinutes(10), null)))]);

        Assert.NotNull(result);
        Assert.Null(result!.Tide);
        Assert.Empty(result.DepthOverTime);
        Assert.Equal(0.9, result.Base.DepthMeters);
        Assert.False(result.DatumsNotReconciled);
    }

    [Fact]
    public void Assimilate_selects_finest_resolution()
    {
        var coarse = new S104TideCandidate("coarse", 0.05, T0, 10, Series((T0, 1.0)));
        var fine = new S104TideCandidate("fine", 0.01, T0, 10, Series((T0, 2.0)));

        var result = _service.Assimilate(Bathy(10.0), [coarse, fine]);

        Assert.Equal("fine", result!.Tide!.DatasetId);
        Assert.Equal(12.0, result.DepthOverTime[0].DepthMeters);
    }

    [Fact]
    public void Assimilate_breaks_resolution_tie_by_latest_issuance()
    {
        var older = new S104TideCandidate("older", 0.01, T0, 10, Series((T0, 1.0)));
        var newer = new S104TideCandidate("newer", 0.01, T0.AddDays(1), 10, Series((T0, 2.0)));

        var result = _service.Assimilate(Bathy(10.0), [older, newer]);

        Assert.Equal("newer", result!.Tide!.DatasetId);
    }

    [Fact]
    public void Assimilate_prefers_dated_candidate_over_undated_on_tie()
    {
        var undated = new S104TideCandidate("undated", 0.01, null, 10, Series((T0, 1.0)));
        var dated = new S104TideCandidate("dated", 0.01, T0, 10, Series((T0, 2.0)));

        var result = _service.Assimilate(Bathy(10.0), [undated, dated]);

        Assert.Equal("dated", result!.Tide!.DatasetId);
    }

    [Fact]
    public void Assimilate_breaks_full_tie_by_dataset_id_regardless_of_order()
    {
        // Equal resolution and both undated: the selection must be stable and
        // independent of enumeration order, so the ordinally-smaller id wins.
        var alpha = new S104TideCandidate("alpha", 0.01, null, 10, Series((T0, 1.0)));
        var bravo = new S104TideCandidate("bravo", 0.01, null, 10, Series((T0, 2.0)));

        var forward = _service.Assimilate(Bathy(10.0), [alpha, bravo]);
        var reversed = _service.Assimilate(Bathy(10.0), [bravo, alpha]);

        Assert.Equal("alpha", forward!.Tide!.DatasetId);
        Assert.Equal("alpha", reversed!.Tide!.DatasetId);
    }

    [Fact]
    public void Assimilate_flags_datum_mismatch_when_s102_and_s104_differ()
    {
        var result = _service.Assimilate(
            Bathy(10.0, datum: 10),
            [new S104TideCandidate("ds1", 0.01, T0, 23, Series((T0, 1.0)))]);

        Assert.True(result!.DatumsNotReconciled);
    }

    [Fact]
    public void Assimilate_does_not_flag_when_datums_match()
    {
        var result = _service.Assimilate(
            Bathy(10.0, datum: 23),
            [new S104TideCandidate("ds1", 0.01, T0, 23, Series((T0, 1.0)))]);

        Assert.False(result!.DatumsNotReconciled);
    }

    [Fact]
    public void Assimilate_flags_when_datum_unknown()
    {
        var result = _service.Assimilate(
            Bathy(10.0, datum: null),
            [new S104TideCandidate("ds1", 0.01, T0, 23, Series((T0, 1.0)))]);

        Assert.True(result!.DatumsNotReconciled);
    }

    [Fact]
    public void Assimilate_never_flags_for_vector_base()
    {
        var result = _service.Assimilate(
            Vector(7.0),
            [new S104TideCandidate("ds1", 0.01, T0, 23, Series((T0, 1.0)))]);

        Assert.False(result!.DatumsNotReconciled);
        Assert.Null(result.UncertaintyMeters);
        Assert.Equal(8.0, result.DepthOverTime[0].DepthMeters);
    }
}
