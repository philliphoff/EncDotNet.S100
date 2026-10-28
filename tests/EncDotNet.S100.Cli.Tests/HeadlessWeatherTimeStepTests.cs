using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Hosting;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Mutable;
using EncDotNet.S100.TestSupport;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #1076: in <c>s100 mcp serve</c>'s headless session, the generic time
/// paths (#1065) work for weather data with no weather-specific code:
/// <c>list_time_steps</c> lists a GRIB2, NetCDF or CAP dataset's steps,
/// <c>set_time_step</c> chooses among them, and the next render draws that
/// step.
/// </summary>
public sealed class HeadlessWeatherTimeStepTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("weather-session-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    public static TheoryData<string> Formats => ["GRIB2", "NetCDF", "CAP"];

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task List_and_set_time_step_use_the_datasets_steps(string format)
    {
        var path = Write(format);
        using var catalog = new HeadlessMutableCatalog();
        var outcome = await catalog.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var id = Assert.Single(outcome.Added);
        using var session = new HeadlessS100Session(catalog);

        var listed = await new ListTimeStepsTool(catalog).InvokeAsync(new ListTimeStepsRequest(id), TestContext.Current.CancellationToken);
        Assert.True(listed.TryGetValue(out var steps), listed.ToString());
        Assert.Equal(format, steps.Spec.Name);
        Assert.Equal(Expected(format), steps.Times.Select(t => t.UtcDateTime));
        Assert.Equal(session.AvailableSteps, steps.Times.Select(t => t.UtcDateTime));

        var set = await new SetTimeStepTool(new StaticCapabilityAccessor<ITimeController>(session))
            .InvokeAsync(new SetTimeStepRequest(Timestamp: steps.Times[^1].AddMinutes(10).ToString("O")), TestContext.Current.CancellationToken);
        Assert.True(set.TryGetValue(out var applied), set.ToString());
        Assert.Equal(steps.Times.Count - 1, applied.Index);
        Assert.Equal(steps.Times.Count, applied.SampleCount);
        Assert.Equal(steps.Times[^1].UtcDateTime, ((ITimeController)session).Current);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task The_time_step_set_changes_what_the_session_renders(string format)
    {
        var path = Write(format);
        using var catalog = new HeadlessMutableCatalog();
        await catalog.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        using var session = new HeadlessS100Session(catalog);
        var time = (ITimeController)session;
        var renderer = (IImageRenderer)session;
        var steps = time.AvailableSteps;

        // CAP: the first step is when the circle's warning starts, the last
        // after every warning has ended, so nothing is drawn then.
        await time.SetTimeAsync(steps[0], TestContext.Current.CancellationToken);
        var first = await renderer.RenderToPngAsync(200, 200, pixelDensity: 1.0, cancellationToken: TestContext.Current.CancellationToken);
        await time.SetTimeAsync(steps[^1], TestContext.Current.CancellationToken);
        var last = await renderer.RenderToPngAsync(200, 200, pixelDensity: 1.0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(last);
        using var a = SKBitmap.Decode(first);
        using var b = SKBitmap.Decode(last);
        int changed = 0;
        for (int y = 0; y < a.Height; y += 2)
        {
            for (int x = 0; x < a.Width; x += 2)
            {
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                    changed++;
            }
        }

        Assert.True(changed > 100, $"Only {changed} sampled pixels differ between the first and last {format} steps.");
    }

    private string Write(string format) => format switch
    {
        "GRIB2" => GribFixtures.CopyFixture(GribFixtures.WaveFilter, Path.Combine(_dir, "waves.grib2")),
        "NetCDF" => NetCdfFixtures.WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc")),
        _ => CapFixtures.CopyFixture(CapFixtures.Circle, Path.Combine(_dir, "circle.xml")),
    };

    private static DateTime[] Expected(string format) => format switch
    {
        "GRIB2" => [GribFixtures.Run, GribFixtures.Run.AddHours(3), GribFixtures.Run.AddHours(6)],
        "NetCDF" => NetCdfFixtures.ChartGrid.Times,
        // The circle's warning in force from 15:00 to 18:00; the zone-coded
        // area has the same times.
        _ => [new DateTime(2026, 8, 28, 15, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 28, 18, 0, 0, DateTimeKind.Utc).AddTicks(1)],
    };
}
