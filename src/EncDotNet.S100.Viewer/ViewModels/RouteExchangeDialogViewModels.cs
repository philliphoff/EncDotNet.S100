using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Routing.Exchange;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Copy to edit dialog (#960, F5): what the copy keeps and can't hold,
/// and its identity, before anything changes.
/// </summary>
internal sealed class RouteCopyDialogViewModel : ViewModelBase
{
    private readonly Action<RouteCopyDialogViewModel> _close;

    /// <summary>Creates the dialog.</summary>
    /// <param name="preview">What the copy would be.</param>
    /// <param name="confirm">Makes the copy.</param>
    /// <param name="close">Closes the dialog.</param>
    public RouteCopyDialogViewModel(RouteCopyPreview preview, Action confirm, Action<RouteCopyDialogViewModel> close)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(close);
        _close = close;
        var source = preview.Source;
        var culture = CultureInfo.CurrentCulture;
        SourceName = source.Name ?? source.Source?.RouteId ?? string.Empty;
        SourceFile = source.Source is { } s
            ? string.Format(culture, Strings.CopyDialog_SourceFileFormat, s.FileName, s.Format, s.FormatVersion).TrimEnd()
            : string.Empty;
        SourceMeta = string.Format(culture, Strings.Routes_RouteMetaFormat, source.Waypoints.Count, source.TotalDistanceNm());
        Keeps = preview.Description.Keeps;
        NotKept = preview.Description.NotKept;
        var identity = preview.Identity;
        IdentityText = identity.PreviousEdition is { } edition
            ? string.Format(culture, Strings.CopyDialog_IdentityFormat, identity.PreviousRouteId, edition)
            : string.Format(culture, Strings.CopyDialog_IdentityNoEditionFormat, identity.PreviousRouteId);
        NamedText = string.Format(culture, Strings.CopyDialog_NamedFormat, preview.Name);
        ConfirmCommand = new RelayCommand(() =>
        {
            _close(this);
            confirm();
        });
        CancelCommand = new RelayCommand(() => _close(this));
    }

    /// <summary>The title.</summary>
    public string Title => Strings.CopyDialog_Title;

    /// <summary>"Makes an editable copy. The received route stays as it is."</summary>
    public string Subtitle => Strings.CopyDialog_Subtitle;

    /// <summary>The received route's name.</summary>
    public string SourceName { get; }

    /// <summary>"nab-southampton.gml · S-421 1.0".</summary>
    public string SourceFile { get; }

    /// <summary>"9 WP · 24,1 NM".</summary>
    public string SourceMeta { get; }

    /// <summary>The "THE COPY KEEPS" heading.</summary>
    public string KeepsHeading => Strings.CopyDialog_KeepsHeading;

    /// <summary>What the copy keeps.</summary>
    public IReadOnlyList<string> Keeps { get; }

    /// <summary>The "NOT IN THE COPY" heading.</summary>
    public string NotKeptHeading => Strings.CopyDialog_NotKeptHeading;

    /// <summary>What the copy can't hold, with reasons.</summary>
    public IReadOnlyList<string> NotKept { get; }

    /// <summary>Whether anything is left out of the copy.</summary>
    public bool HasNotKept => NotKept.Count > 0;

    /// <summary>The "Identity" label.</summary>
    public string IdentityLabel => Strings.CopyDialog_IdentityLabel;

    /// <summary>"New route ID · references GB-NAB-SOU-1 ed. 2 · status Planned".</summary>
    public string IdentityText { get; }

    /// <summary>"Named "… (copy)" and opened in Edit mode. Undo removes it."</summary>
    public string NamedText { get; }

    /// <summary>Makes the copy and closes the dialog.</summary>
    public ICommand ConfirmCommand { get; }

    /// <summary>Closes the dialog without changing anything.</summary>
    public ICommand CancelCommand { get; }
}

/// <summary>One format card in the Export dialog.</summary>
internal sealed class RouteExportFormatOption : ViewModelBase
{
    private readonly RouteExportDialogViewModel _owner;

