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
    private readonly ConcurrentDictionary<string, SpecLabelData> _cache =
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

        var data = GetData(specCode);
        if (data.Groups.TryGetValue(viewingGroupId, out var entry) &&
            !string.IsNullOrWhiteSpace(entry.Label))
        {
            label = entry.Label;
            return true;
        }

        label = string.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to resolve the curated section id a viewing group
    /// belongs to (e.g. <c>"depths"</c>). Groups without a declared
    /// section, and specs that declare no sections at all, return
    /// <see langword="false"/>.
    /// </summary>
    /// <param name="specCode">Spec code (e.g. <c>"S-101"</c>).</param>
    /// <param name="viewingGroupId">Viewing-group integer id.</param>
    /// <param name="sectionId">Curated section id when present.</param>
    /// <returns><see langword="true"/> when a section is declared.</returns>
    public bool TryGetSectionId(string specCode, int viewingGroupId, out string sectionId)
    {
        ArgumentNullException.ThrowIfNull(specCode);

        var data = GetData(specCode);
        if (data.Groups.TryGetValue(viewingGroupId, out var entry) &&
            !string.IsNullOrWhiteSpace(entry.SectionId))
        {
            sectionId = entry.SectionId;
            return true;
        }

        sectionId = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns the curated, ordered sections declared for the spec, or
    /// an empty list when the spec declares none (in which case the
    /// ECDIS panel renders a single flat, unsectioned list).
    /// </summary>
    /// <param name="specCode">Spec code (e.g. <c>"S-101"</c>).</param>
    public IReadOnlyList<EcdisLabelSection> GetSections(string specCode)
    {
        ArgumentNullException.ThrowIfNull(specCode);
        return GetData(specCode).Sections;
    }

    private SpecLabelData GetData(string specCode)
    {
        return _cache.GetOrAdd(specCode, LoadData);
    }

    private SpecLabelData LoadData(string specCode)
    {
        var resourceName = ResolveResourceName(specCode);
        if (resourceName is null)
        {
            return SpecLabelData.Empty;
        }

        try
        {
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return SpecLabelData.Empty;

            var doc = JsonSerializer.Deserialize<EcdisLabelOverrideFile>(stream, JsonOptions);
            if (doc is null) return SpecLabelData.Empty;

            var groups = new Dictionary<int, EntryInfo>(doc.Groups?.Count ?? 0);
            if (doc.Groups is not null)
            {
                foreach (var (key, value) in doc.Groups)
                {
                    if (value is null || string.IsNullOrWhiteSpace(value.Label)) continue;
                    if (!int.TryParse(key, out var id)) continue;
                    var section = string.IsNullOrWhiteSpace(value.Section) ? null : value.Section.Trim();
                    groups[id] = new EntryInfo(value.Label.Trim(), section);
                }
            }

            var sections = new List<EcdisLabelSection>();
            if (doc.Sections is not null)
            {
                foreach (var s in doc.Sections)
                {
                    if (s is null || string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Label))
                        continue;
                    sections.Add(new EcdisLabelSection(s.Id.Trim(), s.Label.Trim()));
                }
            }

            return new SpecLabelData(groups, sections);
        }
        catch (JsonException)
        {
            return SpecLabelData.Empty;
        }
        catch (IOException)
        {
            return SpecLabelData.Empty;
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

        [JsonPropertyName("sections")]
        public List<EcdisLabelSectionEntry>? Sections { get; set; }

        [JsonPropertyName("groups")]
        public Dictionary<string, EcdisLabelOverrideEntry>? Groups { get; set; }
    }

    private sealed class EcdisLabelSectionEntry
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }

    private sealed class EcdisLabelOverrideEntry
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("section")]
        public string? Section { get; set; }
    }

    private readonly record struct EntryInfo(string Label, string? SectionId);

    private sealed record SpecLabelData(
        IReadOnlyDictionary<int, EntryInfo> Groups,
        IReadOnlyList<EcdisLabelSection> Sections)
    {
        public static SpecLabelData Empty { get; } =
            new(new Dictionary<int, EntryInfo>(), Array.Empty<EcdisLabelSection>());
    }
}

/// <summary>
/// A curated, ordered section heading under which ECDIS viewing-group
/// checkboxes are grouped in the display-controls panel.
/// </summary>
/// <param name="Id">Stable section id referenced by group entries.</param>
/// <param name="Label">Human-friendly section heading.</param>
internal readonly record struct EcdisLabelSection(string Id, string Label);
