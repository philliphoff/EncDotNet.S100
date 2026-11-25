using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// One glyph a probe outcome is shown by (R13c, #1722), in an attribute's
/// value symbols or a display setting's before/after pairs. It is a term:
/// it opens its symbol's entry, and its peek says what it is. A colour
/// patch, or an outcome with nothing to draw, is not.
/// </summary>
internal sealed class ReferenceGlyphTile : ViewModelBase
{
    private IImage? _glyph;

    private ReferenceGlyphTile(string? part, LeadGlyph? lead, ReferenceKey? key, string label, Action<ReferenceKey> open)
    {
        Part = part;
        Lead = lead;
        Key = key;
        Label = label;
        OpenCommand = new RelayCommand(() =>
        {
            if (Key is { } k)
                open(k);
        });
    }

    /// <summary>The part shown, as <see cref="ProbeOutcome.Parts"/> writes it, or <see langword="null"/>.</summary>
    public string? Part { get; }

    /// <summary>The glyph to draw, or <see langword="null"/> when there is none.</summary>
    public LeadGlyph? Lead { get; }

    /// <summary>The entry it opens: the symbol, line style or area fill; <see langword="null"/> for a colour patch or nothing.</summary>
    public ReferenceKey? Key { get; }

    /// <summary>Whether it is a term, so it opens an entry.</summary>
    public bool IsKnown => Key is not null;

    /// <summary>"BCNLAT21", "DEPDW", or "Nothing drawn".</summary>
    public string Label { get; }

    /// <summary>Whether the rule drew something it can be shown by.</summary>
    public bool HasLead => Lead is not null;

    /// <summary>Opens <see cref="Key"/>'s entry.</summary>
    public ICommand OpenCommand { get; }

    /// <summary>The glyph, or <see langword="null"/> until it is drawn.</summary>
    public IImage? Glyph
    {
        get => _glyph;
        set => SetProperty(ref _glyph, value);
    }

    /// <summary>The tile of an outcome: the part it is shown by.</summary>
    /// <param name="outcome">The outcome, or <see langword="null"/> when no combination stands for it.</param>
    /// <param name="index">The index the outcome's items belong to.</param>
    /// <param name="open">Opens the tile's entry.</param>
    public static ReferenceGlyphTile Of(ProbeOutcome? outcome, ChartReferenceIndex index, Action<ReferenceKey> open)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(open);
        if (outcome is null)
            return new ReferenceGlyphTile(null, null, null, Strings.Reference_NotProbed, open);
        var part = ProbeValues.MainPart(outcome);
        return Of(part, index.Spec, index, open);
    }

    /// <summary>The tile of a part.</summary>
    /// <param name="part">The part, or <see langword="null"/> for an outcome that drew nothing it can be shown by.</param>
    /// <param name="spec">The product.</param>
    /// <param name="index">The index, which says whether the part's item has an entry.</param>
    /// <param name="open">Opens the tile's entry.</param>
    public static ReferenceGlyphTile Of(string? part, SpecRef spec, ChartReferenceIndex index, Action<ReferenceKey> open)
    {
        var lead = ProbeParts.GlyphOf(part, spec);
        if (lead is null)
            return new ReferenceGlyphTile(part, null, null, Strings.Reference_NothingDrawn, open);
        var isItem = lead.Kind != LeadGlyphKind.Area;
        var key = isItem && index.TryGet(lead.Key, out _) ? lead.Key : (ReferenceKey?)null;
        var label = isItem ? lead.ItemId! : lead.Tokens![0];
        return new ReferenceGlyphTile(part, lead, key, label, open);
    }

    /// <inheritdoc />
    public override string ToString() => Label;
}
