using System.Collections.ObjectModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Tests for <c>S101LuaDataProvider.ResolveAttributeScope</c> — specifically
/// that co-occurring complex attributes which share sub-attribute codes (for
/// example <c>fixedDateRange</c> and <c>periodicDateRange</c>, which both carry
/// <c>dateStart</c>/<c>dateEnd</c>) are delimited from one another so a query
/// scoped to one complex does not absorb a sibling complex's sub-attributes.
/// </summary>
public class S101LuaDataProviderComplexScopeTests
{
    // Numeric attribute codes for the synthetic flat attribute list.
    private const ushort FixedDateRange = 1;
    private const ushort PeriodicDateRange = 2;
    private const ushort DateStart = 3;
    private const ushort DateEnd = 4;

    [Fact]
    public void SimpleAttribute_ScopedToFirstComplex_DoesNotAbsorbSiblingComplex()
    {
        var provider = CreateProvider();
        var context = new RecordingLuaContext();
        provider.RegisterHostFunctions(context);

        var getSimple = Assert.IsAssignableFrom<Func<double, string, string, List<object>>>(
            context.Globals["HostFeatureGetSimpleAttribute"]);

        // fixedDateRange:1 must yield only ITS dateStart / dateEnd, not the
        // periodicDateRange values that follow it in the flat list.
        var fixedStart = getSimple(1, "fixedDateRange:1", "dateStart");
        var fixedEnd = getSimple(1, "fixedDateRange:1", "dateEnd");
        Assert.Equal(new object[] { "20200101" }, fixedStart);
        Assert.Equal(new object[] { "20201231" }, fixedEnd);

        // periodicDateRange:1 must yield only ITS own values.
        var periodicStart = getSimple(1, "periodicDateRange:1", "dateStart");
        var periodicEnd = getSimple(1, "periodicDateRange:1", "dateEnd");
        Assert.Equal(new object[] { "20200401" }, periodicStart);
        Assert.Equal(new object[] { "20200930" }, periodicEnd);
    }

    [Fact]
    public void ComplexAttributeCount_CountsEachComplexIndependently()
    {
        var provider = CreateProvider();
        var context = new RecordingLuaContext();
        provider.RegisterHostFunctions(context);

        var count = Assert.IsAssignableFrom<Func<double, string, string, double>>(
            context.Globals["HostFeatureGetComplexAttributeCount"]);

        Assert.Equal(1, count(1, "", "fixedDateRange"));
        Assert.Equal(1, count(1, "", "periodicDateRange"));
    }

    private static S101LuaDataProvider CreateProvider()
    {
        // Flat attribute list for one feature carrying two sibling complexes
        // that share the dateStart/dateEnd sub-attribute codes:
        //   [fixedDateRange][dateStart][dateEnd][periodicDateRange][dateStart][dateEnd]
        var attributes = new[]
        {
            new S101Attribute(FixedDateRange, 1, string.Empty),
            new S101Attribute(DateStart, 1, "20200101"),
            new S101Attribute(DateEnd, 1, "20201231"),
            new S101Attribute(PeriodicDateRange, 1, string.Empty),
            new S101Attribute(DateStart, 1, "20200401"),
            new S101Attribute(DateEnd, 1, "20200930"),
        };

        var document = new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "scope-test" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 10_000_000,
                CoordinateMultiplicationFactorY = 10_000_000,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = new ReadOnlyDictionary<ushort, string>(
                new Dictionary<ushort, string> { [1] = "TestFeature" }),
            AttributeTypeCatalogue = new ReadOnlyDictionary<ushort, string>(
                new Dictionary<ushort, string>
                {
                    [FixedDateRange] = "fixedDateRange",
                    [PeriodicDateRange] = "periodicDateRange",
                    [DateStart] = "dateStart",
                    [DateEnd] = "dateEnd",
                }),
            Points = ReadOnlyDictionary<uint, S101PointRecord>.Empty,
            CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features =
            [
                new S101FeatureRecord
                {
                    RecordId = 1,
                    FeatureTypeCode = 1,
                    Attributes = attributes,
                },
            ],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };

        // The feature catalogue must declare fixedDateRange / periodicDateRange
        // as complex attributes so the provider recognises their markers when
        // delimiting one instance from the next.
        var featureCatalogue = new FeatureCatalogue
        {
            Name = "S-101 test",
            VersionNumber = "1.0.0",
            VersionDate = "2024-01-01",
            ComplexAttributes =
            [
                new ComplexAttribute { Name = "Fixed Date Range", Code = "fixedDateRange" },
                new ComplexAttribute { Name = "Periodic Date Range", Code = "periodicDateRange" },
            ],
            SimpleAttributes =
            [
                new SimpleAttribute { Name = "Date Start", Code = "dateStart", ValueType = "S100_TruncatedDate" },
                new SimpleAttribute { Name = "Date End", Code = "dateEnd", ValueType = "S100_TruncatedDate" },
            ],
        };

