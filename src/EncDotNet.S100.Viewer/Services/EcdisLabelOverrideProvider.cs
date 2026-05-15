using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Loads viewer-curated, human-friendly labels for ECDIS viewing
/// groups. Labels are sourced from per-spec embedded JSON resources
/// at <c>EncDotNet.S100.Viewer.Resources.EcdisLabels.&lt;spec&gt;.labels.json</c>
/// (with the spec code normalised to e.g. <c>S101</c>).
/// </summary>
/// <remarks>
/// <para>
/// Each spec's Portrayal Catalogue ships an inconsistent set of
/// viewing-group names — some lowercase, some title-case, some
/// containing embedded symbol references (e.g.
/// <c>land area (LANDARE)</c> or <c>cursor [symbol  SY(CURSRA01)]</c>),
/// and in S-127 / S-421 the "name" is just the numeric id. Rather
/// than mutate the upstream catalogue, this provider supplies a
/// curated label per (spec, id) pair which the ECDIS panel uses
/// when displaying viewing groups.
/// </para>
/// <para>
/// The provider is conservative: missing resource files, malformed
/// JSON, and missing entries all silently fall back to no override,
/// so adding a new spec never breaks the panel.
/// </para>
/// </remarks>
internal sealed class EcdisLabelOverrideProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Assembly _assembly;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<int, string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public EcdisLabelOverrideProvider()
        : this(typeof(EcdisLabelOverrideProvider).Assembly)
    {
    }

    internal EcdisLabelOverrideProvider(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assembly = assembly;
    }

    /// <summary>
    /// Attempts to resolve a curated label for the given spec and
    /// viewing-group id.
    /// </summary>
    /// <param name="specCode">Spec code (e.g. <c>"S-101"</c>).</param>
    /// <param name="viewingGroupId">Viewing-group integer id.</param>
    /// <param name="label">Curated label when an override is present.</param>
    /// <returns><see langword="true"/> when a curated label exists.</returns>
    public bool TryGetLabel(string specCode, int viewingGroupId, out string label)
    {
        ArgumentNullException.ThrowIfNull(specCode);

        var map = GetMap(specCode);
        if (map.TryGetValue(viewingGroupId, out var value))
        {
            label = value;
            return true;
        }

        label = string.Empty;
        return false;
    }

    private IReadOnlyDictionary<int, string> GetMap(string specCode)
    {
        return _cache.GetOrAdd(specCode, LoadMap);
    }

    private IReadOnlyDictionary<int, string> LoadMap(string specCode)
    {
        var resourceName = ResolveResourceName(specCode);
        if (resourceName is null)
        {
            return new Dictionary<int, string>();
        }

        try
        {
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return new Dictionary<int, string>();

            var doc = JsonSerializer.Deserialize<EcdisLabelOverrideFile>(stream, JsonOptions);
            if (doc?.Groups is null) return new Dictionary<int, string>();

            var map = new Dictionary<int, string>(doc.Groups.Count);
            foreach (var (key, value) in doc.Groups)
            {
                if (value is null || string.IsNullOrWhiteSpace(value.Label)) continue;
                if (!int.TryParse(key, out var id)) continue;
                map[id] = value.Label.Trim();
            }
            return map;
        }
        catch (JsonException)
        {
            return new Dictionary<int, string>();
        }
        catch (IOException)
        {
            return new Dictionary<int, string>();
        }
    }

    private string? ResolveResourceName(string specCode)
    {
        var normalised = NormaliseSpecCode(specCode);
        if (string.IsNullOrEmpty(normalised)) return null;

        var suffix = $"Resources.EcdisLabels.{normalised}.labels.json";
        foreach (var name in _assembly.GetManifestResourceNames())
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return name;
            }
        }
        return null;
    }

    private static string NormaliseSpecCode(string specCode)
    {
        // Accepts "S-101", "S101", "s-101" -> "S101"
        Span<char> buffer = stackalloc char[specCode.Length];
        var length = 0;
        foreach (var c in specCode)
        {
            if (c == '-' || c == '_' || char.IsWhiteSpace(c)) continue;
            buffer[length++] = char.ToUpperInvariant(c);
        }
        return new string(buffer[..length]);
    }

    private sealed class EcdisLabelOverrideFile
    {
        [JsonPropertyName("specCode")]
        public string? SpecCode { get; set; }

        [JsonPropertyName("groups")]
        public Dictionary<string, EcdisLabelOverrideEntry>? Groups { get; set; }
    }

    private sealed class EcdisLabelOverrideEntry
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }
}
