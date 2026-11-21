namespace EncDotNet.S100.Viewer.Services.Sources;

/// <summary>
/// What an <see cref="ISourceHostExtension"/> attachment says a source's
/// Datasets row should show in place of its generic status, such as a tide
/// feed waiting for its station list (#1630). The row says Paused while the
/// entry is hidden, whatever this says.
/// </summary>
internal interface ISourceStatusOverride
{
    /// <summary>The status to show, or <see langword="null"/> for the generic one.</summary>
    SourceStatus? Status { get; }

    /// <summary>Raised when <see cref="Status"/> may have changed, on any thread.</summary>
    event EventHandler? StatusChanged;
}
