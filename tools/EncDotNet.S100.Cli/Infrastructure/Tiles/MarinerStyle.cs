using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// A set of mariner settings tiles are rendered with (issue #1493): the
/// display category, the depth settings and the S-101 display toggles, named
/// as the viewer names them. Each value is optional; one left out takes the
/// value it falls back to (<see cref="Over"/>), and in the end the
/// processors' own defaults, except that the viewing groups the viewer hides
/// by default are hidden (<see cref="EffectiveHiddenViewingGroups"/>), so a set
/// with nothing in it renders as the viewer shows "All".
/// </summary>
/// <remarks>
/// <c>tiles serve</c> and <c>tiles export</c> take one at start-up from their
/// options; a <c>tiles serve</c> client can also register more under
/// <c>settings/{hash}</c> (<see cref="MarinerStyleRegistry"/>) and ask for one
/// as the <c>{palette}-{hash}</c> style. Depths are in metres.
/// </remarks>
internal sealed record MarinerStyle
{
    /// <summary>The S-101 viewing group of soundings (S-101 portrayal catalogue, <c>Sounding.lua</c>).</summary>
    internal const int SoundingsViewingGroup = 33010;

    /// <summary>The largest depth accepted, in metres.</summary>
    internal const double MaxDepthMetres = 12000;

    /// <summary>The settings with no value set.</summary>
    public static MarinerStyle Empty { get; } = new();

    /// <summary>
    /// The ECDIS display category (S-100 Part 9 §11.7). Unset is every viewing
    /// group (<see cref="EcdisDisplayCategory.All"/>) but the hidden ones
    /// (<see cref="EffectiveHiddenViewingGroups"/>).
    /// </summary>
    public EcdisDisplayCategory? DisplayCategory { get; init; }

    /// <summary>The safety contour in metres (S-101 context parameter <c>SafetyContour</c>).</summary>
    public double? SafetyContour { get; init; }

    /// <summary>The safety depth in metres (<c>SafetyDepth</c>).</summary>
    public double? SafetyDepth { get; init; }

    /// <summary>The shallow contour in metres (<c>ShallowContour</c>).</summary>
    public double? ShallowContour { get; init; }

    /// <summary>The deep contour in metres (<c>DeepContour</c>).</summary>
    public double? DeepContour { get; init; }

    /// <summary>Whether depth areas use four shades rather than two (<c>FourShades</c>).</summary>
    public bool? FourShades { get; init; }

    /// <summary>Whether soundings are drawn (S-101 viewing group 33010).</summary>
    public bool? Soundings { get; init; }

    /// <summary>Whether isolated dangers in shallow water are shown (<c>ShallowWaterDangers</c>).</summary>
    public bool? ShallowWaterDangers { get; init; }

    /// <summary>Whether area boundaries are plain rather than symbolized (<c>PlainBoundaries</c>).</summary>
    public bool? PlainBoundaries { get; init; }

    /// <summary>Whether point symbols are simplified rather than paper-chart (<c>SimplifiedSymbols</c>).</summary>
    public bool? SimplifiedSymbols { get; init; }

    /// <summary>Whether light sectors are drawn to their full range (<c>FullLightLines</c>).</summary>
    public bool? FullLightLines { get; init; }

    /// <summary>Whether features are drawn at every scale, ignoring their SCAMIN (<c>IgnoreScaleMinimum</c>).</summary>
    public bool? IgnoreScaleMinimum { get; init; }

    /// <summary>The unit depths are shown in (<see cref="Pipelines.DepthUnit"/>).</summary>
    public DepthUnit? DepthUnit { get; init; }

    /// <summary>
    /// The viewing groups hidden, per product, in the canonical text form
    /// <see cref="FormatViewingGroups"/> writes (e.g.
    /// <c>S-101:90000,90010,90011</c>); empty to hide none.
    /// <see langword="null"/> is the viewer's default,
    /// <see cref="EcdisDisplaySettings.DefaultHiddenViewingGroups"/> (issue #1665).
    /// </summary>
    /// <remarks>Text rather than a dictionary so the record compares by value.</remarks>
    public string? HiddenViewingGroups { get; init; }

    /// <summary>
    /// The viewing groups hidden: <see cref="HiddenViewingGroups"/>, or the
    /// default set when it is unset. Soundings switched off are not included.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<int>> EffectiveHiddenViewingGroups =>
        HiddenViewingGroups is { } text ? ParseCanonicalViewingGroups(text) : EcdisDisplaySettings.DefaultHiddenViewingGroups;

    /// <summary>Whether no value is set.</summary>
    public bool IsEmpty => this == Empty;

