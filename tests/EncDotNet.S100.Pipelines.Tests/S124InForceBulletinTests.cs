using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// In-force bulletins read from S-124 Edition 2.0.0 GML (#992): the
/// projection of the bulletin's list, and a <see cref="NavigationalWarningSet"/>
/// built from the datasets as the viewer builds it.
/// </summary>
public class S124InForceBulletinTests
{
    private const string Listed = "navwarn_bulletin_navarea_x_104_26.gml";
    private const string Dropped = "navwarn_bulletin_navarea_x_105_26.gml";
    private const string Later = "navwarn_bulletin_navarea_x_111_26.gml";
    private const string Bulletin = "navwarn_bulletin_navarea_x_110_26_in_force.gml";
    private const string Coastal = "navwarn_bulletin_auscoast_190_26.gml";
    private const string NoMessage = "navwarn_bulletin_auscoast_200_26_in_force.gml";
    private const string Untyped = "navwarn_bulletin_navarea_xi_040_26_in_force.gml";

    private static readonly DateTimeOffset BeforeBulletin = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterBulletin = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);

    private static S124DatasetProcessor CreateProcessor(string fileName)
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return new S124DatasetProcessor(
            Path.Combine(AppContext.BaseDirectory, "TestData", "S124", fileName),
            manager,
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
    }

    private static NavigationalWarningInfo Part(string fileName) =>
        Assert.Single(CreateProcessor(fileName).GetNavigationalWarnings()).Warning;

    private static NavigationalWarningSet Set(params string[] fileNames) =>
        new(fileNames.Select(f => CreateProcessor(f).GetNavigationalWarningMessage()!));

    [Fact]
    public void A_bulletin_projects_its_in_force_list()
    {
        var message = CreateProcessor(Bulletin).GetNavigationalWarningMessage();

        Assert.NotNull(message);
        Assert.Equal("NAVAREA X 110/26", message!.Label);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), message.PublicationTime);
        var bulletin = Assert.IsType<NavigationalWarningBulletin>(message.InForceBulletin);
        Assert.False(bulletin.NoMessageOnHand);
        Assert.Equal([104, 110], bulletin.InForce.Select(r => r.Number));
        Assert.All(bulletin.InForce, r => Assert.Equal("NAVAREA X", r.Series));
        Assert.Equal("urn:mrn:synthetic:s124:navareax:104:2026", bulletin.InForce[0].InteroperabilityIdentifier);

        // Its text-only part carries the list too, so the card can judge it.
        Assert.NotNull(Part(Bulletin).InForceBulletin);
    }

    [Fact]
    public void A_no_message_bulletin_has_no_parts_but_a_message()
    {
        var processor = CreateProcessor(NoMessage);

        Assert.Empty(processor.GetNavigationalWarnings());
        var bulletin = processor.GetNavigationalWarningMessage()?.InForceBulletin;
        Assert.NotNull(bulletin);
        Assert.True(bulletin!.NoMessageOnHand);
        Assert.Empty(bulletin.InForce);
    }

    [Fact]
    public void An_ordinary_warning_is_not_a_bulletin()
    {
        Assert.Null(CreateProcessor(Listed).GetNavigationalWarningMessage()!.InForceBulletin);
        Assert.Null(CreateProcessor("navwarn_ccg_ed2.gml").GetNavigationalWarningMessage()!.InForceBulletin);

        // The CCG "warnings in force" bulletin is text only: no machine-readable list.
        Assert.Null(CreateProcessor("navwarn_ccg_ed2_text_only.gml").GetNavigationalWarningMessage()!.InForceBulletin);
    }

    [Fact]
    public void The_set_ends_warnings_the_bulletin_leaves_out()
    {
        var set = Set(Listed, Dropped, Later, Bulletin, Coastal, NoMessage);

        Assert.Equal(NavigationalWarningStatus.InForce, set.StatusAt(Part(Listed), AfterBulletin).Status);
        Assert.Equal(NavigationalWarningStatus.InForce, set.StatusAt(Part(Later), AfterBulletin).Status);
        Assert.Equal(NavigationalWarningStatus.InForce, set.StatusAt(Part(Bulletin), AfterBulletin).Status);

        var dropped = set.StatusAt(Part(Dropped), AfterBulletin);
        Assert.Equal(NavigationalWarningStatusReason.NotInBulletin, dropped.Reason);
        Assert.Equal("NAVAREA X 110/26", dropped.By?.Label);

        var coastal = set.StatusAt(Part(Coastal), AfterBulletin);
        Assert.Equal(NavigationalWarningStatusReason.NotInBulletin, coastal.Reason);
        Assert.Equal("AUSCOAST 200/26", coastal.By?.Label);
    }

    [Fact]
    public void Before_the_bulletin_everything_is_in_force()
    {
        var set = Set(Listed, Dropped, Bulletin, Coastal, NoMessage);

        Assert.Equal(NavigationalWarningStatus.InForce, set.StatusAt(Part(Dropped), BeforeBulletin).Status);
        Assert.Equal(NavigationalWarningStatus.InForce, set.StatusAt(Part(Coastal), BeforeBulletin).Status);
    }

    [Fact]
    public void Edition_1_warnings_have_no_publication_time_so_bulletins_never_judge_them()
    {
        var edition1 = CreateProcessor("navwarn_point.gml").GetNavigationalWarnings()[0].Warning;

        Assert.Null(edition1.PublicationTime);
        var set = new NavigationalWarningSet([
            edition1,
            new NavigationalWarningInfo
            {
                Series = edition1.Series ?? "TEST",
                Reference = new NavigationalWarningReference(edition1.Series ?? "TEST", 999, 2026),
                PublicationTime = AfterBulletin,
                InForceBulletin = new NavigationalWarningBulletin([], NoMessageOnHand: true),
            },
        ]);
        Assert.Equal(edition1.StatusAt(AfterBulletin), set.StatusAt(edition1, AfterBulletin).Status);
    }

    [Fact]
    public void Reference_category_3_alone_makes_a_bulletin()
    {
        // navwarnTypeGeneral 15 and warningType 4: only the In-Force References says it's a bulletin.
        var message = CreateProcessor(Untyped).GetNavigationalWarningMessage()!;

        Assert.Equal([31, 36], message.InForceBulletin!.InForce.Select(r => r.Number));
    }

    [Fact]
    public void The_set_reports_what_each_latest_bulletin_lists_but_is_not_loaded()
    {
        var reports = Set(Listed, Dropped, Bulletin, NoMessage, Untyped).LatestBulletins(AfterBulletin);

        Assert.Empty(Assert.Single(reports, r => r.Series == "NAVAREA X").NotReceived);
        Assert.Empty(Assert.Single(reports, r => r.Series == "AUSCOAST").NotReceived);
        Assert.Equal([31, 36], Assert.Single(reports, r => r.Series == "NAVAREA XI").NotReceived.Select(r => r.Number));
    }
}
