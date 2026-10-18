using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Core.Tests;

/// <summary>
/// <see cref="NavigationalWarningSet"/> (#992): cancellations and in-force
/// bulletins end warnings (S-124 Edition 2.0.0 §9.2.1, §9.3).
/// </summary>
public class NavigationalWarningSetTests
{
    private const string NavareaX = "NAVAREA X";

    private static DateTimeOffset Oct(int day, int hour = 0) => new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);

    private static NavigationalWarningReference Ref(int number, string series = NavareaX, int year = 2026) =>
        new(series, number, year);

    private static NavigationalWarningInfo Warning(int number, DateTimeOffset? published, string series = NavareaX) => new()
    {
        Series = series,
        Number = $"{number:D3}/26",
        Reference = Ref(number, series),
        PublicationTime = published,
    };

    private static NavigationalWarningInfo Bulletin(int number, DateTimeOffset published, string series = NavareaX, params int[] listed) =>
        Warning(number, published, series) with
        {
            InForceBulletin = new NavigationalWarningBulletin([.. listed.Select(n => Ref(n, series))], NoMessageOnHand: false),
        };

    private static NavigationalWarningInfo NoMessage(int number, DateTimeOffset published, string series = NavareaX) =>
        Warning(number, published, series) with { InForceBulletin = new NavigationalWarningBulletin([], NoMessageOnHand: true) };

    [Fact]
    public void A_listed_warning_stays_in_force()
    {
        var listed = Warning(104, Oct(5));
        var set = new NavigationalWarningSet([listed, Bulletin(110, Oct(12), NavareaX, 104, 110)]);

        Assert.Equal(NavigationalWarningState.InForce, set.StatusAt(listed, Oct(14)));
    }

    [Fact]
    public void A_warning_left_out_ends_and_names_the_bulletin()
    {
        var dropped = Warning(105, Oct(6));
        var bulletin = Bulletin(110, Oct(12), NavareaX, 104, 110);
        var set = new NavigationalWarningSet([dropped, bulletin]);

        var state = set.StatusAt(dropped, Oct(14));

        Assert.Equal(NavigationalWarningStatus.Ended, state.Status);
        Assert.Equal(NavigationalWarningStatusReason.NotInBulletin, state.Reason);
        Assert.Same(bulletin, state.By);
        Assert.True(state.IsWithdrawn);
    }

    [Fact]
    public void Before_the_bulletin_is_published_it_judges_nothing()
    {
        var dropped = Warning(105, Oct(6));
        var set = new NavigationalWarningSet([dropped, Bulletin(110, Oct(12), NavareaX, 104)]);

        Assert.Equal(NavigationalWarningStatus.InForce, set.StatusAt(dropped, Oct(11)).Status);
        Assert.Equal(NavigationalWarningStatus.Ended, set.StatusAt(dropped, Oct(12)).Status);
    }

    [Fact]
    public void A_warning_issued_after_the_bulletin_is_not_judged_by_it()
    {
        var later = Warning(111, Oct(13));
        var set = new NavigationalWarningSet([later, Bulletin(110, Oct(12), NavareaX, 104)]);

        Assert.Equal(NavigationalWarningState.InForce, set.StatusAt(later, Oct(14)));
    }

    [Fact]
    public void No_message_on_hand_ends_every_earlier_warning_of_the_series()
    {
        var earlier = Warning(190, Oct(1), "AUSCOAST");
        var set = new NavigationalWarningSet([earlier, NoMessage(200, Oct(12), "AUSCOAST")]);

        var state = set.StatusAt(earlier, Oct(14));

        Assert.Equal(NavigationalWarningStatusReason.NotInBulletin, state.Reason);
    }

    [Fact]
    public void The_latest_of_two_bulletins_wins()
    {
        var a = Warning(104, Oct(5));
        var b = Warning(105, Oct(6));
        var first = Bulletin(110, Oct(5, 12), NavareaX, 104);
        var second = Bulletin(112, Oct(12), NavareaX, 105, 112);
        var set = new NavigationalWarningSet([second, a, first, b]);

        // Between them the first judges: 105 was issued after it.
        Assert.Equal(NavigationalWarningStatus.InForce, set.StatusAt(a, Oct(8)).Status);
        Assert.Equal(NavigationalWarningStatus.InForce, set.StatusAt(b, Oct(8)).Status);

        // After the second, only it counts: 104 drops, 105 is back in its list.
        Assert.Equal(NavigationalWarningStatusReason.NotInBulletin, set.StatusAt(a, Oct(13)).Reason);
        Assert.Same(second, set.StatusAt(a, Oct(13)).By);
        Assert.Equal(NavigationalWarningStatus.InForce, set.StatusAt(b, Oct(13)).Status);
    }

    [Fact]
    public void A_later_bulletin_replaces_the_earlier_one()
    {
        var first = Bulletin(110, Oct(5), NavareaX, 104, 110);
        var second = Bulletin(112, Oct(12), NavareaX, 104, 112);
        var set = new NavigationalWarningSet([first, second]);

        Assert.Equal(NavigationalWarningState.InForce, set.StatusAt(first, Oct(8)));
        var state = set.StatusAt(first, Oct(13));
        Assert.Equal(NavigationalWarningStatusReason.BulletinReplaced, state.Reason);
        Assert.Same(second, state.By);
        Assert.Equal(NavigationalWarningState.InForce, set.StatusAt(second, Oct(13)));
    }

    [Fact]
    public void Another_series_bulletin_does_not_affect_a_warning()
    {
        var navarea = Warning(105, Oct(6));
        var set = new NavigationalWarningSet([navarea, NoMessage(200, Oct(12), "AUSCOAST")]);

        Assert.Equal(NavigationalWarningState.InForce, set.StatusAt(navarea, Oct(14)));
    }

    [Fact]
    public void Series_compare_ignoring_case_and_spacing()
    {
        var dropped = Warning(105, Oct(6), "NAVAREA X");
        var set = new NavigationalWarningSet([dropped, NoMessage(200, Oct(12), "navareax")]);

        Assert.Equal(NavigationalWarningStatusReason.NotInBulletin, set.StatusAt(dropped, Oct(14)).Reason);
    }

    [Fact]
    public void Without_publication_times_bulletins_judge_nothing()
    {
        var undated = Warning(105, published: null);
        var dated = Warning(106, Oct(6));
        var undatedBulletin = Warning(110, published: null) with { InForceBulletin = new NavigationalWarningBulletin([], true) };
        var set = new NavigationalWarningSet([undated, dated, undatedBulletin]);

        Assert.Equal(NavigationalWarningState.InForce, set.StatusAt(undated, Oct(14)));
        Assert.Equal(NavigationalWarningState.InForce, set.StatusAt(dated, Oct(14)));
        Assert.Null(set.LatestBulletin(NavareaX, Oct(14)));
    }

    [Fact]
    public void A_cancellation_ends_the_warning_once_published()
    {
        var cancelled = Warning(104, Oct(5));
        var canceller = Warning(108, Oct(9)) with { Cancels = [Ref(104)] };
        var set = new NavigationalWarningSet([cancelled, canceller]);

        Assert.Equal(NavigationalWarningState.InForce, set.StatusAt(cancelled, Oct(8)));
        var state = set.StatusAt(cancelled, Oct(9));
        Assert.Equal(NavigationalWarningStatusReason.Cancelled, state.Reason);
        Assert.Same(canceller, state.By);
        Assert.Equal(NavigationalWarningState.InForce, set.StatusAt(canceller, Oct(9)));
    }

    [Fact]
    public void A_cancellation_comes_before_the_bulletin_and_the_date_before_both()
    {
        var warning = Warning(105, Oct(6)) with { CancellationDate = Oct(10) };
        var set = new NavigationalWarningSet([
            warning,
            Warning(108, Oct(9)) with { Cancels = [Ref(105)] },
            NoMessage(110, Oct(9)),
        ]);

        Assert.Equal(NavigationalWarningStatusReason.Cancelled, set.StatusAt(warning, Oct(9, 12)).Reason);
        Assert.Equal(NavigationalWarningStatusReason.CancellationDate, set.StatusAt(warning, Oct(10)).Reason);
    }

    [Fact]
    public void Validity_still_applies_with_the_reason()
    {
        var warning = Warning(104, Oct(5)) with { Validity = [new NavigationalWarningValidity(Oct(20), Oct(25))] };
        var set = new NavigationalWarningSet([warning, Bulletin(110, Oct(12), NavareaX, 104)]);

        Assert.Equal(new NavigationalWarningState(NavigationalWarningStatus.NotYetInForce, NavigationalWarningStatusReason.Validity), set.StatusAt(warning, Oct(14)));
        Assert.Equal(NavigationalWarningState.InForce, set.StatusAt(warning, Oct(21)));
        Assert.Equal(NavigationalWarningStatusReason.Validity, set.StatusAt(warning, Oct(26)).Reason);
    }

    [Fact]
    public void The_interoperability_identifier_matches_across_series_spellings()
    {
        var warning = Warning(105, Oct(6)) with
        {
            Reference = Ref(105) with { InteroperabilityIdentifier = "urn:mrn:zz:105" },
        };
        var bulletin = Warning(110, Oct(12)) with
        {
            InForceBulletin = new NavigationalWarningBulletin(
                [new NavigationalWarningReference("NAVAREA 10", 105, 2026) { InteroperabilityIdentifier = "URN:MRN:ZZ:105" }],
                NoMessageOnHand: false),
        };

        Assert.Equal(NavigationalWarningState.InForce, new NavigationalWarningSet([warning, bulletin]).StatusAt(warning, Oct(14)));
    }

    [Fact]
    public void An_empty_set_judges_each_warning_alone()
    {
        var warning = Warning(104, Oct(5)) with { CancellationDate = Oct(10) };

        Assert.Equal(NavigationalWarningState.InForce, NavigationalWarningSet.Empty.StatusAt(warning, Oct(9)));
        Assert.Equal(NavigationalWarningStatusReason.CancellationDate, NavigationalWarningSet.Empty.StatusAt(warning, Oct(10)).Reason);
    }

    [Fact]
    public void Latest_bulletins_list_what_is_not_received()
    {
        var listed = Warning(104, Oct(5));
        var older = Bulletin(100, Oct(5), NavareaX, 99);
        var bulletin = Bulletin(110, Oct(12), NavareaX, 103, 104, 107, 110);
        var set = new NavigationalWarningSet([listed, older, bulletin, NoMessage(200, Oct(12), "AUSCOAST")]);

        var reports = set.LatestBulletins(Oct(14));

        var navarea = Assert.Single(reports, r => r.Series == NavareaX);
        Assert.Same(bulletin, navarea.Bulletin);
        Assert.Equal([103, 107], navarea.NotReceived.Select(r => r.Number));
        Assert.Empty(Assert.Single(reports, r => r.Series == "AUSCOAST").NotReceived);

        // Before the latest, the older bulletin is the series' latest.
        Assert.Equal([99], Assert.Single(set.LatestBulletins(Oct(8))).NotReceived.Select(r => r.Number));
        Assert.Empty(set.LatestBulletins(Oct(4)));
    }

    [Fact]
    public void IsInForceAt_is_the_renderers_test()
    {
        var dropped = Warning(105, Oct(6));
        var set = new NavigationalWarningSet([dropped, Bulletin(110, Oct(12), NavareaX, 104)]);

        Assert.True(set.IsInForceAt(dropped, Oct(11)));
        Assert.False(set.IsInForceAt(dropped, Oct(12)));
    }
}
