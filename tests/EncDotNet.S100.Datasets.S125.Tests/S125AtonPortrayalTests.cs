using System.Text;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.S125.Tests;

/// <summary>
/// Tests for the S-125 → S-101 AtoN portrayal projection and for running the
/// bundled S-101 AtoN Lua rules over S-125 data. The S-125 Portrayal
/// Catalogue portrays only status indications and coverage, so the aids
/// themselves are portrayed with S-101 symbology (see the dataset README).
/// </summary>
public class S125AtonPortrayalTests
{
    private static readonly Lazy<FeatureCatalogue> S101Fc = new(() =>
        new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)).GetCatalogue("S-101")
        ?? throw new InvalidOperationException("S-101 feature catalogue not bundled."));

    private static string TestData(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    private static S125Dataset Parse(string members) => S125Dataset.Open(new MemoryStream(Encoding.UTF8.GetBytes($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <S125:Dataset xmlns:S125="http://www.iho.int/S125/1.0" xmlns:S100="http://www.iho.int/s100gml/5.0"
                      xmlns:gml="http://www.opengis.net/gml/3.2" xmlns:xlink="http://www.w3.org/1999/xlink" gml:id="ds">
        {members}
        </S125:Dataset>
        """)));

    private static string Point(string id, double lat, double lon) =>
        $"""<S125:geometry><S100:pointProperty><gml:Point gml:id="p_{id}"><gml:pos>{lat} {lon}</gml:pos></gml:Point></S100:pointProperty></S125:geometry>""";

    [Fact]
    public void Reader_AttributeTree_KeepsRepeatedValuesAndNestedComplexes()
    {
        var dataset = S125Dataset.Open(TestData("aton_us4va1bf.gml"));

        var sectored = dataset.Features.First(f => f.FeatureType == "LightSectored");
        var sector = Assert.Single(sectored.AttributeTree, a => a.Code == "sectorCharacteristics");
        var lightSector = sector.Children.First(c => c.Code == "lightSector");
        Assert.True(lightSector.IsComplex);
        Assert.Contains(lightSector.Children, c => c.Code == "colour" && c.Value is not null);
        Assert.Contains(lightSector.Children, c => c.Code == "directionalCharacter" && c.IsComplex);

        // A multi-colour aid keeps every colour occurrence, in order.
        Assert.Contains(dataset.Features, f => f.AttributeTree.Count(a => a.Code == "colour") > 1);
    }

    [Fact]
    public void Project_LiftsTopmarkOntoParentStructure()
    {
        var dataset = Parse($"""
            <S125:member><S125:LateralBuoy gml:id="buoy">{Point("b", 36.9, -76.0)}
              <S125:buoyShape>2</S125:buoyShape><S125:colour>4</S125:colour><S125:categoryOfLateralMark>2</S125:categoryOfLateralMark>
            </S125:LateralBuoy></S125:member>
            <S125:member><S125:Topmark gml:id="tm">{Point("t", 36.9, -76.0)}
              <S125:topmarkDaymarkShape>1</S125:topmarkDaymarkShape><S125:colour>4</S125:colour>
              <S125:parent xlink:href="#buoy"/>
            </S125:Topmark></S125:member>
            """);

        var aid = Assert.Single(S125AtonPortrayalProjection.Project(dataset, S101Fc.Value));

        Assert.Equal("LateralBuoy", aid.S101FeatureType);
        var topmark = Assert.Single(aid.Attributes, a => a.Code == "topmark");
        Assert.Equal("1", topmark.Children.Single(c => c.Code == "topmarkDaymarkShape").Value);
        Assert.Equal("4", topmark.Children.Single(c => c.Code == "colour").Value);
    }

    [Fact]
    public void Project_LiftsUnassociatedTopmarkOntoCoLocatedStructure()
    {
        var dataset = Parse($"""
            <S125:member><S125:CardinalBeacon gml:id="bcn">{Point("b", 37.0, -76.1)}
              <S125:categoryOfCardinalMark>1</S125:categoryOfCardinalMark>
            </S125:CardinalBeacon></S125:member>
            <S125:member><S125:Topmark gml:id="tm">{Point("t", 37.0, -76.1)}
              <S125:topmarkDaymarkShape>13</S125:topmarkDaymarkShape>
            </S125:Topmark></S125:member>
            """);

        var aid = Assert.Single(S125AtonPortrayalProjection.Project(dataset, S101Fc.Value));

        Assert.Contains(aid.Attributes, a => a.Code == "topmark");
    }

    [Fact]
    public void Project_MapsTypesAndRenamesAttributes()
    {
        var dataset = S125Dataset.Open(TestData("aton_chesapeake.gml"));

        var aids = S125AtonPortrayalProjection.Project(dataset, S101Fc.Value);

        // Types portrayed by the S-125 catalogue itself are not projected.
        Assert.DoesNotContain(aids, a => a.Source.FeatureType is "AtonStatusIndication" or "DataCoverage");

        // Pre-1.0 objectName / MMSICode spellings reach the S-101 rules.
        var landmark = aids.First(a => a.Source.Id == "lm_capehenry");
        Assert.Contains(landmark.Attributes, a => a.Code == "featureName" && a.Children.Any(c => c.Code == "name"));
        Assert.Contains(aids.First(a => a.Source.Id == "pais_1").Attributes, a => a.Code == "mMSICode");

        // S-101 RecommendedTrack binds the simple orientationValue; S-101
        // NavigationLine binds the orientation complex.
        var track = aids.Single(a => a.S101FeatureType == "RecommendedTrack");
        Assert.Equal("315", track.Attributes.Single(a => a.Code == "orientationValue").Value);
        var line = aids.Single(a => a.S101FeatureType == "NavigationLine");
        Assert.Equal("045", line.Attributes.Single(a => a.Code == "orientation").Children.Single().Value);
    }

    [Fact]
    public void Project_PortraysSyntheticAisAsPhysicalAis()
    {
        var dataset = Parse($"""
            <S125:member><S125:SyntheticAISAidToNavigation gml:id="sais">{Point("s", 36.9, -76.0)}
              <S125:mMSICode>993669999</S125:mMSICode>
            </S125:SyntheticAISAidToNavigation></S125:member>
            """);

        var aid = Assert.Single(S125AtonPortrayalProjection.Project(dataset, S101Fc.Value));

        Assert.Equal("PhysicalAISAidToNavigation", aid.S101FeatureType);
    }

    [Theory]
    [InlineData("", 2)]
    [InlineData("sectorCharacteristics:1", 2)]
    [InlineData("sectorCharacteristics:1;lightSector:2", 1)]
    [InlineData("sectorCharacteristics:1;lightSector:3", 0)]
    [InlineData("sectorCharacteristics", 0)]
    [InlineData("sectorCharacteristics:x", 0)]
    public void ResolveScope_NavigatesPart9AAttributePaths(string path, int expectedCount)
    {
        S125AttributeNode Sector(string colour) => new()
        {
            Code = "lightSector",
            Children = [new S125AttributeNode { Code = "colour", Value = colour }],
        };
        IReadOnlyList<S125AttributeNode> tree =
        [
            new S125AttributeNode { Code = "sectorCharacteristics", Children = [Sector("1"), Sector("3")] },
            new S125AttributeNode { Code = "height", Value = "12" },
        ];

        var scope = S125AtonLuaDataProvider.ResolveScope(tree, path);

        Assert.Equal(expectedCount, scope.Count);
        if (path.EndsWith("lightSector:2", StringComparison.Ordinal))
            Assert.Equal("3", scope.Single().Value);
    }

    [Fact]
    public async Task S101Rules_PortrayDerivedNoaaAids()
    {
        var dataset = S125Dataset.Open(TestData("aton_us4va1bf.gml"));
        var instructions = await RunAsync(dataset);

        var symbols = instructions.OfType<PointInstruction>().Select(p => p.SymbolReference ?? "").ToList();

        // Lateral buoys get S-101 shape/colour symbols (conical / can), lights
        // get flares, and lights get their characteristic text.
        Assert.Contains(symbols, s => s.StartsWith("BOYCON", StringComparison.Ordinal));
        Assert.Contains(symbols, s => s.StartsWith("BOYCAN", StringComparison.Ordinal));
        Assert.Contains(symbols, s => s.StartsWith("LIGHTS", StringComparison.Ordinal));
        Assert.Contains(instructions.OfType<TextInstruction>(), t => t.Text.StartsWith("Fl", StringComparison.Ordinal));

        // Sectored lights produce augmented sector geometry anchored on the feature.
        var sectoredIds = dataset.Features.Where(f => f.FeatureType == "LightSectored").Select(f => f.Id).ToHashSet();
        Assert.Contains(instructions.OfType<LineInstruction>(),
            l => sectoredIds.Contains(l.FeatureReference) && l.CoordinatesOverride is { Count: > 1 });

        // Instructions reference S-125 gml:ids so the geometry provider resolves them.
        var featureIds = dataset.Features.Select(f => f.Id).ToHashSet();
        Assert.All(instructions, i => Assert.Contains(i.FeatureReference, featureIds));
    }

    [Fact]
    public async Task S101Rules_PortrayLiftedTopmark()
    {
        var dataset = Parse($"""
            <S125:member><S125:CardinalBuoy gml:id="buoy">{Point("b", 36.9, -76.0)}
              <S125:buoyShape>4</S125:buoyShape><S125:colour>2</S125:colour><S125:colour>6</S125:colour>
              <S125:colourPattern>1</S125:colourPattern><S125:categoryOfCardinalMark>1</S125:categoryOfCardinalMark>
            </S125:CardinalBuoy></S125:member>
            <S125:member><S125:Topmark gml:id="tm">{Point("t", 36.9, -76.0)}
              <S125:topmarkDaymarkShape>13</S125:topmarkDaymarkShape><S125:colour>2</S125:colour>
              <S125:parent xlink:href="#buoy"/>
            </S125:Topmark></S125:member>
            """);

        var instructions = await RunAsync(dataset);
        var symbols = instructions.OfType<PointInstruction>().Select(p => p.SymbolReference).ToList();

        // Pillar buoy with the cardinal black-yellow banding (S-101 CardinalBuoy rule).
        Assert.Contains(symbols, s => s is not null && s.StartsWith("BOYPIL", StringComparison.Ordinal));
        // topmarkDaymarkShape 13 (2 cones point upward) on a floating mark → TOPMAR05.
        Assert.Contains("TOPMAR05", symbols);
    }

    private static async Task<IReadOnlyList<DrawingInstruction>> RunAsync(S125Dataset dataset)
    {
        var engine = new MoonSharpLuaEngine();
        var manager = new PortrayalCatalogueManager();
        manager.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var catalogue = new S101PortrayalCatalogue(manager.GetProvider("S-101"), engine);
        var executor = new S125AtonLuaRuleExecutor(engine, dataset, catalogue, S101Fc.Value);
        return await executor.ExecuteAsync(MarinerSettings.Default);
    }
}
