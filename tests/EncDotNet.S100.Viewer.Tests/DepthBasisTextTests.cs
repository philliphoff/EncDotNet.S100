using System.Globalization;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Depth;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The depth basis indicator's words (#1191, <c>docs/design/depth-basis-indicator.md</c> §2.2–§2.3).</summary>
public sealed class DepthBasisTextTests
{
    private static readonly DateTimeOffset ViewTime = new(2026, 10, 31, 14, 20, 0, TimeSpan.Zero);
    private static readonly DateTime Start = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static DepthBasis Charted(IReadOnlyList<DepthBasisSource>? depth = null) => new(
        DepthBasisKind.Charted, DepthBasisReason.None, null, ViewTime, null, true, null,
        depth ?? [new DepthBasisSource("US5WA22M", "S-101", 12)], []);

    private static DepthBasis NotApplied(WaterLevelSourceKind kind = WaterLevelSourceKind.Grid, DateTime? run = null) => new(
        DepthBasisKind.Charted, DepthBasisReason.NotApplied, null, ViewTime, null, true, DatumAgreement.Same,
        [new DepthBasisSource("US5WA22M", "S-101", 12)],
        [new DepthBasisSource("104US00_CBOFS", "S-104", 12, Start, End, kind, run)]);

    [Fact]
    public void Charted_has_one_form_and_no_accent()
    {
        var forms = DepthBasisText.Pill(Charted(), TimeFormat.Utc);

        Assert.Equal("Charted", forms.Full);
        Assert.Equal("Charted", forms.Short);
        Assert.Equal("Charted", forms.Compact);
        Assert.Equal(DepthBasisTone.Neutral, forms.Tone);
        Assert.False(forms.ShowsIcon);
    }

    [Fact]
    public void Not_applied_steps_down_through_whole_forms_with_its_icon()
    {
        var forms = DepthBasisText.Pill(NotApplied(), TimeFormat.Utc);

        Assert.Equal("Charted · water level not applied", forms.Full);
        Assert.Equal("Charted · not applied", forms.Short);
        Assert.Equal("Charted", forms.Compact);
        Assert.Equal(DepthBasisTone.Info, forms.Tone);
        Assert.True(forms.ShowsIcon);
        Assert.DoesNotContain("…", forms.Full + forms.Short + forms.Compact);
    }

    [Fact]
    public void The_forms_come_from_their_own_strings()
    {
        var forms = DepthBasisText.Pill(NotApplied(), TimeFormat.Utc);

        Assert.Equal(Strings.Status_DepthBasis_ChartedNotApplied, forms.Full);
        Assert.Equal(Strings.Status_DepthBasis_ChartedNotApplied_Short, forms.Short);
        Assert.Equal(Strings.Status_DepthBasis_ChartedNotApplied_Compact, forms.Compact);
    }

    [Fact]
    public void Display_is_the_full_form_and_empty_when_hidden()
    {
        Assert.Equal("Charted · water level not applied", DepthBasisText.Display(NotApplied(), TimeFormat.Utc));
        Assert.Equal(string.Empty, DepthBasisText.Display(DepthBasis.Hidden, TimeFormat.Utc));
    }

    [Fact]
    public void The_accessible_name_carries_the_state()
    {
        Assert.Equal("Depth basis: Charted", DepthBasisText.AccessibleName(Charted(), TimeFormat.Utc));
        Assert.Equal("Depth basis: Charted, water level not applied", DepthBasisText.AccessibleName(NotApplied(), TimeFormat.Utc));
        Assert.Equal("Depth basis", DepthBasisText.AccessibleName(DepthBasis.Hidden, TimeFormat.Utc));
    }

    [Fact]
    public void The_flyout_and_card_describe_charted_depths()
    {
        Assert.Equal(
            "Charted depths, relative to the vertical datum below. The safety contour and depth shading use these values.",
            DepthBasisText.BasisLine(Charted(), TimeFormat.Utc));
        Assert.Equal("The chart shows charted depths", DepthBasisText.CardLine(Charted(), TimeFormat.Utc));
        Assert.Equal(string.Empty, DepthBasisText.CardLine(DepthBasis.Hidden, TimeFormat.Utc));
    }

