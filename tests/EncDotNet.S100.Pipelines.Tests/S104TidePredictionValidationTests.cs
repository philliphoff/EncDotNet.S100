using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S104.Writing;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Tides.Tests;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Harmonic predictions written as S-104 (#1455) load through the S-104
/// pipeline and pass its validation pack, at NOAA's Seattle station.
/// </summary>
public sealed class S104TidePredictionValidationTests
{
    [Fact]
    public void PredictedSeattle_PassesTheS104ValidationPack()
    {
        var station = NoaaFixture.StationOnMllw("9447130");
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dataset = S104TidePredictions.CreateDataset(
            station, new GeoPosition(47.6026, -122.3393), start, start.AddDays(7), TimeSpan.FromMinutes(6), start);
        var directory = Directory.CreateTempSubdirectory("s104-predicted-").FullName;
        var path = Path.Combine(directory, S104TidePredictions.FileName(station));
        try
        {
            PureHdfWriter.Write(S104StationSeriesWriter.Encode(dataset), path);

            var processor = new S104DatasetProcessor(path, IdentityFactory.Instance);
            var report = Assert.IsType<ValidationReport>(processor.Validate());

            Assert.Empty(report.Findings);
            Assert.Equal(3, report.RulesEvaluated);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class IdentityFactory : ICrsTransformFactory
    {
        public static readonly IdentityFactory Instance = new();

        public ICrsTransform Create(string sourceCrs, string targetCrs) => IdentityCrsTransform.Instance;
    }
}