        return new S101LuaDataProvider(S101Dataset.FromDocument(document), featureCatalogue);
    }

    // ── Nested complex attributes (rhythmOfLight → signalSequence) ──────

    // Numeric codes for the nested-complex fixture.
    private const ushort RhythmOfLight = 10;
    private const ushort LightCharacteristic = 11;
    private const ushort SignalGroup = 12;
    private const ushort SignalSequence = 13;
    private const ushort SignalDuration = 14;
    private const ushort SignalStatus = 15;

    // Codes for the three-level sector-geometry nesting test.
    private const ushort SectorCharacteristics = 20;
    private const ushort LightSector = 21;
    private const ushort SectorLimit = 22;
    private const ushort SectorLimitOne = 23;
    private const ushort SectorLimitTwo = 24;
    private const ushort SectorBearing = 25;
    private const ushort Colour = 26;

    [Fact]
    public void NestedComplex_ScopedThroughParent_ResolvesChildSubAttributes()
    {
        var provider = CreateNestedProvider();
        var context = new RecordingLuaContext();
        provider.RegisterHostFunctions(context);

        var getSimple = Assert.IsAssignableFrom<Func<double, string, string, List<object>>>(
            context.Globals["HostFeatureGetSimpleAttribute"]);

        // rhythmOfLight:1;signalSequence:1 must resolve the FIRST nested phase.
        Assert.Equal(new object[] { "2.0" }, getSimple(1, "rhythmOfLight:1;signalSequence:1", "signalDuration"));
        Assert.Equal(new object[] { "1" }, getSimple(1, "rhythmOfLight:1;signalSequence:1", "signalStatus"));

        // …and signalSequence:2 the SECOND nested phase.
        Assert.Equal(new object[] { "24.0" }, getSimple(1, "rhythmOfLight:1;signalSequence:2", "signalDuration"));
        Assert.Equal(new object[] { "2" }, getSimple(1, "rhythmOfLight:1;signalSequence:2", "signalStatus"));
    }

    [Fact]
    public void NestedComplex_ParentSimpleSubAttribute_StillResolvesWithNestedChildPresent()
    {
        var provider = CreateNestedProvider();
        var context = new RecordingLuaContext();
        provider.RegisterHostFunctions(context);

        var getSimple = Assert.IsAssignableFrom<Func<double, string, string, List<object>>>(
            context.Globals["HostFeatureGetSimpleAttribute"]);

        // The parent's own simple sub-attributes must remain readable even
        // though a nested signalSequence complex follows them in the flat list.
        Assert.Equal(new object[] { "2" }, getSimple(1, "rhythmOfLight:1", "lightCharacteristic"));
        Assert.Equal(new object[] { "(2)" }, getSimple(1, "rhythmOfLight:1", "signalGroup"));
    }

    [Fact]
    public void NestedComplex_CountedWithinParentScope()
    {
        var provider = CreateNestedProvider();
        var context = new RecordingLuaContext();
        provider.RegisterHostFunctions(context);

        var count = Assert.IsAssignableFrom<Func<double, string, string, double>>(
            context.Globals["HostFeatureGetComplexAttributeCount"]);

        Assert.Equal(2, count(1, "rhythmOfLight:1", "signalSequence"));
    }

    [Fact]
    public void DeeplyNestedComplex_ThreeLevels_ResolvesGrandchildSubAttributes()
    {
        var provider = CreateSectorProvider();
        var context = new RecordingLuaContext();
        provider.RegisterHostFunctions(context);

        var getSimple = Assert.IsAssignableFrom<Func<double, string, string, List<object>>>(
            context.Globals["HostFeatureGetSimpleAttribute"]);

        // The two sectorBearing values live three levels deep and share the
        // same code, distinguished only by their sectorLimitOne / sectorLimitTwo
        // parent. A transitive-descendant scope must isolate each.
        Assert.Equal(new object[] { "340.3" },
            getSimple(1, "sectorCharacteristics:1;lightSector:1;sectorLimit:1;sectorLimitOne:1", "sectorBearing"));
        Assert.Equal(new object[] { "8.3" },
            getSimple(1, "sectorCharacteristics:1;lightSector:1;sectorLimit:1;sectorLimitTwo:1", "sectorBearing"));

        // A simple sub-attribute one level down must still resolve.
        Assert.Equal(new object[] { "3" },
            getSimple(1, "sectorCharacteristics:1;lightSector:1", "colour"));

        // The top-level parent's own simple sub-attribute must remain readable
        // despite the deeply nested descendants that follow it.
        Assert.Equal(new object[] { "2" },
            getSimple(1, "sectorCharacteristics:1", "lightCharacteristic"));
    }

    private static S101LuaDataProvider CreateSectorProvider()
    {
        // Flat pre-order attribute list for one feature carrying a
        // sectorCharacteristics complex three levels deep:
        //   [sectorCharacteristics][lightCharacteristic]
        //     [lightSector][colour]
        //       [sectorLimit]
        //         [sectorLimitOne][sectorBearing]
        //         [sectorLimitTwo][sectorBearing]
        var attributes = new[]
        {
            new S101Attribute(SectorCharacteristics, 1, string.Empty),
            new S101Attribute(LightCharacteristic, 1, "2"),
            new S101Attribute(LightSector, 1, string.Empty),
            new S101Attribute(Colour, 1, "3"),
            new S101Attribute(SectorLimit, 1, string.Empty),
            new S101Attribute(SectorLimitOne, 1, string.Empty),
            new S101Attribute(SectorBearing, 1, "340.3"),
            new S101Attribute(SectorLimitTwo, 1, string.Empty),
            new S101Attribute(SectorBearing, 1, "8.3"),
        };

        var document = new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "sector-scope-test" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 10_000_000,
                CoordinateMultiplicationFactorY = 10_000_000,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = new ReadOnlyDictionary<ushort, string>(
                new Dictionary<ushort, string> { [1] = "TestFeature" }),
            AttributeTypeCatalogue = new ReadOnlyDictionary<ushort, string>(
                new Dictionary<ushort, string>
                {
                    [SectorCharacteristics] = "sectorCharacteristics",
                    [LightCharacteristic] = "lightCharacteristic",
                    [LightSector] = "lightSector",
                    [SectorLimit] = "sectorLimit",
                    [SectorLimitOne] = "sectorLimitOne",
                    [SectorLimitTwo] = "sectorLimitTwo",
                    [SectorBearing] = "sectorBearing",
                    [Colour] = "colour",
                }),
            Points = ReadOnlyDictionary<uint, S101PointRecord>.Empty,
            CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features =
            [
                new S101FeatureRecord
                {
                    RecordId = 1,
                    FeatureTypeCode = 1,
                    Attributes = attributes,
                },
            ],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };

        static ComplexAttribute Complex(string code, params string[] refs)
            => new()
            {
                Name = code,
                Code = code,
                SubAttributeBindings = refs.Select(r => new SubAttributeBinding
                {
                    AttributeRef = r,
                    Multiplicity = new Multiplicity { Lower = 0, Upper = null, IsInfinite = true },
                }).ToArray(),
            };

        var featureCatalogue = new FeatureCatalogue
        {
            Name = "S-101 test",
            VersionNumber = "1.0.0",
            VersionDate = "2024-01-01",
            ComplexAttributes =
            [
                Complex("sectorCharacteristics", "lightCharacteristic", "lightSector"),
                Complex("lightSector", "colour", "sectorLimit"),
                Complex("sectorLimit", "sectorLimitOne", "sectorLimitTwo"),
                Complex("sectorLimitOne", "sectorBearing"),
                Complex("sectorLimitTwo", "sectorBearing"),
            ],
            SimpleAttributes =
            [
                new SimpleAttribute { Name = "Light Characteristic", Code = "lightCharacteristic", ValueType = "enumeration" },
                new SimpleAttribute { Name = "Colour", Code = "colour", ValueType = "enumeration" },
                new SimpleAttribute { Name = "Sector Bearing", Code = "sectorBearing", ValueType = "real" },
            ],
        };

        return new S101LuaDataProvider(S101Dataset.FromDocument(document), featureCatalogue);
    }

    private static S101LuaDataProvider CreateNestedProvider()
    {
        // Flat attribute list for one feature carrying a rhythmOfLight complex
        // with two nested signalSequence sub-complexes appended after its own
        // simple sub-attributes:
        //   [rhythmOfLight][lightCharacteristic][signalGroup]
        //     [signalSequence][signalDuration][signalStatus]
        //     [signalSequence][signalDuration][signalStatus]
        var attributes = new[]
        {
            new S101Attribute(RhythmOfLight, 1, string.Empty),
            new S101Attribute(LightCharacteristic, 1, "2"),
            new S101Attribute(SignalGroup, 1, "(2)"),
            new S101Attribute(SignalSequence, 1, string.Empty),
            new S101Attribute(SignalDuration, 1, "2.0"),
            new S101Attribute(SignalStatus, 1, "1"),
            new S101Attribute(SignalSequence, 1, string.Empty),
            new S101Attribute(SignalDuration, 1, "24.0"),
            new S101Attribute(SignalStatus, 1, "2"),
        };

        var document = new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "nested-scope-test" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 10_000_000,
                CoordinateMultiplicationFactorY = 10_000_000,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = new ReadOnlyDictionary<ushort, string>(
                new Dictionary<ushort, string> { [1] = "TestFeature" }),
            AttributeTypeCatalogue = new ReadOnlyDictionary<ushort, string>(
                new Dictionary<ushort, string>
                {
                    [RhythmOfLight] = "rhythmOfLight",
                    [LightCharacteristic] = "lightCharacteristic",
                    [SignalGroup] = "signalGroup",
                    [SignalSequence] = "signalSequence",
                    [SignalDuration] = "signalDuration",
                    [SignalStatus] = "signalStatus",
                }),
            Points = ReadOnlyDictionary<uint, S101PointRecord>.Empty,
            CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features =
            [
                new S101FeatureRecord
                {
                    RecordId = 1,
                    FeatureTypeCode = 1,
                    Attributes = attributes,
                },
            ],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };

        // rhythmOfLight must declare signalSequence as a complex sub-attribute
        // so the provider treats a signalSequence marker as a nested child
        // (collected into the parent scope) rather than a sibling terminator.
        var featureCatalogue = new FeatureCatalogue
        {
            Name = "S-101 test",
            VersionNumber = "1.0.0",
            VersionDate = "2024-01-01",
            ComplexAttributes =
            [
                new ComplexAttribute
                {
                    Name = "Rhythm Of Light",
                    Code = "rhythmOfLight",
                    SubAttributeBindings =
                    [
                        new SubAttributeBinding
                        {
                            AttributeRef = "lightCharacteristic",
                            Multiplicity = new Multiplicity { Lower = 1, Upper = 1 },
                        },
                        new SubAttributeBinding
                        {
                            AttributeRef = "signalGroup",
                            Multiplicity = new Multiplicity { Lower = 0, Upper = null, IsInfinite = true },
                        },
                        new SubAttributeBinding
                        {
                            AttributeRef = "signalSequence",
                            Multiplicity = new Multiplicity { Lower = 0, Upper = null, IsInfinite = true },
                        },
                    ],
                },
                new ComplexAttribute
                {
                    Name = "Signal Sequence",
                    Code = "signalSequence",
                    SubAttributeBindings =
                    [
                        new SubAttributeBinding
                        {
                            AttributeRef = "signalDuration",
                            Multiplicity = new Multiplicity { Lower = 1, Upper = 1 },
                        },
                        new SubAttributeBinding
                        {
                            AttributeRef = "signalStatus",
                            Multiplicity = new Multiplicity { Lower = 1, Upper = 1 },
                        },
                    ],
                },
            ],
            SimpleAttributes =
            [
                new SimpleAttribute { Name = "Light Characteristic", Code = "lightCharacteristic", ValueType = "enumeration" },
                new SimpleAttribute { Name = "Signal Group", Code = "signalGroup", ValueType = "text" },
                new SimpleAttribute { Name = "Signal Duration", Code = "signalDuration", ValueType = "real" },
                new SimpleAttribute { Name = "Signal Status", Code = "signalStatus", ValueType = "enumeration" },
            ],
        };

        return new S101LuaDataProvider(S101Dataset.FromDocument(document), featureCatalogue);
    }

    /// <summary>
    /// Minimal <see cref="ILuaContext"/> that records globals so the host
    /// bindings can be invoked directly without a real Lua engine.
    /// </summary>
    private sealed class RecordingLuaContext : ILuaContext
    {
        public Dictionary<string, object?> Globals { get; } = new();

        public void Execute(string source) { }

        public void SetModuleLoader(Func<string, string?> loader) { }

        public void SetGlobal(string name, object? value) => Globals[name] = value;

        public object? GetGlobal(string name) => Globals.TryGetValue(name, out var value) ? value : null;

        public object? Call(string functionName, params object?[] args) => null;

        public object?[] CallMultiReturn(string functionName, params object?[] args) => [];

        public void Dispose() { }
    }
}