    [Fact]
    public void One_datum_in_view_is_named()
    {
        var line = Assert.Single(DepthBasisText.DatumLines(Charted()));

        Assert.Equal("Mean lower low water", line.Label, ignoreCase: true);
        Assert.Equal(string.Empty, line.Datasets);
        Assert.Null(DepthBasisText.DatumHeading(Charted()));
    }

    [Fact]
    public void Several_datums_list_their_datasets()
    {
        var basis = Charted(
        [
            new DepthBasisSource("102US00_SEATTLE", "S-102", 23),
            new DepthBasisSource("US5WA22M", "S-101", 12),
            new DepthBasisSource("US5WA23M", "S-101", 12),
            new DepthBasisSource("US5WA99M", "S-101", null),
        ]);

        Assert.Equal("Several datums in view", DepthBasisText.DatumHeading(basis));
        var lines = DepthBasisText.DatumLines(basis);
        Assert.Equal(3, lines.Count);
        Assert.Equal("US5WA22M, US5WA23M", lines[0].Datasets);
        Assert.Equal("102US00_SEATTLE", lines[1].Datasets);
        Assert.Equal("Not declared", lines[2].Label);
        Assert.Equal("US5WA99M", lines[2].Datasets);
    }

    [Fact]
    public void Sources_are_capped_at_five()
    {
        var basis = Charted([.. Enumerable.Range(1, 7).Select(i => new DepthBasisSource($"CELL{i}", "S-101", 12))]);

        var lines = DepthBasisText.SourceLines(basis);

        Assert.Equal(6, lines.Count);
        Assert.Equal("CELL5", lines[4]);
        Assert.Equal("and 2 more", lines[5]);
        Assert.Equal(["None in view"], DepthBasisText.SourceLines(Charted([])));
    }

    [Fact]
    public void Water_level_lines_name_each_source_and_say_it_is_not_applied()
    {
        var lines = DepthBasisText.WaterLevelLines(NotApplied(run: Start), waterLevelLoaded: true, TimeFormat.Utc);

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("104US00_CBOFS · grid · run ", lines[0]);
        Assert.Contains(TimeFormatting.FormatTimeRange(Start, End, TimeFormat.Utc), lines[0]);
        Assert.Equal("Not applied to the chart.", lines[1]);
    }

    [Fact]
    public void Water_level_lines_say_when_none_is_loaded_or_in_view()
    {
        Assert.Equal(["No S-104 water level loaded."], DepthBasisText.WaterLevelLines(Charted(), waterLevelLoaded: false, TimeFormat.Utc));
        Assert.Equal(["No S-104 water level in view."], DepthBasisText.WaterLevelLines(Charted(), waterLevelLoaded: true, TimeFormat.Utc));
    }

    [Fact]
    public void A_station_series_is_named_as_one()
    {
        var lines = DepthBasisText.WaterLevelLines(NotApplied(WaterLevelSourceKind.Station), waterLevelLoaded: true, TimeFormat.Utc);

        Assert.StartsWith("104US00_CBOFS · station · ", lines[0]);
    }

    [Fact]
    public void Times_follow_the_local_or_utc_setting()
    {
        var utc = DepthBasisText.WaterLevelLines(NotApplied(run: Start), true, TimeFormat.Utc)[0];
        var local = DepthBasisText.WaterLevelLines(NotApplied(run: Start), true, TimeFormat.Local)[0];

        Assert.Contains(TimeFormatting.Format(Start, TimeFormat.Utc), utc);
        Assert.Contains(TimeFormatting.Format(Start, TimeFormat.Local), local);
    }

