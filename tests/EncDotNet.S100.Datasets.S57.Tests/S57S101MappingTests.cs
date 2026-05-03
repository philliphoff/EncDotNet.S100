using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Datasets.S57.Tests;

public class S57S101MappingTests
{
    [Fact]
    public void Default_ResolvesCommonFeatureClasses()
    {
        var m = S57S101Mapping.Default;

        Assert.Equal("DepthArea", m.ResolveFeatureCode(42));
        Assert.Equal("Coastline", m.ResolveFeatureCode(30));
        Assert.Equal("LandArea", m.ResolveFeatureCode(71));
        Assert.Equal("Sounding", m.ResolveFeatureCode(129));
        Assert.Equal("LightAllAround", m.ResolveFeatureCode(75));
    }

    [Fact]
    public void Default_ResolvesCommonAttributes()
    {
        var m = S57S101Mapping.Default;

        Assert.Equal("depthRangeMinimumValue", m.ResolveAttributeCode(87));
        Assert.Equal("depthRangeMaximumValue", m.ResolveAttributeCode(88));
        Assert.Equal("valueOfSounding", m.ResolveAttributeCode(179));
        Assert.Equal("valueOfDepthContour", m.ResolveAttributeCode(174));
        Assert.Equal("expositionOfSounding", m.ResolveAttributeCode(93));
        Assert.Equal("verticalClearanceValue", m.ResolveAttributeCode(181));
        // OBJNAM (116) intentionally has no flat mapping — featureName is a complex attribute.
        Assert.Null(m.ResolveAttributeCode(116));
    }

    [Fact]
    public void UnknownCode_ReturnsNull()
    {
        var m = S57S101Mapping.Default;
        Assert.Null(m.ResolveFeatureCode(9999));
        Assert.Null(m.ResolveAttributeCode(9999));
    }

    [Fact]
    public void Default_HasCuratedSet()
    {
        var m = S57S101Mapping.Default;
        Assert.True(m.FeatureCount >= 25);
        Assert.True(m.AttributeCount >= 20);
    }

    [Fact]
    public void Builder_AddsCustomMappings()
    {
        var m = new S57S101Mapping.Builder()
            .WithDefaults()
            .AddFeature(9999, "CustomFeature")
            .AddAttribute(8888, "customAttribute")
            .Build();

        Assert.Equal("CustomFeature", m.ResolveFeatureCode(9999));
        Assert.Equal("customAttribute", m.ResolveAttributeCode(8888));
        Assert.Equal("DepthArea", m.ResolveFeatureCode(42));
    }

    [Fact]
    public void ResolveFeature_NoRedirect_UsesDefault()
    {
        var m = S57S101Mapping.Default;
        var resolved = m.ResolveFeature(42, ImmutableDictionary<string, string>.Empty);
        Assert.NotNull(resolved);
        Assert.Equal("DepthArea", resolved!.S101Code);
        Assert.Empty(resolved.AttributeOverrides);
    }

    [Fact]
    public void ResolveFeature_Redirect_PicksTargetClassAndMergesOverrides()
    {
        var ctrpntRule = new S57FeatureRule
        {
            Objl = 999,
            S57Acronym = "CTRPNT",
            DefaultS101Code = null,
            Redirects = ImmutableArray.Create(new S57FeatureRedirect
            {
                ConditionAttribute = "CATCTR",
                ConditionValues = ImmutableArray.Create("1", "5"),
                TargetS101Code = "Landmark",
                AttributeOverrides = ImmutableDictionary.CreateRange(
                    StringComparer.OrdinalIgnoreCase,
                    new[]
                    {
                        new KeyValuePair<string, S57AttributeOverride>(
                            "CATCTR",
                            new S57AttributeOverride
                            {
                                S101Code = "categoryOfLandmark",
                                ValueRemap = ImmutableDictionary.CreateRange(
                                    new[]
                                    {
                                        new KeyValuePair<string, string?>("1", "22"),
                                        new KeyValuePair<string, string?>("5", "23"),
                                    }),
                            }),
                    }),
            }),
        };

        var ctrpntAttrRule = new S57AttributeRule
        {
            Attl = 9001,
            S57Acronym = "CATCTR",
            DefaultS101Code = "categoryOfControlPoint",
        };

        var m = new S57S101Mapping.Builder()
            .AddFeatureRule(ctrpntRule)
            .AddAttributeRule(ctrpntAttrRule)
            .Build();

        var attrs = ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            new[] { new KeyValuePair<string, string>("CATCTR", "1") });

        var resolved = m.ResolveFeature(999, attrs);
        Assert.NotNull(resolved);
        Assert.Equal("Landmark", resolved!.S101Code);

