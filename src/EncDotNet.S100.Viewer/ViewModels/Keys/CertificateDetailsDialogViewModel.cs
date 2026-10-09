using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace EncDotNet.S100.Viewer.ViewModels.Keys;

/// <summary>An identity's or authority's details (#845): what the certificate says, never the key.</summary>
internal sealed class CertificateDetailsDialogViewModel : ViewModelBase
{
    public CertificateDetailsDialogViewModel(string title, IReadOnlyList<KeysField> fields)
    {
        Title = title;
        Fields = fields;
        CloseCommand = new RelayCommand(() => Closed?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? Closed;

    public string Title { get; }
    public IReadOnlyList<KeysField> Fields { get; }
    public ICommand CloseCommand { get; }
}
