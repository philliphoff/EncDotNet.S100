namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// What <see cref="S101ToS57Translator"/> does with a <c>featureName</c> whose
/// <c>nameUsage</c> is not populated.
/// </summary>
public enum S101NameWithoutUsage
{
    /// <summary>
    /// Choose by <c>language</c>: an English (or empty) name becomes
    /// <c>OBJNAM</c>, any other becomes <c>NOBJNM</c>. Annex C §27.132 leaves the
    /// choice to the data producer when no name carries a name usage; this is
    /// the choice the converter makes for them.
    /// </summary>
    ByLanguage,

    /// <summary>Don't convert the name (Annex C §2.5.8).</summary>
    Drop,
}

/// <summary>
/// What <see cref="S101ToS57Translator"/> does with an <c>INFORM</c> or
/// <c>NINFOM</c> longer than <see cref="S101ToS57TranslationOptions.MaxInformLength"/>.
/// </summary>
public enum S101InformLengthPolicy
{
    /// <summary>Keep the whole text and report it (Annex C §2.4.6 recommends the limit only).</summary>
    Keep,

    /// <summary>Cut the text at the limit and report it.</summary>
    Truncate,
}

/// <summary>
/// The choices S-65 Annex C leaves to the converter, for
/// <see cref="S101ToS57Translator"/>.
/// </summary>
public sealed record S101ToS57TranslationOptions
{
    /// <summary>The default options.</summary>
    public static S101ToS57TranslationOptions Default { get; } = new();

    /// <summary>
    /// The separator between text strings folded into one <c>INFORM</c> or
    /// <c>NINFOM</c> (Annex C §2.4.1, §2.4.6). Defaults to <c>";"</c>.
    /// </summary>
    public string InformSeparator { get; init; } = ";";

    /// <summary>
    /// The recommended maximum length of <c>INFORM</c> and <c>NINFOM</c>
    /// (Annex C §2.4.6). Defaults to 300 characters.
    /// </summary>
    public int MaxInformLength { get; init; } = 300;

    /// <summary>What to do with a longer text. Defaults to <see cref="S101InformLengthPolicy.Keep"/>.</summary>
    public S101InformLengthPolicy InformLengthPolicy { get; init; } = S101InformLengthPolicy.Keep;

    /// <summary>
    /// The language whose national name, text and file reference to prefer for
    /// <c>NOBJNM</c>, <c>NINFOM</c> and <c>NTXTDS</c> when there are several
    /// (Annex C §2.4.12.1, §27.132), as an ISO 639-2 code such as <c>fra</c>.
    /// <c>null</c>, the default, takes the first.
    /// </summary>
    public string? NationalLanguage { get; init; }

    /// <summary>
    /// What to do with a name that has no <c>nameUsage</c>. Defaults to
    /// <see cref="S101NameWithoutUsage.ByLanguage"/>.
    /// </summary>
    public S101NameWithoutUsage NameWithoutUsage { get; init; } = S101NameWithoutUsage.ByLanguage;

    /// <summary>
    /// Whether to make the conversions to <c>INFORM</c> that Annex C marks
    /// optional (the clauses with a grey background, §2.4.6). Phase 1 treats
    /// <c>multiplicityOfFeatures</c> (§2.1.1, §27.131) as one of them.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool IncludeOptionalInform { get; init; } = true;
}
