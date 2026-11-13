using System.Text.Json;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;

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
