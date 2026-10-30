using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// S-101 features with date ranges are drawn at the view time, read in the
/// Local/UTC setting, and count towards "Display Not Real Time" (#1126).
/// </summary>
public sealed class DateDependentDrawingTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour = 12) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private sealed class DatedProcessor(params PortrayalTimeInterval[] intervals) : IDatasetProcessor, IDateDependentDatasetProcessor
    {
        public string ProductSpec => "S-101";
        public SpecRef Spec { get; } = new("S-101", default);
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
        public IEnumerable<FeatureSummary> EnumerateFeatures() => [];
        public IReadOnlyList<PortrayalTimeInterval> PortrayalTimeIntervals { get; set; } = intervals;
    }

    private sealed class PlainProcessor : IDatasetProcessor
    {
        public string ProductSpec => "S-102";
        public SpecRef Spec { get; } = new("S-102", default);
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
        public IEnumerable<FeatureSummary> EnumerateFeatures() => [];
    }

    private sealed class MovableViewTime(DateTimeOffset current) : IViewTime
    {
        public DateTimeOffset Current { get; private set; } = current;
        public event EventHandler? Changed;

        public void MoveTo(DateTimeOffset time)
        {
            Current = time;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class StubTimeFormat(TimeFormat initial) : ITimeFormatProvider
    {
        public TimeFormat Current { get; private set; } = initial;
        public event Action<TimeFormat>? TimeFormatChanged;

        public void Set(TimeFormat value)
        {
            Current = value;
            TimeFormatChanged?.Invoke(value);
        }
    }

    private sealed class Zoned(TimeZoneInfo zone) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => zone;
    }

    private static readonly TimeZoneInfo East = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");

    // Drawn until 30.11.2013.
    private static readonly PortrayalTimeInterval OldScheme =
        new(PortrayalTimeKind.Date, null, "20131130", TimeIntervalClosure.LeSemiInterval);

    private sealed class Fixture
    {
        public FakeDatasetLoaderService Loader { get; } = new();
        public DatasetEntry Chart { get; } = new("/tmp/101AA00X0001.000", "S-101");
        public DatasetEntry Bathymetry { get; } = new("/tmp/102AA00.h5", "S-102");
        public DatedProcessor Processor { get; }
        public MovableViewTime ViewTime { get; } = new(Utc(2013, 11, 30));
        public StubTimeFormat TimeFormat { get; } = new(Viewer.TimeFormat.Utc);
        public DateDependentDrawingService Service { get; }

        public Fixture(params PortrayalTimeInterval[] intervals)
        {
            Processor = new DatedProcessor(intervals);
            Loader.Processors = new Dictionary<DatasetEntry, IDatasetProcessor>
            {
                [Chart] = Processor,
                [Bathymetry] = new PlainProcessor(),
            };
            Service = new DateDependentDrawingService(Loader, ViewTime, TimeFormat, new Zoned(East));
        }
    }

    [Fact]
    public void Dated_charts_get_the_view_time_and_count_as_date_dependent()
    {
        var f = new Fixture(OldScheme);

        Assert.Equal(PortrayalTime.At(Utc(2013, 11, 30), TimeZoneInfo.Utc), f.Chart.PortrayalTime);
        Assert.Null(f.Bathymetry.PortrayalTime);
        Assert.True(f.Service.HasDateDependentData);
    }

    [Fact]
    public void A_chart_with_nothing_dated_is_not_date_dependent()
    {
        var f = new Fixture();

        Assert.False(f.Service.HasDateDependentData);
        Assert.NotNull(f.Chart.PortrayalTime);
    }

    [Fact]
    public void Moving_within_the_dates_keeps_the_drawn_time()
    {
        var f = new Fixture(OldScheme);
        var drawn = f.Chart.PortrayalTime;

        f.ViewTime.MoveTo(Utc(2013, 11, 30, 23));
        Assert.Same(drawn, f.Chart.PortrayalTime);

        f.ViewTime.MoveTo(Utc(2013, 12, 1, 0));
        Assert.Equal(Utc(2013, 12, 1, 0), f.Chart.PortrayalTime!.Start);
    }

    [Fact]
    public void Dates_follow_the_local_utc_setting()
    {
        // 23:30 UTC on 30 November is 1 December in the machine's zone.
        var f = new Fixture(OldScheme);
        f.ViewTime.MoveTo(new DateTimeOffset(2013, 11, 30, 23, 30, 0, TimeSpan.Zero));
        var utc = f.Chart.PortrayalTime;

        f.TimeFormat.Set(TimeFormat.Local);

        Assert.NotSame(utc, f.Chart.PortrayalTime);
        Assert.Equal(East, f.Chart.PortrayalTime!.Zone);
    }

    [Fact]
    public void On_load_policy_sets_the_time_for_dated_products_only()
    {
        var f = new Fixture(OldScheme);
        var policy = f.Loader.PortrayalTimeOnLoad!;

        Assert.NotNull(policy(f.Chart, f.Processor));
        Assert.Null(policy(f.Bathymetry, new PlainProcessor()));
    }

    [Fact]
    public void Changed_is_raised_when_dated_data_arrives_or_leaves()
    {
        var f = new Fixture();
        var raised = 0;
        f.Service.Changed += (_, _) => raised++;

        f.Processor.PortrayalTimeIntervals = [OldScheme];
        f.Loader.RaiseLoaded(f.Chart);
        Assert.True(f.Service.HasDateDependentData);

        f.Loader.Processors = new Dictionary<DatasetEntry, IDatasetProcessor>();
        f.Loader.RaiseRemoved(f.Chart);
        Assert.False(f.Service.HasDateDependentData);
        Assert.Equal(2, raised);
    }
}
