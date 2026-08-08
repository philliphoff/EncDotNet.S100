namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>
/// The map clock over time-aware products (S-104 / S-111 / S-411). Backs the
/// <c>set_time_step</c> tool.
/// </summary>
/// <remarks>
/// Time is expressed as UTC instants drawn from <see cref="AvailableSteps"/> —
/// the distinct sample times registered across the loaded time-aware datasets.
/// The tool resolves a requested step index or instant against that list before
/// calling <see cref="SetTimeAsync"/>, which applies time gating and re-renders.
/// A host with nothing time-aware loaded reports an empty
/// <see cref="AvailableSteps"/> and a <see langword="null"/> <see cref="Current"/>.
/// </remarks>
public interface ITimeController
{
    /// <summary>The current map clock, or <see langword="null"/> when unset.</summary>
    DateTime? Current { get; }

    /// <summary>
    /// The distinct, ascending time steps available across the loaded
    /// time-aware datasets. Empty when nothing time-aware is loaded.
    /// </summary>
    IReadOnlyList<DateTime> AvailableSteps { get; }

    /// <summary>
    /// Sets the map clock to <paramref name="time"/>, applies time gating using
    /// the current presentation, and re-renders.
    /// </summary>
    /// <param name="time">The UTC instant to set. Callers normally pass a value drawn from <see cref="AvailableSteps"/>.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    Task SetTimeAsync(DateTime time, CancellationToken cancellationToken = default);
}
