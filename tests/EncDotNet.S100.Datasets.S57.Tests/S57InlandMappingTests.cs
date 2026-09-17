using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.S57.Tests;

/// <summary>
/// The inland ENC (IENC) object classes and attributes in the S-401 mapping
/// (<see cref="S57S101Mapping.ForSpec"/>), issue #608. Codes come from the IEHG
/// Inland ENC Feature Catalogue 2.4; targets are the S-401 Feature Catalogue
/// entries that alias the IENC acronym.
/// </summary>
public class S57InlandMappingTests
{
    private static readonly S57S101Mapping S401 = S57S101Mapping.ForSpec("S-401");

    private static readonly Lazy<FeatureCatalogue> S401Catalogue = new(() =>
    {
        using var stream = Specification.TryOpenFeatureCatalogue("S-401")!;
        return FeatureCatalogueReader.Read(stream);
    });

    [Fact]
    public void S401Mapping_CoversEveryInlandCodeOfTheIencFeatureCatalogue()
    {
        var inlandObjects = S401.FeatureRules.Keys.Where(k => k >= 17000).ToList();
        var inlandAttributes = S401.AttributeRules.Keys.Where(k => k >= 17000).ToList();

        // IENC Feature Catalogue 2.4 corr. 2: 53 inland object classes, 91 inland attributes.
        Assert.Equal(53, inlandObjects.Count);
        Assert.Equal(91, inlandAttributes.Count);
    }

    [Fact]
    public void S101Mapping_HasNoInlandCodes()
    {
        Assert.DoesNotContain(S57S101Mapping.Default.FeatureRules.Keys, k => k >= 17000);
        Assert.DoesNotContain(S57S101Mapping.Default.AttributeRules.Keys, k => k >= 17000);
    }

    [Theory]
    // The codes USACE inland cells actually use (21 sampled cells), with
    // their IENC acronym and S-401 target.
    [InlineData(17032, "slcons", "ShorelineConstruction")]
    [InlineData(17004, "dismar", "DistanceMark")]
    [InlineData(17028, "bcnlat", "LateralBeacon")]
    [InlineData(17011, "bridge", "Bridge")]
    [InlineData(17012, "cblohd", "CableOverhead")]
    [InlineData(17010, "berths", "Berth")]
    [InlineData(17008, "sistaw", "SignalStationWarning")]
    [InlineData(17050, "notmrk", "NoticeMark")]
    [InlineData(17067, "wtwgag", "WaterwayGauge")]
    [InlineData(17021, "ponton", "Pontoon")]
    [InlineData(17035, "daymar", "Daymark")]
    [InlineData(17007, "sistat", "SignalStationTraffic")]
    [InlineData(17005, "resare", "RestrictedArea")]
    [InlineData(17016, "lokbsn", "LockBasin")]
    [InlineData(17018, "m_nsys", "NavigationalSystemOfMarks")]
    [InlineData(17064, "termnl", "Terminal")]
    [InlineData(17024, "pipohd", "PipelineOverhead")]
    [InlineData(17025, "flodoc", "FloatingDock")]
    [InlineData(17020, "hulkes", "Hulk")]
    [InlineData(17030, "cranes", "Crane")]
    [InlineData(17001, "achare", "AnchorageArea")]
    [InlineData(18004, "sensor", "Sensor")]
    public void S401Mapping_MapsObservedInlandObjectClasses(int objl, string acronym, string target)
    {
        var rule = S401.FeatureRules[(ushort)objl];

        Assert.Equal(acronym, rule.S57Acronym);
        Assert.Equal(target, rule.DefaultS101Code);
    }

