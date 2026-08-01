using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

public class S111Dcf1ProcessorTests
{
    [Fact]
    public async Task Processor_PreservesExplicitTimesAndRendersStationGlyphs()
    {
        var path = CreateTempPath();
        try
        {
            S111Dcf1FixtureBuilder.WriteFile(
                path,
                [
                    new() { Latitude = 52.88, Longitude = 4.61 },
                    new() { Latitude = 52.89, Longitude = 4.62 },
                ],
                [
                    TimeStep("20240101T000000Z", (0.3f, 45f), (0.6f, 90f)),
                    TimeStep("20240101T013000Z", (0.5f, 60f), (0.8f, 120f)),
                ],
                declaredInterval: 43_200);
            using var catalogues = new PortrayalCatalogueManager();
            var processor = new S111DatasetProcessor(path, catalogues, new ProjNetCrsTransformFactory());

            Assert.Equal(
                [
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 1, 1, 30, 0, DateTimeKind.Utc),
                ],
                processor.AvailableTimes);

            using var bitmap = await processor.RenderHeadlessAsync(
                256,
                256,
                new S111RenderContext { TimeStep = processor.AvailableTimes[1] });

            Assert.Contains(
                Enumerable.Range(0, bitmap.Width).SelectMany(x =>
                    Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y))),
                color => color != SKColors.White);
            var report = Assert.IsType<ValidationReport>(processor.Validate());
            Assert.Empty(report.Findings);
            Assert.Equal(4, report.RulesEvaluated);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_Dcf1_RejectsDirectionOf360Degrees()
    {
        var path = CreateTempPath();
        try
        {
            S111Dcf1FixtureBuilder.WriteFile(
                path,
                [new() { Latitude = 52.88, Longitude = 4.61 }],
                [TimeStep("20240101T000000Z", (0.3f, 360f))]);
            using var catalogues = new PortrayalCatalogueManager();
            var processor = new S111DatasetProcessor(path, catalogues, new ProjNetCrsTransformFactory());

            var report = Assert.IsType<ValidationReport>(processor.Validate());

            Assert.Contains(report.Findings, finding => finding.RuleId == "S111-STATION-DIRECTION");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_Dcf1_AllowsNoDataSpeedAndDirection()
    {
        var path = CreateTempPath();
        try
        {
            S111Dcf1FixtureBuilder.WriteFile(
                path,
                [new() { Latitude = 52.88, Longitude = 4.61 }],
                [TimeStep(
                    "20240101T000000Z",
                    (S111CoverageSource.FillValue, S111CoverageSource.FillValue))]);
            using var catalogues = new PortrayalCatalogueManager();
            var processor = new S111DatasetProcessor(path, catalogues, new ProjNetCrsTransformFactory());

            var report = Assert.IsType<ValidationReport>(processor.Validate());

            Assert.Empty(report.Findings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");

    private static S111Dcf1FixtureBuilder.TimeStep TimeStep(
        string timePoint,
        params (float Speed, float Direction)[] values) =>
        new()
        {
            TimePoint = timePoint,
            Values = values.Select(value => new S111Dcf1FixtureBuilder.ValueRow
            {
                SurfaceCurrentSpeed = value.Speed,
                SurfaceCurrentDirection = value.Direction,
            }).ToArray(),
        };
}
