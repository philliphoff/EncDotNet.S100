using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for the host-side corrections to the bundled IEHG S-401 portrayal
/// catalogue (<see cref="S401PortrayalPatches"/> and the array-tolerant
/// <c>contains</c> guard). On the EuRIS inland cells these defects sent every
/// Dolphin, WaterwayProfile and CommunicationArea to Default (question mark)
/// symbology.
/// </summary>
public sealed class S401PortrayalPatchTests
{
    private const byte RcnmPoint = 110;
    private const byte RcnmCurveSegment = 120;
    private const byte RcnmSurface = 130;

    private const uint DolphinId = 1;
    private const uint WaterwayProfileId = 2;
    private const uint CommunicationAreaId = 3;

    [Fact]
    public async Task PatchedRules_PortrayWithoutDefaultSymbology()
    {
        var emitted = await PortrayAsync();

        Assert.DoesNotContain(emitted, e => e.InstructionString.Contains("QUESMRK1", StringComparison.Ordinal));
        Assert.Contains(emitted, e => e.FeatureRef == $"{DolphinId}"
            && e.InstructionString.Contains("PointInstruction:MORFAC03", StringComparison.Ordinal));
        Assert.Contains(emitted, e => e.FeatureRef == $"{WaterwayProfileId}"
            && e.InstructionString.Contains("ViewingGroup:13040", StringComparison.Ordinal));
        Assert.Contains(emitted, e => e.FeatureRef == $"{CommunicationAreaId}"
            && e.InstructionString.Contains("NullInstruction", StringComparison.Ordinal));
    }

    [Fact]
    public void S401Patches_AreNotAppliedToOtherProducts()
    {
        var s101 = new S101LuaDataProvider(
            S101Dataset.FromDocument(BuildDocument()),
            new FeatureCatalogue { Name = "S-101", ProductId = "S-101", VersionNumber = "2.0.0", VersionDate = "2025-01-01" });
        var s401 = new S101LuaDataProvider(
            S101Dataset.FromDocument(BuildDocument()),
            new FeatureCatalogue { Name = "S-401", ProductId = "S-401", VersionNumber = "1.3.0", VersionDate = "2026-05-15" });

        Assert.DoesNotContain(S401PortrayalPatches.WaterwayProfileEntryPoint, s101.PostLoadScripts);
        Assert.Contains(S401PortrayalPatches.WaterwayProfileEntryPoint, s401.PostLoadScripts);
        Assert.Contains(S401PortrayalPatches.CommunicationAreaViewingGroup, s401.PostLoadScripts);
    }

    // ── Upstream defect sentinels ────────────────────────────────────────────
    // Each fails once a refreshed IEHG catalogue fixes the defect: delete the
    // matching patch (and its README entry) when it does.

    [Fact]
    public async Task Upstream_WaterwayProfileRuleStillDefinesWaterwayAxis()
    {
        var source = await ReadRuleAsync("WaterwayProfile.lua");
        Assert.True(
            !source.Contains("function WaterwayProfile(", StringComparison.Ordinal),
            "IEHG #40 is fixed upstream: remove S401PortrayalPatches.WaterwayProfileEntryPoint.");
    }

    [Fact]
    public async Task Upstream_CommunicationAreaRuleStillReturnsNoViewingGroup()
    {
        var source = await ReadRuleAsync("CommunicationArea.lua");
        Assert.True(
            Regex.IsMatch(source, @"local viewingGroup\s*\n"),
            "IEHG #41 is fixed upstream: remove S401PortrayalPatches.CommunicationAreaViewingGroup.");
    }

    [Fact]
    public async Task Upstream_DolphinRuleStillTreatsCategoryAsArray()
    {
        var source = await ReadRuleAsync("Dolphin.lua");
        var fc = ReadFeatureCatalogue();
        var binding = fc.FeatureTypes.Single(f => f.Code == "Dolphin")
            .AttributeBindings.Single(b => b.AttributeRef == "categoryOfDolphin");

        Assert.True(
            source.Contains("contains(2,categoryOfDolphin)", StringComparison.Ordinal) && binding.Multiplicity.Upper == 1,
            "The S-401 Dolphin rule and categoryOfDolphin binding agree upstream: the scalar branch of " +
            "S101LuaDataProvider.ContainsGuard is no longer needed for S-401.");
    }

