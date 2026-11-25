namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// What each feature type's portrayal rule draws for the values it reads:
/// the rule run over synthetic features with every combination of probed
/// attribute values and display settings, and the conditions it took for
/// each (symbol dictionary design note §7, R13a).
/// </summary>
/// <remarks>
/// <para>
/// Made by <see cref="RuleProber"/> and stored by <see cref="RuleProbeStore"/>.
/// A set describes the catalogues whose hashes it carries
/// (<see cref="FeatureCatalogueHash"/>, <see cref="PortrayalCatalogueHash"/>),
/// as <c>FeatureCatalogueManager.GetCatalogueHashAsync</c> and
/// <c>PortrayalCatalogueManager.GetCatalogueHashAsync</c> compute them; it
/// doesn't describe any other catalogue.
/// </para>
/// <para>
/// Conditions are named by rule file and line, the way the chart
/// reference's rule reader names them (<see cref="RuleUse.RuleFile"/> and
/// <see cref="RuleCondition.Line"/>), so a variant's
/// <see cref="ProbeOutcome.Conditions"/> pick out the
/// <see cref="RuleCondition"/>s of a <see cref="FeatureTypeEntry"/> that held.
/// </para>
/// </remarks>
public sealed class RuleProbeSet
{
    /// <summary>The version of the stored format that <see cref="RuleProbeStore"/> writes.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>The most combinations probed per feature type, unless the prober is told otherwise.</summary>
    public const int DefaultCap = 512;

    /// <summary>The version of the stored format.</summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>The product specification, such as <c>S-101</c>.</summary>
    public required string Spec { get; init; }

    /// <summary>The lowercase hex SHA-256 content hash of the feature catalogue probed.</summary>
    public required string FeatureCatalogueHash { get; init; }

    /// <summary>The lowercase hex SHA-256 content hash of the portrayal catalogue probed.</summary>
    public required string PortrayalCatalogueHash { get; init; }

    /// <summary>The most combinations probed for each feature type.</summary>
    public int Cap { get; init; } = DefaultCap;

    /// <summary>The probed feature types, ordered by code.</summary>
    public IReadOnlyList<FeatureTypeProbe> FeatureTypes { get; init; } = [];

    /// <summary>Finds a feature type's probe by its code.</summary>
    /// <param name="code">The feature type code, such as <c>LateralBeacon</c>; ordinal.</param>
    /// <returns>The probe, or <see langword="null"/> when the feature type wasn't probed.</returns>
    public FeatureTypeProbe? Find(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var types = FeatureTypes;
        int lo = 0, hi = types.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var c = string.CompareOrdinal(types[mid].Code, code);
            if (c == 0) return types[mid];
            if (c < 0) lo = mid + 1;
            else hi = mid - 1;
        }

        return null;
    }

    /// <summary>Whether the set describes these catalogues.</summary>
    /// <param name="featureCatalogueHash">The feature catalogue's content hash.</param>
    /// <param name="portrayalCatalogueHash">The portrayal catalogue's content hash.</param>
    /// <returns><see langword="true"/> when both hashes match, ignoring case.</returns>
    public bool Matches(string? featureCatalogueHash, string? portrayalCatalogueHash) =>
        string.Equals(FeatureCatalogueHash, featureCatalogueHash, StringComparison.OrdinalIgnoreCase)
        && string.Equals(PortrayalCatalogueHash, portrayalCatalogueHash, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One feature type's probe: the inputs varied, and what the rule drew for
/// each combination of their values.
/// </summary>
/// <remarks>
/// The combinations are every combination of the inputs' values, so they
/// aren't stored: <see cref="Variants"/> holds one outcome per combination,
/// in row-major order over <see cref="Inputs"/> (the first input varies
/// slowest). <see cref="GetVariants"/> pairs each with its values.
/// </remarks>
public sealed class FeatureTypeProbe
{
    /// <summary>The feature type code, such as <c>LateralBeacon</c>.</summary>
    public required string Code { get; init; }

    /// <summary>
    /// Whether the combinations of the values the rule's conditions test
    /// came to more than <see cref="RuleProbeSet.Cap"/>, so some inputs were
    /// probed with fewer values, or held at one, and some branches may not
    /// have been reached. The design note's variant table says "partial".
    /// </summary>
    public bool IsPartial { get; init; }

    /// <summary>
    /// The inputs varied, most important first: the geometric primitive,
    /// then the attributes and display settings the conditions test, then
    /// ones the rule only reads.
    /// </summary>
    public IReadOnlyList<ProbeInput> Inputs { get; init; } = [];

    /// <summary>
    /// The lines of the rule files that hold the conditions of the feature
    /// type's <see cref="RuleUse"/>s, which <see cref="ProbeOutcome.Conditions"/>
    /// index. Ordered by file, then line.
    /// </summary>
    public IReadOnlyList<ProbeConditionLine> ConditionLines { get; init; } = [];

    /// <summary>The distinct outcomes, which <see cref="Variants"/> index.</summary>
    public IReadOnlyList<ProbeOutcome> Outcomes { get; init; } = [];

    /// <summary>
    /// For each combination of the inputs' values, in row-major order over
    /// <see cref="Inputs"/>, the index of its outcome in <see cref="Outcomes"/>.
    /// </summary>
    public IReadOnlyList<int> Variants { get; init; } = [];

    /// <summary>
    /// Each combination's input values, as indexes into each input's
    /// <see cref="ProbeInput.Values"/>, and its outcome.
    /// </summary>
    /// <returns>The variants, in <see cref="Variants"/> order.</returns>
    public IEnumerable<ProbeVariant> GetVariants()
    {
        var values = new int[Inputs.Count];
        for (var v = 0; v < Variants.Count; v++)
        {
            var rest = v;
            for (var i = Inputs.Count - 1; i >= 0; i--)
            {
                var count = Math.Max(1, Inputs[i].Values.Count);
                values[i] = rest % count;
                rest /= count;
            }

            yield return new ProbeVariant([.. values], Outcomes[Variants[v]]);
        }
    }

    /// <summary>
    /// The outcome of the combination of input values given by index, or
    /// <see langword="null"/> when the indexes don't name one.
    /// </summary>
    /// <param name="values">An index into each input's <see cref="ProbeInput.Values"/>, in <see cref="Inputs"/> order.</param>
    /// <returns>The outcome.</returns>
    public ProbeOutcome? OutcomeOf(IReadOnlyList<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != Inputs.Count) return null;
        var offset = 0;
        for (var i = 0; i < Inputs.Count; i++)
        {
            var count = Inputs[i].Values.Count;
            if (values[i] < 0 || values[i] >= count) return null;
            offset = (offset * count) + values[i];
        }

        return offset < Variants.Count ? Outcomes[Variants[offset]] : null;
    }
}

