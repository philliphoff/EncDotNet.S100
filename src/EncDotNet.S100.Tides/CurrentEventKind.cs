namespace EncDotNet.S100.Tides;

/// <summary>What a <see cref="CurrentEvent"/> is.</summary>
public enum CurrentEventKind
{
    /// <summary>Slack water as the ebb ends and the flood begins.</summary>
    SlackBeforeFlood,

    /// <summary>A maximum of the flood: the current stops strengthening and starts to weaken.</summary>
    MaximumFlood,

    /// <summary>
    /// A minimum of the flood: the flood weakens without turning and starts
    /// to strengthen again, between two maximum floods.
    /// </summary>
    MinimumFlood,

    /// <summary>Slack water as the flood ends and the ebb begins.</summary>
    SlackBeforeEbb,

    /// <summary>A maximum of the ebb: the current stops strengthening and starts to weaken.</summary>
    MaximumEbb,

    /// <summary>
    /// A minimum of the ebb: the ebb weakens without turning and starts to
    /// strengthen again, between two maximum ebbs.
    /// </summary>
    MinimumEbb,
}