    /// <summary>These settings, with each value left out taken from <paramref name="fallback"/>.</summary>
    public MarinerStyle Over(MarinerStyle fallback) => new()
    {
        DisplayCategory = DisplayCategory ?? fallback.DisplayCategory,
        SafetyContour = SafetyContour ?? fallback.SafetyContour,
        SafetyDepth = SafetyDepth ?? fallback.SafetyDepth,
        ShallowContour = ShallowContour ?? fallback.ShallowContour,
        DeepContour = DeepContour ?? fallback.DeepContour,
        FourShades = FourShades ?? fallback.FourShades,
        Soundings = Soundings ?? fallback.Soundings,
        ShallowWaterDangers = ShallowWaterDangers ?? fallback.ShallowWaterDangers,
        PlainBoundaries = PlainBoundaries ?? fallback.PlainBoundaries,
        SimplifiedSymbols = SimplifiedSymbols ?? fallback.SimplifiedSymbols,
        FullLightLines = FullLightLines ?? fallback.FullLightLines,
        IgnoreScaleMinimum = IgnoreScaleMinimum ?? fallback.IgnoreScaleMinimum,
        DepthUnit = DepthUnit ?? fallback.DepthUnit,
        HiddenViewingGroups = HiddenViewingGroups ?? fallback.HiddenViewingGroups,
    };

    /// <summary>
    /// The portrayal's mariner settings: <see cref="MarinerSettings.Default"/>
    /// with the values set here and <paramref name="nationalLanguage"/>.
    /// </summary>
    public MarinerSettings ToMarinerSettings(string? nationalLanguage)
    {
        var d = MarinerSettings.Default;
        return d with
        {
            SafetyContour = SafetyContour is { } safetyContour ? Depth.FromMetres(safetyContour) : d.SafetyContour,
            SafetyDepth = SafetyDepth is { } safetyDepth ? Depth.FromMetres(safetyDepth) : d.SafetyDepth,
            ShallowContour = ShallowContour is { } shallowContour ? Depth.FromMetres(shallowContour) : d.ShallowContour,
            DeepContour = DeepContour is { } deepContour ? Depth.FromMetres(deepContour) : d.DeepContour,
            FourShades = FourShades ?? d.FourShades,
            ShallowWaterDangers = ShallowWaterDangers ?? d.ShallowWaterDangers,
            PlainBoundaries = PlainBoundaries ?? d.PlainBoundaries,
            SimplifiedSymbols = SimplifiedSymbols ?? d.SimplifiedSymbols,
            FullLightLines = FullLightLines ?? d.FullLightLines,
            IgnoreScaleMinimum = IgnoreScaleMinimum ?? d.IgnoreScaleMinimum,
            DepthUnit = DepthUnit ?? d.DepthUnit,
            NationalLanguage = nationalLanguage ?? d.NationalLanguage,
        };
    }

    /// <summary>
    /// The ECDIS display settings for the display category, the hidden viewing
    /// groups (<see cref="EffectiveHiddenViewingGroups"/>) and soundings. With
    /// nothing set this is every viewing group (<see cref="EcdisDisplayCategory.All"/>)
    /// but the default hidden ones, as the viewer shows "All".
    /// </summary>
    public EcdisDisplaySettings ToEcdisDisplay()
    {
        var hidden = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (spec, ids) in EffectiveHiddenViewingGroups)
            hidden[spec] = new HashSet<int>(ids);

        if (Soundings == false)
        {
            // S-57 cells are portrayed with the S-101 catalogue; inland ENC shares its viewing groups.
            foreach (var spec in (string[])["S-101", "S-401"])
            {
                var ids = hidden.TryGetValue(spec, out var existing) ? new HashSet<int>(existing) : [];
                ids.Add(SoundingsViewingGroup);
                hidden[spec] = ids;
            }
        }

