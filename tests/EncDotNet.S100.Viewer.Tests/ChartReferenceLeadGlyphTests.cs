using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// A feature type's lead glyph (#1637, moved from #1636): its own point
/// symbol, never the default <c>?</c>; else its colour fills as stripes; else
/// its area fill; else its line; else nothing. The feature type's own rule
/// comes before a shared one.
/// </summary>
public sealed class ChartReferenceLeadGlyphTests
{
    [Fact]
    public async Task Lateral_Beacon_leads_by_Simplified_symbols_and_never_with_BCNDEF13()
    {
        var index = await ChartReferenceIndexes.S101Async();
        var beacon = ChartReferenceIndexes.Feature(index, "LateralBeacon");
        Assert.Contains(beacon.Uses, u => u.ItemId == "BCNDEF13");

        var simplified = LeadGlyph.For(beacon, index, simplifiedSymbols: true);
        var paper = LeadGlyph.For(beacon, index, simplifiedSymbols: false);

        Assert.Equal(LeadGlyphKind.Symbol, simplified.Kind);
        Assert.Equal("BCNLAT21", simplified.ItemId);
        // With Simplified symbols off, the first case of the paper chart branch
        // (beacon tower, red-green-red); BCNGEN01 is drawn only for beacon
        // shapes 5 and 7.
        Assert.Equal(LeadGlyphKind.Symbol, paper.Kind);
        Assert.Equal("BCNTOW30", paper.ItemId);
        Assert.Contains(beacon.Uses, u => u.ItemId == paper.ItemId && u.Conditions.Any(c => c is { Subject: "SimplifiedSymbols", Operator: ConditionOperator.IsFalse }));
        Assert.DoesNotContain(LeadGlyph.DefaultSymbols, d => d == simplified.ItemId || d == paper.ItemId);
        // The glyph is cached under the symbol, so feature types that lead with it share it.
        Assert.Equal(new ReferenceKey(index.Spec, ReferenceKind.Symbol, "BCNLAT21"), simplified.Key);
    }

    [Fact]
    public async Task Depth_Area_leads_with_an_area_patch_in_a_representative_shade()
    {
        var index = await ChartReferenceIndexes.S101Async();
        var depthArea = ChartReferenceIndexes.Feature(index, "DepthArea");

        var lead = LeadGlyph.For(depthArea, index, simplifiedSymbols: false);

        // Not the restriction symbols its shared rule draws for some values.
        Assert.Equal(LeadGlyphKind.Area, lead.Kind);
        Assert.Equal(depthArea.Key, lead.Key);
        Assert.NotNull(lead.Tokens);
        Assert.Equal("NODTA", lead.Tokens[0]);
        Assert.Contains("DEPVS", lead.Tokens);
        Assert.Contains("DEPDW", lead.Tokens);
        Assert.True(lead.Tokens.Count <= LeadGlyph.MaxColours);

        // One patch, in a blue shade: not "no data", not the tile's deep water.
        Assert.Equal("DEPVS", lead.AreaToken);
    }

    [Fact]
    public async Task Lines_lead_when_there_is_no_symbol_or_fill_and_some_feature_types_have_nothing()
    {
        var index = await ChartReferenceIndexes.S101Async();

        var cable = LeadGlyph.For(ChartReferenceIndexes.Feature(index, "CableSubmarine"), index, false);
        Assert.Equal((LeadGlyphKind.LineStyle, "CBLSUB06"), (cable.Kind, cable.ItemId));

        // Coastline's own plain line, not the low-accuracy symbol of a shared rule.
        var coastline = LeadGlyph.For(ChartReferenceIndexes.Feature(index, "Coastline"), index, false);
        Assert.Equal(LeadGlyphKind.Pen, coastline.Kind);
        Assert.Equal("CSTLN", coastline.Pen?.Token);

        var coverage = LeadGlyph.For(ChartReferenceIndexes.Feature(index, "DataCoverage"), index, false);
        Assert.Equal(LeadGlyphKind.None, coverage.Kind);
        Assert.False(coverage.HasGlyph);
    }

    [Fact]
    public async Task A_shared_rules_symbol_family_leads_with_its_first_member()
    {
        var index = await ChartReferenceIndexes.S101Async();

        var sounding = LeadGlyph.For(ChartReferenceIndexes.Feature(index, "Sounding"), index, false);

        Assert.Equal(LeadGlyphKind.Symbol, sounding.Kind);
        Assert.StartsWith("SOUND", sounding.ItemId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Own_rule_uses_come_first_and_the_default_symbol_never_leads()
    {
        var index = await ChartReferenceIndexes.S101Async();
        SymbolUse Symbol(string id, string? via = null) => new()
        {
            ItemId = id,
            ItemKind = ReferenceKind.Symbol,
            RuleFile = via ?? "Made",
            ViaRule = via,
        };
        FeatureTypeEntry Feature(params SymbolUse[] uses) => new()
        {
            Key = new ReferenceKey(index.Spec, ReferenceKind.FeatureType, "Made"),
            Name = "Made",
            Uses = uses,
        };

        Assert.Equal("BOYCAN01", LeadGlyph.For(Feature(Symbol("BCNDEF13"), Symbol("QUESMRK1"), Symbol("LOWACC01", via: "QUAPOS01"), Symbol("BOYCAN01")), index, false).ItemId);
        Assert.Equal("LOWACC01", LeadGlyph.For(Feature(Symbol("BCNDEF13"), Symbol("LOWACC01", via: "QUAPOS01")), index, false).ItemId);
        Assert.Equal(LeadGlyphKind.None, LeadGlyph.For(Feature(Symbol("BCNDEF13")), index, false).Kind);
    }

    [Fact]
    public async Task An_items_glyph_is_the_item_and_a_familys_its_first_member()
    {
        var index = await ChartReferenceIndexes.S101Async();
        PortrayalItemEntry Item(ReferenceKind kind, string code) =>
            Assert.IsType<PortrayalItemEntry>(index.TryGet(new ReferenceKey(index.Spec, kind, code), out var e) ? e : null);

        Assert.Equal((LeadGlyphKind.Symbol, "BCNLAT21"), (LeadGlyph.For(Item(ReferenceKind.Symbol, "BCNLAT21")).Kind, LeadGlyph.For(Item(ReferenceKind.Symbol, "BCNLAT21")).ItemId));
        Assert.Equal(LeadGlyphKind.AreaFill, LeadGlyph.For(Item(ReferenceKind.AreaFill, "DRGARE01")).Kind);
        Assert.Equal(LeadGlyphKind.LineStyle, LeadGlyph.For(Item(ReferenceKind.LineStyle, "CBLSUB06")).Kind);
        var family = Item(ReferenceKind.Symbol, "SOUNDG");
        Assert.Equal(family.Members[0], LeadGlyph.For(family).ItemId);
    }
}
