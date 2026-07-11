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
