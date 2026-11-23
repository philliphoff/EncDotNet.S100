using System.Collections.Concurrent;
using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// What a term says about its entry before its peek opens: the peek's text,
/// which is also the term's accessible description (R8, #1651).
/// </summary>
/// <param name="Key">The entry.</param>
/// <param name="Value">A listed value's code, for a term that names one; otherwise <see langword="null"/>.</param>
/// <param name="KindLabel">"Feature type", "Symbol", "Listed value"…</param>
/// <param name="Name">The entry's name, or the listed value's label.</param>
/// <param name="Definition">The definition, or a symbol's meaning in its first sentence; <see langword="null"/> for none.</param>
/// <param name="Lead">The glyph the peek shows, or <see langword="null"/> for an entry without one.</param>
internal sealed record ReferenceTermInfo(
    ReferenceKey Key,
    string? Value,
    string KindLabel,
    string Name,
    string? Definition,
    LeadGlyph? Lead)
{
    /// <summary>"Feature type, Lateral Beacon: A beacon used to indicate …"</summary>
    public string Description => Definition is null
        ? string.Format(CultureInfo.CurrentCulture, Strings.Reference_TermDescriptionShort, KindLabel, Name)
        : string.Format(CultureInfo.CurrentCulture, Strings.Reference_TermDescription, KindLabel, Name, Definition);
}

/// <summary>
/// The chart reference behind terms (symbol dictionary design note §4.2,
/// §4.6, R8, #1651): whether the index holds a term's entry, what its peek
/// says and shows, and where a click opens it.
/// </summary>
/// <remarks>
/// <para>
/// The Legend tab and Display settings share one, which opens entries in the
/// Reference tab. An entry view gets its own (<see cref="With"/>), seeded
/// with the entry's index so its terms resolve at once, and opening where
/// its links open: in the tab, or on the study page.
/// </para>
/// <para>
/// Indexes are read through <see cref="ChartReferenceService"/> and kept
/// until their catalogues change.
/// </para>
/// </remarks>
internal sealed class ReferenceTerms
{
    /// <summary>The peek glyph's size, in logical pixels.</summary>
    public const int PeekGlyphSize = 48;

    private readonly Func<SpecRef, Task<ChartReferenceIndex?>>? _indexes;
    private readonly Func<SpecRef, ChartReferenceArtwork?>? _artwork;
    private readonly ConcurrentDictionary<string, ChartReferenceIndex> _ready;
    private readonly Action<ReferenceKey> _open;
    private readonly Func<bool> _simplified;

    /// <summary>Creates the terms.</summary>
    /// <param name="glyphs">The glyph cache, whose palette peeks draw in.</param>
    /// <param name="open">Opens an entry: a term's click, and a peek's Open in reference.</param>
    /// <param name="indexes">Reads a product's index; <see langword="null"/> for only the seeded ones.</param>
    /// <param name="artwork">A product's artwork reader; <see langword="null"/> draws no glyphs.</param>
    /// <param name="examiner">The S-100 eXaminer links; <see langword="null"/> hides them.</param>
    /// <param name="urls">Opens a web page.</param>
    /// <param name="simplifiedSymbols">Whether a feature type leads with its simplified symbol.</param>
    public ReferenceTerms(
        ChartReferenceGlyphCache glyphs,
        Action<ReferenceKey> open,
        Func<SpecRef, Task<ChartReferenceIndex?>>? indexes = null,
        Func<SpecRef, ChartReferenceArtwork?>? artwork = null,
        IS100ExaminerLinkBuilder? examiner = null,
        IUrlOpener? urls = null,
        Func<bool>? simplifiedSymbols = null)
        : this(glyphs, open, indexes, artwork, examiner, urls, simplifiedSymbols ?? (static () => false), new(StringComparer.OrdinalIgnoreCase))
    {
    }

