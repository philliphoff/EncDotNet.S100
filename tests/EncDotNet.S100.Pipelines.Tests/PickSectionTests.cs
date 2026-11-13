using System.Text.Json;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for pick sections: <see cref="IceEggCodeContributor"/>,
/// <see cref="PickContributorRegistry"/>, <see cref="IceEggCodeSection"/> and
/// <see cref="PickSectionKindRegistry"/>.
/// </summary>
public class PickSectionTests
{
    private static PickContext Context(string featureType, params (string Code, string Value)[] attributes) => new()
    {
        Spec = new SpecRef("S-411", default),
        FeatureRef = "f1",
        FeatureType = featureType,
        Attributes = [.. attributes.Select(a => new PickAttribute { Code = a.Code, RawValue = a.Value })],
    };

    [Fact]
    public void Contributor_SeaIce_GetsEggCode()
    {
        var section = IceEggCodeContributor.Instance.Contribute(
            Context("seaice", ("iceact", "70"), ("iceapc", "[30, 40]"), ("icesod", "[91, 87]"), ("iceflz", "[5, 4]")));

        var egg = Assert.IsType<IceEggCodeSection>(section).Egg;
        Assert.Equal("70", egg.TotalConcentration!.Text);
        Assert.Equal(new[] { "91", "87" }, egg.StagesOfDevelopment.Select(v => v.Text));
    }

    [Fact]
    public void Contributor_AnyFeatureWithAConcentration_GetsEggCode()
    {
        // A customer feed's ice polygon, not an S-411 feature type.
        var section = IceEggCodeContributor.Instance.Contribute(
            Context("AcmeIcePolygon", ("iceact", "9+"), ("icesod", "[93]")));

        Assert.Equal("9+", Assert.IsType<IceEggCodeSection>(section).Egg.TotalConcentration!.Text);
    }

    [Theory]
    [InlineData("LineOfIceLead")]
    [InlineData("IceFracture")]
    public void Contributor_StageOfDevelopmentAlone_GetsNoEggCode(string featureType)
    {
        Assert.Null(IceEggCodeContributor.Instance.Contribute(Context(featureType, ("icesod", "[87]"))));
    }

    [Fact]
    public void Contributor_FeatureWithoutEggAttributes_GetsNoEggCode()
    {
        Assert.Null(IceEggCodeContributor.Instance.Contribute(Context("DepthArea", ("DRVAL1", "5"))));
    }

    [Fact]
    public void Contributor_ResolvesDefinitionsAgainstCatalogueCodes()
    {
        var context = Context("seaice", ("iceact", "70"), ("icesod", "[87]")) with
        {
            ResolveListedValueDefinition = (code, value) => $"{code}:{value}",
        };

        var egg = Assert.IsType<IceEggCodeSection>(IceEggCodeContributor.Instance.Contribute(context)).Egg;

        Assert.Equal("totalConcentration:70", egg.TotalConcentration!.Definition);
        Assert.Equal("iceStageofDevelopment:87", egg.StagesOfDevelopment[0].Definition);
    }

    [Fact]
    public void Registry_SectionTheProcessorSet_WinsOverAContribution()
    {
        var own = new IceEggCodeSection(IceEggCodeBuilder.Build("1", null, null, null)!);
        var registry = PickContributorRegistry.CreateDefault();

        var sections = registry.Contribute(Context("seaice", ("iceact", "70")), [own]);

        Assert.Same(own, Assert.Single(sections));
    }

    [Fact]
    public void Registry_FirstContributorOfAKind_Wins()
    {
        var registry = new PickContributorRegistry();
        registry.Add(new FixedContributor(new TestSection("first")));
        registry.Add(new FixedContributor(new TestSection("second")));

        var section = Assert.Single(registry.Contribute(Context("x")));

        Assert.Equal("first", Assert.IsType<TestSection>(section).Note);
    }

    [Fact]
    public void Registry_NoContribution_ReturnsTheExistingList()
    {
        IReadOnlyList<PickSection> existing = [];

        Assert.Same(existing, PickContributorRegistry.CreateDefault().Contribute(Context("DepthArea"), existing));
    }

    [Fact]
    public void FeatureInfo_EggCodeShim_ReadsTheSection()
    {
        var egg = IceEggCodeBuilder.Build("70", null, null, null)!;
        var info = new FeatureInfo { FeatureRef = "f1", FeatureType = "SeaIce", Attributes = [] }
            .WithSections([new IceEggCodeSection(egg)]);

#pragma warning disable CS0618 // The shim is what's under test.
        Assert.Same(egg, info.EggCode);
#pragma warning restore CS0618
    }

