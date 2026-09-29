using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model for "Connect to a shared feed…" (issue #680, UX refinement §1):
/// one URL field for a feed served by <c>s100 feed serve</c> on another
/// computer, checked for reachability (and that it really is an S-100 feed)
/// before it is added.
/// </summary>
internal sealed class SharedFeedDialogViewModel : ViewModelBase
{
    private readonly Func<Uri, CancellationToken, Task<CatalogueProbe>> _probe;
    private string _url = string.Empty;
    private string? _error;
    private bool _isChecking;

    public SharedFeedDialogViewModel(Func<Uri, CancellationToken, Task<CatalogueProbe>> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !_isChecking && !string.IsNullOrWhiteSpace(_url));
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Raised with the feed, described as a catalogue, once it has been checked.</summary>
    public event EventHandler<KnownCatalogueSource>? Connected;

    /// <summary>Raised when the user cancels.</summary>
    public event EventHandler? Cancelled;

    /// <summary>The feed URL the server printed.</summary>
    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value ?? string.Empty))
            {
                Error = null;
                ((AsyncRelayCommand)ConnectCommand).NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Why the feed could not be added, if it could not.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _error is not null;

    /// <summary>True while the feed is being checked.</summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
                ((AsyncRelayCommand)ConnectCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>The command to run on the other computer.</summary>
    public string ServeCommand => Strings.Library_SharedFeedCommand;

    /// <summary>Checks the URL and, when it is a reachable S-100 feed, raises <see cref="Connected"/>.</summary>
    public ICommand ConnectCommand { get; }

    public ICommand CancelCommand { get; }

    private async Task ConnectAsync()
    {
        if (!Uri.TryCreate(_url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Error = Strings.Library_SharedFeedInvalid;
            return;
        }

        IsChecking = true;
        Error = null;
        try
        {
            var probe = await _probe(uri, CancellationToken.None).ConfigureAwait(true);
            if (probe.Format != KnownCatalogueFormat.S100Feed)
            {
                Error = Strings.Library_SharedFeedNotAFeed;
                return;
            }

            Connected?.Invoke(this, KnownCatalogueSources.FromUrl(uri, KnownCatalogueFormat.S100Feed, probe.Title));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            Error = string.Format(CultureInfo.CurrentCulture, Strings.Library_SharedFeedUnreachableFormat, ex.Message);
        }
        finally
        {
            IsChecking = false;
        }
    }
}
