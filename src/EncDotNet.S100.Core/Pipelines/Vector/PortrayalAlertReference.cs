using System.Globalization;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// A reference from a drawing instruction to an alert in the portrayal
/// catalogue's alert catalogue (S-100 Part 9 §9-11.2.5), set by the
/// <c>AlertReference:alertReference[,plan[,monitor]]</c> command of Part 9a
/// §9a-11.2.2.9: the S-101 <c>NavHazard</c>, <c>ProhAre</c> or
/// <c>SafetyContour</c> alert a feature raises when a route crosses it.
/// </summary>
/// <param name="Reference">The alert's id in the alert catalogue, e.g. <c>ProhAre</c>.</param>
/// <param name="PlanViewingGroup">
/// The viewing group of the alert's highlight in route planning, or
/// <see langword="null"/> when none is given and the highlight can't be turned off.
/// </param>
/// <param name="MonitorViewingGroup">
/// The viewing group of the alert's highlight in route monitoring, or
/// <see langword="null"/> when none is given.
/// </param>
public sealed record PortrayalAlertReference(string Reference, int? PlanViewingGroup = null, int? MonitorViewingGroup = null)
{
    /// <summary>
    /// Parses the value of an <c>AlertReference</c> command.
    /// </summary>
    /// <param name="value">The command's value, e.g. <c>ProhAre,53023,53023</c>.</param>
    /// <returns>
    /// The reference, or <see langword="null"/> when the value is empty: a
    /// bare <c>AlertReference</c> clears the alert for the instructions that
    /// follow.
    /// </returns>
    public static PortrayalAlertReference? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split(',');
        var reference = parts[0].Trim();
        if (reference.Length == 0)
            return null;

        return new PortrayalAlertReference(reference, ViewingGroup(parts, 1), ViewingGroup(parts, 2));
    }

    private static int? ViewingGroup(string[] parts, int index) =>
        index < parts.Length && int.TryParse(parts[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var group)
            ? group
            : null;
}
