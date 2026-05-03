using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Spec-agnostic view of a loaded dataset's time samples, used by the
/// global time slider to aggregate timelines across S-104, S-111 and
/// S-411 datasets.
/// </summary>
/// <remarks>
/// <para>S-104 and S-111 datasets carry many samples (one per HDF5
/// <c>Group_NNN</c>) and snap to the <em>nearest</em> sample. S-411
/// datasets are snapshot-per-file: a single-element <see cref="AvailableTimes"/>
/// list with the dataset's issue time, snapped <em>at-or-before</em> the
/// global clock.</para>
/// <para>Implementations are expected to be cheap to call and free of
/// side-effects; <see cref="SnapTo"/> must not mutate the underlying
/// processor's selected time step.</para>
/// </remarks>
internal interface ITimeAwareDataset
{
    /// <summary>
    /// All time samples this dataset can render at, in any order. Empty
    /// for non-time-varying datasets (which the slider ignores).
    /// </summary>
    IReadOnlyList<DateTime> AvailableTimes { get; }

    /// <summary>
    /// The time at which the dataset is currently rendered, or
    /// <c>null</c> if it has not yet been rendered or carries no
    /// recognised timestamp.
    /// </summary>
    DateTime? CurrentTime { get; }

    /// <summary>
    /// Returns the sample this dataset would render if the global clock
    /// were set to <paramref name="t"/>, or <c>null</c> if the dataset
    /// should be hidden at that time (e.g. an S-411 snapshot whose
    /// issue date is after <paramref name="t"/>).
    /// </summary>
    DateTime? SnapTo(DateTime t);
}
