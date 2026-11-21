namespace EncDotNet.S100.Collections.KnownSources;

/// <summary>
/// Recognises the catalogue or feed at a URL (#670, #1399): checks it is an
/// http or https URL, reads the start of what it serves and names its format
/// as a <see cref="KnownCatalogueSource"/>. The viewer's "Add by URL" and MCP
/// <c>add_library_source url=…</c> both recognise URLs through this.
/// </summary>
public sealed class CatalogueUrlRecognizer
{
    private readonly Func<Uri, CancellationToken, Task<CatalogueProbe>> _probe;

    /// <summary>Creates the recognizer.</summary>
    /// <param name="probe">Reads the start of a URL and recognises its format (<see cref="CatalogueFormatDetector"/>).</param>
    public CatalogueUrlRecognizer(Func<Uri, CancellationToken, Task<CatalogueProbe>> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    /// <summary>Parses <paramref name="text"/> (trimmed) as an absolute http or https URL.</summary>
    /// <param name="text">The URL as typed.</param>
    /// <param name="uri">The URL, when it is one.</param>
    /// <returns>True when <paramref name="text"/> is an http or https URL.</returns>
    public static bool TryParse(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri)
    {
        if (Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }

    /// <summary>Reads <paramref name="uri"/> and names the catalogue or feed it serves.</summary>
    /// <param name="uri">An http or https URL (see <see cref="TryParse"/>).</param>
    /// <param name="cancellationToken">Cancels reading it.</param>
    /// <returns>The source, or why there is none.</returns>
    public async Task<CatalogueUrlRecognition> RecognizeAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        CatalogueProbe probe;
        try
        {
            probe = await _probe(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return new CatalogueUrlRecognition(null, CatalogueUrlProblem.Unreadable, ex.Message);
        }

        if (probe.Format is { } format)
            return new CatalogueUrlRecognition(KnownCatalogueSources.FromUrl(uri, format, probe.Title), null, null);
        return probe.IsJson ? new CatalogueUrlRecognition(null, CatalogueUrlProblem.Json, null)
            : probe.RootElement is { } root ? new CatalogueUrlRecognition(null, CatalogueUrlProblem.UnsupportedRoot, root)
            : new CatalogueUrlRecognition(null, CatalogueUrlProblem.NotXml, null);
    }
}

/// <summary>What <see cref="CatalogueUrlRecognizer.RecognizeAsync"/> found.</summary>
/// <param name="Source">The recognised catalogue or feed, or <see langword="null"/>.</param>
/// <param name="Problem">Why nothing was recognised, or <see langword="null"/> when <paramref name="Source"/> is set.</param>
/// <param name="Detail">The error's message (<see cref="CatalogueUrlProblem.Unreadable"/>) or the root element (<see cref="CatalogueUrlProblem.UnsupportedRoot"/>).</param>
public sealed record CatalogueUrlRecognition(KnownCatalogueSource? Source, CatalogueUrlProblem? Problem, string? Detail);

/// <summary>Why a URL is not a catalogue or feed that can be added.</summary>
public enum CatalogueUrlProblem
{
    /// <summary>The URL could not be read.</summary>
    Unreadable,

    /// <summary>It serves JSON that is not an S-100 feed.</summary>
    Json,

    /// <summary>It serves XML whose root element is not a supported catalogue.</summary>
    UnsupportedRoot,

    /// <summary>It serves neither XML nor JSON.</summary>
    NotXml,
}
