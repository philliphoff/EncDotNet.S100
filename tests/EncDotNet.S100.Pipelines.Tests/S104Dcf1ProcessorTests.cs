using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S104.Tests.Fixtures;
using EncDotNet.S100.Validation;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

public class S104Dcf1ProcessorTests
{
    [Fact]
    public async Task Processor_PreservesExplicitTimesAndRendersStationGlyphs()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            S104Dcf1FixtureBuilder.WriteFile(
                path,
                [
                    new() { Latitude = 51.5, Longitude = 3.2 },
                    new() { Latitude = 51.6, Longitude = 3.3 },
                ],
                [
                    TimeStep("20240101T000000Z", (1.0f, 1), (2.0f, 2)),
                    TimeStep("20240101T013000Z", (1.5f, 2), (2.5f, 3)),
                ],
                declaredInterval: 43_200);
            var processor = new S104DatasetProcessor(path, new ProjNetCrsTransformFactory());

            Assert.Equal(
                [
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 1, 1, 30, 0, DateTimeKind.Utc),
                ],
                processor.AvailableTimes);

            using var bitmap = await processor.RenderHeadlessAsync(
                256,
                256,
                new S104RenderContext { TimeStep = processor.AvailableTimes[1] });

            Assert.Contains(
                Enumerable.Range(0, bitmap.Width).SelectMany(x =>
                    Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y))),
                color => color != SKColors.White);
            Assert.Empty(Assert.IsType<ValidationReport>(processor.Validate()).Findings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static S104Dcf1FixtureBuilder.TimeStep TimeStep(
        string timePoint,
        params (float Height, short Trend)[] values) =>
        new()
        {
            TimePoint = timePoint,
            Values = values.Select(value => new S104Dcf1FixtureBuilder.ValueRow
            {
                WaterLevelHeight = value.Height,
                WaterLevelTrend = value.Trend,
            }).ToArray(),
        };
}
