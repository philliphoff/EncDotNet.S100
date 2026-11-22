namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>What a <see cref="RuleFact"/> records.</summary>
internal enum RuleFactKind
{
    /// <summary>A portrayal item named by id.</summary>
    Item,

    /// <summary>An item family named by its prefix.</summary>
    Family,

    /// <summary>A colour fill token.</summary>
    ColorFill,

    /// <summary>A plain line.</summary>
    Pen,

    /// <summary>A viewing group id.</summary>
    ViewingGroup,
}

/// <summary>
/// One thing a rule file names, with the conditions that lead there.
/// </summary>
internal sealed record RuleFact(
    RuleFactKind Kind,
    string Value,
    IReadOnlyList<RuleCondition> Conditions,
    int Line,
    string? Owner)
{
    /// <summary>For <see cref="RuleFactKind.Item"/>: the kind of item.</summary>
    public ReferenceKind ItemKind { get; init; } = ReferenceKind.Symbol;

    public string Dash { get; init; } = "solid";

    public double Width { get; init; }
}

/// <summary>
/// An XSLT <c>xsl:call-template</c>: from a feature type's template
/// (<see cref="Owner"/>) or from another named template.
/// </summary>
internal sealed record TemplateCall(string? Owner, string Template, IReadOnlyList<RuleCondition> Conditions);

/// <summary>What an XSLT named template draws, and the templates it calls.</summary>
internal sealed class NamedTemplate
{
    public List<RuleFact> Facts { get; } = [];

    public List<TemplateCall> Calls { get; } = [];
}

/// <summary>
/// What a rule reader found in one rule file.
/// </summary>
internal sealed class RuleFacts
{
    public RuleFacts(string name) => Name = name;

    /// <summary>The rule file name without its extension.</summary>
    public string Name { get; }

    public List<RuleFact> Facts { get; } = [];

    /// <summary>The rule files the file requires (Lua) or includes (XSLT), without extensions.</summary>
    public List<string> Requires { get; } = [];

    /// <summary>
    /// For Lua: the names of global functions the file defines whose first
    /// parameter is <c>feature</c>, which is how a feature type's entry
    /// point looks when no feature catalogue is given.
    /// </summary>
    public HashSet<string> FeatureEntryPoints { get; } = new(StringComparer.Ordinal);

    /// <summary>For XSLT: the <c>href</c>s of the stylesheets the file includes or imports.</summary>
    public List<string> Includes { get; } = [];

    /// <summary>For XSLT: the calls feature types' templates make to named templates.</summary>
    public List<TemplateCall> Calls { get; } = [];

    /// <summary>For XSLT: the named templates the file defines.</summary>
    public Dictionary<string, NamedTemplate> NamedTemplates { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The catalogue vocabulary the rule readers match text against.
/// </summary>
internal sealed class RuleVocabulary
{
    public RuleVocabulary(
        IReadOnlyDictionary<string, IReadOnlyList<ReferenceKind>> items,
        IReadOnlyDictionary<string, (string Id, ReferenceKind Kind)> itemsByFileStem,
        IReadOnlySet<string> familyPrefixes,
        IReadOnlySet<string> colorTokens)
    {
        Items = items;
        ItemsByFileStem = itemsByFileStem;
        FamilyPrefixes = familyPrefixes;
        ColorTokens = colorTokens;
    }

    /// <summary>
    /// Item id to its kinds. An id can name both a symbol and a line style,
    /// as S-101's <c>ACHARE51</c> does.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ReferenceKind>> Items { get; }

    /// <summary>Item file name without extension to item id and kind.</summary>
    public IReadOnlyDictionary<string, (string Id, ReferenceKind Kind)> ItemsByFileStem { get; }

    public IReadOnlySet<string> FamilyPrefixes { get; }

    public IReadOnlySet<string> ColorTokens { get; }
}