/// <summary>What a <see cref="ProbeInput"/> sets on the synthetic feature or the portrayal context.</summary>
public enum ProbeInputKind
{
    /// <summary>
    /// The feature's geometric primitive: <see cref="ProbeInput.Code"/> is
    /// <c>PrimitiveType</c> and the values are <c>Point</c>, <c>MultiPoint</c>,
    /// <c>Curve</c>, <c>Surface</c> or <c>None</c>, as Lua's
    /// <c>PrimitiveType</c> names them.
    /// </summary>
    Primitive,

    /// <summary>
    /// A simple attribute: <see cref="ProbeInput.Code"/> is its code and each
    /// value is its encoded value, a listed value's code or a number; values
    /// of a multi-valued attribute are joined with commas, as in <c>3,4,3</c>.
    /// </summary>
    Attribute,

    /// <summary>
    /// A display setting (context parameter): <see cref="ProbeInput.Code"/>
    /// is its id and the values are <c>true</c> and <c>false</c>.
    /// </summary>
    ContextParameter,
}

/// <summary>An input a probe varies, and the values it took.</summary>
/// <param name="Kind">What the input sets.</param>
/// <param name="Code">The attribute code, context parameter id, or <c>PrimitiveType</c>.</param>
/// <param name="Values">
/// The values probed, in order. <see langword="null"/> is an attribute left
/// out of the feature. A context parameter that the rule tests is probed
/// <c>true</c> then <c>false</c>; one held at its catalogue default has
/// that one value.
/// </param>
public sealed record ProbeInput(ProbeInputKind Kind, string Code, IReadOnlyList<string?> Values);

/// <summary>
/// A line of a rule file that holds conditions, as the rule reader records
/// them (<see cref="RuleUse.RuleFile"/>, <see cref="RuleCondition.Line"/>).
/// </summary>
/// <param name="File">The rule file without its extension, such as <c>LateralBeacon</c>.</param>
/// <param name="Line">The 1-based line of the <c>if</c>, <c>elseif</c> or <c>else</c>.</param>
/// <remarks>
/// A condition held when the first line of its branch's body ran: Lua runs
/// an <c>if</c> or <c>elseif</c> line whether or not it holds, and never
/// runs an <c>else</c> line. A branch whose body starts on its condition's
/// own line can't be told apart, so it's left out.
/// </remarks>
public readonly record struct ProbeConditionLine(string File, int Line);

/// <summary>What a rule drew for one or more combinations, and the conditions it took.</summary>
public sealed class ProbeOutcome
{
    /// <summary>
    /// The drawn parts, in the order drawn, without repeats:
    /// <list type="bullet">
    ///   <item><c>symbol:ID</c> for a point symbol, such as <c>symbol:BCNLAT21</c>;</item>
    ///   <item><c>line:ID</c> for a catalogue line style;</item>
    ///   <item><c>pen:TOKEN,WIDTH,DASH</c> for a plain line, such as <c>pen:CHBLK,0.32,dash</c>;</item>
    ///   <item><c>fill:ID</c> for an area fill pattern;</item>
    ///   <item><c>colour:TOKEN</c> for a plain colour fill, such as <c>colour:DEPVS</c>;</item>
    ///   <item><c>text:TEXT</c> for text.</item>
    /// </list>
    /// </summary>
    public IReadOnlyList<string> Parts { get; init; } = [];

    /// <summary>The viewing groups of the drawing instructions, in the order drawn, without repeats.</summary>
    public IReadOnlyList<int> ViewingGroups { get; init; } = [];

    /// <summary>
    /// The conditions that held, as indexes into
    /// <see cref="FeatureTypeProbe.ConditionLines"/>, ascending.
    /// </summary>
    public IReadOnlyList<int> Conditions { get; init; } = [];

    /// <summary>
    /// Whether the rule raised an error, so the catalogue's default
    /// symbology was drawn instead (S-101 <c>main.lua</c>).
    /// </summary>
    public bool IsError { get; init; }
}

/// <summary>One combination of a probe's input values and its outcome.</summary>
/// <param name="Values">An index into each input's <see cref="ProbeInput.Values"/>, in <see cref="FeatureTypeProbe.Inputs"/> order.</param>
/// <param name="Outcome">What the rule drew.</param>
public sealed record ProbeVariant(IReadOnlyList<int> Values, ProbeOutcome Outcome);
