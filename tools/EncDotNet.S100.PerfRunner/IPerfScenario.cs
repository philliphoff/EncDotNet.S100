namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Contract for a performance scenario. Implementations live under
/// <c>Scenarios/</c> and are registered in <see cref="ScenarioRegistry"/>.
/// </summary>
public interface IPerfScenario
{
    /// <summary>Short kebab-case identifier (e.g. <c>s101-portray-cold</c>).</summary>
    string Name { get; }

    /// <summary>Human-readable description shown by <c>--help</c>.</summary>
    string Description { get; }

    /// <summary>
    /// Executes one iteration of the scenario.
    /// </summary>
    /// <param name="ctx">Shared context providing corpus path and helpers.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RunAsync(PerfContext ctx, CancellationToken ct);
}
