using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Testing.Scenarios.Hdf5;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// How S-104 water levels and S-111 currents were produced (#1505): the
/// processors read <c>dataDynamicity</c> (S-104) and <c>typeOfCurrentData</c>
/// (S-111) onto one <see cref="DataDynamicity"/>, and label every picked value
/// with it.
/// </summary>
public sealed class DataDynamicityTests
{
    private static readonly ProjNetCrsTransformFactory Transforms = new();

    // The fixture's 2×2 grid starts at 50°N 1°W, 0.01° a cell.
    private const double Latitude = 50.005;
    private const double Longitude = -0.995;

    private static readonly DateTime FixtureIssueTime = new(2021, 4, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, null)]
    [InlineData(1, DataDynamicity.Observation)]
    [InlineData(2, DataDynamicity.AstronomicalPrediction)]
    [InlineData(3, DataDynamicity.AnalysisOrHybrid)]
    [InlineData(4, DataDynamicity.HydrodynamicHindcast)]
    [InlineData(5, DataDynamicity.HydrodynamicForecast)]
    [InlineData(0, null)]
    [InlineData(6, null)]
    public void S104_codes_are_its_dataDynamicity(int? code, DataDynamicity? expected) =>
        Assert.Equal(expected, DataDynamicityCodes.FromS104(code));

    [Theory]
    [InlineData(null, null)]
    [InlineData(1, DataDynamicity.Observation)]
    [InlineData(2, DataDynamicity.Observation)]
    [InlineData(3, DataDynamicity.AstronomicalPrediction)]
    [InlineData(4, DataDynamicity.AnalysisOrHybrid)]
    [InlineData(5, DataDynamicity.HydrodynamicHindcast)]
    [InlineData(6, DataDynamicity.HydrodynamicForecast)]
    [InlineData(7, null)]
    public void S111_typeOfCurrentData_maps_onto_S104_numbers(int? code, DataDynamicity? expected) =>
        Assert.Equal(expected, DataDynamicityCodes.FromS111TypeOfCurrentData(code));

    [Fact]
    public void An_S104_forecast_labels_its_values_with_the_run_from_the_issue_time()
    {
        using var file = WriteS104(dataDynamicity: 5);
        var processor = new S104DatasetProcessor(file.Path, Transforms);

        Assert.Equal(DataDynamicity.HydrodynamicForecast, ((ITimeAwareDatasetProcessor)processor).DataDynamicity);
        var row = Assert.Single(processor.GetCoverageInfo(Latitude, Longitude, null)!.Attributes, a => a.Code == PickAttribute.DataDynamicityCode);
        Assert.Equal("Data", row.Name);
        Assert.Equal("5", row.RawValue);
        Assert.Equal(FixtureIssueTime, row.DateTimeValue);
        Assert.Equal("Forecast, run 2021-04-01 00:00Z", row.DisplayValue);
    }

    [Fact]
    public void An_S104_prediction_says_it_is_not_a_forecast_and_carries_no_run()
    {
        using var file = WriteS104(dataDynamicity: 2);
        var processor = new S104DatasetProcessor(file.Path, Transforms);

        var row = Assert.Single(processor.GetCoverageInfo(Latitude, Longitude, null)!.Attributes, a => a.Code == PickAttribute.DataDynamicityCode);
        Assert.Equal("2", row.RawValue);
        Assert.Null(row.DateTimeValue);
        Assert.Equal("Predicted · astronomical tide only, not a forecast", row.DisplayValue);
    }

    [Fact]
    public void An_S104_without_dataDynamicity_has_no_label()
    {
        using var file = WriteS104(dataDynamicity: null);
        var processor = new S104DatasetProcessor(file.Path, Transforms);

        Assert.Null(((ITimeAwareDatasetProcessor)processor).DataDynamicity);
        Assert.DoesNotContain(processor.GetCoverageInfo(Latitude, Longitude, null)!.Attributes, a => a.Code == PickAttribute.DataDynamicityCode);
    }

    [Fact]
    public void An_S111_forecast_labels_its_values_from_typeOfCurrentData()
    {
        using var file = new TempFile(".h5");
        var values = Enumerable.Range(0, 4)
            .Select(i => new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 0.5f * i, SurfaceCurrentDirection = 45f })
            .ToArray();
        S111FixtureBuilder.WriteFile(file.Path, values, 2, 2, useF64GridAttrs: true, useUnsignedCounts: false); // typeOfCurrentData 6
        using var manager = S111TestCatalogues.Create();
        using var processor = new S111DatasetProcessor(file.Path, manager, Transforms);

        Assert.Equal(DataDynamicity.HydrodynamicForecast, ((ITimeAwareDatasetProcessor)processor).DataDynamicity);
        var row = Assert.Single(processor.GetCoverageInfo(Latitude, Longitude, null)!.Attributes, a => a.Code == PickAttribute.DataDynamicityCode);
        Assert.Equal("5", row.RawValue);
        Assert.Equal(FixtureIssueTime, row.DateTimeValue);
    }

    private static TempFile WriteS104(int? dataDynamicity)
    {
        var file = new TempFile(".h5");
        var values = Enumerable.Range(0, 4)
            .Select(i => new S104FixtureBuilder.SpecRow { WaterLevelHeight = 0.5f + i * 0.1f, WaterLevelTrend = 1 })
            .ToArray();
        S104FixtureBuilder.WriteFile(file.Path, values, 2, 2, useF64GridAttrs: true, useUnsignedCounts: false, dataDynamicity: dataDynamicity);
        return file;
    }

    private sealed class TempFile(string extension) : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + extension);

        public void Dispose()
        {
            try { File.Delete(Path); }
            catch (IOException) { /* best effort */ }
        }
    }
}
