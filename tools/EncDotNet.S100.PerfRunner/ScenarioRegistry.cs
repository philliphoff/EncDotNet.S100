namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Static registry of available performance scenarios.
/// </summary>
public static class ScenarioRegistry
{
    private static readonly Dictionary<string, Func<IPerfScenario>> s_factories = new(StringComparer.OrdinalIgnoreCase);

    static ScenarioRegistry()
    {
        Register(() => new Scenarios.S101PortrayColdScenario());
        Register(() => new Scenarios.S101PortrayWarmScenario());
        Register(() => new Scenarios.S101RenderWarmScenario());
        Register(() => new Scenarios.S102CoverageScenario());
        Register(() => new Scenarios.S124VectorScenario());
        Register(() => new Scenarios.S201VectorScenario());
        Register(() => new Scenarios.ExchangeSetOpenScenario());
    }

    /// <summary>Registers a scenario factory keyed by its <see cref="IPerfScenario.Name"/>.</summary>
    public static void Register(Func<IPerfScenario> factory)
    {
        var instance = factory();
        s_factories[instance.Name] = factory;
    }

    /// <summary>Returns a new instance of the named scenario, or <c>null</c> if not found.</summary>
    public static IPerfScenario? Create(string name) =>
        s_factories.TryGetValue(name, out var factory) ? factory() : null;

    /// <summary>All registered scenario names.</summary>
    public static IEnumerable<string> Names => s_factories.Keys;
}
