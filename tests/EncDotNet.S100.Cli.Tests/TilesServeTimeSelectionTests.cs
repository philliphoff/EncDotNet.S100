using System.Security.Cryptography;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #1676: <c>tiles serve</c> draws each time-varying dataset at a time
/// asked for by its own <c>TimeSemantics</c>, as the viewer's map does, rather
/// than snapping every time to the nearest listed step: a tropical cyclone
/// (<c>continuous</c>) at the time itself, an S-104 (<c>atOrBefore</c>) at its
/// step at or before it, and an S-111 (<c>nearest</c>) at its nearest step.
/// </summary>
public sealed class TilesServeTimeSelectionTests
{
    private static string S104 => Path.Combine(AppContext.BaseDirectory, "TestData", "S104", "104US004SC1CO_20251217T12Z.h5");

    private static string S111 => Path.Combine(AppContext.BaseDirectory, "TestData", "S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");

    private static DateTime Utc(int month, int day, int hour, int minute = 0, int second = 0) =>
        new(2022, month, day, hour, minute, second, DateTimeKind.Utc);

    // Ian advisory 20: forecast points from 28 Sep 00Z (tau 0) to 2 Oct 00Z;
    // its past track from 22 Sep 18Z.
    private static readonly DateTime Tau0 = Utc(9, 28, 0);

    private static readonly DateTime LastPoint = Utc(10, 2, 0);

    private static RenderedTileSource OpenRendered(TilesServeCommand.Settings settings)
    {
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        return source!;
    }

