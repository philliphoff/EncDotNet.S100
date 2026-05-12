using System.Diagnostics;

namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Shared <see cref="ActivitySource"/> for performance-runner concerns.
/// </summary>
/// <remarks>
/// <para>
/// The runner emits one <c>perf.iteration</c> activity per measured
/// iteration. The activity name and its tags
/// (<c>perf.scenario</c>, <c>perf.round</c>, <c>perf.iteration</c>,
/// <c>perf.side</c>) form the wire contract that the
/// <c>EncDotNet.S100.PerfReport</c> tool relies on for median/MAD-based
/// gating.
/// </para>
/// <para>
/// The source name lives under <c>EncDotNet.S100.*</c> so it is picked
/// up by the existing wildcard <c>AddSource("EncDotNet.S100.*")</c>
/// registration without further configuration.
/// </para>
/// </remarks>
internal static class PerfActivitySource
{
    /// <summary>The activity-source name. Matches the wildcard registered by the runner.</summary>
    public const string Name = "EncDotNet.S100.PerfRunner";

    /// <summary>The activity name emitted around each measured iteration.</summary>
    public const string IterationActivityName = "perf.iteration";

    /// <summary>Tag carrying the scenario name on every iteration activity.</summary>
    public const string ScenarioTag = "perf.scenario";

    /// <summary>Tag carrying the one-based round number on every iteration activity.</summary>
    public const string RoundTag = "perf.round";

    /// <summary>Tag carrying the zero-based iteration index within the round.</summary>
    public const string IterationIndexTag = "perf.iter";

    /// <summary>Tag carrying the optional "side" label (e.g. <c>baseline</c> or <c>candidate</c>).</summary>
    public const string SideTag = "perf.side";

    /// <summary>The shared activity source instance.</summary>
    public static readonly ActivitySource Instance = new(Name);
}
