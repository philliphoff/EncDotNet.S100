using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>How a stretch of the departure band is drawn (handoff D5).</summary>
internal enum DepartureBandKind
{
    /// <summary>Departures that meet your settings (success).</summary>
    Meets,

    /// <summary>Departures below your UKC setting somewhere (destructive at 35 %).</summary>
    Below,

    /// <summary>Departures whose arrival is after the S-104 data ends (hatched).</summary>
    ArrivalAfterDataEnds,

    /// <summary>Departures with no data for part of the passage (muted).</summary>
    NoData,
}

/// <summary>A run of departures of one kind, as fractions of the band's width.</summary>
/// <param name="Start">Left edge, 0–1.</param>
/// <param name="End">Right edge, 0–1.</param>
/// <param name="Kind">How the run is drawn.</param>
/// <param name="First">The run's first departure.</param>
/// <param name="Last">The run's last departure.</param>
internal sealed record DepartureBandSegment(double Start, double End, DepartureBandKind Kind, DateTimeOffset First, DateTimeOffset Last);

/// <summary>
/// The numbers behind the departure band (#903, handoff D5): the swept
/// departures as runs across the S-104 range, and the mapping between a
/// departure and a fraction of the band's width. Like
/// <see cref="ClearanceProfile"/>, it is built per result and owns its
/// x-mapping, so the view only draws and the drag only converts.
/// </summary>
/// <remarks>
/// Departure <c>i</c> stands for half a step either side of it, so the knob
/// sits at the middle of the departure's cell and the runs tile the band.
/// </remarks>
internal sealed class DepartureBand
{
    private DepartureBand(PassageDepartureWindow window, IReadOnlyList<DepartureBandSegment> segments)
    {
        Window = window;
        Segments = segments;
    }

    public PassageDepartureWindow Window { get; }

    /// <summary>The first departure (the band's left end, at half a step in).</summary>
    public DateTimeOffset From => Window.From;

    /// <summary>The last departure, where the S-104 data ends.</summary>
    public DateTimeOffset To => Window.To;

    public TimeSpan Step => Window.Step > TimeSpan.Zero ? Window.Step : PassageCheckService.WindowStep;

    /// <summary>The runs, left to right, tiling 0–1.</summary>
    public IReadOnlyList<DepartureBandSegment> Segments { get; }

    public static DepartureBandKind KindOf(PassageVerdict verdict) => verdict switch
    {
        PassageVerdict.MeetsSettings or PassageVerdict.MeetsSettingsTight => DepartureBandKind.Meets,
        PassageVerdict.BelowSetting => DepartureBandKind.Below,
        PassageVerdict.WaterLevelEndsBeforeArrival => DepartureBandKind.ArrivalAfterDataEnds,
        _ => DepartureBandKind.NoData,
    };

    public static DepartureBand Build(PassageDepartureWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var options = window.Sweep.Options;
        var segments = new List<DepartureBandSegment>();
        var band = new DepartureBand(window, segments);
        var start = 0;
        for (var i = 1; i <= options.Count; i++)
        {
            if (i < options.Count && KindOf(options[i].Verdict) == KindOf(options[start].Verdict))
                continue;
            var first = options[start].Departure;
            var last = options[i - 1].Departure;
            segments.Add(new DepartureBandSegment(
                band.CellStart(first),
                band.CellEnd(last),
                KindOf(options[start].Verdict),
                first,
                last));
            start = i;
        }

        return band;
    }

    // The band spans From − step/2 … To + step/2.
    private double SpanTicks => (To - From).Ticks + Step.Ticks;

    private double CellStart(DateTimeOffset departure) => Math.Clamp(Fraction(departure) - (Step.Ticks / 2.0 / SpanTicks), 0, 1);

    private double CellEnd(DateTimeOffset departure) => Math.Clamp(Fraction(departure) + (Step.Ticks / 2.0 / SpanTicks), 0, 1);

    /// <summary>A departure's position across the band (its cell's middle), clamped to 0–1.</summary>
    public double Fraction(DateTimeOffset departure) =>
        Math.Clamp(((departure - From).Ticks + (Step.Ticks / 2.0)) / SpanTicks, 0, 1);

    /// <summary>The swept departure under a fraction of the band's width, clamped to the range.</summary>
    public DateTimeOffset DepartureAt(double fraction)
    {
        var f = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        var steps = Math.Floor(f * SpanTicks / Step.Ticks);
        var last = Math.Round((To - From).Ticks / (double)Step.Ticks);
        return From + TimeSpan.FromTicks((long)(Math.Clamp(steps, 0, last) * Step.Ticks));
    }

    /// <summary>The departure <paramref name="steps"/> swept departures from <paramref name="departure"/>, clamped to the range.</summary>
    public DateTimeOffset Nudge(DateTimeOffset departure, int steps)
    {
        var index = Math.Round((departure - From).Ticks / (double)Step.Ticks) + steps;
        var last = Math.Round((To - From).Ticks / (double)Step.Ticks);
        return From + TimeSpan.FromTicks((long)(Math.Clamp(index, 0, last) * Step.Ticks));
    }

    /// <summary>The departure at the middle of the range, for the middle axis label.</summary>
    public DateTimeOffset Middle => From + TimeSpan.FromTicks((To - From).Ticks / 2);
}
