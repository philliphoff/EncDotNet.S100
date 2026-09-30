using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Collections.Manifests;

/// <summary>
/// A local collection manifest: named groups of local paths (folders,
/// exchange sets, ZIPs or loose datasets), for example one group per
/// producing country. See <c>docs/local-collection-manifest.md</c>.
/// </summary>
/// <param name="Format">Always <see cref="CollectionManifest.FormatName"/>.</param>
/// <param name="Version">The format version.</param>
/// <param name="Title">A title for the manifest, used to name the collection.</param>
/// <param name="Description">An optional description.</param>
/// <param name="Groups">The groups, in display order.</param>
public sealed record CollectionManifestDocument(
    string Format,
    int Version,
    string? Title,
    string? Description,
    IReadOnlyList<CollectionManifestGroup> Groups);

/// <summary>One group of a <see cref="CollectionManifestDocument"/>.</summary>
/// <param name="Id">The group's identifier, unique within the manifest (case-insensitively).</param>
/// <param name="Name">The display name, or <see langword="null"/> to use <paramref name="Id"/>.</param>
/// <param name="Description">An optional description.</param>
/// <param name="Paths">
/// The group's paths: relative to the manifest's folder, or absolute. Each is
/// a folder, an exchange set (folder, catalogue or ZIP), or a loose dataset.
/// </param>
/// <param name="Recursive">Whether folders are scanned recursively.</param>
public sealed record CollectionManifestGroup(
    string Id,
    string? Name,
    string? Description,
    IReadOnlyList<string> Paths,
    bool Recursive = true)
{
    /// <summary>The name to show: <see cref="Name"/>, or <see cref="Id"/> when it has none.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

    /// <summary>
    /// The group's paths made absolute against <paramref name="manifestDirectory"/>,
    /// with either slash accepted and duplicates removed.
    /// </summary>
    public IReadOnlyList<string> ResolvePaths(string manifestDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifestDirectory);
        return Paths
            .Select(p => Path.GetFullPath(
                p.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar),
                manifestDirectory))
            .Select(p => Path.TrimEndingDirectorySeparator(p))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>What a manifest group resolves to on disk, without scanning it.</summary>
/// <param name="Id">The group id.</param>
/// <param name="Name">The group's display name.</param>
/// <param name="Description">The group's description, if any.</param>
/// <param name="PathCount">How many distinct paths the group lists.</param>
/// <param name="MissingPathCount">How many of them do not exist.</param>
public sealed record CollectionManifestGroupSummary(
    string Id, string Name, string? Description, int PathCount, int MissingPathCount);

/// <summary>Reads and recognises local collection manifests.</summary>
public static partial class CollectionManifest
{
    /// <summary>The <c>format</c> value that identifies a manifest.</summary>
    public const string FormatName = "encdotnet-s100-collection";

    /// <summary>The format version this library reads.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The conventional file-name suffix of a manifest.</summary>
    public const string FileSuffix = ".s100collection.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads a manifest, validating it.</summary>
    /// <exception cref="CollectionManifestException">
    /// The content is not a valid manifest; <see cref="CollectionManifestException.Problems"/> says where.
    /// </exception>
    /// <exception cref="NotSupportedException">The manifest is from a newer, unknown format version.</exception>
    public static CollectionManifestDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            bytes = bytes[3..];

