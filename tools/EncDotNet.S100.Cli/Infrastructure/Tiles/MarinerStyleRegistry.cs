using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// The mariner settings sets clients of <c>tiles serve</c> have registered
/// (issue #1493), each under a hash the client chooses. A registered set
/// <c>{hash}</c> is served as the style <c>{palette}-{hash}</c> in every
/// palette: on the <c>/{palette}-{hash}/{z}/{x}/{y}</c> tile URLs, as a WMTS
/// style and as a WMS <c>STYLES</c> value. Thread-safe.
/// </summary>
/// <remarks>
/// A hash is registered once: registering it again with the same settings
/// changes nothing, and with other settings is refused, so a tile cached
/// under a style never goes stale. The registry lives as long as the
/// server, across reloads of the datasets.
/// </remarks>
internal sealed class MarinerStyleRegistry
{
    /// <summary>The most settings sets held.</summary>
    public const int Capacity = 256;

    /// <summary>The longest hash accepted.</summary>
    public const int MaxHashLength = 64;

    /// <summary>The largest request body accepted, in bytes.</summary>
    public const int MaxBodyBytes = 16 * 1024;

    /// <summary>The path of the settings sets under the server's base URL.</summary>
    public const string Path = "settings";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, MarinerStyle> _styles = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    /// <summary>What <see cref="Register"/> did.</summary>
    public enum Outcome
    {
        /// <summary>The hash is new and now registered.</summary>
        Created,

        /// <summary>The hash was already registered with the same settings.</summary>
        Unchanged,

        /// <summary>The hash is registered with other settings; nothing changed.</summary>
        Conflict,

        /// <summary>The registry holds <see cref="Capacity"/> sets; nothing changed.</summary>
        Full,
    }

