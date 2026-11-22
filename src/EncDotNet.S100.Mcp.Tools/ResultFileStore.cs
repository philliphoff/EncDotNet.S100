using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>A file a tool wrote for its caller to fetch.</summary>
/// <param name="Id">The file's id: its name in the store's folder.</param>
/// <param name="Uri">The MCP resource URI a client reads it by.</param>
/// <param name="Path">The absolute path on the server's file system.</param>
/// <param name="Bytes">The file's size in bytes.</param>
public sealed record StoredResultFile(
    [property: Description("The file's id.")] string Id,
    [property: Description("The MCP resource URI to fetch it with resources/read.")] string Uri,
    [property: Description("Absolute path on the server's file system.")] string Path,
    [property: Description("File size in bytes.")] long Bytes);

/// <summary>
/// A folder of large tool results (#1615), such as navigability grids, that a
/// tool writes and a client fetches: by path when it shares the server's file
/// system (stdio), else as an MCP resource (<c>{UriPrefix}{id}</c>) through
/// <c>resources/read</c>. Old files expire.
/// </summary>
/// <remarks>
/// <para>
/// After each write the store deletes its files older than
/// <see cref="MaxAge"/> and all but the <see cref="MaxFiles"/> newest. It
/// only ever deletes or serves files whose names it could have written
/// (<c>{prefix}-{stamp}-{random}.{extension}</c>), so a folder shared by
/// several server processes stays bounded and nothing else in it is
/// touched.
/// </para>
/// <para>
/// Ids are plain file names; anything else (a path, <c>..</c>) is refused.
/// </para>
/// </remarks>
public sealed partial class ResultFileStore
{
    /// <summary>The default most files kept.</summary>
    public const int DefaultMaxFiles = 50;

    /// <summary>The default age after which a file is deleted.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(24);

    private readonly string _prefix;
    private readonly TimeProvider _time;
    private readonly Lock _prune = new();

    /// <summary>Creates a store.</summary>
    /// <param name="directory">The folder; created on the first write.</param>
    /// <param name="filePrefix">The start of every file name, such as <c>navigability</c>: letters, digits and hyphens.</param>
    /// <param name="uriPrefix">The resource URI before the id, such as <c>s100://grids/</c>.</param>
    /// <param name="maxFiles">The most files kept.</param>
    /// <param name="maxAge">How long a file is kept.</param>
    /// <param name="time">The clock; the system's when null.</param>
    public ResultFileStore(string directory, string filePrefix, string uriPrefix, int maxFiles = DefaultMaxFiles, TimeSpan? maxAge = null, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(uriPrefix);
        if (!PrefixPattern().IsMatch(filePrefix ?? string.Empty))
            throw new ArgumentException("The file prefix is letters, digits and hyphens.", nameof(filePrefix));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);
        Directory = System.IO.Path.GetFullPath(directory);
        _prefix = filePrefix!;
        UriPrefix = uriPrefix;
        MaxFiles = maxFiles;
        MaxAge = maxAge ?? DefaultMaxAge;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxAge, TimeSpan.Zero, nameof(maxAge));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The folder.</summary>
    public string Directory { get; }

    /// <summary>The resource URI before the id.</summary>
    public string UriPrefix { get; }

    /// <summary>The most files kept.</summary>
    public int MaxFiles { get; }

    /// <summary>How long a file is kept.</summary>
    public TimeSpan MaxAge { get; }

    /// <summary>Writes a file, then deletes expired ones.</summary>
    /// <param name="extension">The file extension without its dot: letters and digits.</param>
    /// <param name="bytes">The contents.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The file.</returns>
    public async Task<StoredResultFile> WriteAsync(string extension, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!ExtensionPattern().IsMatch(extension ?? string.Empty))
            throw new ArgumentException("The extension is letters and digits.", nameof(extension));

        System.IO.Directory.CreateDirectory(Directory);
        var stamp = _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        var id = $"{_prefix}-{stamp}-{Guid.NewGuid().ToString("N")[..8]}.{extension}";
        var path = System.IO.Path.Combine(Directory, id);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        Prune(keep: id);
        return new StoredResultFile(id, UriPrefix + id, path, bytes.LongLength);
    }

    /// <summary>Finds a stored file by id or resource URI.</summary>
    /// <param name="idOrUri">The id, or <see cref="UriPrefix"/> and the id.</param>
    /// <param name="file">The file, when found.</param>
    /// <returns>True when the store has the file.</returns>
    public bool TryGet(string? idOrUri, out StoredResultFile? file)
    {
        file = null;
        if (idOrUri is null)
            return false;
        var id = idOrUri.StartsWith(UriPrefix, StringComparison.Ordinal) ? idOrUri[UriPrefix.Length..] : idOrUri;
        if (!IsOurs(id))
            return false;
        var info = new FileInfo(System.IO.Path.Combine(Directory, id));
        if (!info.Exists)
            return false;
        file = new StoredResultFile(id, UriPrefix + id, info.FullName, info.Length);
        return true;
    }

    /// <summary>Deletes the files older than <see cref="MaxAge"/> and all but the <see cref="MaxFiles"/> newest.</summary>
    /// <returns>How many files were deleted.</returns>
    public int Prune() => Prune(keep: null);

    private int Prune(string? keep)
    {
        lock (_prune)
        {
            if (!System.IO.Directory.Exists(Directory))
                return 0;

            var cutoff = _time.GetUtcNow().UtcDateTime - MaxAge;
            var files = new DirectoryInfo(Directory)
                .EnumerateFiles()
                .Where(f => IsOurs(f.Name))
                .OrderByDescending(f => f.Name == keep)
                .ThenByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            var deleted = 0;
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (file.Name == keep || (i < MaxFiles && file.LastWriteTimeUtc >= cutoff))
                    continue;
                try
                {
                    file.Delete();
                    deleted++;
                }
                catch (IOException)
                {
                    // Another process may be reading or deleting it; the next prune tries again.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return deleted;
        }
    }

    private bool IsOurs(string name) =>
        name.StartsWith(_prefix + "-", StringComparison.Ordinal) && IdPattern().IsMatch(name[(_prefix.Length + 1)..]);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]*$")]
    private static partial Regex PrefixPattern();

    [GeneratedRegex("^[A-Za-z0-9]{1,8}$")]
    private static partial Regex ExtensionPattern();

    // {stamp}-{random}.{extension}, as WriteAsync names them.
    [GeneratedRegex("^[0-9]{8}T[0-9]{6}-[0-9a-f]{8}\\.[A-Za-z0-9]{1,8}$")]
    private static partial Regex IdPattern();
}