        RawManifest? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawManifest>(bytes, ReadOptions);
        }
        catch (JsonException ex)
        {
            var path = string.IsNullOrEmpty(ex.Path) || ex.Path == "$" ? null : ex.Path.TrimStart('$', '.');
            throw new CollectionManifestException(
                [new CollectionManifestProblem(ex.LineNumber + 1, path, SyntaxMessage(ex))], ex);
        }

        var lines = LineMap.Build(bytes);
        CollectionManifestProblem Problem(string path, string message) => new(lines.Find(path), path, message);

        if (raw is null)
            throw new CollectionManifestException([new CollectionManifestProblem(null, null, "The manifest is empty.")]);
        if (raw.Format != FormatName)
            throw new CollectionManifestException([Problem("format", $"expected '{FormatName}', found '{raw.Format}'.")]);
        if (raw.Version is not { } version)
            throw new CollectionManifestException([Problem("version", "required.")]);
        if (version > CurrentVersion)
            throw new NotSupportedException($"Manifest version {version} is newer than supported version {CurrentVersion}.");
        if (raw.Groups is not { Count: > 0 })
            throw new CollectionManifestException([Problem("groups", "at least one group is required.")]);

        var problems = new List<CollectionManifestProblem>();
        var groups = new List<CollectionManifestGroup>(raw.Groups.Count);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < raw.Groups.Count; i++)
        {
            var at = string.Create(CultureInfo.InvariantCulture, $"groups[{i}]");
            if (raw.Groups[i] is not { } g)
            {
                problems.Add(Problem(at, "a group object is required."));
                continue;
            }

            var valid = true;
            if (string.IsNullOrWhiteSpace(g.Id))
            {
                problems.Add(Problem(at, "'id' is required."));
                valid = false;
            }
            else if (!IdPattern().IsMatch(g.Id))
            {
                problems.Add(Problem(at + ".id", $"'{g.Id}' may only contain letters, digits, '.', '_' and '-'."));
                valid = false;
            }
            else if (!seen.TryAdd(g.Id, i))
            {
                problems.Add(Problem(at + ".id", $"duplicate '{g.Id}' (also groups[{seen[g.Id]}])."));
                valid = false;
            }

            var paths = (g.Paths ?? []).OfType<string>().Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            if (paths.Length == 0)
            {
                problems.Add(Problem(g.Paths is null ? at : at + ".paths", "'paths' is required and must list at least one path."));
                valid = false;
            }

            if (valid)
            {
                groups.Add(new CollectionManifestGroup(
                    g.Id!,
                    string.IsNullOrWhiteSpace(g.Name) ? null : g.Name.Trim(),
                    string.IsNullOrWhiteSpace(g.Description) ? null : g.Description.Trim(),
                    paths,
                    g.Recursive ?? true));
            }
        }

        if (problems.Count > 0)
            throw new CollectionManifestException(problems);

        return new CollectionManifestDocument(
            FormatName,
            version,
            string.IsNullOrWhiteSpace(raw.Title) ? null : raw.Title.Trim(),
            string.IsNullOrWhiteSpace(raw.Description) ? null : raw.Description.Trim(),
            groups);
    }

    /// <summary>The parser's message without its trailing "Path: … | LineNumber: …" position suffix.</summary>
    private static string SyntaxMessage(JsonException ex)
    {
        var message = ex.Message;
        var cut = message.IndexOf(" Path: ", StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }

    /// <summary>Reads the manifest file at <paramref name="path"/>.</summary>
    /// <exception cref="CollectionManifestException">The file is not a valid manifest.</exception>
    /// <exception cref="NotSupportedException">The manifest is from a newer, unknown format version.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static CollectionManifestDocument ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>
    /// Returns true when <paramref name="path"/> is an existing manifest file:
    /// its name ends with <see cref="FileSuffix"/>, or it is a <c>.json</c>
    /// file whose top-level <c>format</c> is <see cref="FormatName"/>.
    /// </summary>
    public static bool IsManifestPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
            return false;
        if (path.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            using var stream = File.OpenRead(path);
            var head = new byte[4096];
            var length = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return ReadTopLevelFormat(head.AsSpan(0, length)) == FormatName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Summarises each group of <paramref name="manifest"/>: its paths, and
    /// how many are missing under <paramref name="manifestDirectory"/>. Only
    /// checks that paths exist; nothing is scanned.
    /// </summary>
    public static IReadOnlyList<CollectionManifestGroupSummary> Summarize(
        CollectionManifestDocument manifest, string manifestDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifestDirectory);

        return manifest.Groups
            .Select(g =>
            {
                var paths = g.ResolvePaths(manifestDirectory);
                return new CollectionManifestGroupSummary(
                    g.Id, g.DisplayName, g.Description, paths.Count,
                    paths.Count(p => !Directory.Exists(p) && !File.Exists(p)));
            })
            .ToArray();
    }

    /// <summary>Reads the top-level <c>format</c> string of a (possibly truncated) JSON head.</summary>
    private static string? ReadTopLevelFormat(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            head = head[3..];

        try
        {
            var reader = new Utf8JsonReader(head, isFinalBlock: false, new JsonReaderState(new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }));
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (!reader.Read())
                    return null;
                if (name == "format")
                    return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                if (!reader.TrySkip())
                    return null;
            }
        }
        catch (JsonException)
        {
            // Not JSON, or truncated mid-token.
        }

        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex IdPattern();

    private sealed record RawManifest(
        string? Format,
        int? Version,
        string? Title,
        string? Description,
        IReadOnlyList<RawGroup?>? Groups);

    private sealed record RawGroup(
        string? Id,
        string? Name,
        string? Description,
        IReadOnlyList<string?>? Paths,
        bool? Recursive);
}