    public RouteExportFormatOption(RouteExportDialogViewModel owner, IRouteFormat format, string? version)
    {
        _owner = owner;
        Format = format;
        _selectedVersion = version is not null && format.Versions.Contains(version) ? version : format.Versions[0];
    }

    /// <summary>The format.</summary>
    public IRouteFormat Format { get; }

    /// <summary>"S-421".</summary>
    public string Name => Format.Name;

    /// <summary>"· IHO route plan, edition 1.0".</summary>
    public string Description => "· " + Format.Description;

    /// <summary>"For S-100 systems."</summary>
    public string Audience => Format.Audience;

    /// <summary>The automation id of the card: <c>RouteExport.Format.S421</c>.</summary>
    public string AutomationId => "RouteExport.Format." + Format.Name.Replace("-", string.Empty, StringComparison.Ordinal);

    /// <summary>The versions this build writes.</summary>
    public IReadOnlyList<string> Versions => Format.Versions;

    /// <summary>Whether to show a version choice (more than one version).</summary>
    public bool HasVersionChoice => Format.Versions.Count > 1;

    private string _selectedVersion;
    /// <summary>The version to write.</summary>
    public string SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
                _owner.Refresh();
        }
    }

    /// <summary>Whether this format is chosen.</summary>
    public bool IsSelected
    {
        get => ReferenceEquals(_owner.Selected, this);
        set
        {
            if (value)
                _owner.Selected = this;
        }
    }

    internal void RaiseSelected() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>
/// The Export dialog (#960, F7): the format and version, how the route is
/// identified in the file, "Also write my check settings" (off), and what the
/// format can't hold.
/// </summary>
internal sealed class RouteExportDialogViewModel : ViewModelBase
{
    private readonly RouteExchangeService _exchange;
    private readonly Route _route;
    private readonly IRouteDialogs? _dialogs;
    private readonly Action<RouteExportDialogViewModel> _close;
    private RouteExportFormatOption? _selected;

