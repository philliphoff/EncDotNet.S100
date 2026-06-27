using System;
using System.Collections.Generic;
using Avalonia.Threading;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IGeographicPickPresenter"/>: marshals an MCP-driven
/// pick onto the UI thread and forwards it to <see cref="IPickService"/>,
/// which updates the shared <see cref="ViewModels.PickReportViewModel"/>
/// (and thereby the on-screen panel and pick highlight).
/// </summary>
internal sealed class DispatcherGeographicPickPresenter : IGeographicPickPresenter
{
    private readonly IPickService _pickService;
    private readonly Action<Action> _marshal;

    /// <summary>Creates a new presenter.</summary>
    /// <param name="pickService">Pick service that owns pick-report updates.</param>
    /// <param name="marshal">
    /// Optional UI-thread marshalling override. Defaults to
    /// <see cref="Dispatcher.UIThread"/>; tests inject a synchronous
    /// implementation.
    /// </param>
    public DispatcherGeographicPickPresenter(IPickService pickService, Action<Action>? marshal = null)
    {
        ArgumentNullException.ThrowIfNull(pickService);
        _pickService = pickService;
        _marshal = marshal ?? DispatcherMarshal;
    }

    /// <inheritdoc />
    public void Present(double latitude, double longitude, IReadOnlyList<GeographicPickFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        _marshal(() => _pickService.PresentGeographicPick(latitude, longitude, features));
    }

    private static void DispatcherMarshal(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
