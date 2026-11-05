namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// Implemented by an <see cref="ILuaDataProvider"/> whose rules can be split
/// across several Lua states (<see cref="LuaShardOptions"/>): it says which
/// features must be portrayed in the same state.
/// </summary>
/// <remarks>
/// The S-100 Part 9a framework keeps some state on its Lua feature objects
/// while <c>PortrayalMain</c> runs, and a few rules read another feature's:
/// a feature that places its text on a <c>TextPlacement</c> writes the text
/// to that feature's portrayal, and a light offsets its description by the
/// text already drawn by features at the same point. Those features must
/// share a Lua state, so the provider puts them in one group. Features in
/// different groups must not affect each other's output.
/// </remarks>
public interface ILuaShardGrouping
{
    /// <summary>
    /// Groups the features a full <c>PortrayalMain</c> run visits.
    /// </summary>
    /// <param name="featureRefs">
    /// The features in the order the run visits them, as the rules pass their
    /// ids to <c>HostPortrayalEmit</c> (<see cref="EmittedInstruction.FeatureRef"/>).
    /// A feature may appear more than once.
    /// </param>
    /// <returns>
    /// One group number per entry of <paramref name="featureRefs"/>; entries
    /// with the same number are portrayed in the same Lua state. Every entry
    /// for one feature gets the same number.
    /// </returns>
    IReadOnlyList<int> GetShardGroups(IReadOnlyList<string> featureRefs);
}