        return new EcdisDisplaySettings
        {
            Category = DisplayCategory ?? EcdisDisplayCategory.All,
            HiddenViewingGroups = hidden,
        };
    }

    /// <summary>The settings as JSON, with only the values set, in a fixed order.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject();
        if (DisplayCategory is { } category)
            json["displayCategory"] = category.ToString();
        Add("safetyContour", SafetyContour);
        Add("safetyDepth", SafetyDepth);
        Add("shallowContour", ShallowContour);
        Add("deepContour", DeepContour);
        Add("fourShades", FourShades);
        Add("soundings", Soundings);
        Add("shallowWaterDangers", ShallowWaterDangers);
        Add("plainBoundaries", PlainBoundaries);
        Add("simplifiedSymbols", SimplifiedSymbols);
        Add("fullLightLines", FullLightLines);
        Add("ignoreScaleMinimum", IgnoreScaleMinimum);
        if (DepthUnit is { } unit)
            json["depthUnit"] = unit.ToString();
        if (HiddenViewingGroups is { } groups)
        {
            var byProduct = new JsonObject();
            foreach (var (spec, ids) in ParseCanonicalViewingGroups(groups))
                byProduct[spec] = new JsonArray([.. ids.Order().Select(id => JsonValue.Create(id))]);
            json["hiddenViewingGroups"] = byProduct;
        }

        return json;

        void Add(string name, object? value)
        {
            if (value is double number)
                json[name] = JsonValue.Create(number);
            else if (value is bool flag)
                json[name] = JsonValue.Create(flag);
        }
    }

    /// <summary>
    /// A key that is the same for the same settings and different otherwise:
    /// 16 lowercase hexadecimal characters, empty for <see cref="Empty"/>.
    /// Rendered tiles are cached under it.
    /// </summary>
    public string Key
    {
        get
        {
            if (IsEmpty)
                return string.Empty;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ToJson().ToJsonString()));
            return Convert.ToHexStringLower(hash, 0, 8);
        }
    }

    /// <summary>
    /// The values set, as text for tile-set metadata: e.g. <c>safetyContour</c>
    /// → <c>10</c>, <c>fourShades</c> → <c>true</c>,
    /// <c>hiddenViewingGroups</c> → <c>S-101:90000,90010</c>.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> Describe() =>
        // A JSON value's text is invariant: numbers as JSON writes them, true/false, strings unquoted.
        ToJson().Select(p => new KeyValuePair<string, string>(
            p.Key,
            p.Key == "hiddenViewingGroups" ? HiddenViewingGroups! : p.Value!.ToString()));

    /// <summary>
    /// Reads settings from a JSON object, the names as <see cref="ToJson"/>
    /// writes them (in any letter case). An unknown name, a value of the wrong
    /// type or out of range, is an error.
    /// </summary>
    public static bool TryParse(string json, out MarinerStyle style, out string error)
    {
        style = Empty;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            error = "The body is not JSON: " + e.Message;
            return false;
        }

        if (node is not JsonObject obj)
        {
            error = "The body must be a JSON object.";
            return false;
        }

        var result = Empty;
        foreach (var (name, value) in obj)
        {
            string? problem = null;
            switch (name.ToLowerInvariant())
            {
                case "displaycategory":
                    if (TryEnum<EcdisDisplayCategory>(value, out var category))
                        result = result with { DisplayCategory = category };
                    else
                        problem = "must be one of DisplayBase, Standard, OtherInformation, All";
                    break;
                case "depthunit":
                    if (TryEnum<DepthUnit>(value, out var unit))
                        result = result with { DepthUnit = unit };
                    else
                        problem = "must be one of Metres, Feet, FathomsFeet, Fathoms";
                    break;
                case "safetycontour":
                    problem = Depth(value, d => result = result with { SafetyContour = d });
                    break;
                case "safetydepth":
                    problem = Depth(value, d => result = result with { SafetyDepth = d });
                    break;
                case "shallowcontour":
                    problem = Depth(value, d => result = result with { ShallowContour = d });
                    break;
                case "deepcontour":
                    problem = Depth(value, d => result = result with { DeepContour = d });
                    break;
                case "fourshades":
                    problem = Flag(value, b => result = result with { FourShades = b });
                    break;
                case "soundings":
                    problem = Flag(value, b => result = result with { Soundings = b });
                    break;
                case "shallowwaterdangers":
                    problem = Flag(value, b => result = result with { ShallowWaterDangers = b });
                    break;
                case "plainboundaries":
                    problem = Flag(value, b => result = result with { PlainBoundaries = b });
                    break;
                case "simplifiedsymbols":
                    problem = Flag(value, b => result = result with { SimplifiedSymbols = b });
                    break;
                case "fulllightlines":
                    problem = Flag(value, b => result = result with { FullLightLines = b });
                    break;
                case "ignorescaleminimum":
                    problem = Flag(value, b => result = result with { IgnoreScaleMinimum = b });
                    break;
                case "hiddenviewinggroups":
                    problem = ViewingGroups(value, text => result = result with { HiddenViewingGroups = text });
                    break;
                default:
                    error = $"Unknown setting '{name}'.";
                    return false;
            }

            if (problem is not null)
            {
                error = $"'{name}' {problem}.";
                return false;
            }
        }

        style = result;
        error = string.Empty;
        return true;

        static string? Depth(JsonNode? value, Action<double> set)
        {
            if (value is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<double>(out var metres)
                && IsValidDepth(metres))
            {
                set(metres);
                return null;
            }

            return $"must be a depth in metres from 0 to {MaxDepthMetres.ToString(CultureInfo.InvariantCulture)}";
        }

        static string? Flag(JsonNode? value, Action<bool> set)
        {
            if (value is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            {
                set(v.GetValue<bool>());
                return null;
            }

            return "must be true or false";
        }

        static string? ViewingGroups(JsonNode? value, Action<string> set)
        {
            const string shapeProblem = "must be an object of product specifications to arrays of viewing-group ids, e.g. { \"S-101\": [90010] }";
            if (value is not JsonObject byProduct)
                return shapeProblem;

            var groups = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
            foreach (var (spec, idsNode) in byProduct)
            {
                if (!TryNormalizeSpec(spec, out var canonical) || idsNode is not JsonArray array)
                    return shapeProblem;
                var ids = groups.TryGetValue(canonical, out var existing) ? existing : groups[canonical] = [];
                foreach (var id in array)
                {
                    if (id is not JsonValue v || v.GetValueKind() != JsonValueKind.Number || !v.TryGetValue<int>(out var number) || number < 0)
                        return shapeProblem;
                    ids.Add(number);
                }
            }

            set(FormatViewingGroups(groups));
            return null;
        }

        static bool TryEnum<T>(JsonNode? value, out T parsed)
            where T : struct, Enum
        {
            parsed = default;
            return value is JsonValue v && v.GetValueKind() == JsonValueKind.String
                && TryParseName(v.GetValue<string>(), out parsed);
        }
    }

    /// <summary>
    /// Parses <c>--hide-viewing-groups</c> values: each <c>SPEC:ID,ID,…</c>
    /// (e.g. <c>S-101:90000,90010</c>; the spec with or without its hyphen),
    /// or a single <c>none</c> to hide no viewing group. Repeated values for
    /// the same product are combined. <paramref name="canonical"/> is the
    /// <see cref="HiddenViewingGroups"/> text.
    /// </summary>
    public static bool TryParseViewingGroupsOption(IReadOnlyList<string> values, out string canonical, out string error)
    {
        canonical = string.Empty;
        error = string.Empty;
        if (values.Count == 1 && string.Equals(values[0].Trim(), "none", StringComparison.OrdinalIgnoreCase))
            return true;

        var groups = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.Equals(value.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            {
                error = "--hide-viewing-groups none cannot be combined with other values.";
                return false;
            }

            var colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0 || !TryNormalizeSpec(value[..colon], out var spec))
            {
                error = $"Invalid --hide-viewing-groups value '{value}'. Use SPEC:ID,ID,… (e.g. S-101:90000,90010,90011), or none to show every viewing group.";
                return false;
            }

            var set = groups.TryGetValue(spec, out var existing) ? existing : groups[spec] = [];
            foreach (var token in value[(colon + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                {
                    error = $"Invalid viewing-group id '{token}' in --hide-viewing-groups '{value}'. Ids are whole numbers, e.g. 90010.";
                    return false;
                }

                set.Add(id);
            }
        }

        canonical = FormatViewingGroups(groups);
        return true;
    }

    /// <summary>
    /// The canonical <see cref="HiddenViewingGroups"/> text: products in
    /// ordinal order, each <c>SPEC:ID,ID</c> with ids ascending, joined by
    /// <c>;</c>. Products with no ids are left out.
    /// </summary>
    internal static string FormatViewingGroups(IEnumerable<KeyValuePair<string, SortedSet<int>>> groups) =>
        string.Join(';', groups
            .Where(g => g.Value.Count > 0)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key + ":" + string.Join(',', g.Value.Select(id => id.ToString(CultureInfo.InvariantCulture)))));

    private static Dictionary<string, IReadOnlySet<int>> ParseCanonicalViewingGroups(string text)
    {
        var groups = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = part.IndexOf(':', StringComparison.Ordinal);
            groups[part[..colon]] = part[(colon + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => int.Parse(id, CultureInfo.InvariantCulture))
                .ToHashSet();
        }

        return groups;
    }

    /// <summary>A product specification as <c>S-NNN</c>, from <c>S-101</c>, <c>s101</c> and the like.</summary>
    private static bool TryNormalizeSpec(string text, out string spec)
    {
        var trimmed = text.Trim();
        spec = string.Empty;
        if (trimmed.Length < 2 || char.ToUpperInvariant(trimmed[0]) != 'S')
            return false;
        var digits = trimmed[1] == '-' ? trimmed[2..] : trimmed[1..];
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return false;
        spec = "S-" + digits;
        return true;
    }

    /// <summary>Whether <paramref name="metres"/> is an accepted depth.</summary>
    public static bool IsValidDepth(double metres) => double.IsFinite(metres) && metres is >= 0 and <= MaxDepthMetres;

    /// <summary>Parses an enum member by name, ignoring case; numbers are refused.</summary>
    public static bool TryParseName<T>(string? text, out T parsed)
        where T : struct, Enum
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(text)
            && !char.IsAsciiDigit(text.Trim()[0]) && text.Trim()[0] != '-'
            && Enum.TryParse(text.Trim(), ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed);
    }
}
