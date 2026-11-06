namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// Process-wide state of the chart rendering engine. Until a chart session
/// owns its options (map rendering slice&#160;8), every map in a process
/// renders with one <see cref="ChartSessionOptions"/> instance.
/// </summary>
public static class ChartEngine
{
    /// <summary>
    /// The options the engine renders with, seeded from the environment on
    /// first use (<see cref="ChartSessionOptions.FromEnvironment"/>). A host
    /// changes them in place, for example from its settings.
    /// </summary>
    public static ChartSessionOptions Options { get; } = ChartSessionOptions.FromEnvironment();
}