    [Theory]
    [InlineData(17012, "catslc", "categoryOfShorelineConstruction")]
    [InlineData(17104, "watlev", "waterLevelEffect")]
    [InlineData(17064, "wtwdis", "waterwayDistance")]
    [InlineData(17011, "catlam", "categoryOfLateralMark")]
    [InlineData(17101, "catcbl", "categoryOfCable")]
    [InlineData(17066, "catbrt", "categoryOfBerth")]
    [InlineData(17056, "dirimp", "directionOfImpact")]
    [InlineData(17003, "catsiw", "categoryOfSignalStationWarning")]
    [InlineData(17052, "catnmk", "categoryOfNoticeMark")]
    [InlineData(17063, "fnctnm", "functionOfNoticeMark")]
    [InlineData(17078, "catgag", "categoryOfWaterwayGauge")]
    [InlineData(17002, "catsit", "categoryOfSignalStationTraffic")]
    [InlineData(17004, "restrn", "restriction")]
    [InlineData(17009, "marsys", "marksNavigationalSystemOf")]
    [InlineData(17008, "cathaf", "categoryOfHarbourFacility")]
    [InlineData(17076, "trshgd", "transshippingGoods")]
    [InlineData(17102, "cathlk", "categoryOfHulk")]
    [InlineData(17088, "reflev", "referenceGravitationalLevel")]
    [InlineData(17077, "unlocd", "uNLocationCode")]
    [InlineData(17000, "catach", "categoryOfAnchorage")]
    [InlineData(18019, "catsen", "categoryOfSensor")]
    [InlineData(18020, "fnctsn", "functionOfSensor")]
    public void S401Mapping_MapsObservedInlandAttributes(int attl, string acronym, string target)
    {
        var rule = S401.AttributeRules[(ushort)attl];

        Assert.Equal(acronym, rule.S57Acronym);
        Assert.Equal(target, rule.DefaultS101Code);
    }

    [Theory]
    [InlineData(17074, "horcll")] // lock basin dimensions need a complex attribute
    [InlineData(17075, "horclw")]
    public void S401Mapping_DefersInlandAttributesThatNeedComplexAssembly(int attl, string acronym)
    {
        var rule = S401.AttributeRules[(ushort)attl];

        Assert.Equal(acronym, rule.S57Acronym);
        Assert.Null(rule.DefaultS101Code);
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("2", null)] // feet: no S-401 equivalent
    [InlineData("3", "3")]
    [InlineData("4", "7")]
    [InlineData("5", "4")]
    [InlineData("6", "5")]
    public void S401Mapping_Hunits_RemapsToDistanceUnitOfMeasurement(string hunits, string? expected)
    {
        var feature = new ResolvedFeature("DistanceMark", new Dictionary<string, S57AttributeOverride>());

        var resolved = S401.ResolveAttribute(17103, hunits, feature);

        Assert.Equal(expected, resolved?.Value);
        if (resolved is not null)
            Assert.Equal("distanceUnitOfMeasurement", resolved.S101Code);
    }

    [Fact]
    public void S401Mapping_InlandTwins_ReuseTheirStandardRule()
    {
        // An inland twin re-registers a standard acronym in lower case; it must
        // translate exactly as its upper-case counterpart does.
        foreach (var rule in S401.FeatureRules.Values.Where(r => r.Objl >= 17000))
        {
            var twin = S401.FeatureRules.Values.FirstOrDefault(r =>
                r.Objl < 17000 && string.Equals(r.S57Acronym, rule.S57Acronym, StringComparison.OrdinalIgnoreCase));
            if (twin is null) continue;

            Assert.Equal(twin.DefaultS101Code, rule.DefaultS101Code);
            Assert.Equal(twin.Redirects, rule.Redirects);
            Assert.Same(twin.AttributeOverrides, rule.AttributeOverrides);
        }

        foreach (var rule in S401.AttributeRules.Values.Where(r => r.Attl >= 17000))
        {
            var twin = S401.AttributeRules.Values.FirstOrDefault(r =>
                r.Attl < 17000 && string.Equals(r.S57Acronym, rule.S57Acronym, StringComparison.OrdinalIgnoreCase));
            if (twin is null) continue;

            Assert.Equal(twin.DefaultS101Code, rule.DefaultS101Code);
            Assert.Same(twin.DefaultValueRemap, rule.DefaultValueRemap);
        }
    }