    private ReferenceTerms(
        ChartReferenceGlyphCache glyphs,
        Action<ReferenceKey> open,
        Func<SpecRef, Task<ChartReferenceIndex?>>? indexes,
        Func<SpecRef, ChartReferenceArtwork?>? artwork,
        IS100ExaminerLinkBuilder? examiner,
        IUrlOpener? urls,
        Func<bool> simplifiedSymbols,
        ConcurrentDictionary<string, ChartReferenceIndex> ready)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(open);
        Glyphs = glyphs;
        _open = open;
        _indexes = indexes;
        _artwork = artwork;
        Examiner = examiner;
        Urls = urls;
        _simplified = simplifiedSymbols;
        _ready = ready;
    }

    /// <summary>The glyph cache, whose palette and tile peeks use.</summary>
    public ChartReferenceGlyphCache Glyphs { get; }

    /// <summary>The S-100 eXaminer links, or <see langword="null"/>.</summary>
    public IS100ExaminerLinkBuilder? Examiner { get; }

    /// <summary>Opens a web page, or <see langword="null"/>.</summary>
    public IUrlOpener? Urls { get; }

    /// <summary>The terms the app shares: indexes from <paramref name="service"/>, opening in the Reference tab.</summary>
    /// <param name="service">The chart reference indexes.</param>
    /// <param name="glyphs">The glyph cache.</param>
    /// <param name="navigator">The Reference tab's trail.</param>
    /// <param name="examiner">The S-100 eXaminer links.</param>
    /// <param name="urls">Opens a web page.</param>
    /// <param name="simplifiedSymbols">Whether a feature type leads with its simplified symbol.</param>
    public static ReferenceTerms For(
        ChartReferenceService service,
        ChartReferenceGlyphCache glyphs,
        ReferenceNavigator navigator,
        IS100ExaminerLinkBuilder? examiner,
        IUrlOpener? urls,
        Func<bool>? simplifiedSymbols = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(navigator);
        var terms = new ReferenceTerms(
            glyphs,
            navigator.Open,
            spec => service.GetIndexAsync(spec),
            service.GetArtwork,
            examiner,
            urls,
            simplifiedSymbols);
        service.Changed += (_, e) => terms.Forget(e.Spec);
        return terms;
    }

    /// <summary>
    /// These terms for an entry view: <paramref name="index"/> resolves at
    /// once, and a click opens with <paramref name="open"/>.
    /// </summary>
    /// <param name="index">The entry's index.</param>
    /// <param name="open">Opens an entry, where the entry view's links do.</param>
    /// <param name="artwork">The entry's artwork reader, or <see langword="null"/> for these terms' own.</param>
    public ReferenceTerms With(ChartReferenceIndex index, Action<ReferenceKey> open, ChartReferenceArtwork? artwork = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        var ready = new ConcurrentDictionary<string, ChartReferenceIndex>(_ready, StringComparer.OrdinalIgnoreCase);
        ready[index.Spec.Name] = index;
        Func<SpecRef, ChartReferenceArtwork?>? art = artwork is null ? _artwork : spec => SameSpec(spec, index.Spec) ? artwork : _artwork?.Invoke(spec);
        return new ReferenceTerms(Glyphs, open, _indexes, art, Examiner, Urls, _simplified, ready);
    }

    /// <summary>A plain set of terms over one index, for hosts that build none (tests, the study page).</summary>
    /// <param name="context">The entry view's host.</param>
    internal static ReferenceTerms Of(ReferenceEntryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var simplified = context.SimplifiedSymbols;
        return new ReferenceTerms(context.Glyphs, context.Open, simplifiedSymbols: () => simplified)
            .With(context.Index, context.Open, context.Artwork);
    }

    /// <summary>Opens <paramref name="key"/>'s entry.</summary>
    /// <param name="key">The entry.</param>
    public void Open(ReferenceKey key) => _open(key);

    /// <summary>
    /// What <paramref name="key"/>'s term says, when its index is ready;
    /// otherwise <see langword="false"/>, and <see cref="DescribeAsync"/> reads it.
    /// </summary>
    /// <param name="key">The entry.</param>
    /// <param name="value">A listed value's code, or <see langword="null"/>.</param>
    /// <param name="info">What the term says, or <see langword="null"/> when the index has no such entry.</param>
    /// <returns>Whether the index was ready.</returns>
    public bool TryDescribe(ReferenceKey key, string? value, out ReferenceTermInfo? info)
    {
        info = null;
        if (key.Spec.Name is null || !_ready.TryGetValue(key.Spec.Name, out var index))
            return false;
        info = Describe(index, key, value);
        return true;
    }

    /// <summary>What <paramref name="key"/>'s term says, reading its index first when needed.</summary>
    /// <param name="key">The entry.</param>
    /// <param name="value">A listed value's code, or <see langword="null"/>.</param>
    /// <returns>What the term says, or <see langword="null"/> when the catalogues have no such entry: a plain word.</returns>
    public async Task<ReferenceTermInfo?> DescribeAsync(ReferenceKey key, string? value = null)
    {
        if (TryDescribe(key, value, out var held))
            return held;
        if (_indexes is null || key.Spec.Name is null)
            return null;
        ChartReferenceIndex? index;
        try
        {
            index = await _indexes(key.Spec).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A catalogue that can't be read: the word stays plain.
            return null;
        }

        if (index is null)
            return null;
        _ready[key.Spec.Name] = index;
        return Describe(index, key, value);
    }

    /// <summary>
    /// The feature type entry of a feature a dataset holds, by the dataset's
    /// product and the feature's type code (symbol dictionary R9b, #1681):
    /// <c>S-101</c> and <c>LateralBeacon</c> give
    /// <c>S-101/FeatureType/LateralBeacon</c>. The key is not checked against
    /// the index; a term or <see cref="FeatureTypeAsync"/> does that.
    /// </summary>
    /// <remarks>
    /// A product portrayed with another's catalogue (an S-57 cell, drawn with
    /// S-101's) has codes that aren't that catalogue's, so it gives
    /// <see langword="null"/>; its features stay plain.
    /// </remarks>
    /// <param name="productSpec">The dataset's product, such as <c>S-101</c>.</param>
    /// <param name="featureType">The feature's type code.</param>
    /// <returns>The key, or <see langword="null"/>.</returns>
    public static ReferenceKey? FeatureKeyOf(string? productSpec, string? featureType)
    {
        if (string.IsNullOrWhiteSpace(featureType) || !SpecName.TryNormalize(productSpec, out var spec))
            return null;
        var portrayal = EncDotNet.S100.Datasets.Pipelines.ProductRegistry.Default.PortrayalSpecName(spec);
        if (!string.Equals(portrayal, spec, StringComparison.Ordinal))
            return null;
        return new ReferenceKey(new SpecRef(spec, default), ReferenceKind.FeatureType, featureType.Trim());
    }

    /// <summary>
    /// What the term of a dataset feature's type says (<see cref="FeatureKeyOf"/>),
    /// reading its index first when needed (R9b, #1681).
    /// </summary>
    /// <param name="productSpec">The dataset's product, such as <c>S-101</c>.</param>
    /// <param name="featureType">The feature's type code.</param>
    /// <returns>What the term says, or <see langword="null"/> when the catalogues have no such entry.</returns>
    public Task<ReferenceTermInfo?> FeatureTypeAsync(string? productSpec, string? featureType) =>
        FeatureKeyOf(productSpec, featureType) is { } key ? DescribeAsync(key) : Task.FromResult<ReferenceTermInfo?>(null);

    /// <summary>The S-100 eXaminer page of a term, or <see langword="null"/>: links are off, or it has no page.</summary>
    /// <param name="info">The term.</param>
    public string? ExaminerUrl(ReferenceTermInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (Examiner is null || Urls is null)
            return null;
        var spec = info.Key.Spec.Name;
        return info.Key.Kind switch
        {
            ReferenceKind.FeatureType => Examiner.BuildFeatureUrl(spec, info.Key.Code),
            ReferenceKind.Attribute => Examiner.BuildAttributeUrl(spec, null, info.Key.Code),
            _ => null,
        };
    }

    /// <summary>Draws a term's glyph for its peek, or <see langword="null"/>.</summary>
    /// <param name="info">The term.</param>
    public async Task<Avalonia.Media.IImage?> GlyphAsync(ReferenceTermInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.Lead is not { HasGlyph: true } lead || _artwork?.Invoke(info.Key.Spec) is not { } artwork)
            return null;
        if (Glyphs.TryGet(lead.Key, PeekGlyphSize, PeekGlyphSize, out var held))
            return held;
        var palette = Glyphs.Palette;
        var row = await artwork.BuildAsync(lead, palette).ConfigureAwait(false);
        return Glyphs.Palette != palette ? null : Glyphs.Get(lead.Key, PeekGlyphSize, PeekGlyphSize, _ => row);
    }

    /// <summary>The first sentence of a text: "A beacon. It marks…" gives "A beacon."</summary>
    /// <param name="text">The text.</param>
    internal static string FirstSentence(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        for (int i = 0; i < trimmed.Length - 1; i++)
        {
            if (trimmed[i] is '.' or '!' or '?' && char.IsWhiteSpace(trimmed[i + 1]))
                return trimmed[..(i + 1)];
        }

        return trimmed;
    }

    private void Forget(SpecRef spec)
    {
        if (spec.Name is null)
            _ready.Clear();
        else
            _ready.TryRemove(spec.Name, out _);
    }

    private ReferenceTermInfo? Describe(ChartReferenceIndex index, ReferenceKey key, string? value)
    {
        if (!index.TryGet(key, out var entry))
            return null;
        var definition = string.IsNullOrWhiteSpace(entry.Definition) ? null : entry.Definition.Trim();
        switch (entry)
        {
            case AttributeEntry attribute when value is not null:
                {
                    if (attribute.ListedValues.FirstOrDefault(v => v.Code == value) is not { } listed)
                        return null;
                    var valueDefinition = string.IsNullOrWhiteSpace(listed.Definition) ? null : listed.Definition.Trim();
                    return new ReferenceTermInfo(
                        entry.Key,
                        value,
                        string.Format(CultureInfo.CurrentCulture, Strings.Reference_Kind_ListedValue, RuleConditionText.SentenceCase(attribute.Name)),
                        listed.Label,
                        valueDefinition,
                        null);
                }

            case FeatureTypeEntry feature:
                return new ReferenceTermInfo(entry.Key, null, Strings.Reference_Kind_FeatureType, entry.Name, definition, LeadGlyph.For(feature, index, _simplified()));

            case PortrayalItemEntry item:
                {
                    var kind = item.IsFamily
                        ? Strings.Reference_Kind_Family
                        : item.Key.Kind switch
                        {
                            ReferenceKind.LineStyle => Strings.Reference_Kind_LineStyle,
                            ReferenceKind.AreaFill => Strings.Reference_Kind_AreaFill,
                            _ => Strings.Reference_Kind_Symbol,
                        };
                    // A symbol's meaning in a sentence; its name is its description's start.
                    var name = ChartReferenceRowViewModel.NameOf(entry);
                    var meaning = definition is null || ChartReferenceRowViewModel.DescriptionRepeatsName(entry) ? null : FirstSentence(definition);
                    return new ReferenceTermInfo(entry.Key, null, kind, name, meaning, LeadGlyph.For(item));
                }

            case AttributeEntry:
                return new ReferenceTermInfo(entry.Key, null, Strings.Reference_Kind_Attribute, RuleConditionText.SentenceCase(entry.Name), definition, null);

            case DisplaySettingEntry:
                return new ReferenceTermInfo(entry.Key, null, Strings.Reference_Kind_DisplaySetting, RuleConditionText.SentenceCase(entry.Name), definition, null);

            default:
                return new ReferenceTermInfo(entry.Key, null, entry.Key.Kind.ToString(), entry.Name, definition, null);
        }
    }

    private static bool SameSpec(SpecRef a, SpecRef b) =>
        string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
}