    private static async Task<string> ReadRuleAsync(string fileName)
    {
        using var source = Specification.CreatePortrayalCatalogueSource("S-401");
        using var stream = await source.OpenAsync($"Rules/{fileName}", TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);
        return (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
    }

    private static FeatureCatalogue ReadFeatureCatalogue()
    {
        using var stream = Specification.TryOpenFeatureCatalogue("S-401")
            ?? throw new InvalidOperationException("S-401 feature catalogue not bundled.");
        return FeatureCatalogueReader.Read(stream);
    }

    private static async Task<IReadOnlyList<EmittedInstruction>> PortrayAsync()
    {
        var fc = ReadFeatureCatalogue();
        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-401");
        var provider = await PortrayalCatalogueProvider.OpenAsync(pcSource);
        var lua = new MoonSharpLuaEngine();
        var catalogue = new S101PortrayalCatalogue(provider, lua);
        var executor = new S101LuaRuleExecutor(lua, S101Dataset.FromDocument(BuildDocument()), catalogue, fc);
        return await executor.ExecuteRawAsync(MarinerSettings.Default, TestContext.Current.CancellationToken);
    }

    private static S101Document BuildDocument()
    {
        const ushort dolphin = 1, waterwayProfile = 2, communicationArea = 3;
        const ushort categoryOfDolphin = 1;

        var points = new Dictionary<uint, S101PointRecord>
        {
            [1] = new() { RecordId = 1, X = 105_000_000, Y = 530_000_000 },
            [2] = new() { RecordId = 2, X = 105_100_000, Y = 530_000_000 },
            [3] = new() { RecordId = 3, X = 105_100_000, Y = 530_100_000 },
            [4] = new() { RecordId = 4, X = 105_000_000, Y = 530_100_000 },
        };
        var curves = new Dictionary<uint, S101CurveSegmentRecord>
        {
            [10] = new()
            {
                RecordId = 10,
                PointAssociations = [new(RcnmPoint, 1, 1), new(RcnmPoint, 2, 2)],
            },
            [11] = new()
            {
                RecordId = 11,
                PointAssociations = [new(RcnmPoint, 2, 1), new(RcnmPoint, 2, 2)],
                IntermediateCoordinates = [(530_100_000, 105_100_000), (530_100_000, 105_000_000), (530_000_000, 105_000_000), (530_000_000, 105_100_000)],
            },
        };
        var surfaces = new Dictionary<uint, S101SurfaceRecord>
        {
            [20] = new() { RecordId = 20, RingAssociations = [new(RcnmCurveSegment, 11, 1, 1)] },
        };

        S101FeatureRecord Feature(uint id, ushort type, byte rcnm, uint spatial, params S101Attribute[] attributes) => new()
        {
            RecordId = id,
            FeatureTypeCode = type,
            Attributes = attributes,
            SpatialAssociations = [new(rcnm, spatial, 1)],
            FeatureAssociations = [],
            InformationAssociations = [],
        };

        return new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "PATCHES.000", ProductSpecification = "INT.IHO.S-401.1.3.0" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 10_000_000,
                CoordinateMultiplicationFactorY = 10_000_000,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = new Dictionary<ushort, string>
            {
                [dolphin] = "Dolphin",
                [waterwayProfile] = "WaterwayProfile",
                [communicationArea] = "CommunicationArea",
            },
            AttributeTypeCatalogue = new Dictionary<ushort, string> { [categoryOfDolphin] = "categoryOfDolphin" },
            Points = points,
            CurveSegments = curves,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = surfaces,
            Features =
            [
                Feature(DolphinId, dolphin, RcnmPoint, 3, new S101Attribute(categoryOfDolphin, 1, "1")),
                Feature(WaterwayProfileId, waterwayProfile, RcnmCurveSegment, 10),
                Feature(CommunicationAreaId, communicationArea, RcnmSurface, 20),
            ],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };
    }
}
