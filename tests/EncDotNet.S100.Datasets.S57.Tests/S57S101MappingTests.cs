using EncDotNet.S100.Datasets.S57;
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
}
