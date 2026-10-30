namespace EncDotNet.S100.Portrayals;

/// <summary>
/// The priority of an alert (S-100 Part 9 §9-17, MSC.530(106) Appendix 5).
/// </summary>
public enum AlertPriority
{
    /// <summary>Shown to the mariner without alerting.</summary>
    Indication,

    /// <summary>A caution: awareness of a condition that isn't yet a warning.</summary>
    Caution,

    /// <summary>A warning: needs attention.</summary>
    Warning,

    /// <summary>An alarm: needs immediate attention and action.</summary>
    Alarm,
}

/// <summary>
/// A portrayal catalogue's alert catalogue (S-100 Part 9 §9-17): the alerts
/// drawing instructions refer to with <c>AlertReference</c>, with their
/// messages and highlights. S-101 defines <c>SafetyContour</c>,
/// <c>ProhAre</c> and <c>NavHazard</c>.
/// </summary>
public sealed class AlertCatalogue
{
    /// <summary>The catalogue's version, from the root <c>@version</c>; empty when absent.</summary>
    public required string Version { get; init; }

    /// <summary>The alerts, in document order.</summary>
    public required IReadOnlyList<AlertDefinition> Alerts { get; init; }

    /// <summary>The highlights, in document order.</summary>
    public required IReadOnlyList<AlertHighlight> Highlights { get; init; }

    /// <summary>The messages, by id.</summary>
    public required IReadOnlyDictionary<string, AlertMessage> Messages { get; init; }

    /// <summary>The alert with an id, or <see langword="null"/> when there's none.</summary>
    /// <param name="id">The alert's id, as an <c>AlertReference</c> names it.</param>
    /// <returns>The alert, or <see langword="null"/>.</returns>
    public AlertDefinition? FindAlert(string id) =>
        Alerts.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    /// <summary>The highlight with an id, or <see langword="null"/> when there's none.</summary>
    /// <param name="id">The highlight's id.</param>
    /// <returns>The highlight, or <see langword="null"/>.</returns>
    public AlertHighlight? FindHighlight(string id) =>
        Highlights.FirstOrDefault(h => string.Equals(h.Id, id, StringComparison.Ordinal));
}

/// <summary>An alert in an <see cref="AlertCatalogue"/>.</summary>
/// <param name="Id">The alert's id, e.g. <c>NavHazard</c>.</param>
/// <param name="Name">Its name, from its <c>description</c>.</param>
/// <param name="RoutePlan">How it's raised in route planning, or <see langword="null"/> when it isn't.</param>
/// <param name="RouteMonitor">How it's raised in route monitoring, or <see langword="null"/> when it isn't.</param>
public sealed record AlertDefinition(string Id, string Name, AlertUse? RoutePlan, AlertUse? RouteMonitor);

/// <summary>How an alert is raised in route planning or route monitoring.</summary>
/// <param name="MessageId">The id of the message shown, or <see langword="null"/>.</param>
/// <param name="HighlightId">The id of the highlight drawn, or <see langword="null"/>.</param>
/// <param name="Priorities">The priorities the mariner can choose from; one when there's no choice.</param>
/// <param name="DefaultPriority">The priority used unless the mariner chooses another.</param>
public sealed record AlertUse(string? MessageId, string? HighlightId, IReadOnlyList<AlertPriority> Priorities, AlertPriority DefaultPriority);

/// <summary>A highlight an alert draws, and the viewing groups that turn it on or off.</summary>
/// <param name="Id">The highlight's id.</param>
/// <param name="Style">Its style, e.g. <c>AlarmHighlight</c> or <c>CautionHighlight</c>.</param>
/// <param name="Optional">Whether the mariner may turn it off.</param>
/// <param name="MessageId">The id of the message shown when it's off, or <see langword="null"/>.</param>
/// <param name="ViewingGroups">
/// The viewing groups it's drawn in; for S-101 <c>ProhAre</c>, one per kind
/// of area (53012–53024), which <c>AlertReference</c> names.
/// </param>
public sealed record AlertHighlight(string Id, string Style, bool Optional, string? MessageId, IReadOnlyList<int> ViewingGroups);

/// <summary>A message an alert or highlight shows.</summary>
/// <param name="Id">The message's id.</param>
/// <param name="Text">Its text.</param>
public sealed record AlertMessage(string Id, string Text);