    [Fact]
    public void Times_follow_the_current_culture()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var line = DepthBasisText.WaterLevelLines(NotApplied(run: Start), true, TimeFormat.Local)[0];
            Assert.Contains(Start.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("de-DE")), line);

            var utc = DepthBasisText.WaterLevelLines(NotApplied(run: Start), true, TimeFormat.Utc)[0];
            Assert.Contains(TimeFormatting.Format(Start, TimeFormat.Utc), utc);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Where_water_level_is_applied()
    {
        Assert.Equal(
            ["Object information: the Depth card at a picked point.", "Route check: clearance at each ETA."],
            DepthBasisText.WhereAppliedLines(chartAdjustmentOn: false));
        Assert.Equal(
            ["The chart, at the Timeline's view time.", "Object information: the Depth card at a picked point.", "Route check: clearance at each ETA."],
            DepthBasisText.WhereAppliedLines(chartAdjustmentOn: true));
    }

    private static readonly DateTime Step = new(2026, 10, 31, 14, 20, 0, DateTimeKind.Utc);

    private static DepthBasis Adjusted(bool wholeView = true, DatumAgreement datum = DatumAgreement.Same) =>
        NotApplied() with
        {
            Kind = DepthBasisKind.Adjusted,
            Reason = DepthBasisReason.None,
            WaterLevelStatus = WaterLevelStatus.Applied,
            WaterLevelStepTime = Step,
            CoversWholeView = wholeView,
            Datum = datum,
        };

    [Fact]
    public void Adjusted_states_name_the_time_the_layers_are_for()
    {
        var whole = DepthBasisText.Pill(Adjusted(), TimeFormat.Utc);
        Assert.Equal("+ water level 14:20 UTC", whole.Full);
        Assert.Equal("Adjusted 14:20 UTC", whole.Compact);
        Assert.Equal(DepthBasisTone.Adjusted, whole.Tone);
        Assert.False(whole.ShowsIcon);

        var part = DepthBasisText.Pill(Adjusted(wholeView: false), TimeFormat.Utc);
        Assert.Equal("+ water level 14:20 UTC · part of view", part.Full);
        Assert.Equal("Part adjusted 14:20 UTC", part.Short);

        Assert.Equal(
            "+ water level 14:20 UTC · datum unconfirmed",
            DepthBasisText.Display(Adjusted(datum: DatumAgreement.Unknown), TimeFormat.Utc));
        Assert.Equal("The chart shows adjusted depths at 14:20 UTC", DepthBasisText.CardLine(Adjusted(), TimeFormat.Utc));
        Assert.StartsWith("Charted depth + water level at 14:20 UTC.", DepthBasisText.BasisLine(Adjusted(), TimeFormat.Utc));
    }

    [Fact]
    public void Unavailable_states_are_charted_with_a_caution()
    {
        var unavailable = NotApplied() with
        {
            Reason = DepthBasisReason.WaterLevel,
            WaterLevelStatus = WaterLevelStatus.AfterEnd,
            WaterLevelStepTime = Step,
        };
        var forms = DepthBasisText.Pill(unavailable, TimeFormat.Utc);
        Assert.Equal("Charted · no water level at 14:20 UTC", forms.Full);
        Assert.Equal("Charted · no water level", forms.Short);
        Assert.Equal("Charted", forms.Compact);
        Assert.Equal(DepthBasisTone.Caution, forms.Tone);
        Assert.True(forms.ShowsIcon);

        var mismatch = unavailable with
        {
            WaterLevelStatus = WaterLevelStatus.DatumMismatch,
            NotAdjusted = [new DepthBasisSource("102US004FL2AI252277", "S-102", 12)],
        };
        Assert.Equal("Charted · datum mismatch", DepthBasisText.Display(mismatch, TimeFormat.Utc));
        Assert.Contains(
            "Not adjusted: 102US004FL2AI252277 on Mean Lower Low Water, water level on Mean Lower Low Water.",
            DepthBasisText.WaterLevelLines(mismatch, waterLevelLoaded: true, TimeFormat.Utc));
    }

    [Fact]
    public void Product_not_adjusted_names_the_product_not_the_datum()
    {
        var basis = new DepthBasis(
            DepthBasisKind.Charted,
            DepthBasisReason.ProductNotAdjusted,
            null,
            new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero),
            null,
            true,
            DatumAgreement.Same,
            [new DepthBasisSource("US5WA22M.000", "S-57", 12)],
            [new DepthBasisSource("tide-predictions", "prediction", 12)],
            [new DepthBasisSource("US5WA22M.000", "S-57", 12)]);

        var forms = DepthBasisText.Pill(basis, TimeFormat.Utc);
        Assert.Equal("Charted · S-57 not adjusted", forms.Full);
        Assert.Equal("Charted", forms.Compact);
        Assert.Equal(DepthBasisTone.Info, forms.Tone);
        Assert.Contains(
            "Not adjusted: US5WA22M.000 (S-57 depths are drawn as charted).",
            DepthBasisText.WaterLevelLines(basis, waterLevelLoaded: true, TimeFormat.Utc));
    }
}