    private static IReadOnlyDictionary<string, string> Attributes(string acronym, string value)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [acronym] = value };

    [Fact]
    public void S401Mapping_InlandNavigationalSystemOfMarksWithOrient_RedirectsToLocalDirectionOfBuoyage()
    {
        // IEHG conversion guidance 3.77: m_nsys (the inland twin of M_NSYS)
        // with an ORIENT value becomes LocalDirectionOfBuoyage.
        var feature = S401.ResolveFeature(17018, Attributes("ORIENT", "270"))!;

        Assert.Equal("LocalDirectionOfBuoyage", feature.S101Code);
        Assert.Equal("orientationValue", S401.ResolveAttribute(117, "270", feature)!.S101Code);
        Assert.Equal("marksNavigationalSystemOf", S401.ResolveAttribute(17009, "11", feature)!.S101Code);

        var plain = S401.ResolveFeature(17018, Attributes("marsys", "11"))!;
        Assert.Equal("NavigationalSystemOfMarks", plain.S101Code);
    }

    [Theory]
    [InlineData(17001, 17000, "catach")]
    [InlineData(4, 8, "CATACH")]
    public void S401Mapping_AnchorageAreaWithSmallCraftMooring_RedirectsToMooringArea(int objl, int attl, string acronym)
    {
        var feature = S401.ResolveFeature((ushort)objl, Attributes(acronym, "8"))!;

        Assert.Equal("MooringArea", feature.S101Code);
        var attribute = S401.ResolveAttribute((ushort)attl, "8", feature)!;
        Assert.Equal("categoryOfMooringArea", attribute.S101Code);
        Assert.Equal("1", attribute.Value);

        // Only a lone 8 reaches MooringArea; any other anchorage code is dropped
        // rather than misread as a categoryOfMooringArea code.
        Assert.Null(S401.ResolveAttribute((ushort)attl, "3", feature));
        Assert.Null(S401.ResolveAttribute((ushort)attl, "10", feature));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("7")]
    [InlineData("10")]
    [InlineData("7,8")]
    public void S401Mapping_AnchorageAreaWithOtherCategory_StaysAnchorageArea(string catach)
    {
        Assert.Equal("AnchorageArea", S401.ResolveFeature(17001, Attributes("catach", catach))!.S101Code);
        Assert.Equal("AnchorageArea", S401.ResolveFeature(4, Attributes("CATACH", catach))!.S101Code);
        Assert.Equal("AnchorageArea", S401.ResolveFeature(17001, Attributes("OBJNAM", "x"))!.S101Code);
    }

    [Theory]
    [InlineData(17000)]
    [InlineData(8)]
    public void S401Mapping_CategoryOfAnchorage_Remaps10To16(int attl)
    {
        var anchorage = S401.ResolveFeature(17001, Attributes("OBJNAM", "x"))!;

        var remapped = S401.ResolveAttribute((ushort)attl, "10", anchorage)!;
        Assert.Equal("categoryOfAnchorage", remapped.S101Code);
        Assert.Equal("16", remapped.Value);
        Assert.Equal("9", S401.ResolveAttribute((ushort)attl, "9", anchorage)!.Value);
        Assert.Equal("8", S401.ResolveAttribute((ushort)attl, "8", anchorage)!.Value);
    }

    [Fact]
    public void S101Mapping_AnchorageRules_AreUnchanged()
    {
        // The S-401 anchorage rules must not leak into the S-101 default.
        var s101 = S57S101Mapping.Default;
        Assert.Empty(s101.FeatureRules[4].Redirects);
        Assert.Empty(s101.AttributeRules[8].DefaultValueRemap);

        var feature = s101.ResolveFeature(4, Attributes("CATACH", "8"))!;
        Assert.Equal("AnchorageArea", feature.S101Code);
        Assert.Equal("8", s101.ResolveAttribute(8, "8", feature)!.Value);
        Assert.Equal("10", s101.ResolveAttribute(8, "10", feature)!.Value);
    }

    [Fact]
    public void S401Mapping_AnchorageRules_ShareOneRuleAcrossTwins()
    {
        Assert.Same(S401.FeatureRules[4].Redirects, S401.FeatureRules[17001].Redirects);
        Assert.Same(S401.AttributeRules[8].DefaultValueRemap, S401.AttributeRules[17000].DefaultValueRemap);
    }

    [Fact]
    public void S401Mapping_OnlyTargetsWhatTheS401CatalogueDefines()
    {
        var catalogue = S101FeatureAttributeBindings.ForSpec("S-401");

        foreach (var rule in S401.FeatureRules.Values)
        {
            if (rule.DefaultS101Code is { } code)
                Assert.True(catalogue.DefinesFeatureType(code), $"{rule.S57Acronym} → {code}");
        }

        foreach (var rule in S401.AttributeRules.Values)
        {
            if (rule.DefaultS101Code is { } code)
                Assert.True(catalogue.DefinesAttribute(code), $"{rule.S57Acronym} → {code}");
        }
    }

    [Fact]
    public void S401Mapping_InlandAttributeTargets_AreBoundByAFeatureOrInformationType()
    {
        // A target must be bound directly by a feature type, or by the
        // information type the translator emits for a time schedule; the
        // translator gates the latter off features.
        var catalogue = S401Catalogue.Value;
        var bound = catalogue.FeatureTypes
            .SelectMany(ft => ft.AttributeBindings)
            .Concat(catalogue.InformationTypes.Single(it => it.Code == "TimeScheduleInGeneral").AttributeBindings)
            .Select(b => b.AttributeRef)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var rule in S401.AttributeRules.Values.Where(r => r.Attl >= 17000))
        {
            if (rule.DefaultS101Code is { } code)
                Assert.True(bound.Contains(code), $"{rule.S57Acronym} → {code}");
        }
    }

    [Theory]
    // Bound on S-401 TimeScheduleInGeneral (alias tisdge).
    [InlineData(17092, "cattab", "categoryOfTimeAndBehaviour")]
    [InlineData(17093, "schref", "timeScheduleReference")]
    [InlineData(17094, "useshp", "useOfShip")]
    [InlineData(17099, "aptref", "averagePassingTimeReference")]
    [InlineData(33066, "shptyp", "typeOfShip")]
    public void S401Mapping_MapsTimeScheduleAttributes(int attl, string acronym, string target)
    {
        var rule = S401.AttributeRules[(ushort)attl];

        Assert.Equal(acronym, rule.S57Acronym);
        Assert.Equal(target, rule.DefaultS101Code);
        Assert.True(S101FeatureAttributeBindings.ForSpec("S-401").Binds("TimeScheduleInGeneral", target));
    }

    [Fact]
    public void S401Mapping_DropsAttributesTheS401CatalogueLacks()
    {
        // CATICE → categoryOfIce exists in S-101 but not in S-401.
        Assert.Equal("categoryOfIce", S57S101Mapping.Default.ResolveAttributeCode(32));
        Assert.Null(S401.ResolveAttributeCode(32));

        // CATBRG maps to the bridge category attributes S-101 and S-401 share
        // (categoryOfOpeningBridge and friends), so S-401 keeps it.
        Assert.Equal("categoryOfOpeningBridge", S401.ResolveAttributeCode(9));
    }

    [Fact]
    public void RestrictToAttributes_ClearsUndefinedTargetsOnly()
    {
        var mapping = new S57S101Mapping.Builder()
            .AddAttribute(1, "AAAAAA", "kept")
            .AddAttribute(2, "BBBBBB", "gone")
            .Build();

        var restricted = mapping.RestrictToAttributes(code => code == "kept");

        Assert.Equal("kept", restricted.ResolveAttributeCode(1));
        Assert.Null(restricted.ResolveAttributeCode(2));
        Assert.True(restricted.AttributeRules.ContainsKey(2));
        Assert.Throws<ArgumentNullException>(() => mapping.RestrictToAttributes(null!));
    }

    [Fact]
    public void ResolveAttribute_ByAttl_DistinguishesAcronymTwins()
    {
        // CATSLC and its inland twin catslc share an acronym case-insensitively;
        // resolving by ATTL must still pick each code's own rule.
        var mapping = new S57S101Mapping.Builder()
            .AddAttribute(60, "CATSLC", "standardTarget")
            .AddAttribute(17012, "catslc", "inlandTarget")
            .Build();
        var feature = new ResolvedFeature("ShorelineConstruction",
            new Dictionary<string, S57AttributeOverride>());

        Assert.Equal("standardTarget", mapping.ResolveAttribute(60, "1", feature)!.S101Code);
        Assert.Equal("inlandTarget", mapping.ResolveAttribute(17012, "1", feature)!.S101Code);
        Assert.Null(mapping.ResolveAttribute(999, "1", feature));
    }

    [Fact]
    public void DefinesAttribute_FollowsCatalogue()
    {
        Assert.True(S101FeatureAttributeBindings.Default.DefinesAttribute("categoryOfIce"));
        Assert.False(S101FeatureAttributeBindings.ForSpec("S-401").DefinesAttribute("categoryOfIce"));
        Assert.False(S101FeatureAttributeBindings.ForSpec("S-401").DefinesAttribute("categoryofice"));
        Assert.True(S101FeatureAttributeBindings.ForSpec("S-401").DefinesAttribute("categoryOfNoticeMark"));
        Assert.False(S101FeatureAttributeBindings.Default.DefinesAttribute("categoryOfNoticeMark"));
        Assert.False(S101FeatureAttributeBindings.Default.DefinesAttribute(null));
    }
}
