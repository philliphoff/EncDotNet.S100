using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The SECOM part of the "Add to Library" dialog (issue #804), over the core's
/// <see cref="SecomScope"/>: the service's products as the one facet. A source
/// can be kept in sync (#807) and narrowed to the current map view.
/// </summary>
internal sealed partial class AddToLibraryDialogViewModel
{
    private SecomScope? Secom => Scope as SecomScope;

    /// <summary>True when adding a SECOM service.</summary>
    public bool IsSecom => _kind == LibrarySourceKind.Secom;

    /// <summary>
    /// True to keep a local copy of every object, downloaded and pruned on
    /// each refresh. On by default when the selection is small enough.
    /// </summary>
    public bool SecomSync
    {
        get => Secom?.Sync == true;
        set
        {
            if (Secom is not { } secom)
                return;
            secom.Sync = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SecomSyncHint));
        }
    }

    /// <summary>True when the service is read only for the map view as it was when this was ticked.</summary>
    public bool SecomInMapView
    {
        get => Secom?.InMapView == true;
        set => _ = SetSecomInMapViewAsync(value, CancellationToken.None);
    }

    /// <summary>Narrows the service to the current map view (or not) and re-reads what it offers there.</summary>
    internal async Task SetSecomInMapViewAsync(bool inMapView, CancellationToken cancellationToken)
    {
        if (Secom is not { } secom || inMapView == secom.InMapView)
            return;
        secom.InMapView = inMapView;
        OnPropertyChanged(nameof(SecomInMapView));
        await LoadCatalogAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>True when there is a map view to narrow the service to.</summary>
    public bool CanScopeSecomToMapView => Secom?.CanScopeToMapView == true;

    /// <summary>"Downloads 9.8 MB now", or why syncing is not advised.</summary>
    public string? SecomSyncHint => Secom?.SyncHint;
}
