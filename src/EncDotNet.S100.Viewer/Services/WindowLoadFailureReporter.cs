using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IDatasetLoadFailureReporter"/> implementation.
/// Resolves the current main window via the injected
/// <see cref="Func{TResult}"/> and shows a
/// <see cref="DatasetLoadFailureDialog"/> on the UI thread. Falls back
/// to logging when no main window is available (e.g. during early
/// startup or headless smoke tests).
/// </summary>
internal sealed class WindowLoadFailureReporter : IDatasetLoadFailureReporter
{
    private readonly Func<Window?> _ownerResolver;
    private readonly ILogger<WindowLoadFailureReporter> _logger;

    public WindowLoadFailureReporter(
        Func<Window?> ownerResolver,
        ILogger<WindowLoadFailureReporter> logger)
    {
        ArgumentNullException.ThrowIfNull(ownerResolver);
        ArgumentNullException.ThrowIfNull(logger);
        _ownerResolver = ownerResolver;
        _logger = logger;
    }

    public async Task ReportAsync(DatasetEntry entry, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(exception);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = _ownerResolver();
            if (owner is null)
            {
                _logger.LogError(
                    exception,
                    "Dataset load failed for {File} but no owner window is available.",
                    entry.FilePath);
                return;
            }

            try
            {
                await DatasetLoadFailureDialog.ShowAsync(
                    owner, entry.DisplayName, entry.FilePath, exception);
            }
            catch (Exception dialogEx)
            {
                _logger.LogError(
                    dialogEx,
                    "Failed to show dataset load-failure dialog for {File}; original error: {Original}",
                    entry.FilePath,
                    exception);
            }
        });
    }
}
