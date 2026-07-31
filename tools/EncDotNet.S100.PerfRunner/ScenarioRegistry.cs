namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Static registry of available performance scenarios.
/// </summary>
public static class ScenarioRegistry
{
    private static readonly Dictionary<string, Func<IPerfScenario>> Factories = new(StringComparer.OrdinalIgnoreCase);

    static ScenarioRegistry()
    {
        Register(() => new Scenarios.S101PortrayColdScenario());
        Register(() => new Scenarios.S101PortrayWarmScenario());
        Register(() => new Scenarios.S101RenderWarmScenario());
        Register(() => new Scenarios.S101RenderRepeatScenario());
        Register(() => new Scenarios.S102CoverageScenario());
        Register(() => new Scenarios.S102CoverageOpenScenario());
        Register(() => new Scenarios.S102CoverageRenderLargeScenario());
        Register(() => new Scenarios.S102CoverageRenderRepeatScenario());
        Register(() => new Scenarios.S102CoverageViewportFitScenario());
        Register(() => new Scenarios.S102CoverageViewportZoomedInScenario());
        Register(() => new Scenarios.S102CoverageViewportZoomedOutScenario());
        Register(() => new Scenarios.S111ArrowRepeatScenario());
        Register(() => new Scenarios.S124VectorScenario());
        Register(() => new Scenarios.S201VectorScenario());
        Register(() => new Scenarios.ExchangeSetOpenScenario());
        Register(() => new Scenarios.S101RealColdScenario());
        Register(() => new Scenarios.S101RealWarmScenario());
        Register(() => new Scenarios.S101PickWarmScenario());
        Register(() => new Scenarios.S102RealWarmScenario());
        Register(() => new Scenarios.S111RealWarmScenario());
    }

    /// <summary>Registers a scenario factory keyed by its <see cref="IPerfScenario.Name"/>.</summary>
    public static void Register(Func<IPerfScenario> factory)
    {
        var instance = factory();
        Factories[instance.Name] = factory;
    }

    /// <summary>Returns a new instance of the named scenario, or <c>null</c> if not found.</summary>
    public static IPerfScenario? Create(string name) =>
        Factories.TryGetValue(name, out var factory) ? factory() : null;

    /// <summary>All registered scenario names.</summary>
    public static IEnumerable<string> Names => Factories.Keys;
}
