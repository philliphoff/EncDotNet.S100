using EncDotNet.S100.Features;
using EncDotNet.S100.Scripting;
using System.Collections.ObjectModel;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Tests for the diagnostic trace routing of <see cref="S101LuaDataProvider"/>.
/// </summary>
/// <remarks>
/// The S-100 Part 9A portrayal rules emit <c>Debug.Trace</c> diagnostics for
/// expected, spec-compliant fallbacks — for example the <c>OBSTRN07</c> rule
/// raising "Neither valueOfSounding or defaultClearanceDepth have a value" for an
/// Obstruction/Wreck/UnderwaterAwashRock feature that legitimately carries no
/// depth value, after which <c>main.lua</c> substitutes Default symbology. These
/// traces must not be written to standard output by default (issue #241).
/// </remarks>
public class S101LuaDataProviderTraceTests
{
    [Fact]
    public void DebugTrace_RoutesToInjectedSink()
    {
        var captured = new List<string>();
        var provider = CreateProvider(captured.Add);
        var context = new RecordingLuaContext();

        provider.RegisterHostFunctions(context);
        var hostTrace = Assert.IsAssignableFrom<Action<string>>(context.Globals["HostDebugTrace"]);

        hostTrace("Neither valueOfSounding or defaultClearanceDepth have a value");

        var message = Assert.Single(captured);
        Assert.Equal("[Lua] Neither valueOfSounding or defaultClearanceDepth have a value", message);
    }

    [Fact]
    public void DebugTrace_WithoutSink_DoesNotWriteToConsole()
    {
        var provider = CreateProvider(trace: null);
        var context = new RecordingLuaContext();
        provider.RegisterHostFunctions(context);
        var hostTrace = Assert.IsAssignableFrom<Action<string>>(context.Globals["HostDebugTrace"]);

        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            hostTrace("some diagnostic message");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(string.Empty, writer.ToString());
    }

    private static S101LuaDataProvider CreateProvider(Action<string>? trace)
    {
        var document = new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "trace-test" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 10_000_000,
                CoordinateMultiplicationFactorY = 10_000_000,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            Points = ReadOnlyDictionary<uint, S101PointRecord>.Empty,
            CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features = [],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };

        var featureCatalogue = new FeatureCatalogue
        {
            Name = "S-101 test",
            VersionNumber = "1.0.0",
            VersionDate = "2024-01-01",
        };

        return new S101LuaDataProvider(S101Dataset.FromDocument(document), featureCatalogue, trace);
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
