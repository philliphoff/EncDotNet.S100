using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Routes panel's route exchange (#960, #1400): Import route…, Copy to
/// edit and Export…, and the notices under the route header about where an
/// edited copy came from (a newer source edition, F4) and its preserved
/// schedules that no longer match (F6).
/// </summary>
internal sealed class RouteExchangeFooterViewModel : ViewModelBase
{
    private readonly RoutesService _routes;
    private readonly RouteExchangeService _exchange;
    private readonly IRouteDialogs _dialogs;

    /// <summary>Creates the footer over the routes, the exchange service and the dialogs.</summary>
    public RouteExchangeFooterViewModel(RoutesService routes, RouteExchangeService exchange, IRouteDialogs dialogs)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(dialogs);
        _routes = routes;
        _exchange = exchange;
        _dialogs = dialogs;

        ImportRouteCommand = new AsyncRelayCommand(ImportRouteAsync);
        CopyToEditCommand = new RelayCommand<RouteRowViewModel>(
            row => CopyToEdit(row?.Route ?? _routes.Routes.ActiveRoute),
            row => (row?.Route ?? _routes.Routes.ActiveRoute) is { IsReadOnly: true });
        ExportRouteCommand = new RelayCommand<RouteRowViewModel>(
            row => Export(row?.Route ?? _routes.Routes.ActiveRoute),
            row => (row?.Route ?? _routes.Routes.ActiveRoute) is { Waypoints.Count: >= 2 });
        DropStaleSchedulesCommand = new RelayCommand(() => _routes.Routes.ActiveRoute?.DropStalePreservedSchedules());
        KeepStaleSchedulesCommand = new RelayCommand(() => _routes.Routes.ActiveRoute?.SetKeepStalePreservedSchedules(true));
        // Ctrl+E and the Edit tool on a received route offer Copy to edit (A8).
        _routes.CopyToEditRequested += (_, _) => CopyToEdit(_routes.Routes.ActiveRoute);
    }

    /// <summary>Import route…: picks a file and opens it as a received route (F2).</summary>
    public IAsyncRelayCommand ImportRouteCommand { get; }

    /// <summary>Copy to edit: the footer, the ⋯ menu, Ctrl+E and the waypoint menu (F5, A8).</summary>
    public ICommand CopyToEditCommand { get; }

    /// <summary>Export…: the route ⋯ menu (F7).</summary>
    public ICommand ExportRouteCommand { get; }

    /// <summary>"Drop them": drops preserved schedules that no longer match (F6).</summary>
    public ICommand DropStaleSchedulesCommand { get; }

    /// <summary>"Keep and export as they are" (F6).</summary>
    public ICommand KeepStaleSchedulesCommand { get; }

    /// <summary>Raised when Import route… opened a route, so the panel zooms to it.</summary>
    public event EventHandler? Imported;

    /// <summary>Whether the footer shows Copy to edit in Edit's place (A8).</summary>
    public bool ShowCopyToEdit => _routes.Routes.ActiveRoute is { IsReadOnly: true };

    /// <summary>"Source has a newer edition (ed. 4)" on a copy whose source has one open (F4), else <c>null</c>.</summary>
    public string? NewerEditionText
        => _routes.Routes.ActiveRoute is { IsReadOnly: false } route && _exchange.NewerSourceEdition(route) is { } edition
            ? string.Format(CultureInfo.CurrentCulture, Strings.Routes_NewerEditionFormat, edition)
            : null;

    /// <summary>Whether <see cref="NewerEditionText"/> shows.</summary>
    public bool HasNewerEdition => NewerEditionText is not null;

    /// <summary>"2 schedules no longer match the route" (F6), else <c>null</c>.</summary>
    public string? StaleSchedulesText
        => _routes.Routes.ActiveRoute is { HasStalePreservedSchedules: true, Preserved: { } preserved }
            ? preserved.ScheduleCount == 1
                ? Strings.Routes_StaleSchedule
                : string.Format(CultureInfo.CurrentCulture, Strings.Routes_StaleSchedulesFormat, preserved.ScheduleCount)
            : null;

    /// <summary>Whether the stale-schedules notice shows.</summary>
    public bool HasStaleSchedules => StaleSchedulesText is not null;

    /// <summary>Whether the mariner chose to keep the stale schedules.</summary>
    public bool IsKeepingStaleSchedules => _routes.Routes.ActiveRoute?.KeepStalePreservedSchedules ?? false;

    /// <summary>Re-reads the active route's state: the panel calls it when the routes or check mode change.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(ShowCopyToEdit));
        OnPropertyChanged(nameof(NewerEditionText));
        OnPropertyChanged(nameof(HasNewerEdition));
        OnPropertyChanged(nameof(StaleSchedulesText));
        OnPropertyChanged(nameof(HasStaleSchedules));
        OnPropertyChanged(nameof(IsKeepingStaleSchedules));
        ((RelayCommand<RouteRowViewModel>)CopyToEditCommand).NotifyCanExecuteChanged();
        ((RelayCommand<RouteRowViewModel>)ExportRouteCommand).NotifyCanExecuteChanged();
    }

    private async Task ImportRouteAsync()
    {
        if (await _dialogs.PickImportFileAsync(_exchange.Formats.FilePatterns) is not { } path)
            return;
        var result = await _exchange.ImportAsync(path);
        if (result.Route is null)
        {
            _dialogs.Show(new RouteImportErrorDialogViewModel(Strings.ImportError_Title, result.Error ?? Strings.Import_NotARoute, d => _dialogs.Close(d)), 420);
            return;
        }
        Imported?.Invoke(this, EventArgs.Empty);
    }

    private void CopyToEdit(Route? route)
    {
        if (route is not { IsReadOnly: true })
            return;
        var preview = _exchange.PreviewCopy(route);
        _dialogs.Show(new RouteCopyDialogViewModel(preview, () => _exchange.Copy(route), d => _dialogs.Close(d)), 520);
    }

    private void Export(Route? route)
    {
        if (route is not { Waypoints.Count: >= 2 })
            return;
        _dialogs.Show(new RouteExportDialogViewModel(_exchange, route, _dialogs, d => _dialogs.Close(d)), 500);
    }
}
