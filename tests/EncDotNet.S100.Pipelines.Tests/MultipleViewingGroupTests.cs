using System.Collections.ObjectModel;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Drawing instructions assigned several viewing groups (S-100 Part 9
/// §9-11.1.3, Part 9a §9a-11.2.2.1 <c>ViewingGroup:vg1,vg2</c>) keep every id
/// and are disabled when any of them is hidden. This is what lets S-101's
/// 90020 / 90021 switch off the INFORM01 "additional information" marker.
/// </summary>
public class MultipleViewingGroupTests
{
    [Fact]
    public void Parser_KeepsEveryViewingGroupId()
    {
        var parsed = DrawingInstructionParser.Parse(
            "F1", "ViewingGroup:27070,90020,90021;DrawingPriority:24;PointInstruction:INFORM01");

        var point = Assert.Single(parsed.OfType<PointInstruction>());
        Assert.Equal(27070, point.ViewingGroup);
        Assert.Equal([90020, 90021], point.AdditionalViewingGroups);
    }

    [Fact]
    public void Parser_SingleViewingGroup_HasNoAdditionalGroups()
    {
        var parsed = DrawingInstructionParser.Parse(
            "F1", "ViewingGroup:12410;DrawingPriority:15;PointInstruction:PILPNT02");

        var point = Assert.Single(parsed.OfType<PointInstruction>());
        Assert.Equal(12410, point.ViewingGroup);
        Assert.Empty(point.AdditionalViewingGroups);
    }

    [Fact]
    public void Parser_LaterViewingGroupCommand_ReplacesEarlierGroups()
    {
        // ViewingGroup is a state command: the second one replaces the whole
        // set, so the trailing symbol must not inherit 90020.
        var parsed = DrawingInstructionParser.Parse(
            "F1",
            "ViewingGroup:27070,90020;PointInstruction:INFORM01;" +
            "ViewingGroup:12410;PointInstruction:PILPNT02");

        var points = parsed.OfType<PointInstruction>().ToList();
        Assert.Equal([90020], points[0].AdditionalViewingGroups);
        Assert.Equal(12410, points[1].ViewingGroup);
        Assert.Empty(points[1].AdditionalViewingGroups);
    }

    [Fact]
    public void Parser_SkipsUnparseableAndDuplicateIds()
    {
        var parsed = DrawingInstructionParser.Parse(
            "F1", "ViewingGroup:x,27070,90020,27070,90020;PointInstruction:INFORM01");

        var point = Assert.Single(parsed.OfType<PointInstruction>());
        Assert.Equal(27070, point.ViewingGroup);
        Assert.Equal([90020], point.AdditionalViewingGroups);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(27070, false)]
    [InlineData(90020, false)]
    [InlineData(90021, false)]
    [InlineData(12410, true)]
    public void Controller_InstructionHiddenWhenAnyGroupHidden(int? hiddenGroup, bool expectedVisible)
    {
        var controller = new ViewingGroupController();
        if (hiddenGroup is { } id)
            controller.SetUserOverride(id, false);

        var instruction = new PointInstruction
        {
            FeatureReference = "F1",
            ViewingGroup = 27070,
            AdditionalViewingGroups = [90020, 90021],
            SymbolReference = "INFORM01",
        };

        Assert.Equal(expectedVisible, controller.IsVisible(instruction));
    }

    [Fact]
    public void Controller_ModeMembershipMustContainEveryGroup()
    {
        var controller = new ViewingGroupController();
        controller.SetActiveModeMembership(new HashSet<int> { 27070 });

        var instruction = new PointInstruction
        {
            FeatureReference = "F1",
            ViewingGroup = 27070,
            AdditionalViewingGroups = [90020],
        };
        Assert.False(controller.IsVisible(instruction));

        // An explicit user override re-enables the secondary group.
        controller.SetUserOverride(90020, true);
        Assert.True(controller.IsVisible(instruction));
    }

    [Fact]
    public async Task S101_AdditionalInformation_Inform01HiddenBy90020_OwnSymbolKept()
    {
        var dataset = PileWithNauticalInformation();

        using var fcStream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new InvalidOperationException("S-101 feature catalogue not bundled.");
        var fc = FeatureCatalogueReader.Read(fcStream);

        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-101");
        var pcProvider = await PortrayalCatalogueProvider.OpenAsync(pcSource);
        var luaEngine = new MoonSharpLuaEngine();
        var catalogue = new S101PortrayalCatalogue(pcProvider, luaEngine);
        await catalogue.SwitchPaletteAsync(PaletteType.Day);

        async Task<IReadOnlyList<string?>> RenderSymbols()
        {
            var executor = new S101LuaRuleExecutor(luaEngine, dataset, catalogue, fc);
            var layer = (IVectorLayer)await new PortrayalPipeline(executor)
                .ProcessAsync(new S101FeatureXmlSource(dataset), catalogue);
            return layer.Instructions
                .OfType<PointInstruction>()
                .Where(p => p.FeatureReference == "1")
                .Select(p => p.SymbolReference)
                .ToList();
        }

        var visible = await RenderSymbols();
        Assert.Contains("PILPNT02", visible);
        Assert.Contains("INFORM01", visible);

        catalogue.ViewingGroups.SetUserOverride(90020, false);
        var hidden = await RenderSymbols();
        Assert.Contains("PILPNT02", hidden);
        Assert.DoesNotContain("INFORM01", hidden);
    }

    // A point Pile (own symbol PILPNT02, viewing group 12410) with an
    // AdditionalInformation association to a NauticalInformation record whose
    // information complex carries text — the S-101 rule then also emits
    // "ViewingGroup:12410,90020;…;PointInstruction:INFORM01".
    private static S101Dataset PileWithNauticalInformation()
    {
        const int cmf = 10_000_000;
        const ushort information = 1, text = 2, language = 3;

        var document = new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "inform01" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = cmf,
                CoordinateMultiplicationFactorY = cmf,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = new Dictionary<ushort, string> { [1] = "Pile" },
            AttributeTypeCatalogue = new Dictionary<ushort, string>
            {
                [information] = "information",
                [text] = "text",
                [language] = "language",
            },
            Points = new Dictionary<uint, S101PointRecord>
            {
                [10] = new() { RecordId = 10, Y = (int)(47.6 * cmf), X = (int)(-122.3 * cmf) },
            },
            MultiPoints = ReadOnlyDictionary<uint, S101MultiPointRecord>.Empty,
            CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features =
            [
                new S101FeatureRecord
                {
                    RecordId = 1,
                    FeatureTypeCode = 1,
                    SpatialAssociations = [new S101SpatialAssociation(110, 10, 1)],
                    InformationAssociations = [new S101InformationAssociation(1, 100, 1)],
                },
            ],
            InformationTypes = new Dictionary<uint, S101InformationRecord>
            {
                [100] = new()
                {
                    RecordId = 100,
                    InformationTypeCode = 1,
                    Attributes =
                    [
                        new S101Attribute(information, 1, string.Empty),
                        new S101Attribute(text, 1, "Private pile", ParentIndex: 1),
                        new S101Attribute(language, 1, "eng", ParentIndex: 1),
                    ],
                },
            },
            InformationTypeCatalogue = new Dictionary<ushort, string> { [1] = "NauticalInformation" },
            InformationAssociationCatalogue = new Dictionary<ushort, string> { [1] = "AdditionalInformation" },
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = new Dictionary<ushort, string> { [1] = "theInformation" },
        };
        return S101Dataset.FromDocument(document);
    }
}
