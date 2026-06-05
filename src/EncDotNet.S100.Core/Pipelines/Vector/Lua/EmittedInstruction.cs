namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// A single drawing instruction emitted by the S-100 Part 9A Lua portrayal
/// pipeline, before it is parsed into the typed
/// <see cref="DrawingInstruction"/> hierarchy. This is the raw output of
/// <c>HostPortrayalEmit</c>: a feature reference plus a semicolon-separated
/// key:value instruction string.
/// </summary>
/// <remarks>
/// Exposed via the Lua-only diagnostic surface
/// (<see cref="ILuaVectorRuleExecutor.ExecuteRaw"/>) for tooling and tests
/// that inspect the pre-parse emit stream. Production callers should use
/// <see cref="IVectorRuleExecutor.Execute"/>, which returns typed instructions.
/// </remarks>
public sealed class EmittedInstruction
{
    /// <summary>Feature reference string (the feature's record/numeric ID).</summary>
    public required string FeatureRef { get; init; }

    /// <summary>Semicolon-separated key:value drawing instruction string.</summary>
    public required string InstructionString { get; init; }

    /// <summary>Observed context-parameter names used during rule evaluation.</summary>
    public required string ObservedParameters { get; init; }
}
