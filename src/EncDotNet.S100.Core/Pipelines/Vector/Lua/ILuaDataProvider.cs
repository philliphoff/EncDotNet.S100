using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// The C# side of the S-100 Part 9A Lua Portrayal Model host interface for a
/// single render. A provider bridges a product's dataset (ISO 8211 records for
/// S-101, GML features for S-131) to the Lua <c>Host*</c> functions, collects
/// the instructions emitted by <c>HostPortrayalEmit</c>, and resolves feature
/// references back to their feature-type codes.
/// </summary>
/// <remarks>
/// <para>
/// Providers are <b>stateful and render-scoped</b>: they accumulate emitted
/// instructions in <see cref="EmittedInstructions"/> as the Lua engine runs.
/// A fresh provider is created per render via
/// <see cref="ILuaDataProviderFactory.Create"/>, so the same executor can be
/// reused across renders without shared mutable state.
/// </para>
/// <para>
/// The S-101 and S-131 providers implement an identical surface over different
/// stores, which is what makes the generic <see cref="LuaRuleExecutor"/>
/// possible.
/// </para>
/// </remarks>
public interface ILuaDataProvider
{
    /// <summary>Drawing instructions emitted during portrayal execution.</summary>
    IReadOnlyList<EmittedInstruction> EmittedInstructions { get; }

    /// <summary>
    /// Resolves a Lua-side feature reference (the stringified record id passed
    /// to <c>HostPortrayalEmit</c>) to its feature-type code (e.g.
    /// <c>DEPCNT</c>), or <see langword="null"/> when it cannot be resolved.
    /// Used for per-feature-type telemetry.
    /// </summary>
    string? GetFeatureTypeCode(string featureRef);

    /// <summary>
    /// Registers all <c>Host*</c> functions (and any debug table) on the given
    /// Lua context.
    /// </summary>
    void RegisterHostFunctions(ILuaContext lua);

    /// <summary>
    /// Product-specific Lua scripts that must run <b>after</b> <c>main.lua</c>
    /// loads and <b>before</b> context-parameter initialisation and
    /// <c>PortrayalMain</c>. These are the host-adapter shims (e.g.
    /// <c>HostGetSpatial</c> / <c>HostFeatureGetSpatialAssociations</c>
    /// wrappers) and compatibility patches. The list is executed in order.
    /// </summary>
    /// <remarks>
    /// Owned by the provider because the shims bridge <i>this provider's</i>
    /// host functions into Lua objects; S-131's spatial shim differs from
    /// S-101's because its geometry is synthesised from GML.
    /// </remarks>
    IReadOnlyList<string> PostLoadScripts { get; }
}