        var attr = m.ResolveAttribute("CATCTR", "1", resolved);
        Assert.NotNull(attr);
        Assert.Equal("categoryOfLandmark", attr!.S101Code);
        Assert.Equal("22", attr.Value);
    }

    [Fact]
    public void ResolveFeature_Redirect_NotMatched_UsesDefault_OrDrops()
    {
        var rule = new S57FeatureRule
        {
            Objl = 999,
            S57Acronym = "CTRPNT",
            DefaultS101Code = null, // drop when no redirect matches
            Redirects = ImmutableArray.Create(new S57FeatureRedirect
            {
                ConditionAttribute = "CATCTR",
                ConditionValues = ImmutableArray.Create("1"),
                TargetS101Code = "Landmark",
            }),
        };
        var m = new S57S101Mapping.Builder().AddFeatureRule(rule).Build();

        var attrs = ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            new[] { new KeyValuePair<string, string>("CATCTR", "9") });

        Assert.Null(m.ResolveFeature(999, attrs));
    }

    [Fact]
    public void ResolveAttribute_ValueRemap_DropsAttribute()
    {
        var attrRule = new S57AttributeRule
        {
            Attl = 9100,
            S57Acronym = "FOO",
            DefaultS101Code = "foo",
            DefaultValueRemap = ImmutableDictionary.CreateRange(
                new[] { new KeyValuePair<string, string?>("99", null) }),
        };
        var featRule = new S57FeatureRule
        {
            Objl = 9101,
            S57Acronym = "FEA",
            DefaultS101Code = "Feature",
        };
        var m = new S57S101Mapping.Builder()
            .AddFeatureRule(featRule)
            .AddAttributeRule(attrRule)
            .Build();

        var resolved = m.ResolveFeature(9101, ImmutableDictionary<string, string>.Empty)!;

        Assert.Null(m.ResolveAttribute("FOO", "99", resolved));
        Assert.Equal("foo", m.ResolveAttribute("FOO", "1", resolved)!.S101Code);
    }

    [Fact]
    public void BuildAcronymView_KeysByS57Acronym()
    {
        var m = S57S101Mapping.Default;
        var view = m.BuildAcronymView(new[]
        {
            new S57Attribute(87, "5.0"),  // DRVAL1
            new S57Attribute(88, "10.0"), // DRVAL2
            new S57Attribute(9999, "x"),  // unknown — dropped
        });
        Assert.Equal("5.0", view["DRVAL1"]);
        Assert.Equal("10.0", view["DRVAL2"]);
        Assert.False(view.ContainsKey("UNKNOWN"));
    }

    // ── IHO Conversion Guidance § 4.3 — CTRPNT → Landmark ──────────────

    [Fact]
    public void Ctrpnt_WithCatctr1_RedirectsToLandmark_TriangulationMark()
    {
        var m = S57S101Mapping.Default;
        var attrs = m.BuildAcronymView(new[] { new S57Attribute(16, "1") }); // CATCTR=1

        var resolved = m.ResolveFeature(33, attrs); // CTRPNT
        Assert.NotNull(resolved);
        Assert.Equal("Landmark", resolved!.S101Code);

        var attr = m.ResolveAttribute("CATCTR", "1", resolved);
        Assert.NotNull(attr);
        Assert.Equal("categoryOfLandmark", attr!.S101Code);
        Assert.Equal("22", attr.Value);
    }

    [Fact]
    public void Ctrpnt_WithCatctr5_RedirectsToLandmark_BoundaryMark()
    {
        var m = S57S101Mapping.Default;
        var attrs = m.BuildAcronymView(new[] { new S57Attribute(16, "5") });

        var resolved = m.ResolveFeature(33, attrs);
        Assert.NotNull(resolved);
        Assert.Equal("Landmark", resolved!.S101Code);

        var attr = m.ResolveAttribute("CATCTR", "5", resolved);
        Assert.NotNull(attr);
        Assert.Equal("categoryOfLandmark", attr!.S101Code);
        Assert.Equal("23", attr.Value);
    }

    [Fact]
    public void Ctrpnt_WithOtherCatctr_IsDropped()
    {
        var m = S57S101Mapping.Default;
        var attrs = m.BuildAcronymView(new[] { new S57Attribute(16, "2") });

        Assert.Null(m.ResolveFeature(33, attrs));
    }

    [Fact]
    public void Ctrpnt_WithoutCatctr_IsDropped()
    {
        var m = S57S101Mapping.Default;
        Assert.Null(m.ResolveFeature(33, ImmutableDictionary<string, string>.Empty));
    }

    [Fact]
    public void Lndmrk_MapsToLandmark()
    {
        var m = S57S101Mapping.Default;
        Assert.Equal("Landmark", m.ResolveFeatureCode(74));
    }
}
