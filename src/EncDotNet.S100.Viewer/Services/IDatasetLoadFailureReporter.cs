using System;
using System.Threading.Tasks;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Surface dataset-load failures to the user. The default UI
/// implementation shows a modal dialog with structured details; tests
/// substitute a no-op implementation. Implementations must be safe to
/// call from any thread.
/// </summary>
internal interface IDatasetLoadFailureReporter
{
    /// <summary>
    /// Report a load failure for <paramref name="entry"/>. Returns when
    /// the user has dismissed any UI surfaced by the reporter (so the
    /// caller can rely on this to serialize follow-up status updates).
    /// </summary>
    Task ReportAsync(DatasetEntry entry, Exception exception);
}
