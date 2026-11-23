using CommunityToolkit.Mvvm.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// One row of Object information's attribute table (symbol dictionary
/// design note §4.3, R9, #1678): the attribute's name and value in the
/// feature catalogue's words, each a term once the chart reference names
/// its entry. A complex attribute's sub-attributes are rows of their own.
/// </summary>
/// <remarks>
/// A row starts plain; <see cref="Apply"/> makes its words terms when the
/// pick's index is read. Codes and S-57 acronyms aren't shown: they are in
/// <see cref="CodeLine"/>, which Copy identity reads.
/// </remarks>
internal sealed class PickAttributeRowViewModel : ObservableObject
{
    private string _name;
    private ReferenceKey? _nameKey;
    private string _valueText;
    private ReferenceKey? _valueKey;
    private string? _valueCode;
    private TermText? _valueTerms;
    private string _codeLine;

    /// <summary>A plain row for <paramref name="attribute"/>.</summary>
    /// <param name="attribute">The pick's attribute, formatted for display.</param>
    public PickAttributeRowViewModel(PickAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        Attribute = attribute;
        Children = [.. attribute.Children.Select(c => new PickAttributeRowViewModel(c))];
        _name = attribute.DisplayName;
        _valueText = attribute.DisplayText;
        _codeLine = CodeLineOf(attribute.Code, null);
    }

    /// <summary>The pick's attribute.</summary>
    public PickAttribute Attribute { get; }

    /// <summary>A complex attribute's sub-attributes.</summary>
    public IReadOnlyList<PickAttributeRowViewModel> Children { get; }

    /// <summary>The mark before the name, such as <c>"AML"</c>, or <see langword="null"/>.</summary>
    public string? Mark => Attribute.Mark;

    /// <summary>The attribute's name, as the feature catalogue gives it.</summary>
    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    /// <summary>The attribute's entry, or <see langword="null"/> while the name is plain.</summary>
    public ReferenceKey? NameKey
    {
        get => _nameKey;
        private set => SetProperty(ref _nameKey, value);
    }

    /// <summary>The value's words: a listed value's label, or the value as formatted.</summary>
    public string ValueText
    {
        get => _valueText;
        private set => SetProperty(ref _valueText, value);
    }

    /// <summary>The attribute a single listed value belongs to, or <see langword="null"/> while the value is plain.</summary>
    public ReferenceKey? ValueKey
    {
        get => _valueKey;
        private set => SetProperty(ref _valueKey, value);
    }

    /// <summary>A single listed value's code, or <see langword="null"/>.</summary>
    public string? ValueCode
    {
        get => _valueCode;
        private set => SetProperty(ref _valueCode, value);
    }

    /// <summary>
    /// A list of listed values, such as an S-57 <c>COLOUR</c> of <c>3,1</c>:
    /// "red, white", each a term; <see langword="null"/> for a single value.
    /// </summary>
    public TermText? ValueTerms
    {
        get => _valueTerms;
        private set
        {
            if (SetProperty(ref _valueTerms, value))
                OnPropertyChanged(nameof(HasValueList));
        }
    }

    /// <summary>Whether the value is a list of listed values (<see cref="ValueTerms"/>).</summary>
    public bool HasValueList => _valueTerms is not null;

    /// <summary>"colour (COLOUR) = 3": the row's code and raw value, for Copy identity; a complex attribute's code alone.</summary>
    public string CodeLine
    {
        get => _codeLine;
        private set => SetProperty(ref _codeLine, value);
    }

    /// <summary>
    /// Makes the row's words terms over <paramref name="index"/>: the name
    /// when the index holds the attribute, by code or S-57 acronym, and the
    /// value when it is one or more of its listed values. Applies to the
    /// sub-attributes too.
    /// </summary>
    /// <param name="index">The pick's index.</param>
    public void Apply(ChartReferenceIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        foreach (var child in Children)
            child.Apply(index);

        var entry = PickTerms.Attribute(index, Attribute.Code);
        CodeLine = CodeLineOf(Attribute.Code, entry);
        if (entry is null)
            return;

        Name = Attribute.Name ?? entry.Name;
        NameKey = entry.Key;
        if (entry.ListedValuesOf(Attribute.RawValue) is not { } values)
            return;
        if (values.Count == 1)
        {
            ValueText = Attribute.DisplayValue ?? values[0].Label;
            ValueCode = values[0].Code;
            ValueKey = entry.Key;
            return;
        }

        ValueTerms = TermText.Join(", ", values.Select(v => TermText.Term(v.Label, entry.Key, v.Code)));
        ValueText = ValueTerms.Text;
    }

    // A complex attribute has no value of its own: its code alone.
    private string CodeLineOf(string code, AttributeEntry? entry)
    {
        var text = PickTerms.CodeText(code, entry?.Code, entry?.Alias);
        return Children.Count > 0 && string.IsNullOrEmpty(Attribute.RawValue) ? text : $"{text} = {Attribute.RawValue}";
    }
}