    /// <summary>Creates the dialog.</summary>
    /// <param name="exchange">The export flow.</param>
    /// <param name="route">The route to export.</param>
    /// <param name="dialogs">The save picker and error dialog; none in some tests.</param>
    /// <param name="close">Closes the dialog.</param>
    public RouteExportDialogViewModel(RouteExchangeService exchange, Route route, IRouteDialogs? dialogs, Action<RouteExportDialogViewModel> close)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(close);
        _exchange = exchange;
        _route = route;
        _dialogs = dialogs;
        _close = close;
        Formats = new ObservableCollection<RouteExportFormatOption>(exchange.Formats.Formats.Select(f =>
            new RouteExportFormatOption(this, f, f.Name == exchange.LastExportFormat ? exchange.LastExportVersion : null)));
        _selected = Formats.FirstOrDefault(f => f.Name == exchange.LastExportFormat) ?? Formats.FirstOrDefault();
        Summary = string.Format(CultureInfo.CurrentCulture, Strings.ExportDialog_SummaryFormat, route.Name, route.Waypoints.Count, route.TotalDistanceNm());
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => _selected is not null);
        CancelCommand = new RelayCommand(() => _close(this));
        Refresh();
    }

    /// <summary>The title.</summary>
    public string Title => Strings.ExportDialog_Title;

    /// <summary>"Nab Tower to Southampton (copy) · 9 WP · 24,1 NM".</summary>
    public string Summary { get; }

    /// <summary>The "FORMAT" heading.</summary>
    public string FormatHeading => Strings.ExportDialog_FormatHeading;

    /// <summary>The format cards.</summary>
    public ObservableCollection<RouteExportFormatOption> Formats { get; }

    /// <summary>The chosen format.</summary>
    public RouteExportFormatOption? Selected
    {
        get => _selected;
        set
        {
            if (value is null || ReferenceEquals(_selected, value))
                return;
            _selected = value;
            foreach (var option in Formats)
                option.RaiseSelected();
            OnPropertyChanged();
            Refresh();
        }
    }

    private bool _includeCheckSettings;
    /// <summary>"Also write my check settings": off by default (decision 16).</summary>
    public bool IncludeCheckSettings
    {
        get => _includeCheckSettings;
        set
        {
            if (SetProperty(ref _includeCheckSettings, value))
                Refresh();
        }
    }

    /// <summary>The checkbox label.</summary>
    public string IncludeCheckSettingsLabel => Strings.ExportDialog_IncludeCheckSettings;

    /// <summary>The checkbox's note.</summary>
    public string IncludeCheckSettingsNote => Strings.ExportDialog_IncludeCheckSettingsNote;

    /// <summary>The "Identity" label.</summary>
    public string IdentityLabel => Strings.CopyDialog_IdentityLabel;

    private string _identityText = string.Empty;
    /// <summary>How the route is identified in the file (decision 17).</summary>
    public string IdentityText
    {
        get => _identityText;
        private set => SetProperty(ref _identityText, value);
    }

    private string? _cannotHoldText;
    /// <summary>The fields the chosen format can't hold, or <c>null</c> when nothing is lost.</summary>
    public string? CannotHoldText
    {
        get => _cannotHoldText;
        private set
        {
            if (SetProperty(ref _cannotHoldText, value))
                OnPropertyChanged(nameof(HasCannotHold));
        }
    }

    /// <summary>Whether anything is lost.</summary>
    public bool HasCannotHold => _cannotHoldText is not null;

    /// <summary>The Export… button's label.</summary>
    public string ExportLabel => Strings.ExportDialog_Confirm;

    /// <summary>Opens the save dialog, writes the route and closes.</summary>
    public IAsyncRelayCommand ExportCommand { get; }

    /// <summary>Closes without writing.</summary>
    public ICommand CancelCommand { get; }

    /// <summary>The options the dialog stands for.</summary>
    public RouteExportOptions Options => new(_selected?.SelectedVersion, IncludeCheckSettings);

    internal void Refresh()
    {
        if (_selected is null || _exchange.PreviewExport(_route, _selected.Name, Options) is not { } preview)
            return;
        var identity = preview.Identity;
        var culture = CultureInfo.CurrentCulture;
        IdentityText = identity.Kept
            ? string.Format(culture, Strings.ExportDialog_IdentityKeptFormat, identity.RouteId, identity.Edition)
            : identity.PreviousRouteId is not null
                ? string.Format(culture, Strings.ExportDialog_IdentityCopyFormat, identity.PreviousRouteId, identity.PreviousEdition)
                : string.Format(culture, Strings.ExportDialog_IdentityNewFormat, identity.RouteId);
        CannotHoldText = preview.CannotHold.Count == 0 ? null : string.Join(" ", preview.CannotHold);
    }

    private async Task ExportAsync()
    {
        if (_selected is null || _exchange.PreviewExport(_route, _selected.Name, Options) is not { } preview)
            return;
        var path = _dialogs is null ? null : await _dialogs.PickExportFileAsync(preview.SuggestedFileName, preview.Format.Name, preview.Format.FileExtension);
        if (path is null)
            return;
        try
        {
            _exchange.ExportToFile(_route, preview.Format.Name, Options, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _close(this);
            _dialogs?.Show(new RouteImportErrorDialogViewModel(
                Strings.ExportDialog_Title,
                string.Format(CultureInfo.CurrentCulture, Strings.ExportDialog_FailedFormat, ex.Message),
                d => _dialogs.Close(d)), 420);
            return;
        }
        _close(this);
    }
}

/// <summary>Import route…'s one-line error (#960, F3), and Export's.</summary>
internal sealed class RouteImportErrorDialogViewModel : ViewModelBase
{
    /// <summary>Creates the dialog.</summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The one line.</param>
    /// <param name="close">Closes the dialog.</param>
    public RouteImportErrorDialogViewModel(string title, string message, Action<RouteImportErrorDialogViewModel> close)
    {
        ArgumentNullException.ThrowIfNull(close);
        Title = title;
        Message = message;
        CloseCommand = new RelayCommand(() => close(this));
    }

    /// <summary>The title.</summary>
    public string Title { get; }

    /// <summary>The one line.</summary>
    public string Message { get; }

    /// <summary>The Close button's label.</summary>
    public string CloseLabel => Strings.Button_Close;

    /// <summary>Closes the dialog.</summary>
    public ICommand CloseCommand { get; }
}
