namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Leaves out the drawing instructions that aren't valid at the time the map
/// shows (S-100 Part 9 <c>timeValid</c>; see
/// <see cref="DrawingInstruction.TimeValid"/>).
/// </summary>
/// <remarks>
/// The filter runs on a display list that has already been portrayed, so a
/// host can move the view time without running the portrayal rules again.
/// </remarks>
public static class PortrayalTimeFilter
{
    /// <summary>
    /// Whether <paramref name="instruction"/> is drawn at <paramref name="time"/>:
    /// it has no time intervals, or <paramref name="time"/> meets one of them.
    /// </summary>
    /// <param name="instruction">The instruction.</param>
    /// <param name="time">The time the map shows.</param>
    /// <returns><see langword="true"/> when the instruction is drawn.</returns>
    public static bool IsDrawn(DrawingInstruction instruction, PortrayalTime time)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(time);
        if (instruction.TimeValid.Count == 0)
            return true;
        foreach (var interval in instruction.TimeValid)
        {
            if (interval.Overlaps(time))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The instructions of <paramref name="instructions"/> drawn at
    /// <paramref name="time"/>, in order; all of them when
    /// <paramref name="time"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="instructions">The display list.</param>
    /// <param name="time">The time the map shows, or <see langword="null"/> for no filter.</param>
    /// <returns>The instructions drawn; <paramref name="instructions"/> itself when none is left out.</returns>
    public static IReadOnlyList<DrawingInstruction> Apply(
        IReadOnlyList<DrawingInstruction> instructions, PortrayalTime? time)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        if (time is null)
            return instructions;

        // Judge each distinct interval list once: a dated feature's
        // instructions share one list.
        var verdicts = new Dictionary<IReadOnlyList<PortrayalTimeInterval>, bool>(ReferenceEqualityComparer.Instance);
        List<DrawingInstruction>? kept = null;
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            var drawn = instruction.TimeValid.Count == 0
                || (verdicts.TryGetValue(instruction.TimeValid, out var known)
                    ? known
                    : verdicts[instruction.TimeValid] = IsDrawn(instruction, time));
            if (drawn)
            {
                kept?.Add(instruction);
            }
            else if (kept is null)
            {
                kept = new List<DrawingInstruction>(instructions.Count);
                for (var j = 0; j < i; j++)
                    kept.Add(instructions[j]);
            }
        }

        return kept ?? instructions;
    }

    /// <summary>The distinct time intervals of <paramref name="instructions"/>, in first-seen order.</summary>
    /// <param name="instructions">The display list.</param>
    /// <returns>The intervals; empty when every instruction is always drawn.</returns>
    public static IReadOnlyList<PortrayalTimeInterval> Intervals(IEnumerable<DrawingInstruction> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        var seen = new HashSet<PortrayalTimeInterval>();
        var result = new List<PortrayalTimeInterval>();
        foreach (var instruction in instructions)
        {
            foreach (var interval in instruction.TimeValid)
            {
                if (seen.Add(interval))
                    result.Add(interval);
            }
        }

        return result;
    }

    /// <summary>
    /// Whether moving the map from <paramref name="from"/> to
    /// <paramref name="to"/> can change what a display list with
    /// <paramref name="intervals"/> draws: some interval holds at one and not
    /// the other.
    /// </summary>
    /// <param name="intervals">The display list's intervals (<see cref="Intervals"/>).</param>
    /// <param name="from">The time drawn now.</param>
    /// <param name="to">The new time.</param>
    /// <returns><see langword="true"/> when the list should be filtered again.</returns>
    public static bool Changes(IEnumerable<PortrayalTimeInterval> intervals, PortrayalTime? from, PortrayalTime? to)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        if (from is null || to is null)
            return from != to && intervals.Any();
        return intervals.Any(i => i.Overlaps(from) != i.Overlaps(to));
    }
}
