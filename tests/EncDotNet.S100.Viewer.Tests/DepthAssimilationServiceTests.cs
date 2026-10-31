using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Services.Depth;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The pick report's depth assimilation over the water-level field: the
/// series order is the shared source's (<c>CatalogWaterLevelSource</c>, tested
/// with the passage check's sources), heights are read as the passage check
/// reads them, and water level on an incompatible datum is refused.
/// </summary>
public class DepthAssimilationServiceTests
{
    private const int Lat = 23;
    private const int Mllw = 12;

    private readonly DepthAssimilationService _service = new();

    private static readonly DateTime T0 = new(2024, 7, 23, 0, 0, 0, DateTimeKind.Utc);

    private static StepWaterLevelSeries Series(string id, int? datum, params (DateTime Time, double? Height)[] points) =>
        new(id, datum, points.Select(p => p.Time).ToList(), points.Select(p => p.Height).ToList());

    private static BaseDepthResult Bathy(double depth, double? unc = 0.3, int? datum = Lat) =>
        new(depth, BaseDepthSource.Bathymetry, unc, datum, SoundingDistanceMeters: null);

    private static BaseDepthResult Vector(double depth, int? datum = Lat) =>
        new(depth, BaseDepthSource.DepthArea, null, datum, null);

    [Fact]
    public void Assimilate_returns_null_when_no_base_depth()
    {
        Assert.Null(_service.Assimilate(null, []));
    }

    [Fact]
    public void Assimilate_composes_adjusted_depth()
    {
        var result = _service.Assimilate(
            Bathy(12.0),
            [Series("ds1", Lat, (T0, 1.5), (T0.AddMinutes(10), -1.0))]);

        Assert.NotNull(result);
        Assert.Equal(2, result!.DepthOverTime.Count);
        Assert.Equal(13.5, result.DepthOverTime[0].DepthMeters);
        Assert.Equal(11.0, result.DepthOverTime[1].DepthMeters);
        Assert.Equal("ds1", result.Tide!.DatasetId);
        Assert.Equal(0.3, result.UncertaintyMeters);
        Assert.Null(result.Refusal);
    }

    [Fact]
    public void Water_level_at_a_time_is_interpolated_and_never_clamped()
    {
        var result = _service.Assimilate(Bathy(12.0), [Series("ds1", Lat, (T0, 1.0), (T0.AddHours(1), 2.0))])!;

        Assert.Equal(1.5, result.WaterLevelAt(T0.AddMinutes(30))!.Value.HeightMeters, 6);
        Assert.Null(result.WaterLevelAt(T0.AddHours(2)));
        Assert.Null(result.WaterLevelAt(T0.AddMinutes(-1)));
    }

    [Fact]
    public void Assimilate_maps_nodata_to_null_depth()
    {
        var result = _service.Assimilate(
            Bathy(12.0),
            [Series("ds1", Lat, (T0, null), (T0.AddMinutes(10), 2.0))]);

        Assert.NotNull(result);
        Assert.Null(result!.DepthOverTime[0].DepthMeters);
        Assert.Equal(14.0, result.DepthOverTime[1].DepthMeters);
    }

    [Fact]
    public void Assimilate_partial_state_when_no_series_covers()
    {
        var result = _service.Assimilate(Bathy(12.0), []);

        Assert.NotNull(result);
        Assert.Null(result!.Tide);
        Assert.Empty(result.DepthOverTime);
        Assert.Equal(0.3, result.UncertaintyMeters);
        Assert.Null(result.Refusal);
    }

    [Fact]
    public void Assimilate_falls_back_to_static_when_all_steps_are_nodata()
    {
        // The grid overlaps but every step at the picked cell is NoData (a
        // land-masked cell): the static base-only state, not an all-null curve.
        var result = _service.Assimilate(
            Bathy(0.9),
            [Series("ds1", Lat, (T0, null), (T0.AddMinutes(10), null))]);

        Assert.NotNull(result);
        Assert.Null(result!.Tide);
        Assert.Empty(result.DepthOverTime);
        Assert.Equal(0.9, result.Base.DepthMeters);
        Assert.Null(result.Refusal);
    }

