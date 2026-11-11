namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The "Add collection manifest" dialog (and "Choose groups…"): a one-page
/// host for an <see cref="AddToLibraryDialogViewModel"/> of kind
/// <see cref="Collections.Library.LibrarySourceKind.LocalManifest"/>, which does the work. It
/// exists so the dialog manager can show this page rather than the plain
/// "Add to Library" dialog for the same scope model.
/// </summary>
internal sealed class AddCollectionManifestDialogViewModel : ViewModelBase
{
    public AddCollectionManifestDialogViewModel(AddToLibraryDialogViewModel scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Scope = scope;
    }

    /// <summary>The manifest scope and target being chosen.</summary>
    public AddToLibraryDialogViewModel Scope { get; }
}
