using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// In-force bulletins in the viewer (#992): the panel, the Warning card and
/// MCP judge every part with <see cref="NavigationalWarningsService.Set"/>, so a
/// warning the latest bulletin leaves out is "Ended" everywhere, with why.
/// </summary>
public sealed class NavigationalWarningsBulletinTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public NavigationalWarningsBulletinTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private static DateTimeOffset Oct(int day, int hour = 0) => new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);

    /// <summary>A dataset of parts, plus an optional whole-dataset message (a bulletin with no parts).</summary>
    private sealed class FakeProcessor(NavigationalWarningInfo? message, params (NavigationalWarningInfo Warning, GeoPosition? At)[] parts) : IDatasetProcessor
    {
        public string ProductSpec => "S-124";
        public SpecRef Spec { get; } = new("S-124", default);
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
        public IEnumerable<FeatureSummary> EnumerateFeatures() => [];
        public NavigationalWarningInfo? GetNavigationalWarningMessage() => message;

        public IReadOnlyList<NavigationalWarningFeature> GetNavigationalWarnings() =>
            parts.Select((p, i) => new NavigationalWarningFeature($"part.{i}", i, p.Warning)).ToList();

        public S100FeatureGeometry? GetFeatureGeometryAt(int ordinal) => new()
        {
            Primitive = parts[ordinal].At is null ? S100GeometryType.None : S100GeometryType.Point,
            Points = parts[ordinal].At is { } at ? [at] : [],
        };
    }

    private sealed class NoPick : IPickService
    {
        public void HandlePick(Mapsui.MapInfo? mapInfo, IReadOnlyList<DynamicPickHit>? dynamicHits = null) { }
        public void PresentGeographicPick(double latitude, double longitude, IReadOnlyList<GeographicPickFeature> features) { }
        public bool NavigateToReference(EncDotNet.S100.Datasets.Pipelines.FeatureReference reference) => false;
        public bool OpenFeature(IDatasetProcessor processor, string featureRef, string datasetFileName) => false;
        public bool OpenFeatureAt(IDatasetProcessor processor, int ordinal, string datasetFileName) => false;
    }

    private sealed class ViewTime(DateTimeOffset current) : IViewTime
    {
        public DateTimeOffset Current { get; } = current;
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;
        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static NavigationalWarningInfo Warning(string series, int number, DateTimeOffset published) => new()
    {
        Series = series,
        Number = $"{number:D3}/26",
        Reference = new NavigationalWarningReference(series, number, 2026),
        WarningType = "Survey operations",
        GeneralArea = "Coral Sea",
        Issuer = "AMSA",
        PublicationTime = published,
        Validity = [new NavigationalWarningValidity(published, null)],
        Texts = [new NavigationalWarningText("eng", $"{series} {number:D3}/26")],
    };

    private static NavigationalWarningInfo Bulletin(string series, int number, DateTimeOffset published, params int[] listed) =>
        Warning(series, number, published) with
        {
            WarningType = "Warnings in force",
            InForceBulletin = new NavigationalWarningBulletin(
                [.. listed.Select(n => new NavigationalWarningReference(series, n, 2026))],
                NoMessageOnHand: listed.Length == 0),
        };

    /// <summary>
    /// NAVAREA X: 104 (listed), 105 (left out), 111 (after the bulletin) and
    /// bulletin 110 (text only); AUSCOAST 190 and a no-message bulletin 200
    /// that has no parts, so only its dataset message carries it.
    /// </summary>
    private sealed class Fixture
    {
        public FakeDatasetLoaderService Loader { get; } = new();
        public NavigationalWarningsService Service { get; }
        public NavigationalWarningsViewModel Panel { get; }

        public Fixture(DateTimeOffset at)
        {
            Loader.Processors = new Dictionary<DatasetEntry, IDatasetProcessor>
            {
                [new DatasetEntry("/tmp/navarea_x.gml", "S-124")] = new FakeProcessor(null,
                    (Warning("NAVAREA X", 104, Oct(5)), new GeoPosition(-16.5, 146.0)),
                    (Warning("NAVAREA X", 105, Oct(6)), new GeoPosition(-17.0, 146.5)),
                    (Warning("NAVAREA X", 111, Oct(13)), new GeoPosition(-17.5, 147.0)),
                    (Bulletin("NAVAREA X", 110, Oct(12), 104, 110), null)),
                [new DatasetEntry("/tmp/auscoast_190.gml", "S-124")] = new FakeProcessor(null,
                    (Warning("AUSCOAST", 190, Oct(1)), new GeoPosition(-34.0, 151.5))),
                [new DatasetEntry("/tmp/auscoast_200.gml", "S-124")] = new FakeProcessor(Bulletin("AUSCOAST", 200, Oct(12))),
            };
            Service = new NavigationalWarningsService(Loader);
            Panel = new NavigationalWarningsViewModel(
                Service,
                new NoPick(),
                viewTime: new ViewTime(at),
                timeFormat: new Utc(),
                loader: Loader,
                clock: new FixedClock(Now));
        }

        public NavigationalWarningEntry Entry(string number) =>
            Service.Warnings.Single(w => w.Warning.Number == number);
    }

    private static string Shape(NavigationalWarningsViewModel panel) => string.Join(" | ", panel.Items.Select(i => i switch
    {
        NavigationalWarningGroupItem g => $"[{g.Name} {g.Count}]",
        NavigationalWarningRowItem r => r.Number,
        _ => "?",
    }));

    [Fact]
    public void Warnings_the_bulletin_leaves_out_drop_into_not_in_force_with_why()
    {
        var f = new Fixture(Oct(14));
        f.Panel.IsNotInForceExpanded = true;

        Assert.Equal(
            "[NAVAREA X 3] | 111/26 | 110/26 | 104/26 | [Not in force at view time 2] | 190/26 | 105/26",
            Shape(f.Panel));
        Assert.Equal(3, f.Panel.InForceCount);
        Assert.Equal(2, f.Panel.NotInForceCount);
        var dropped = f.Panel.Items.OfType<NavigationalWarningRowItem>().Single(r => r.Number == "105/26");
        Assert.Equal("Ended — not in in-force bulletin NAVAREA X 110/26", dropped.SubText);
        var coastal = f.Panel.Items.OfType<NavigationalWarningRowItem>().Single(r => r.Number == "190/26");
        Assert.Equal("Ended — not in in-force bulletin AUSCOAST 200/26", coastal.SubText);
    }

    [Fact]
    public void Before_the_bulletins_every_warning_is_in_force()
    {
        var f = new Fixture(Oct(11));

        Assert.Equal("[AUSCOAST 1] | 190/26 | [NAVAREA X 2] | 105/26 | 104/26 | [Not in force at view time 2]", Shape(f.Panel));
        // 111/26 and bulletin 110/26 haven't started yet; that's their validity, not a bulletin.
        Assert.Equal(
            new NavigationalWarningState(NavigationalWarningStatus.NotYetInForce, NavigationalWarningStatusReason.Validity),
            f.Service.StatusAt(f.Entry("111/26"), Oct(11)));
    }

    [Fact]
    public void The_card_pill_and_reason_follow_the_set()
    {
        var f = new Fixture(Oct(14));
        var card = new NavigationalWarningCardViewModel(f.Entry("105/26").Warning, "navarea_x.gml", TimeFormat.Utc, Oct(14));

        // On its own the card can't know about the bulletin.
        Assert.True(card.IsInForce);
        Assert.False(card.HasStatusReason);

        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        card.SetWarningSet(f.Service.Set);

        Assert.True(card.IsEnded);
        Assert.Equal("Ended", card.StatusText);
        Assert.Equal("Ended — not in in-force bulletin NAVAREA X 110/26", card.StatusReasonText);
        Assert.Contains(nameof(NavigationalWarningCardViewModel.StatusReasonText), changed);

        card.SetViewTime(Oct(11));
        Assert.True(card.IsInForce);
        Assert.Null(card.StatusReasonText);
    }

    [Fact]
    public void The_card_gets_the_set_from_the_panel()
    {
        var f = new Fixture(Oct(14));

        Assert.Same(f.Service.Set, ((INavigationalWarningNavigator)f.Panel).WarningSet);
        Assert.Equal(6, f.Service.Set.Count);
    }

    [Fact]
    public void Mcp_lists_the_status_reason_and_the_ending_bulletin()
    {
        var f = new Fixture(Oct(14));

        var dto = NavigationalWarningsToolBase.ToDto(f.Panel, f.Service);

        var dropped = dto.Warnings.Single(w => w.Number == "105/26");
        Assert.Equal("ended", dropped.Status);
        Assert.Equal("not_in_bulletin", dropped.StatusReason);
        Assert.Equal("NAVAREA X 110/26", dropped.EndedBy);
        Assert.Equal("Ended — not in in-force bulletin NAVAREA X 110/26", dropped.StatusReasonText);
        var listed = dto.Warnings.Single(w => w.Number == "104/26");
        Assert.Equal(("in_force", (string?)null, (string?)null), (listed.Status, listed.StatusReason, listed.EndedBy));
        Assert.Equal(3, dto.InForce);
    }
}