/// <summary>One problem that makes a manifest unreadable.</summary>
/// <param name="Line">The 1-based line it is on, when known.</param>
/// <param name="Path">Where in the document it is (e.g. <c>groups[5].id</c>), when known.</param>
/// <param name="Message">What is wrong.</param>
public sealed record CollectionManifestProblem(long? Line, string? Path, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => Path is null ? Message : $"{Path}: {Message}";
}

/// <summary>A manifest could not be read; <see cref="Problems"/> lists every problem found.</summary>
public sealed class CollectionManifestException : JsonException
{
    /// <summary>Creates the exception for <paramref name="problems"/>.</summary>
    public CollectionManifestException(IReadOnlyList<CollectionManifestProblem> problems, Exception? innerException = null)
        : base(string.Join(Environment.NewLine, problems), innerException)
    {
        Problems = problems;
    }

    /// <summary>The problems, in document order.</summary>
    public IReadOnlyList<CollectionManifestProblem> Problems { get; }
}

/// <summary>
/// Maps document paths (<c>format</c>, <c>groups[3]</c>, <c>groups[3].id</c>)
/// to the 1-based line they start on, for problem messages.
/// </summary>
internal sealed class LineMap
{
    private readonly Dictionary<string, long> _lines = new(StringComparer.Ordinal);

    public long? Find(string path)
    {
        // Fall back to the enclosing element: "groups[3].paths" → "groups[3]".
        for (var p = path; p.Length > 0; p = Parent(p))
        {
            if (_lines.TryGetValue(p, out var line))
                return line;
        }

        return null;

        static string Parent(string p)
        {
            var cut = Math.Max(p.LastIndexOf('.'), p.LastIndexOf('['));
            return cut > 0 ? p[..cut] : string.Empty;
        }
    }

    public static LineMap Build(byte[] bytes)
    {
        var map = new LineMap();
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            // Track only what problems need: top-level properties, the groups
            // array's objects, and their properties.
            string? topProperty = null;
            var groupIndex = -1;
            while (reader.Read())
            {
                var line = LineOf(bytes, reader.TokenStartIndex);
                switch (reader.CurrentDepth, reader.TokenType)
                {
                    case (1, JsonTokenType.PropertyName):
                        topProperty = reader.GetString();
                        if (topProperty is not null)
                            map._lines.TryAdd(topProperty, line);
                        break;
                    case (2, JsonTokenType.StartObject) when topProperty == "groups":
                        groupIndex++;
                        map._lines.TryAdd(string.Create(CultureInfo.InvariantCulture, $"groups[{groupIndex}]"), line);
                        break;
                    case (2, JsonTokenType.Null or JsonTokenType.String or JsonTokenType.Number
                        or JsonTokenType.True or JsonTokenType.False or JsonTokenType.StartArray) when topProperty == "groups":
                        groupIndex++;
                        map._lines.TryAdd(string.Create(CultureInfo.InvariantCulture, $"groups[{groupIndex}]"), line);
                        if (reader.TokenType == JsonTokenType.StartArray)
                            reader.Skip();
                        break;
                    case (3, JsonTokenType.PropertyName) when topProperty == "groups":
                        map._lines.TryAdd(
                            string.Create(CultureInfo.InvariantCulture, $"groups[{groupIndex}].{reader.GetString()}"), line);
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Only reached for content the serializer accepted; keep what was mapped.
        }

        return map;
    }

    private static long LineOf(byte[] bytes, long index)
    {
        long line = 1;
        for (var i = 0; i < index && i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
                line++;
        }

        return line;
    }
}
