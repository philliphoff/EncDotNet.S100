using System.Net;
using EncDotNet.S100.Cli.Web;
using EncDotNet.S100.Cli.Web.Map;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the <c>tiles serve</c> web page's time bar (issue #1580): times
/// in the browser's zone or UTC, the step the page opens on, the all-past and
/// all-future notes, the second line, the day ticks, <c>t=</c> in the hash,
/// and the rendered bar.
/// </summary>
public sealed class ServeTimeBarTests
{
    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2021, month, day, hour, minute, 0, DateTimeKind.Utc);

    // Europe/London around the 2021 change to BST (28 March, 01:00 UTC), as a browser reports it.
    private static readonly ServeTimeZone London = ServeTimeZone.FromBrowser("Europe/London",
    [
        new(Utc(3, 27, 12), TimeSpan.Zero, "GMT"),
        new(Utc(3, 28, 1), TimeSpan.FromHours(1), "BST"),
    ]);

    // The UKHO Solent trial forecast: 288 steps, every 15 minutes from 1 April 2021 00:00 UTC.
    private static readonly DateTime[] Solent = [.. Enumerable.Range(0, 288).Select(i => Utc(4, 1, 0).AddMinutes(15 * i))];

    [Fact]
    public void Times_show_in_the_browsers_zone_with_its_abbreviation_or_in_utc()
    {
        var time = Utc(4, 1, 10);

        Assert.Equal("Thu 1 Apr 2021", ServeTimes.FormatDate(time, London));
        Assert.Equal("11:00 BST", ServeTimes.FormatClock(time, London));
        Assert.Equal("10:00 UTC", ServeTimes.FormatClock(time, ServeTimeZone.Utc));
        Assert.Equal("Europe/London (BST)", London.Describe(time));
        Assert.Equal("UTC", ServeTimeZone.Utc.Describe(time));

        // Before the change the browser's GMT applies; late in the day it's the next day in the zone.
        Assert.Equal("00:30 GMT", ServeTimes.FormatClock(Utc(3, 28, 0, 30), London));
        Assert.Equal("Thu 1 Apr 2021", ServeTimes.FormatDate(Utc(3, 31, 23, 30), London));
        Assert.Equal("Wed 31 Mar 2021", ServeTimes.FormatDate(Utc(3, 31, 23, 30), ServeTimeZone.Utc));
    }

    [Fact]
    public void A_zone_without_an_abbreviation_shows_its_offset()
    {
        var kolkata = ServeTimeZone.FromBrowser("Asia/Kolkata", [new(Utc(1, 1, 0), new TimeSpan(5, 30, 0), "")]);
        var tokyo = ServeTimeZone.FromBrowser("Asia/Tokyo", [new(Utc(1, 1, 0), TimeSpan.FromHours(9), "")]);

        Assert.Equal("15:30 GMT+5:30", ServeTimes.FormatClock(Utc(4, 1, 10), kolkata));
        Assert.Equal("19:00 GMT+9", ServeTimes.FormatClock(Utc(4, 1, 10), tokyo));
        Assert.True(ServeTimeZone.FromBrowser("Europe/London", []).IsUtc);
    }

    [Fact]
    public void The_page_opens_on_the_hash_step_then_now_then_the_first()
    {
        // t= picks the nearest step.
        Assert.Equal(40, ServeTimes.StartIndex(Solent, Utc(11, 1, 0), Utc(4, 1, 10, 2)));

        // Now within the steps: the latest at or before now.
        Assert.Equal(41, ServeTimes.StartIndex(Solent, Utc(4, 1, 10, 20), null));
        Assert.Equal(0, ServeTimes.StartIndex(Solent, Utc(4, 1, 0), null));

        // All past or all future: the first, as the tiles do.
        Assert.Equal(0, ServeTimes.StartIndex(Solent, Utc(11, 18, 0), null));
        Assert.Equal(0, ServeTimes.StartIndex(Solent, Utc(1, 1, 0), null));
        Assert.Equal(0, ServeTimes.StartIndex([], Utc(1, 1, 0), null));
    }

    [Fact]
    public void All_steps_in_the_past_say_so_and_offer_the_latest()
    {
        var now = Utc(11, 18, 12);
        Assert.Equal(ServeTimeSpan.AllPast, ServeTimes.Span(Solent, now));

        var first = ServeTimes.Notice(Solent, 0, now, London)!;
        Assert.Equal("All 288 time steps are in the past (1–4 Apr 2021). Showing the first one, as the tiles do.", first.Text);
        Assert.Equal("Go to latest", first.ActionLabel);
        Assert.Equal(287, first.ActionIndex);

        var later = ServeTimes.Notice(Solent, 40, now, London)!;
        Assert.Equal("All 288 time steps are in the past (1–4 Apr 2021).", later.Text);
        Assert.Equal("Go to latest", later.ActionLabel);

        // At the latest there's nothing to offer.
        Assert.Null(ServeTimes.Notice(Solent, 287, now, London)!.ActionLabel);
    }

    [Fact]
    public void All_steps_in_the_future_say_when_they_start_and_offer_the_first()
    {
        var now = Utc(3, 1, 0);
        Assert.Equal(ServeTimeSpan.AllFuture, ServeTimes.Span(Solent, now));

        var first = ServeTimes.Notice(Solent, 0, now, London)!;
        Assert.Equal("All 288 time steps are in the future. Starts Thu 1 Apr 2021, 01:00 BST.", first.Text);
        Assert.Null(first.ActionLabel);

        var later = ServeTimes.Notice(Solent, 12, now, ServeTimeZone.Utc)!;
        Assert.Equal("All 288 time steps are in the future. Starts Thu 1 Apr 2021, 00:00 UTC.", later.Text);
        Assert.Equal("Go to first step", later.ActionLabel);
        Assert.Equal(0, later.ActionIndex);
    }

    [Fact]
    public void Steps_around_now_have_no_note()
    {
        Assert.Equal(ServeTimeSpan.Current, ServeTimes.Span(Solent, Utc(4, 2, 0)));
        Assert.Null(ServeTimes.Notice(Solent, 0, Utc(4, 2, 0), London));
        Assert.Equal("The time step is in the past (1 Apr 2021).", ServeTimes.Notice([Utc(4, 1, 0)], 0, Utc(5, 1, 0), London)!.Text);
    }

    [Theory]
    [InlineData(15, "every 15 min")]
    [InlineData(60, "every hour")]
    [InlineData(180, "every 3 h")]
    [InlineData(1440, "every day")]
    public void The_interval_is_the_one_most_steps_share(int minutes, string expected) =>
        Assert.Equal(expected, ServeTimes.Interval([.. Enumerable.Range(0, 5).Select(i => Utc(4, 1, 0).AddMinutes(minutes * i))]));

    [Fact]
    public void Irregular_or_single_steps_have_no_interval()
    {
        Assert.Null(ServeTimes.Interval([Utc(4, 1, 0)]));
        Assert.Null(ServeTimes.Interval([Utc(4, 1, 0), Utc(4, 1, 1), Utc(4, 1, 3), Utc(4, 1, 7)]));
    }

    [Fact]
    public void The_second_line_names_the_product_dynamicity_issue_and_step()
    {
        ServedDataset[] datasets =
        [
            new("101GB0040242B", "101GB0040242B.000", "S-101", null, null, null, null, [], null, null, null),
            new("111UK_Solent", "111UK.h5", "S-111", null, null, null, null, Solent, Utc(4, 14, 5).AddSeconds(59), "Forecast", null),
        ];

        var context = ServeTimes.Context(datasets, Solent, 40, London);

        Assert.Equal(["S-111"], context.Products);
        Assert.Equal("Forecast · issued Wed 14 Apr 2021 06:00 BST · step 41 of 288, every 15 min", context.Line);
    }

    [Fact]
    public void Day_ticks_fall_on_each_days_first_step_in_the_zone()
    {
        var utc = ServeTimes.DayTicks(Solent, ServeTimeZone.Utc);
        Assert.Equal(["Thu 1", "Fri 2", "Sat 3"], utc.Select(t => t.Label));
        Assert.Equal(0, utc[0].Fraction);
        Assert.Equal(96.0 / 287, utc[1].Fraction, 6);

        // In BST the day starts an hour earlier in UTC, and the last hour is Sunday.
        var london = ServeTimes.DayTicks(Solent, London);
        Assert.Equal(["Thu 1", "Fri 2", "Sat 3", "Sun 4"], london.Select(t => t.Label));
        Assert.Equal(92.0 / 287, london[1].Fraction, 6);

        // Many days: labels thin out, ticks stay.
        DateTime[] month = [.. Enumerable.Range(0, 30).Select(i => Utc(4, 1, 0).AddDays(i))];
        var thinned = ServeTimes.DayTicks(month, ServeTimeZone.Utc);
        Assert.Equal(30, thinned.Count);
        Assert.Equal(["Thu 1", "Fri 9", "Sat 17", "Sun 25"], thinned.Where(t => t.Label.Length > 0).Select(t => t.Label));

        // Days crowded together by a gap between datasets' steps keep apart.
        DateTime[] gap = [.. Enumerable.Range(0, 96).Select(i => Utc(4, 1, 0).AddHours(i)), .. Enumerable.Range(0, 3).Select(i => Utc(6, 1, 0).AddDays(i))];
        var spaced = ServeTimes.DayTicks(gap, ServeTimeZone.Utc, maxLabels: 7).Where(t => t.Label.Length > 0).ToList();
        Assert.Equal(["Thu 1", "Fri 2", "Sat 3", "Sun 4", "Tue 1"], spaced.Select(t => t.Label));
    }

    [Fact]
    public void The_step_round_trips_through_the_hash()
    {
        Assert.Equal("2021-04-01T10:00Z", ServeTimes.FormatHash(Utc(4, 1, 10)));
        Assert.Equal("2021-04-01T10:00:30Z", ServeTimes.FormatHash(Utc(4, 1, 10).AddSeconds(30)));

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["t"] = "2021-04-01T10:00Z" };
        Assert.Equal(Utc(4, 1, 10), ServeTimes.FromHash(parameters));
        Assert.Equal(DateTimeKind.Utc, ServeTimes.FromHash(parameters)!.Value.Kind);
        Assert.Equal(Utc(4, 1, 10), ServeTimes.FromHash(new Dictionary<string, string> { ["t"] = "2021-04-01T11:00:00+01:00" }));
        Assert.Null(ServeTimes.FromHash(new Dictionary<string, string> { ["t"] = "soon" }));
        Assert.Null(ServeTimes.FromHash(new Dictionary<string, string>()));

        var (_, parsed) = ServeHash.Parse("#13/50.775/-1.235&t=2021-04-01T10:00Z");
        Assert.Equal(Utc(4, 1, 10), ServeTimes.FromHash(parsed));
        Assert.Equal("t=2021-04-01T10:00Z", ServeHash.FormatParameters(parameters));
    }

    [Fact]
    public async Task The_bar_has_the_transport_the_time_the_track_and_local_or_utc()
    {
        var now = Utc(11, 18, 12);
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(TimeBar.Times)] = Solent,
            [nameof(TimeBar.Index)] = 40,
            [nameof(TimeBar.Zone)] = London,
            [nameof(TimeBar.Context)] = new ServeStepContext(["S-111"], "Forecast · step 41 of 288, every 15 min"),
            [nameof(TimeBar.Notice)] = ServeTimes.Notice(Solent, 40, now, London),
        });

        foreach (var label in new[] { "Previous time step", "Play", "Next time step", "Time step" })
            Assert.Contains($"aria-label=\"{label}\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"time-date\">Thu 1 Apr 2021</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"time-clock\">11:00 BST</span>", html, StringComparison.Ordinal);
        Assert.Contains("max=\"287\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"40\"", html, StringComparison.Ordinal);
        foreach (var day in new[] { "Thu 1", "Fri 2", "Sat 3", "Sun 4" })
            Assert.Matches($"<span class=\"time-tick-label\" data-edge=\"[a-z]+\">{day}</span>", html);
        Assert.Matches("aria-checked=\"true\"[^>]*><span>Local</span>", html);
        Assert.Contains("<span class=\"time-product\">S-111</span>", html, StringComparison.Ordinal);
        Assert.Contains("All 288 time steps are in the past (1–4 Apr 2021).", html, StringComparison.Ordinal);
        Assert.Contains(">Go to latest</button>", html, StringComparison.Ordinal);
        Assert.Contains("data-docked=\"false\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_the_ends_the_step_buttons_are_disabled_and_utc_shows_utc()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(TimeBar.Times)] = Solent,
            [nameof(TimeBar.Index)] = 0,
            [nameof(TimeBar.Zone)] = ServeTimeZone.Utc,
            [nameof(TimeBar.Utc)] = true,
            [nameof(TimeBar.Playing)] = true,
            [nameof(TimeBar.Docked)] = true,
        });

        Assert.Matches("aria-label=\"Previous time step\"[^>]*disabled", html);
        Assert.DoesNotMatch("aria-label=\"Next time step\"[^>]*disabled", html);
        Assert.Contains("aria-label=\"Pause\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"time-clock\">00:00 UTC</span>", html, StringComparison.Ordinal);
        Assert.Matches("aria-checked=\"true\"[^>]*><span>UTC</span>", html);
        Assert.Contains("data-docked=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("time-notice", html, StringComparison.Ordinal);
    }

    private static async Task<string> RenderAsync(Dictionary<string, object?> parameters)
    {
        await using var services = new ServiceCollection().AddSingleton<IJSRuntime, NoJSRuntime>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TimeBar>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        return WebUtility.HtmlDecode(html);
    }

    // Static rendering never calls into the page.
    private sealed class NoJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new InvalidOperationException();
    }
}