    [Fact]
    public void EggSection_ToRows_ListsEachRow()
    {
        var egg = IceEggCodeBuilder.Build("70", "[30, 40]", "[91, 87]", "[5, 4]", snowDepthCm: 12)!;

        var rows = new IceEggCodeSection(egg).ToRows();

        Assert.Equal(new[] { "Ct", "C", "S", "F", "Sd" }, rows.Select(r => r.Code));
        Assert.Equal("91 87", rows.Single(r => r.Code == "S").RawValue);
    }

    [Fact]
    public void KindRegistry_SerializesTheEggCode()
    {
        var kinds = PickSectionKindRegistry.CreateDefault();
        var section = new IceEggCodeSection(IceEggCodeBuilder.Build("70", null, "[91]", null)!);

        var json = kinds.Find(IceEggCodeSection.KindName)!.Serialize(section);

        var total = json.GetProperty("egg").GetProperty("totalConcentration");
        Assert.Equal("70", total.GetProperty("text").GetString());
        Assert.Equal("TotalConcentration", total.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Array, json.GetProperty("egg").GetProperty("stagesOfDevelopment").ValueKind);
    }

    [Fact]
    public void KindRegistry_DuplicateKind_Throws()
    {
        var kinds = PickSectionKindRegistry.CreateDefault();

        Assert.Throws<ArgumentException>(() => kinds.Register(kinds.Find(IceEggCodeSection.KindName)!));
    }

    private static StationTimeSeriesSnapshot Station() => new()
    {
        StationId = "9447130",
        StationName = "Seattle",
        Latitude = 47.6,
        Longitude = -122.3,
        Times = [new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 13, 1, 0, 0, DateTimeKind.Utc)],
        Channels =
        [
            new StationTimeSeriesChannel { Key = "waterLevelHeight", DisplayName = "Height", Unit = "m", Values = [1.25f, -9999f], FillValue = -9999f },
        ],
    };

    private static NavigationalWarningInfo Warning() => new()
    {
        Series = "NAVAREA IV",
        Number = "123/26",
        WarningType = "Local",
        GeneralArea = "Gulf of Maine",
        Validity = [new NavigationalWarningValidity(new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero), null)],
        Texts = [new NavigationalWarningText("eng", "Buoy off station.")],
        AffectedAreas = [new S100FeatureGeometry { Primitive = S100GeometryType.Point, Points = [new GeoPosition(43, -70)] }],
    };

    [Fact]
    public void StationSeriesSection_ToRows_SkipsFillValues()
    {
        var rows = new StationSeriesSection(Station()).ToRows();

        Assert.Equal("Seattle (9447130)", rows.Single(r => r.Code == "station").RawValue);
        Assert.Equal("1.25 to 1.25 m", rows.Single(r => r.Code == "waterLevelHeight").RawValue);
    }

    [Fact]
    public void NavigationalWarningSection_ToRows_ListsTheMessage()
    {
        var rows = new NavigationalWarningSection(Warning()).ToRows();

        Assert.Equal("NAVAREA IV 123/26", rows.Single(r => r.Code == "warning").RawValue);
        Assert.Equal("2026-11-13 00:00Z to further notice", rows.Single(r => r.Code == "validity").RawValue);
        Assert.Equal("Buoy off station.", rows.Single(r => r.Code == "text").RawValue);
    }

    [Fact]
    public void KindRegistry_SerializesStationSeriesAndWarnings()
    {
        var kinds = PickSectionKindRegistry.CreateDefault();

        var station = kinds.Find(StationSeriesSection.KindName)!.Serialize(new StationSeriesSection(Station()));
        var warning = kinds.Find(NavigationalWarningSection.KindName)!.Serialize(new NavigationalWarningSection(Warning()));

        Assert.Equal("9447130", station.GetProperty("series").GetProperty("stationId").GetString());
        Assert.Equal(2, station.GetProperty("series").GetProperty("times").GetArrayLength());
        Assert.Equal("123/26", warning.GetProperty("warning").GetProperty("number").GetString());
        Assert.Equal("Point", warning.GetProperty("warning").GetProperty("affectedAreas")[0].GetProperty("primitive").GetString());
    }

    [Fact]
    public void FeatureInfo_StationAndWarningShims_ReadTheSections()
    {
        var station = Station();
        var warning = Warning();
        var info = new FeatureInfo { FeatureRef = "f1", FeatureType = "x", Attributes = [] }
            .WithSections([new StationSeriesSection(station), new NavigationalWarningSection(warning)]);

#pragma warning disable CS0618 // The shims are what's under test.
        Assert.Same(station, info.StationSeries);
        Assert.Same(warning, info.NavigationalWarning);
#pragma warning restore CS0618
    }

    private sealed record TestSection(string Note) : PickSection
    {
        public override string Kind => "test.note";

        public override IReadOnlyList<PickAttribute> ToRows() => [];
    }

    private sealed class FixedContributor(PickSection section) : IPickContributor
    {
        public PickSection? Contribute(PickContext context) => section;
    }
}