    [Fact]
    public void Assimilate_uses_the_source_order()
    {
        var preferred = Series("fine", Lat, (T0, 2.0));
        var other = Series("coarse", Lat, (T0, 1.0));

        var result = _service.Assimilate(Bathy(10.0), [preferred, other]);

        Assert.Equal("fine", result!.Tide!.DatasetId);
        Assert.Equal(12.0, result.DepthOverTime[0].DepthMeters);
    }

    [Theory]
    [InlineData(Mllw, Lat, DatumAgreement.Different)]
    [InlineData(null, Lat, DatumAgreement.Unknown)]
    [InlineData(Lat, null, DatumAgreement.Unknown)]
    [InlineData(Lat, 4326, DatumAgreement.Unknown)] // an EPSG code isn't an S-100 datum
    public void Incompatible_datums_are_refused_not_applied(int? depthDatum, int? waterDatum, DatumAgreement agreement)
    {
        var result = _service.Assimilate(Bathy(10.0, datum: depthDatum), [Series("ds1", waterDatum, (T0, 1.0))])!;

        Assert.Null(result.Tide);
        Assert.Empty(result.DepthOverTime);
        Assert.Null(result.WaterLevelAt(T0));
        var refusal = Assert.IsType<WaterLevelRefusal>(result.Refusal);
        Assert.Equal(agreement, refusal.Agreement);
        Assert.Equal(depthDatum, refusal.DepthDatumCode);
        Assert.Equal(waterDatum, refusal.WaterLevelDatumCode);
        Assert.Equal("ds1", refusal.WaterLevelDatasetId);
    }

    [Fact]
    public void A_series_on_the_depth_datum_is_used_past_a_refused_one()
    {
        var result = _service.Assimilate(
            Bathy(10.0, datum: Lat),
            [Series("mllw", Mllw, (T0, 5.0)), Series("lat", Lat, (T0, 1.0))])!;

        Assert.Equal("lat", result.Tide!.DatasetId);
        Assert.Equal(11.0, result.DepthOverTime[0].DepthMeters);
        Assert.Null(result.Refusal);
    }

    [Fact]
    public void S101_base_is_checked_against_its_sounding_datum()
    {
        var applied = _service.Assimilate(Vector(7.0, datum: Lat), [Series("ds1", Lat, (T0, 1.0))])!;
        var refused = _service.Assimilate(Vector(7.0, datum: Mllw), [Series("ds1", Lat, (T0, 1.0))])!;

        Assert.Null(applied.UncertaintyMeters);
        Assert.Equal(8.0, applied.DepthOverTime[0].DepthMeters);
        Assert.Equal(DatumAgreement.Different, refused.Refusal!.Agreement);
        Assert.Equal(BaseDepthSource.DepthArea, refused.Refusal.DepthSource);
    }

    [Fact]
    public void Curve_carries_the_combined_uncertainty()
    {
        var series = new StepWaterLevelSeries("ds1", Lat, [T0], [1.0], uncertainty: 0.4);

        var result = _service.Assimilate(Bathy(10.0, unc: 0.3), [series])!;

        Assert.Equal(0.5, result.DepthOverTime[0].UncertaintyMeters!.Value, 9);
    }

    [Theory]
    [InlineData(0.3, 0.4, 0.5)]
    [InlineData(0.3, null, 0.3)]
    [InlineData(null, 0.4, 0.4)]
    [InlineData(null, null, null)]
    public void Uncertainties_combine_as_independent(double? charted, double? water, double? expected) =>
        Assert.Equal(expected, LocationDepthResult.CombineUncertainty(charted, water) is { } c ? Math.Round(c, 9) : null);
}
