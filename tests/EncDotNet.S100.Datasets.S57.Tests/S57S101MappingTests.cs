using System.Collections.ObjectModel;

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
        var resolved = m.ResolveFeature(42, ReadOnlyDictionary<string, string>.Empty);
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
            Redirects = [new S57FeatureRedirect
            {
                ConditionAttribute = "CATCTR",
                ConditionValues = ["1", "5"],
                TargetS101Code = "Landmark",
                AttributeOverrides = new Dictionary<string, S57AttributeOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CATCTR"] = new S57AttributeOverride
                    {
                        S101Code = "categoryOfLandmark",
                        ValueRemap = new Dictionary<string, string?>
                        {
                            ["1"] = "22",
                            ["5"] = "23",
                        },
                    },
                },
            }],
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

        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CATCTR"] = "1",
        };

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
            Redirects = [new S57FeatureRedirect
            {
                ConditionAttribute = "CATCTR",
                ConditionValues = ["1"],
                TargetS101Code = "Landmark",
            }],
        };
        var m = new S57S101Mapping.Builder().AddFeatureRule(rule).Build();

        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CATCTR"] = "9",
        };

        Assert.Null(m.ResolveFeature(999, attrs));
    }

    [Fact]
    public void Build_RedirectWithoutPresenceOrValues_Throws()
    {
        var rule = new S57FeatureRule
        {
            Objl = 999,
            S57Acronym = "CTRPNT",
            DefaultS101Code = "ControlPoint",
            Redirects = [new S57FeatureRedirect
            {
                ConditionAttribute = "CATCTR",
                // Neither a presence test nor any values: can never match.
                ConditionPresent = false,
                TargetS101Code = "Landmark",
            }],
        };

        var ex = Assert.Throws<ArgumentException>(
            () => new S57S101Mapping.Builder().AddFeatureRule(rule).Build());
        Assert.Contains("never match", ex.Message);
    }

    [Fact]
    public void Build_PresenceRedirectWithoutValues_Succeeds()
    {
        var rule = new S57FeatureRule
        {
            Objl = 999,
            S57Acronym = "LIGHTS",
            DefaultS101Code = "LightAllAround",
            Redirects = [new S57FeatureRedirect
            {
                ConditionAttribute = "SECTR1",
                ConditionPresent = true,
                TargetS101Code = "LightSectored",
            }],
        };

        var m = new S57S101Mapping.Builder().AddFeatureRule(rule).Build();
        Assert.NotNull(m);
    }

    // ── Geometry-conditional redirects (S57GeometryPrimitive) ──────────

    [Fact]
    public void Build_PrimitiveOnlyRedirectWithoutAttribute_Succeeds()
    {
        // A purely geometry-based redirect (no ConditionAttribute) is valid.
        var rule = new S57FeatureRule
        {
            Objl = 999,
            S57Acronym = "MORFAC",
            DefaultS101Code = "Dolphin",
            Redirects = [new S57FeatureRedirect
            {
                ConditionPrimitives = [S57GeometryPrimitive.Curve, S57GeometryPrimitive.Surface],
                TargetS101Code = "ShorelineConstruction",
            }],
        };

        var m = new S57S101Mapping.Builder().AddFeatureRule(rule).Build();
        Assert.NotNull(m);
    }

    [Fact]
    public void Build_RedirectWithNoConditionAtAll_Throws()
    {
        var rule = new S57FeatureRule
        {
            Objl = 999,
            S57Acronym = "MORFAC",
            DefaultS101Code = "Dolphin",
            Redirects = [new S57FeatureRedirect
            {
                // No attribute condition and no primitive condition.
                TargetS101Code = "ShorelineConstruction",
            }],
        };

        var ex = Assert.Throws<ArgumentException>(
            () => new S57S101Mapping.Builder().AddFeatureRule(rule).Build());
        Assert.Contains("no condition", ex.Message);
    }

    [Fact]
    public void Build_BrokenAttributeConditionWithPrimitive_StillThrows()
    {
        // ConditionAttribute set, ConditionPresent false, no values: the
        // attribute gate can never pass, so it must throw even though a
        // primitive condition is also present.
        var rule = new S57FeatureRule
        {
            Objl = 999,
            S57Acronym = "MORFAC",
            DefaultS101Code = "Dolphin",
            Redirects = [new S57FeatureRedirect
            {
                ConditionAttribute = "CATMOR",
                ConditionPresent = false,
                ConditionPrimitives = [S57GeometryPrimitive.Point],
                TargetS101Code = "Bollard",
            }],
        };

        var ex = Assert.Throws<ArgumentException>(
            () => new S57S101Mapping.Builder().AddFeatureRule(rule).Build());
        Assert.Contains("never match", ex.Message);
    }

    [Fact]
    public void ResolveFeature_PrimitiveRedirect_MatchesOnlyForListedPrimitives()
    {
        var rule = new S57FeatureRule
        {
            Objl = 999,
            S57Acronym = "MORFAC",
            DefaultS101Code = "Dolphin",
            Redirects = [new S57FeatureRedirect
            {
                ConditionPrimitives = [S57GeometryPrimitive.Curve, S57GeometryPrimitive.Surface],
                TargetS101Code = "ShorelineConstruction",
            }],
        };
        var m = new S57S101Mapping.Builder().AddFeatureRule(rule).Build();
        var noAttrs = ReadOnlyDictionary<string, string>.Empty;

        Assert.Equal("ShorelineConstruction",
            m.ResolveFeature(999, noAttrs, S57GeometryPrimitive.Surface)!.S101Code);
        Assert.Equal("ShorelineConstruction",
            m.ResolveFeature(999, noAttrs, S57GeometryPrimitive.Curve)!.S101Code);
        Assert.Equal("Dolphin",
            m.ResolveFeature(999, noAttrs, S57GeometryPrimitive.Point)!.S101Code);
        // No primitive supplied → geometry gate fails → default.
        Assert.Equal("Dolphin", m.ResolveFeature(999, noAttrs)!.S101Code);
    }

    [Fact]
    public void ResolveFeature_CombinedAttributeAndPrimitiveRedirect_RequiresBoth()
    {
        // Mirrors the MORFAC CATMOR=3 → Bollard rule: only fires for a point
        // whose CATMOR is 3.
        var rule = new S57FeatureRule
        {
            Objl = 999,
            S57Acronym = "MORFAC",
            DefaultS101Code = "Dolphin",
            Redirects = [new S57FeatureRedirect
            {
                ConditionAttribute = "CATMOR",
                ConditionValues = ["3"],
                ConditionPrimitives = [S57GeometryPrimitive.Point],
                TargetS101Code = "Bollard",
            }],
        };
        var attrRule = new S57AttributeRule { Attl = 9004, S57Acronym = "CATMOR", DefaultS101Code = "categoryOfDolphin" };
        var m = new S57S101Mapping.Builder().AddFeatureRule(rule).AddAttributeRule(attrRule).Build();
        var catmor3 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["CATMOR"] = "3" };

        // Point + CATMOR 3 → Bollard.
        Assert.Equal("Bollard",
            m.ResolveFeature(999, catmor3, S57GeometryPrimitive.Point)!.S101Code);
        // Wrong primitive → default Dolphin.
        Assert.Equal("Dolphin",
            m.ResolveFeature(999, catmor3, S57GeometryPrimitive.Surface)!.S101Code);
        // Right primitive, wrong value → default Dolphin.
        var catmor2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["CATMOR"] = "2" };
        Assert.Equal("Dolphin",
            m.ResolveFeature(999, catmor2, S57GeometryPrimitive.Point)!.S101Code);
    }

    [Fact]
    public void ResolveAttribute_DropOverride_RemovesAttribute()
    {
        // A redirect that drops the condition attribute (S57AttributeOverride
        // with Drop = true) must yield null so it is not emitted on the target.
        var rule = new S57FeatureRule
        {
            Objl = 999,
            S57Acronym = "MORFAC",
            DefaultS101Code = "Dolphin",
            Redirects = [new S57FeatureRedirect
            {
                ConditionAttribute = "CATMOR",
                ConditionValues = ["3"],
                ConditionPrimitives = [S57GeometryPrimitive.Point],
                TargetS101Code = "Bollard",
                AttributeOverrides = new Dictionary<string, S57AttributeOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CATMOR"] = new S57AttributeOverride { Drop = true },
                },
            }],
        };
        var attrRule = new S57AttributeRule { Attl = 9004, S57Acronym = "CATMOR", DefaultS101Code = "categoryOfDolphin" };
        var m = new S57S101Mapping.Builder().AddFeatureRule(rule).AddAttributeRule(attrRule).Build();
        var catmor3 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["CATMOR"] = "3" };

        var resolved = m.ResolveFeature(999, catmor3, S57GeometryPrimitive.Point)!;
        Assert.Equal("Bollard", resolved.S101Code);
        Assert.Null(m.ResolveAttribute("CATMOR", "3", resolved));
    }

    [Fact]
    public void ResolveAttribute_ValueRemap_DropsAttribute()
    {
        var attrRule = new S57AttributeRule
        {
            Attl = 9100,
            S57Acronym = "FOO",
            DefaultS101Code = "foo",
            DefaultValueRemap = new Dictionary<string, string?>
            {
                ["99"] = null,
            },
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

        var resolved = m.ResolveFeature(9101, ReadOnlyDictionary<string, string>.Empty)!;

        Assert.Null(m.ResolveAttribute("FOO", "99", resolved));
        Assert.Equal("foo", m.ResolveAttribute("FOO", "1", resolved)!.S101Code);
    }

    [Fact]
    public void BuildAcronymView_KeysByS57Acronym()
    {
        var m = S57S101Mapping.Default;
        var view = m.BuildAcronymView(new[]
        {
            new EncDotNet.S57.S57AttributeValue { AttributeCode = 87, Value = "5.0" },  // DRVAL1
            new EncDotNet.S57.S57AttributeValue { AttributeCode = 88, Value = "10.0" }, // DRVAL2
            new EncDotNet.S57.S57AttributeValue { AttributeCode = 9999, Value = "x" },  // unknown — dropped
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
        var attrs = m.BuildAcronymView(new[] { new EncDotNet.S57.S57AttributeValue { AttributeCode = 16, Value = "1" } }); // CATCTR=1

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
        var attrs = m.BuildAcronymView(new[] { new EncDotNet.S57.S57AttributeValue { AttributeCode = 16, Value = "5" } });

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
        var attrs = m.BuildAcronymView(new[] { new EncDotNet.S57.S57AttributeValue { AttributeCode = 16, Value = "2" } });

        Assert.Null(m.ResolveFeature(33, attrs));
    }

    [Fact]
    public void Ctrpnt_WithoutCatctr_IsDropped()
    {
        var m = S57S101Mapping.Default;
        Assert.Null(m.ResolveFeature(33, ReadOnlyDictionary<string, string>.Empty));
    }

    [Fact]
    public void Lndmrk_MapsToLandmark()
    {
        var m = S57S101Mapping.Default;
        Assert.Equal("Landmark", m.ResolveFeatureCode(74));
    }

    // ── S-65 Annex B § 12.2 — M_NSYS with ORIENT → LocalDirectionOfBuoyage ──

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-401")]
    public void Mnsys_WithOrient_RedirectsToLocalDirectionOfBuoyage(string spec)
    {
        var m = S57S101Mapping.ForSpec(spec);
        var attrs = m.BuildAcronymView(new[]
        {
            new EncDotNet.S57.S57AttributeValue { AttributeCode = 109, Value = "1" },    // MARSYS
            new EncDotNet.S57.S57AttributeValue { AttributeCode = 117, Value = "45.5" }, // ORIENT
        });

        var resolved = m.ResolveFeature(306, attrs)!; // M_NSYS
        Assert.Equal("LocalDirectionOfBuoyage", resolved.S101Code);

        var marsys = m.ResolveAttribute(109, "1", resolved)!;
        Assert.Equal("marksNavigationalSystemOf", marsys.S101Code);
        Assert.Equal("1", marsys.Value);
        var orient = m.ResolveAttribute(117, "45.5", resolved)!;
        Assert.Equal("orientationValue", orient.S101Code);
        Assert.Equal("45.5", orient.Value);

        // LocalDirectionOfBuoyage binds both attributes directly.
        var bindings = S101FeatureAttributeBindings.ForSpec(spec);
        Assert.True(bindings.Binds(resolved.S101Code, marsys.S101Code));
        Assert.True(bindings.Binds(resolved.S101Code, orient.S101Code));
    }

    [Theory]
    [InlineData("S-101", false)]
    [InlineData("S-101", true)]
    [InlineData("S-401", false)]
    [InlineData("S-401", true)]
    public void Mnsys_WithoutOrientValue_StaysNavigationalSystemOfMarks_AndDropsOrient(string spec, bool emptyOrient)
    {
        // An empty ORIENT (S-57 "unknown") is no value, so it does not redirect;
        // NavigationalSystemOfMarks binds no orientation, so ORIENT is dropped.
        var m = S57S101Mapping.ForSpec(spec);
        var values = new List<EncDotNet.S57.S57AttributeValue>
        {
            new() { AttributeCode = 109, Value = "2" }, // MARSYS
        };
        if (emptyOrient)
            values.Add(new() { AttributeCode = 117, Value = "" });

        var resolved = m.ResolveFeature(306, m.BuildAcronymView(values))!;

        Assert.Equal("NavigationalSystemOfMarks", resolved.S101Code);
        Assert.Equal("marksNavigationalSystemOf", m.ResolveAttribute(109, "2", resolved)!.S101Code);
        Assert.Null(m.ResolveAttribute(117, "", resolved));
        Assert.False(S101FeatureAttributeBindings.ForSpec(spec).Binds(resolved.S101Code, "orientationValue"));
    }

    // ── IHO Conversion Guidance § 4.5.1 — COALNE/CATCOA → natureOfSurface ──

    [Theory]
    [InlineData("3", "4")]   // sandy shore → sand
    [InlineData("4", "5")]   // stony shore → stone
    [InlineData("5", "7")]   // shingly shore → pebbles
    [InlineData("9", "14")]  // coral reef → coral
    [InlineData("11", "17")] // shelly shore → shells
    public void Coalne_CatcoaSurfaceValues_RedirectToNatureOfSurface(string s57Value, string expectedS101Value)
    {
        var m = S57S101Mapping.Default;
        var attrs = m.BuildAcronymView(new[] { new EncDotNet.S57.S57AttributeValue { AttributeCode = 15, Value = s57Value } }); // CATCOA

        var resolved = m.ResolveFeature(30, attrs); // COALNE
        Assert.NotNull(resolved);
        Assert.Equal("Coastline", resolved!.S101Code);

        var attr = m.ResolveAttribute("CATCOA", s57Value, resolved);
        Assert.NotNull(attr);
        Assert.Equal("natureOfSurface", attr!.S101Code);
        Assert.Equal(expectedS101Value, attr.Value);
    }

    [Fact]
    public void Coalne_OtherCatcoaValues_FallThroughToCategoryOfCoastline()
    {
        var m = S57S101Mapping.Default;
        var attrs = m.BuildAcronymView(new[] { new EncDotNet.S57.S57AttributeValue { AttributeCode = 15, Value = "1" } });

        var resolved = m.ResolveFeature(30, attrs)!;
        var attr = m.ResolveAttribute("CATCOA", "1", resolved);

        Assert.NotNull(attr);
        Assert.Equal("categoryOfCoastline", attr!.S101Code);
        Assert.Equal("1", attr.Value); // value passed through unchanged
    }

    [Fact]
    public void Coalne_WithoutCatcoa_StillResolvesToCoastline()
    {
        var m = S57S101Mapping.Default;
        var resolved = m.ResolveFeature(30, ReadOnlyDictionary<string, string>.Empty);
        Assert.NotNull(resolved);
        Assert.Equal("Coastline", resolved!.S101Code);
    }

    // ── New v3.4 feature classes (§ 4.5/4.6/4.7 1:1 mappings) ───────────

    [Theory]
    [InlineData(10, "Berth")]
    [InlineData(28, "Checkpoint")]
    [InlineData(35, "Crane")]
    [InlineData(44, "DistanceMark")]
    [InlineData(45, "DockArea")]
    [InlineData(47, "DryDock")]
    [InlineData(57, "FloatingDock")]
    [InlineData(61, "Gate")]
    [InlineData(62, "Gridiron")]
    [InlineData(64, "HarbourFacility")]
    [InlineData(65, "Hulk")]
    [InlineData(69, "Lake")]
    [InlineData(72, "LandElevation")]
    [InlineData(73, "LandRegion")]
    [InlineData(79, "LockBasin")]
    [InlineData(90, "Pile")]
    [InlineData(95, "Pontoon")]
    [InlineData(107, "Rapids")]
    [InlineData(114, "River")]
    [InlineData(122, "ShorelineConstruction")]
    [InlineData(126, "SlopeTopline")]
    [InlineData(127, "SlopingGround")]
    [InlineData(128, "SmallCraftFacility")]
    [InlineData(157, "Waterfall")]
    public void NewFeatureClasses_MapToExpectedS101Code(ushort objl, string expected)
    {
        var m = S57S101Mapping.Default;
        Assert.Equal(expected, m.ResolveFeatureCode(objl));
    }

    // ── Corpus-audit gap coverage: S-57 → S-101 feature aliases ─────────

    [Theory]
    [InlineData(1, "AdministrationArea")]    // ADMARE
    [InlineData(12, "Building")]             // BUISGL
    [InlineData(21, "CableOverhead")]        // CBLOHD
    [InlineData(22, "CableSubmarine")]       // CBLSUB
    [InlineData(27, "CautionArea")]          // CTNARE (ambiguous alias → CautionArea)
    [InlineData(39, "Daymark")]              // DAYMAR
    [InlineData(58, "FogSignal")]            // FOGSIG
    [InlineData(81, "MagneticVariation")]    // MAGVAR
    [InlineData(87, "OffshorePlatform")]     // OFSPLF
    [InlineData(94, "PipelineSubmarineOnLand")] // PIPSOL
    [InlineData(109, "RecommendedTrack")]    // RECTRC
    [InlineData(119, "SeaAreaNamedWaterArea")] // SEAARE
    [InlineData(121, "SeabedArea")]          // SBDARE
    [InlineData(125, "SiloTank")]            // SILTNK
    [InlineData(148, "TrafficSeparationSchemeLanePart")] // TSSLPT
    [InlineData(154, "UnsurveyedArea")]      // UNSARE
    [InlineData(155, "Vegetation")]          // VEGATN
    [InlineData(158, "WeedKelp")]            // WEDKLP
    public void GapFeatureClasses_MapToExpectedS101Code(ushort objl, string expected)
    {
        var m = S57S101Mapping.Default;
        Assert.Equal(expected, m.ResolveFeatureCode(objl));
    }

    [Theory]
    [InlineData(301, "QualityOfNonBathymetricData")] // M_ACCY
    [InlineData(302, "DataCoverage")]                // M_COVR
    [InlineData(305, "InformationArea")]             // M_NPUB
    [InlineData(306, "NavigationalSystemOfMarks")]   // M_NSYS
    [InlineData(308, "QualityOfBathymetricData")]    // M_QUAL
    [InlineData(309, "SoundingDatum")]               // M_SDAT
    [InlineData(310, "QualityOfSurvey")]             // M_SREL
    [InlineData(312, "VerticalDatumOfData")]         // M_VDAT
    public void GapMetaObjects_MapToExpectedS101Feature(ushort objl, string expected)
    {
        var m = S57S101Mapping.Default;
        Assert.Equal(expected, m.ResolveFeatureCode(objl));
    }

    // ── Corpus-audit gap coverage: S-57 → S-101 attribute aliases ───────

    [Theory]
    [InlineData(2, "beaconShape")]               // BCNSHP
    [InlineData(4, "buoyShape")]                 // BOYSHP
    [InlineData(8, "categoryOfAnchorage")]       // CATACH
    [InlineData(10, "categoryOfBuiltUpArea")]    // CATBUA
    [InlineData(35, "categoryOfLandmark")]       // CATLMK
    [InlineData(45, "categoryOfPile")]           // CATPLE
    [InlineData(56, "categoryOfRestrictedArea")] // CATREA
    [InlineData(66, "categoryOfSpecialPurposeMark")] // CATSPM
    [InlineData(92, "exhibitionConditionOfLight")]   // EXCLIT
    [InlineData(94, "function")]                 // FUNCTN
    [InlineData(109, "marksNavigationalSystemOf")]   // MARSYS
    [InlineData(117, "orientationValue")]        // ORIENT
    [InlineData(131, "restriction")]             // RESTRN
    [InlineData(141, "signalGroup")]             // SIGGRP
    [InlineData(142, "signalPeriod")]            // SIGPER
    [InlineData(156, "techniqueOfVerticalMeasurement")] // TECSOU
    [InlineData(172, "trafficFlow")]             // TRAFIC
    [InlineData(178, "valueOfNominalRange")]     // VALNMR
    [InlineData(185, "verticalDatum")]           // VERDAT
    public void GapAttributes_MapToExpectedS101Code(ushort attl, string expected)
    {
        var m = S57S101Mapping.Default;
        Assert.Equal(expected, m.ResolveAttributeCode(attl));
    }

    [Theory]
    [InlineData(11, "categoryOfCable")]          // CATCBL
    [InlineData(27, "categoryOfFogSignal")]      // CATFOG
    [InlineData(47, "categoryOfPipelinePipe")]   // CATPIP
    [InlineData(59, "categoryOfSeaArea")]        // CATSEA
    [InlineData(63, "categoryOfSiloTank")]       // CATSIL
    [InlineData(68, "categoryOfVegetation")]     // CATVEG
    [InlineData(70, "categoryOfWeedKelp")]       // CATWED
    [InlineData(111, "nationality")]             // NATION
    [InlineData(176, "valueOfMagneticVariation")] // VALMAG
    [InlineData(188, "categoryOfTidalStream")]   // CAT_TS
    public void GapAttributesSecondWave_MapToExpectedS101Code(ushort attl, string expected)
    {
        var m = S57S101Mapping.Default;
        Assert.Equal(expected, m.ResolveAttributeCode(attl));
    }

    // Attributes that are sub-attributes of an S-101 *complex* attribute must
    // stay unmapped here — a flat emission would be non-conformant. They need
    // dedicated complex-attribute assembly (like OBJNAM → featureName).
    // (SECTR1/SECTR2 are the exception: they carry a rule so the LightSectored
    // feature redirect can see them, but the translator still diverts them into
    // the sectorCharacteristics complex — see SectorLimitRedirectAttributes.)
    [Theory]
    [InlineData(116)] // OBJNAM → name (sub of featureName)
    [InlineData(107)] // LITCHR → lightCharacteristic
    [InlineData(98)]  // HORCLR → horizontalClearanceValue
    [InlineData(85)]  // DATEND → dateEnd
    [InlineData(86)]  // DATSTA → dateStart
    [InlineData(114)] // NATQUA → natureOfSurfaceQualifyingTerms
    [InlineData(147)] // SORDAT → complex sourceIndication/reportedDate
    [InlineData(148)] // SORIND → complex sourceIndication
    public void ComplexSubAttributes_RemainUnmapped(ushort attl)
    {
        var m = S57S101Mapping.Default;
        Assert.Null(m.ResolveAttributeCode(attl));
    }

    // SECTR1/SECTR2 carry a rule (mapping to sectorBearing) solely so the
    // sectored-light feature redirect can detect them in the acronym view; the
    // translator diverts them into the sectorCharacteristics complex.
    [Theory]
    [InlineData(136)] // SECTR1
    [InlineData(137)] // SECTR2
    public void SectorLimitRedirectAttributes_MapToSectorBearing(ushort attl)
    {
        var m = S57S101Mapping.Default;
        Assert.Equal("sectorBearing", m.ResolveAttributeCode(attl));
    }

    // ── BRIDGE / CATBRG (S-65 Annex B § 4.8.10) ────────────────────────

    [Theory]
    [InlineData("1", "openingBridge", "false")]           // fixed bridge
    [InlineData("2", "openingBridge", "true")]            // opening bridge
    [InlineData("3", "categoryOfOpeningBridge", "3")]     // swing bridge
    [InlineData("4", "categoryOfOpeningBridge", "4")]     // lifting bridge
    [InlineData("5", "categoryOfOpeningBridge", "5")]     // bascule bridge
    [InlineData("6", "bridgeConstruction", "3")]          // pontoon bridge
    [InlineData("7", "categoryOfOpeningBridge", "7")]     // draw bridge
    [InlineData("8", "bridgeConstruction", "5")]          // transporter bridge
    [InlineData("9", "bridgeFunction", "3")]              // footbridge → pedestrian
    [InlineData("10", "bridgeConstruction", "2")]         // viaduct
    [InlineData("11", "bridgeFunction", "4")]             // aqueduct
    [InlineData("12", "bridgeConstruction", "4")]         // suspension bridge
    public void Bridge_CatbrgValue_MapsToS101BridgeAttribute(string s57Value, string expectedCode, string expectedValue)
    {
        var m = S57S101Mapping.Default;
        var resolved = m.ResolveFeature(11, new Dictionary<string, string> { ["CATBRG"] = s57Value }); // BRIDGE
        Assert.NotNull(resolved);
        Assert.Equal("Bridge", resolved!.S101Code);

        var attr = m.ResolveAttribute("CATBRG", s57Value, resolved);

        Assert.NotNull(attr);
        Assert.Equal(expectedCode, attr!.S101Code);
        Assert.Equal(expectedValue, attr.Value);
    }

    [Fact]
    public void Default_AttributeTargets_AreDefinedByS101Catalogue()
    {
        // Every S-101 attribute the default table can emit — rule defaults and
        // per-feature / per-redirect overrides — must exist in the bundled
        // S-101 Feature Catalogue.
        using var stream = EncDotNet.S100.Specifications.Specification.TryOpenFeatureCatalogue("S-101");
        Assert.NotNull(stream);
        var fc = EncDotNet.S100.Features.FeatureCatalogueReader.Read(stream!);
        var defined = fc.SimpleAttributes.Select(a => a.Code)
            .Concat(fc.ComplexAttributes.Select(a => a.Code))
            .ToHashSet(StringComparer.Ordinal);

        var m = S57S101Mapping.Default;
        var targets = new List<(string Source, string Code)>();
        foreach (var (attl, rule) in m.AttributeRules)
        {
            if (rule.DefaultS101Code is not null)
                targets.Add(($"ATTL {attl} {rule.S57Acronym}", rule.DefaultS101Code));
        }

        foreach (var (objl, rule) in m.FeatureRules)
        {
            var overrides = rule.Redirects
                .SelectMany(r => r.AttributeOverrides)
                .Concat(rule.AttributeOverrides);
            foreach (var (acronym, ov) in overrides)
            {
                var source = $"OBJL {objl} {rule.S57Acronym}/{acronym}";
                if (ov.S101Code is not null)
                    targets.Add((source, ov.S101Code));
                targets.AddRange(ov.S101CodeByValue.Values.Select(code => (source, code)));
            }
        }

        Assert.NotEmpty(targets);
        var undefined = targets.Where(t => !defined.Contains(t.Code)).Select(t => $"{t.Source} → {t.Code}");
        Assert.Empty(undefined);
    }

    // ── Mapping per target product (issue #608) ──────────────────────

    [Fact]
    public void ForSpec_S101_IsDefault()
    {
        Assert.Same(S57S101Mapping.Default, S57S101Mapping.ForSpec("S-101"));
        Assert.Same(S57S101Mapping.Default, S57S101Mapping.ForSpec("s-101"));
    }

    [Fact]
    public void ForSpec_S401_IsSharedAndDistinct()
    {
        var s401 = S57S101Mapping.ForSpec("S-401");

        Assert.Same(s401, S57S101Mapping.ForSpec("S-401"));
        Assert.NotSame(S57S101Mapping.Default, s401);
    }

    [Fact]
    public void ForSpec_S401_OnlyTargetsClassesTheS401CatalogueDefines()
    {
        var catalogue = S101FeatureAttributeBindings.ForSpec("S-401");

        foreach (var rule in S57S101Mapping.ForSpec("S-401").FeatureRules.Values)
        {
            if (rule.DefaultS101Code is { } code)
                Assert.True(catalogue.DefinesFeatureType(code), $"{rule.S57Acronym} → {code}");
            foreach (var redirect in rule.Redirects)
                Assert.True(catalogue.DefinesFeatureType(redirect.TargetS101Code), $"{rule.S57Acronym} → {redirect.TargetS101Code}");
        }
    }

    [Fact]
    public void ForSpec_S401_KeepsEveryObjectClassSoDropsAreReportedAsRuleDrops()
    {
        var s401 = S57S101Mapping.ForSpec("S-401");

        // Every standard code is kept (the S-401 table adds the inland codes on top).
        Assert.Subset(s401.FeatureRules.Keys.ToHashSet(), S57S101Mapping.Default.FeatureRules.Keys.ToHashSet());
        Assert.Subset(s401.AttributeRules.Keys.ToHashSet(), S57S101Mapping.Default.AttributeRules.Keys.ToHashSet());

        // RAPIDS → Rapids (S-101 only) loses its target; COALNE → Coastline keeps it.
        Assert.Equal("Rapids", S57S101Mapping.Default.ResolveFeatureCode(107));
        Assert.Null(s401.ResolveFeatureCode(107));
        Assert.Equal("Coastline", s401.ResolveFeatureCode(30));
    }

    [Fact]
    public void Default_RestrictedToS101Catalogue_IsUnchanged()
    {
        // The default table must only target classes the S-101 FC defines.
        var restricted = S57S101Mapping.Default.RestrictToFeatureTypes(
            S101FeatureAttributeBindings.Default.DefinesFeatureType);

        foreach (var (objl, rule) in S57S101Mapping.Default.FeatureRules)
            Assert.Same(rule, restricted.FeatureRules[objl]);
    }

    [Fact]
    public void RestrictToFeatureTypes_RemovesRedirectsToUndefinedClasses()
    {
        var mapping = new S57S101Mapping.Builder()
            .AddFeatureRule(new S57FeatureRule
            {
                Objl = 33,
                S57Acronym = "CTRPNT",
                DefaultS101Code = "Kept",
                Redirects =
                [
                    new S57FeatureRedirect { ConditionAttribute = "CATCTR", ConditionValues = ["1"], TargetS101Code = "Gone" },
                    new S57FeatureRedirect { ConditionAttribute = "CATCTR", ConditionValues = ["5"], TargetS101Code = "Kept" },
                ],
            })
            .Build();

        var restricted = mapping.RestrictToFeatureTypes(code => code == "Kept");

        var rule = restricted.FeatureRules[33];
        Assert.Equal("Kept", rule.DefaultS101Code);
        Assert.Equal("Kept", Assert.Single(rule.Redirects).TargetS101Code);
        Assert.Throws<ArgumentNullException>(() => mapping.RestrictToFeatureTypes(null!));
    }

    [Fact]
    public void ForSpec_UnbundledOrBlankSpec_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => S57S101Mapping.ForSpec("S-999"));
        Assert.Throws<ArgumentException>(() => S57S101Mapping.ForSpec(""));
    }
}
