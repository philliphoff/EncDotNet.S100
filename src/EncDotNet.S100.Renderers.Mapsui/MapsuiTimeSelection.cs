using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>How a time-aware dataset picks the sample it draws for the clock.</summary>
public enum MapsuiTimeSelectionKind
{
    /// <summary>The nearest sample, within the tolerance either side (S-111).</summary>
    Nearest,

    /// <summary>The latest sample at or before the clock, held for the tolerance (S-104, S-411).</summary>
    AtOrBefore,

    /// <summary>
    /// The clock itself, to the minute, at any time: the dataset's range is
    /// from the tolerance before the first sample to the last, and outside it
    /// the dataset draws only what doesn't move with time (a tropical
    /// cyclone, whose storm moves between forecast points and whose cone
    /// always draws; <see cref="EncDotNet.S100.Datasets.Pipelines.TimeStepSelection.Continuous"/>).
    /// </summary>
    Continuous,
}

/// <summary>
/// The time rule shared by the session's time policy and
/// <see cref="MapsuiMapTimedDataset.SampleAt"/>: which sample a dataset draws
/// at a clock value, or none when nothing lies within its tolerance (#706).
/// A thin wrapper over the renderer-neutral <see cref="TimeStepSelector"/>,
/// which <c>s100 tiles serve</c> uses too (#1676).
/// </summary>
public static class MapsuiTimeSelection
{
    /// <summary>
    /// The sample of <paramref name="sorted"/> drawn at <paramref name="time"/>,
    /// or <see langword="null"/> when none is within <paramref name="tolerance"/>
    /// (<see cref="TimeStepSelector.Select(IReadOnlyList{DateTime}, TimeStepSelection, TimeSpan, DateTime)"/>).
    /// </summary>
    /// <param name="sorted">The dataset's samples, ascending and distinct.</param>
    /// <param name="kind">How the sample is picked.</param>
    /// <param name="tolerance">How far from the clock a sample may be.</param>
    /// <param name="time">The clock value.</param>
    public static DateTime? Select(IReadOnlyList<DateTime> sorted, MapsuiTimeSelectionKind kind, TimeSpan tolerance, DateTime time) =>
        TimeStepSelector.Select(sorted, ToSelection(kind), tolerance, time);

    /// <summary>The latest sample before <paramref name="time"/>, or null.</summary>
    public static DateTime? Previous(IReadOnlyList<DateTime> sorted, DateTime time) => TimeStepSelector.Previous(sorted, time);

    /// <summary>The earliest sample after <paramref name="time"/>, or null.</summary>
    public static DateTime? Next(IReadOnlyList<DateTime> sorted, DateTime time) => TimeStepSelector.Next(sorted, time);

    /// <summary>The <see cref="TimeStepSelection"/> of <paramref name="kind"/>.</summary>
    /// <param name="kind">How a sample is picked.</param>
    public static TimeStepSelection ToSelection(MapsuiTimeSelectionKind kind) => kind switch
    {
        MapsuiTimeSelectionKind.AtOrBefore => TimeStepSelection.AtOrBefore,
        MapsuiTimeSelectionKind.Continuous => TimeStepSelection.Continuous,
        _ => TimeStepSelection.Nearest,
    };

    /// <summary>The <see cref="MapsuiTimeSelectionKind"/> of <paramref name="selection"/>.</summary>
    /// <param name="selection">How a step is picked.</param>
    public static MapsuiTimeSelectionKind FromSelection(TimeStepSelection selection) => selection switch
    {
        TimeStepSelection.AtOrBefore => MapsuiTimeSelectionKind.AtOrBefore,
        TimeStepSelection.Continuous => MapsuiTimeSelectionKind.Continuous,
        _ => MapsuiTimeSelectionKind.Nearest,
    };
}