    /// <summary>The number of sets registered. Sets are never removed, so it only grows.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _order.Count;
        }
    }

    /// <summary>The registered hashes, lowercase, in the order they were registered.</summary>
    public IReadOnlyList<string> Hashes
    {
        get
        {
            lock (_gate)
                return [.. _order];
        }
    }

    /// <summary>
    /// Whether <paramref name="hash"/> can name a settings set: 1 to
    /// <see cref="MaxHashLength"/> ASCII letters, digits or underscores.
    /// Hashes are matched without regard to case.
    /// </summary>
    public static bool IsValidHash(string? hash) =>
        !string.IsNullOrEmpty(hash) && hash.Length <= MaxHashLength && hash.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>Registers <paramref name="style"/> under <paramref name="hash"/> (one <see cref="IsValidHash"/> accepts).</summary>
    public Outcome Register(string hash, MarinerStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (!IsValidHash(hash))
            throw new ArgumentException($"'{hash}' is not a valid settings hash.", nameof(hash));

        hash = hash.ToLowerInvariant();
        lock (_gate)
        {
            if (_styles.TryGetValue(hash, out var existing))
                return existing == style ? Outcome.Unchanged : Outcome.Conflict;
            if (_styles.Count >= Capacity)
                return Outcome.Full;

            _styles[hash] = style;
            _order.Add(hash);
            return Outcome.Created;
        }
    }

    /// <summary>The settings registered under <paramref name="hash"/>, in any letter case.</summary>
    public bool TryGet(string hash, out MarinerStyle style)
    {
        lock (_gate)
        {
            if (_styles.TryGetValue(hash.ToLowerInvariant(), out var found))
            {
                style = found;
                return true;
            }
        }

        style = MarinerStyle.Empty;
        return false;
    }

    /// <summary>
    /// Splits a style <c>{palette}-{hash}</c> into its palette and hash;
    /// a style without a <c>-</c> is a palette alone, with a <see langword="null"/> hash.
    /// </summary>
    public static (string Palette, string? Hash) Split(string style)
    {
        int dash = style.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? (style, null) : (style[..dash], style[(dash + 1)..]);
    }

    /// <summary>The styles of every registered set in each of <paramref name="palettes"/>: <c>{palette}-{hash}</c>, set by set.</summary>
    public IEnumerable<string> Styles(IReadOnlyList<string> palettes) =>
        Hashes.SelectMany(hash => palettes.Select(palette => palette + "-" + hash));

    /// <summary>
    /// Maps <c>PUT</c> and <c>GET</c> <c>settings/{hash}</c> on
    /// <paramref name="app"/>, registering and reading the settings sets of
    /// the source <paramref name="registry"/> returns (none: 404).
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="registry">The registry of the source being served, read at each request.</param>
    /// <param name="palettes">The palettes of the source being served, read at each request.</param>
    public static void Map(WebApplication app, Func<MarinerStyleRegistry?> registry, Func<IReadOnlyList<string>> palettes)
    {
        app.MapPut("/" + Path + "/{hash}", context => PutAsync(context, registry(), palettes()));
        app.MapGet("/" + Path + "/{hash}", context => GetAsync(context, registry(), palettes()));
    }

    private static async Task PutAsync(HttpContext context, MarinerStyleRegistry? registry, IReadOnlyList<string> palettes)
    {
        var hash = (string)context.Request.RouteValues["hash"]!;
        if (registry is null)
        {
            await ErrorAsync(context, StatusCodes.Status404NotFound, "This server has no mariner settings: it serves a built tile set.").ConfigureAwait(false);
            return;
        }

        if (!IsValidHash(hash))
        {
            await ErrorAsync(context, StatusCodes.Status400BadRequest, $"A settings hash is 1 to {MaxHashLength} letters, digits or underscores.").ConfigureAwait(false);
            return;
        }

        if (context.Request.ContentLength > MaxBodyBytes)
        {
            await ErrorAsync(context, StatusCodes.Status413PayloadTooLarge, $"The settings are larger than {MaxBodyBytes} bytes.").ConfigureAwait(false);
            return;
        }

        string body;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8))
        {
            var buffer = new char[MaxBodyBytes + 1];
            int read = 0, n;
            while (read < buffer.Length && (n = await reader.ReadAsync(buffer.AsMemory(read), context.RequestAborted).ConfigureAwait(false)) > 0)
                read += n;
            if (read > MaxBodyBytes)
            {
                await ErrorAsync(context, StatusCodes.Status413PayloadTooLarge, $"The settings are larger than {MaxBodyBytes} bytes.").ConfigureAwait(false);
                return;
            }

            body = new string(buffer, 0, read);
        }

        if (!MarinerStyle.TryParse(body, out var style, out var error))
        {
            await ErrorAsync(context, StatusCodes.Status400BadRequest, error).ConfigureAwait(false);
            return;
        }

        switch (registry.Register(hash, style))
        {
            case Outcome.Created:
                context.Response.StatusCode = StatusCodes.Status201Created;
                break;
            case Outcome.Unchanged:
                context.Response.StatusCode = StatusCodes.Status200OK;
                break;
            case Outcome.Conflict:
                await ErrorAsync(context, StatusCodes.Status409Conflict, $"Settings '{hash}' are already registered with other values; use another hash.").ConfigureAwait(false);
                return;
            default:
                await ErrorAsync(context, StatusCodes.Status507InsufficientStorage, $"The server holds {Capacity} settings sets already.").ConfigureAwait(false);
                return;
        }

        await WriteAsync(context, hash.ToLowerInvariant(), style, palettes).ConfigureAwait(false);
    }

    private static Task GetAsync(HttpContext context, MarinerStyleRegistry? registry, IReadOnlyList<string> palettes)
    {
        var hash = (string)context.Request.RouteValues["hash"]!;
        if (registry is null || !registry.TryGet(hash, out var style))
            return ErrorAsync(context, StatusCodes.Status404NotFound, $"No settings are registered as '{hash}'.");

        return WriteAsync(context, hash.ToLowerInvariant(), style, palettes);
    }

    /// <summary>Writes the registered settings and the styles they are served as.</summary>
    private static Task WriteAsync(HttpContext context, string hash, MarinerStyle style, IReadOnlyList<string> palettes)
    {
        var json = new JsonObject
        {
            ["hash"] = hash,
            ["settings"] = style.ToJson(),
            ["styles"] = new JsonArray([.. palettes.Where(p => !p.Contains('-', StringComparison.Ordinal)).Select(p => (JsonNode?)(p + "-" + hash))]),
        };
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-cache";
        return context.Response.WriteAsync(json.ToJsonString(TileSetMetadata.JsonOptions), context.RequestAborted);
    }

    private static Task ErrorAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(new JsonObject { ["error"] = message }.ToJsonString(), context.RequestAborted);
    }
}
