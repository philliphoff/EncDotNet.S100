namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// A <c>--source</c> argument (data source design note D9): a source id, then
/// optionally <c>:</c> and <c>name=value</c> pairs separated by commas, such as
/// <c>nmea.weather:Input=udp:10110</c>. The id ends at the first colon; a value
/// can't contain a comma.
/// </summary>
/// <param name="Id">The source's id.</param>
/// <param name="Options">Its option values by name; empty for its defaults.</param>
internal sealed record SourceArgument(string Id, IReadOnlyDictionary<string, string> Options)
{
    /// <summary>Parses <paramref name="text"/>, or says why it can't.</summary>
    /// <param name="text">The argument.</param>
    /// <param name="error">Why it isn't a source argument.</param>
    public static SourceArgument? TryParse(string? text, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "--source needs a source id, such as ndbc.realtime ('s100 sources' lists them).";
            return null;
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var id = (colon < 0 ? text : text[..colon]).Trim();
        if (id.Length == 0)
        {
            error = $"--source '{text}' has no source id before the ':'.";
            return null;
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (colon >= 0)
        {
            foreach (var pair in text[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var equals = pair.IndexOf('=', StringComparison.Ordinal);
                if (equals <= 0)
                {
                    error = $"--source '{text}': '{pair}' isn't name=value.";
                    return null;
                }
                options[pair[..equals].Trim()] = pair[(equals + 1)..].Trim();
            }
        }

        return new SourceArgument(id, options);
    }
}
