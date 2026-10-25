namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>What a time-varying dataset's time steps are.</summary>
public enum TimeStepKind
{
    /// <summary>Each step is a model forecast for that time (S-111, S-104 today, weather grids).</summary>
    Forecast,

    /// <summary>Each step is a model analysis: the model's best estimate of a past or present time.</summary>
    Analysis,

    /// <summary>Each step is an observation or measurement (a tide gauge, a buoy, an ice chart).</summary>
    Observation,
}

/// <summary>Which time step a dataset draws when the map time falls between steps.</summary>
public enum TimeStepSelection
{
    /// <summary>The nearest step, before or after, within the validity either side.</summary>
    Nearest,

    /// <summary>The latest step at or before the map time, held for the validity.</summary>
    AtOrBefore,
}

/// <summary>
/// How a time-varying dataset behaves on the timeline (design note
/// <c>docs/design/weather-sources.md</c> §8.4): what its steps are, which step
/// it draws between steps, and how long a step stays valid. Reported by
/// <see cref="ITimeAwareDatasetProcessor.TimeSemantics"/>, so a host needs no
/// per-product rules.
/// </summary>
/// <param name="Kind">What the steps are.</param>
/// <param name="Selection">Which step is drawn between steps.</param>
/// <param name="Validity">
/// How far from the map time a step may be and still be drawn (either side for
/// <see cref="TimeStepSelection.Nearest"/>, after the step for
/// <see cref="TimeStepSelection.AtOrBefore"/>), or <see langword="null"/> for
/// the dataset's typical step: the median interval between its steps, or one
/// hour when it has a single step.
/// </param>
public sealed record TimeSemantics(
    TimeStepKind Kind,
    TimeStepSelection Selection,
    TimeSpan? Validity = null)
{
    /// <summary>
    /// Forecast steps, drawn from the nearest step within the typical step: the
    /// behaviour of a processor that does not say.
    /// </summary>
    public static TimeSemantics Default { get; } = new(TimeStepKind.Forecast, TimeStepSelection.Nearest);
}

/// <summary>
/// The model run a forecast dataset belongs to (design note
/// <c>docs/design/weather-sources.md</c> §8.4), reported by
/// <see cref="ITimeAwareDatasetProcessor.ForecastRun"/>.
/// </summary>
/// <param name="Model">
/// The model or producer that ran it, as users read it: <c>gfs</c>,
/// <c>cbofs</c>, or the product (<c>S-111</c>) when the data names no model.
/// </param>
/// <param name="ReferenceTime">
/// The run's reference time (UTC), the time its forecast hours count from, or
/// <see langword="null"/> when the data does not carry it. A host may then
/// fall back to a run time in the file name, or to the later
/// <see cref="ITimeAwareDatasetProcessor.IssueTime"/>.
/// </param>
public sealed record DatasetForecastRun(string Model, DateTime? ReferenceTime = null);