    /// <summary>
    /// A hash of every tile at zoom 6 (or the nearest zoom served) at
    /// <paramref name="time"/>, as the issue's mosaics compare them.
    /// </summary>
    private static async Task<string> MosaicAsync(RenderedTileSource source, DateTime? time)
    {
        int zoom = Math.Clamp(6, source.Layout.MinZoom, source.Layout.MaxZoom);
        var area = source.Layout.Area;
        var (firstRow, lastRow) = XyzTileGrid.Rows(zoom, area.MinY, area.MaxY);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (int x in XyzTileGrid.Columns(zoom, area.MinX, area.MaxX))
        {
            for (int y = firstRow; y <= lastRow; y++)
            {
                var tile = await source.ReadAsync(zoom, x, y, null, time, TestContext.Current.CancellationToken);
                hash.AppendData(BitConverter.GetBytes(tile?.Length ?? -1));
                if (tile is not null)
                    hash.AppendData(tile);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    [Fact]
    public async Task A_tropical_cyclone_draws_at_the_time_itself()
    {
        Assert.SkipUnless(File.Exists(TropicalCycloneFixtures.IanForecast), "Ian fixture not present.");
        using var source = OpenRendered(new TilesServeCommand.Settings { Input = TropicalCycloneFixtures.IanForecast, Parallel = 2 });

        // The steps are still the forecast points.
        Assert.Equal(Tau0, source.Times[0]);
        Assert.Equal(LastPoint, source.Times[^1]);

        var tau0 = await MosaicAsync(source, Tau0);
        var past = await MosaicAsync(source, Utc(9, 25, 12));
        var issued = await MosaicAsync(source, Utc(9, 28, 3));
        var later = await MosaicAsync(source, Utc(9, 28, 6));
        var last = await MosaicAsync(source, LastPoint);
        var beyond = await MosaicAsync(source, Utc(10, 5, 0));

        // On the past track, and between forecast points, the storm moves
        // (before #1676 these all drew tau 0).
        Assert.NotEqual(tau0, past);
        Assert.NotEqual(tau0, issued);
        Assert.NotEqual(tau0, later);
        Assert.NotEqual(issued, later);

        // Beyond the forecast, the cone and track without the storm.
        Assert.NotEqual(last, beyond);

        // Times in the same minute share their tiles.
        Assert.Equal(issued, await MosaicAsync(source, Utc(9, 28, 3, 0, 40)));
    }

    [Fact]
    public void A_tropical_cyclone_chooses_the_time_to_the_minute()
    {
        Assert.SkipUnless(File.Exists(TropicalCycloneFixtures.IanForecast), "Ian fixture not present.");
        Assert.Equal(0, TileRenderSession.TryOpen(new TilesServeCommand.Settings { Input = TropicalCycloneFixtures.IanForecast }, out var session));
        using (session)
        {
            foreach (bool strict in new[] { false, true })
            {
                var choice = session!.ChooseTime(Utc(9, 28, 3, 7, 59), strict);
                Assert.Equal(Utc(9, 28, 3, 7), choice.Time);
                Assert.False(choice.Empty);
                var described = Assert.Single(session.DescribeTime(choice));
                Assert.Equal(Datasets.Pipelines.TimeStepSelection.Continuous, described.Selection);
                Assert.Equal(Utc(9, 28, 3, 7), described.Step);

                // The same minute is the same scene.
                Assert.Equal(choice with { Steps = null }, session.ChooseTime(Utc(9, 28, 3, 7, 1), strict) with { Steps = null });

                // Before the first best-track point and after the last
                // forecast point it still draws, with or without --strict-time.
                Assert.Equal(Utc(9, 1, 0), Assert.Single(session.DescribeTime(session.ChooseTime(Utc(9, 1, 0), strict))).Step);
                Assert.Equal(Utc(10, 5, 0), Assert.Single(session.DescribeTime(session.ChooseTime(Utc(10, 5, 0), strict))).Step);
            }

            // On a forecast point, the scene of that step.
            var onStep = session!.ChooseTime(Tau0, strict: false);
            Assert.Equal(Tau0, onStep.Time);
            Assert.Null(onStep.Variant);
        }
    }

    [Fact]
    public void A_tropical_cyclone_reports_its_selection_and_range()
    {
        Assert.SkipUnless(File.Exists(TropicalCycloneFixtures.IanForecast), "Ian fixture not present.");
        using var source = OpenRendered(new TilesServeCommand.Settings { Input = TropicalCycloneFixtures.IanForecast, Parallel = 2 });

        var dataset = Assert.Single(source.ToDatasetsJson()["datasets"]!.AsArray())!;
        Assert.Equal("continuous", (string?)dataset["timeSelection"]);
        Assert.Equal("2022-09-22T18:00:00Z", (string?)dataset["timeRange"]![0]);
        Assert.Equal("2022-10-02T00:00:00Z", (string?)dataset["timeRange"]![1]);

        var json = source.ToTileJson(null, Utc(9, 28, 3, 30, 15));
        Assert.Equal("2022-09-28T03:30:15Z", (string?)json["s100"]!["time"]);
        var described = Assert.Single(json["s100"]!["datasets"]!.AsArray())!;
        Assert.Equal("continuous", (string?)described["timeSelection"]);
        Assert.Equal("2022-09-28T03:30:00Z", (string?)described["time"]);

        // Without a time, the selection alone.
        var plain = Assert.Single(source.ToTileJson(null)["s100"]!["datasets"]!.AsArray())!;
        Assert.Equal("continuous", (string?)plain["timeSelection"]);
        Assert.Null(plain.AsObject()["time"]);
    }

    [Fact]
    public void At_or_before_never_draws_a_later_step()
    {
        Assert.SkipUnless(File.Exists(S104), "S-104 fixture not present.");
        Assert.Equal(0, TileRenderSession.TryOpen(new TilesServeCommand.Settings { Input = S104 }, out var session));
        using (session)
        {
            var steps = session!.Times;
            Assert.True(steps.Count >= 3);

            foreach (bool strict in new[] { false, true })
            {
                // Just before the next step: still this one.
                var between = session.ChooseTime(steps[2] - TimeSpan.FromMinutes(1), strict);
                var described = Assert.Single(session.DescribeTime(between));
                Assert.Equal(Datasets.Pipelines.TimeStepSelection.AtOrBefore, described.Selection);
                Assert.Equal(steps[1], described.Step);
                Assert.Equal(steps[1], between.Time);
                Assert.False(between.Empty);

                // Times that draw the same step share a scene.
                Assert.Equal(between with { Steps = null }, session.ChooseTime(steps[1] + TimeSpan.FromMinutes(1), strict) with { Steps = null });

                // Before the first step, nothing.
                var before = session.ChooseTime(steps[0] - TimeSpan.FromMinutes(1), strict);
                Assert.Null(Assert.Single(session.DescribeTime(before)).Step);
                Assert.True(before.Empty);
            }

            // After the last step: held however long, or for a step with --strict-time.
            var after = steps[^1] + TimeSpan.FromDays(30);
            Assert.Equal(steps[^1], Assert.Single(session.DescribeTime(session.ChooseTime(after, strict: false))).Step);
            Assert.Equal(steps[^1], Assert.Single(session.DescribeTime(session.ChooseTime(steps[^1] + TimeSpan.FromMinutes(1), strict: true))).Step);
            Assert.True(session.ChooseTime(after, strict: true).Empty);
        }
    }

    [Fact]
    public async Task At_or_before_draws_no_tile_before_the_first_step()
    {
        Assert.SkipUnless(File.Exists(S104), "S-104 fixture not present.");
        using var source = OpenRendered(new TilesServeCommand.Settings { Input = S104, Parallel = 2 });
        var first = source.Times[0];

        var dataset = Assert.Single(source.ToDatasetsJson()["datasets"]!.AsArray())!;
        Assert.Equal("atOrBefore", (string?)dataset["timeSelection"]);
        Assert.Equal(TileRenderSession.FormatTime(first), (string?)dataset["timeRange"]![0]);

        Assert.NotEqual(await MosaicAsync(source, null), await MosaicAsync(source, first.AddMinutes(-1)));
        Assert.Equal(await MosaicAsync(source, first), await MosaicAsync(source, first.AddMinutes(1)));
    }

    [Fact]
    public void Nearest_draws_the_nearest_step_before_or_after()
    {
        Assert.SkipUnless(File.Exists(S111), "S-111 fixture not present.");
        Assert.Equal(0, TileRenderSession.TryOpen(new TilesServeCommand.Settings { Input = S111 }, out var session));
        using (session)
        {
            var steps = session!.Times;
            foreach (bool strict in new[] { false, true })
            {
                var choice = session.ChooseTime(steps[2] - TimeSpan.FromMinutes(20), strict);
                var described = Assert.Single(session.DescribeTime(choice));
                Assert.Equal(Datasets.Pipelines.TimeStepSelection.Nearest, described.Selection);
                Assert.Equal(steps[2], described.Step);
                Assert.Null(choice.Variant);

                Assert.Equal(steps[0], Assert.Single(session.DescribeTime(session.ChooseTime(steps[0] - TimeSpan.FromMinutes(30), strict))).Step);
                Assert.Equal(steps[^1], Assert.Single(session.DescribeTime(session.ChooseTime(steps[^1] + TimeSpan.FromMinutes(30), strict))).Step);
            }

            // Far before the first step and after the last: held, or nothing with --strict-time.
            Assert.Equal(steps[0], Assert.Single(session.DescribeTime(session.ChooseTime(steps[0].AddDays(-3), strict: false))).Step);
            Assert.Equal(steps[^1], Assert.Single(session.DescribeTime(session.ChooseTime(steps[^1].AddDays(3), strict: false))).Step);
            Assert.True(session.ChooseTime(steps[0].AddDays(-3), strict: true).Empty);
            Assert.True(session.ChooseTime(steps[^1].AddDays(3), strict: true).Empty);
        }
    }
}
