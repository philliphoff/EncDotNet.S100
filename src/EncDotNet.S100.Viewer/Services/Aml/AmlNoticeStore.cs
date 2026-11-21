using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.Aml;

/// <summary>
/// The AML sets whose first-open notification has been shown (#1371,
/// decision 12), so it isn't shown again. Kept in the viewer's data directory
/// (<c>aml-notices.txt</c>), one set key per line.
/// </summary>
internal sealed class AmlNoticeStore
{
    private readonly string? _path;
    private readonly bool _readOnly;
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private HashSet<string>? _keys;

    /// <summary>Creates a store at <paramref name="path"/>; <see langword="null"/> keeps it in memory.</summary>
    /// <param name="path">The file, or <see langword="null"/>.</param>
    /// <param name="readOnly">Whether the viewer's settings are read-only: the store reads but never writes.</param>
    /// <param name="logger">Logs a file that can't be read or written.</param>
    public AmlNoticeStore(string? path, bool readOnly, ILogger? logger)
    {
        _path = path;
        _readOnly = readOnly;
        _logger = logger;
    }

    /// <summary>Whether the set <paramref name="key"/> has been announced.</summary>
    public bool HasAnnounced(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
            return Keys().Contains(key);
    }

    /// <summary>Records that the set <paramref name="key"/> has been announced.</summary>
    public void MarkAnnounced(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        string[] lines;
        lock (_gate)
        {
            if (!Keys().Add(key.ReplaceLineEndings(" ")))
                return;
            lines = [.. _keys!];
        }

        if (_path is null || _readOnly)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllLines(_path, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not save the AML sets already announced to {Path}", _path);
        }
    }

    private HashSet<string> Keys()
    {
        if (_keys is not null)
            return _keys;

        _keys = new HashSet<string>(StringComparer.Ordinal);
        if (_path is null || !File.Exists(_path))
            return _keys;
        try
        {
            foreach (var line in File.ReadAllLines(_path))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    _keys.Add(line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not read the AML sets already announced from {Path}", _path);
        }

        return _keys;
    }
}
