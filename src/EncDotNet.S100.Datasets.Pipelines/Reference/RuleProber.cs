using System.Globalization;
using System.Text;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>Options for <see cref="RuleProber.ProbeAsync"/>.</summary>
public sealed class RuleProberOptions
{
    /// <summary>The most combinations to probe per feature type; <see cref="RuleProbeSet.DefaultCap"/> unless set.</summary>
    public int Cap { get; init; } = RuleProbeSet.DefaultCap;

    /// <summary>
    /// The feature types to probe, by code; <see langword="null"/> for every
    /// feature type with a rule.
    /// </summary>
    public IReadOnlyCollection<string>? FeatureTypes { get; init; }

    /// <summary>How many Lua sessions to run at once; 1 unless set.</summary>
    public int MaxDegreeOfParallelism { get; init; } = 1;

    /// <summary>The most synthetic features one Lua session portrays before the next is opened.</summary>
    public int FeaturesPerSession { get; init; } = 4096;
}

/// <summary>Progress of <see cref="RuleProber.ProbeAsync"/>.</summary>
/// <param name="Done">The combinations portrayed so far.</param>
/// <param name="Total">The combinations to portray.</param>
public readonly record struct RuleProbeProgress(int Done, int Total);

/// <summary>
/// Runs each feature type's portrayal rule over the values of the
/// attributes and display settings it reads, and records what each
/// combination draws and which conditions held (symbol dictionary design
/// note §7, R13a).
/// </summary>
/// <remarks>
/// <para>
/// <b>Synthetic features.</b> Each combination is a feature with the
/// chosen primitive and attribute values, given to the catalogue's own
/// Lua rules through a host bridge of its own and portrayed in a
/// <see cref="LuaTraceSession"/>, so the lines that ran are known.
/// </para>
/// <para>
/// <b>Values.</b> The chart reference's rule facts say what each feature
/// type's rule tests (<see cref="FeatureTypeEntry.Uses"/> and their
/// <see cref="RuleUse.Conditions"/>):
/// </para>
/// <list type="bullet">
///   <item>enumerations take the values the conditions test, an untested
///   one where the rule tests whether the attribute is there, and no value;
///   a multi-valued attribute tested by index, as in <c>colour[1] == 3 and
///   colour[2] == 4</c>, takes the lists the conditions test together. The
///   other listed values are added when the cap allows;</item>
///   <item>numbers take values either side of, and at, each threshold the
///   conditions compare them with and each numeric display setting the
///   rule reads (safety contour, safety depth, …);</item>
///   <item>Boolean display settings the conditions test take <c>true</c> and <c>false</c>.</item>
/// </list>
/// <para>
/// <b>Cap.</b> When the combinations come to more than
/// <see cref="RuleProberOptions.Cap"/>, inputs are cut from the least
/// important (tested least) up, and the feature type is marked
/// <see cref="FeatureTypeProbe.IsPartial"/>.
/// </para>
/// <para>
/// <b>Engine.</b> Probing needs a line hook: pass the LuaCSharp engine
/// (<c>LuaEngineFactory.CreateForTracing</c>), whichever engine draws the map.
/// Only S-101 is probed for now (<see cref="Supports"/>); XSLT products
/// aren't.
/// </para>
/// </remarks>
public static partial class RuleProber
{
    private const string PrimitiveCode = "PrimitiveType";

    /// <summary>Whether <paramref name="spec"/>'s rules can be probed.</summary>
    /// <param name="spec">The product specification.</param>
    /// <returns><see langword="true"/> for S-101.</returns>
    public static bool Supports(string? spec) => string.Equals(spec, "S-101", StringComparison.Ordinal);

    /// <summary>Probes every feature type's rule.</summary>
    /// <param name="index">The chart reference built from these catalogues, for the rules' conditions.</param>
    /// <param name="features">The feature catalogue.</param>
    /// <param name="portrayal">The portrayal catalogue whose Lua rules run.</param>
    /// <param name="traceEngine">An engine with a line hook (LuaCSharp).</param>
    /// <param name="featureCatalogueHash">The feature catalogue's content hash, recorded in the result.</param>
    /// <param name="portrayalCatalogueHash">The portrayal catalogue's content hash, recorded in the result.</param>
    /// <param name="options">Options; defaults when <see langword="null"/>.</param>
    /// <param name="progress">Reports combinations portrayed.</param>
    /// <param name="cancellationToken">Cancels probing.</param>
    /// <returns>The probe results.</returns>
    /// <exception cref="NotSupportedException">The spec isn't probed, or the engine has no line hook.</exception>
    public static async Task<RuleProbeSet> ProbeAsync(
        ChartReferenceIndex index,
        FeatureCatalogue features,
        PortrayalCatalogueProvider portrayal,
        ILuaEngine traceEngine,
        string featureCatalogueHash,
        string portrayalCatalogueHash,
        RuleProberOptions? options = null,
        IProgress<RuleProbeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(portrayal);
        ArgumentNullException.ThrowIfNull(traceEngine);
        ArgumentException.ThrowIfNullOrEmpty(featureCatalogueHash);
        ArgumentException.ThrowIfNullOrEmpty(portrayalCatalogueHash);
        options ??= new RuleProberOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Cap, 1);
        if (!Supports(index.Spec.Name))
            throw new NotSupportedException($"Probing {index.Spec.Name}'s rules isn't supported; only S-101's are.");

        // The rules are read once, straight from the catalogue (its decoded
        // asset cache isn't for concurrent use), and every session reads
        // this snapshot.
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in portrayal.Catalogue.RuleFiles)
        {
            if (!Path.GetExtension(file.FileName).Equals(".lua", StringComparison.OrdinalIgnoreCase) || sources.ContainsKey(file.FileName))
                continue;
            try
            {
                await using var stream = await portrayal.FetchRuleAsync(file.FileName, cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                sources[file.FileName] = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or PortrayalAssetNotFoundException or UnauthorizedAccessException)
            {
                // A rule file the source lacks: its feature types draw the default symbology.
            }
        }

        var contextParameters = portrayal.Catalogue.ContextParameters
            .Select(p => new LuaContextParameter(p.Id, p.Type, p.Default))
            .ToList();
        var ruleSource = new SnapshotRuleSource(sources, contextParameters);
        var rules = sources.ToDictionary(p => Path.GetFileNameWithoutExtension(p.Key), p => p.Value, StringComparer.OrdinalIgnoreCase);
        var vocabulary = new ProbeVocabulary(features, portrayal.Catalogue.ContextParameters, rules);

        var wanted = options.FeatureTypes?.ToHashSet(StringComparer.Ordinal);
        var plans = index.FeatureTypes
            .Where(f => !f.IsAbstract && f.RuleFile is not null && (wanted is null || wanted.Contains(f.Code)))
            .OrderBy(f => f.Code, StringComparer.Ordinal)
            .Select(f => ProbePlan.Create(f, vocabulary, options.Cap))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        // Each combination's outcome, filled in by the sessions.
        var results = plans.Select(p => new ProbeOutcome?[p.Count]).ToArray();
        var slices = Slices(plans, options.FeaturesPerSession);
        var total = plans.Sum(p => p.Count);
        var done = 0;

        await Parallel.ForEachAsync(
            slices,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.MaxDegreeOfParallelism), CancellationToken = cancellationToken },
            async (slice, ct) =>
            {
                await RunSliceAsync(slice, plans, results, ruleSource, features, traceEngine, ct).ConfigureAwait(false);
                var now = Interlocked.Add(ref done, slice.Items.Count);
                progress?.Report(new RuleProbeProgress(now, total));
            }).ConfigureAwait(false);

        var probes = new List<FeatureTypeProbe>(plans.Count);
        for (var p = 0; p < plans.Count; p++)
        {
            probes.Add(plans[p].ToProbe(results[p]));
        }

        return new RuleProbeSet
        {
            Spec = index.Spec.Name ?? string.Empty,
            FeatureCatalogueHash = featureCatalogueHash,
            PortrayalCatalogueHash = portrayalCatalogueHash,
            Cap = options.Cap,
            FeatureTypes = probes,
        };
    }

    /// <summary>One Lua session's work: combinations that share their display settings.</summary>
    private sealed record Slice(IReadOnlyDictionary<string, string> Context, List<(int Plan, int Combination)> Items);

    private static List<Slice> Slices(List<ProbePlan> plans, int perSession)
    {
        perSession = Math.Max(1, perSession);
        var groups = new SortedDictionary<string, (Dictionary<string, string> Context, List<(int, int)> Items)>(StringComparer.Ordinal);
        for (var p = 0; p < plans.Count; p++)
        {
            for (var c = 0; c < plans[p].Count; c++)
            {
                var context = plans[p].ContextOf(c);
                var key = string.Join(';', context.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}={k.Value}"));
                if (!groups.TryGetValue(key, out var group))
                {
                    group = (context, []);
                    groups[key] = group;
                }

                group.Items.Add((p, c));
            }
        }

        var slices = new List<Slice>();
        foreach (var (context, items) in groups.Values)
        {
            for (var i = 0; i < items.Count; i += perSession)
            {
                slices.Add(new Slice(context, items.GetRange(i, Math.Min(perSession, items.Count - i))));
            }
        }

        return slices;
    }

    private static async Task RunSliceAsync(
        Slice slice,
        List<ProbePlan> plans,
        ProbeOutcome?[][] results,
        ILuaRuleSource ruleSource,
        FeatureCatalogue features,
        ILuaEngine traceEngine,
        CancellationToken cancellationToken)
    {
        var synthetic = slice.Items.Select(i => plans[i.Plan].FeatureOf(i.Combination)).ToList();
        var factory = new ProbeFeatureProviderFactory(synthetic, features);

        // Display settings the slice's combinations set; the rest keep the catalogue's defaults.
        var bindings = slice.Context
            .Select(p => new LuaContextParameterBinding(p.Key, _ => p.Value, v => (string)v!))
            .ToList();
        var executor = new LuaRuleExecutor(traceEngine, ruleSource, factory, "S-101", bindings);
        using var session = await executor.OpenTraceSessionAsync(traceEngine, MarinerSettings.Default, cancellationToken)
            .ConfigureAwait(false);
        var provider = factory.Provider ?? throw new InvalidOperationException("The probe session has no data provider.");

        var warmed = new HashSet<int>();
        for (var i = 0; i < slice.Items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (plan, combination) = slice.Items[i];
            var id = (i + 1).ToString(CultureInfo.InvariantCulture);

            // A feature type's first portrayal in a session also runs its
            // rule files' top-level lines as they load; portray it once
            // first, so that trace is thrown away.
            if (warmed.Add(plan))
            {
                session.Trace([id]);
            }

            provider.ResetError();
            try
            {
                var trace = session.Trace([id])[0];
                results[plan][combination] = plans[plan].Outcome(trace, provider.RuleFailed);
            }
            catch (InvalidOperationException)
            {
                // An error main.lua doesn't catch: nothing was drawn.
                results[plan][combination] = new ProbeOutcome { IsError = true };
            }
        }
    }

    /// <summary>The rule sources read once, for sessions on several threads.</summary>
    private sealed class SnapshotRuleSource(IReadOnlyDictionary<string, string> sources, IReadOnlyList<LuaContextParameter> contextParameters)
        : ILuaRuleSource
    {
        private readonly IReadOnlyList<string> _names = [.. sources.Keys.Order(StringComparer.Ordinal)];

        public IReadOnlyList<LuaContextParameter> ContextParameters => contextParameters;

        public ValueTask<IReadOnlyList<string>> GetLuaSourceNamesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_names);

        public ValueTask<string?> GetLuaSourceAsync(string fileName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(sources.TryGetValue(fileName, out var source) ? source : null);
    }

    /// <summary>What a plan needs from the catalogues.</summary>
    internal sealed class ProbeVocabulary
    {
        public ProbeVocabulary(
            FeatureCatalogue features,
            IReadOnlyList<ContextParameter> contextParameters,
            IReadOnlyDictionary<string, string> rules)
        {
            FeatureTypes = features.FeatureTypes
                .GroupBy(t => t.Code, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            Attributes = features.SimpleAttributes
                .GroupBy(a => a.Code, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            ContextParameters = contextParameters
                .GroupBy(p => p.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            Rules = rules;
            Lines = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, FeatureType> FeatureTypes { get; }

        public IReadOnlyDictionary<string, SimpleAttribute> Attributes { get; }

        public IReadOnlyDictionary<string, ContextParameter> ContextParameters { get; }

        /// <summary>Rule file stem to source.</summary>
        public IReadOnlyDictionary<string, string> Rules { get; }

        private Dictionary<string, string[]> Lines { get; }

        public string[]? LinesOf(string file)
        {
            lock (Lines)
            {
                if (Lines.TryGetValue(file, out var lines)) return lines;
                if (!Rules.TryGetValue(file, out var source)) return null;
                lines = source.Split('\n');
                Lines[file] = lines;
                return lines;
            }
        }

        public bool IsBoolean(string contextParameter) =>
            ContextParameters.TryGetValue(contextParameter, out var p) && string.Equals(p.Type, "boolean", StringComparison.OrdinalIgnoreCase);

        public double? NumericDefault(string contextParameter) =>
            ContextParameters.TryGetValue(contextParameter, out var p)
            && (string.Equals(p.Type, "double", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Type, "real", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Type, "integer", StringComparison.OrdinalIgnoreCase))
            && double.TryParse(p.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        public string BooleanDefault(string contextParameter) =>
            ContextParameters.TryGetValue(contextParameter, out var p) && string.Equals(p.Default.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                ? "true"
                : "false";
    }

    /// <summary>
    /// The first line of each condition's branch body: the line whose
    /// running shows the condition held. Zero when it can't be told.
    /// </summary>
    internal static int EvidenceLine(string[] lines, int conditionLine)
    {
        if (conditionLine < 1 || conditionLine > lines.Length) return 0;
        var header = Code(lines[conditionLine - 1]).Trim();
        int last;
        if (IsWord(header, "else"))
        {
            if (header.Length > "else".Length) return 0;
            last = conditionLine;
        }
        else
        {
            // The condition runs to 'then', perhaps over several lines.
            last = 0;
            for (var l = conditionLine; l <= lines.Length && l < conditionLine + 20; l++)
            {
                var code = Code(lines[l - 1]);
                var then = IndexOfWord(code, "then");
                if (then < 0) continue;

                // A body on the 'then' line runs with the condition; it can't be told apart.
                if (code[(then + 4)..].Trim().Length > 0) return 0;
                last = l;
                break;
            }

            if (last == 0) return 0;
        }

        for (var l = last + 1; l <= lines.Length; l++)
        {
            var code = Code(lines[l - 1]).Trim();
            if (code.Length == 0) continue;

            // An empty branch: nothing runs to show it was taken.
            if (IsWord(code, "end") || IsWord(code, "else") || IsWord(code, "elseif")) return 0;
            return l;
        }

        return 0;

        static bool IsWord(string text, string word) =>
            text.StartsWith(word, StringComparison.Ordinal)
            && (text.Length == word.Length || !(char.IsLetterOrDigit(text[word.Length]) || text[word.Length] == '_'));
    }

    /// <summary>
    /// A line of Lua without its comment, and with the insides of its
    /// strings blanked, so keywords and <c>--</c> in strings don't count.
    /// </summary>
    private static string Code(string line)
    {
        var code = new StringBuilder(line.Length);
        char? quote = null;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote is { } q)
            {
                if (c == '\\' && i + 1 < line.Length)
                {
                    code.Append("  ");
                    i++;
                    continue;
                }

                if (c == q) quote = null;
                code.Append(c == q ? c : ' ');
            }
            else if (c is '\'' or '"')
            {
                quote = c;
                code.Append(c);
            }
            else if (c == '-' && i + 1 < line.Length && line[i + 1] == '-')
            {
                break;
            }
            else
            {
                code.Append(c);
            }
        }

        return code.ToString().TrimEnd('\r');
    }

    private static int IndexOfWord(string text, string word)
    {
        var from = 0;
        while (true)
        {
            var at = text.IndexOf(word, from, StringComparison.Ordinal);
            if (at < 0) return -1;
            var before = at == 0 || !(char.IsLetterOrDigit(text[at - 1]) || text[at - 1] == '_');
            var end = at + word.Length;
            var after = end == text.Length || !(char.IsLetterOrDigit(text[end]) || text[end] == '_');
            if (before && after) return at;
            from = at + 1;
        }
    }

    /// <summary>The drawn parts of a feature's instructions, in order, without repeats.</summary>
    internal static List<string> Parts(IEnumerable<DrawingInstruction> instructions, out List<int> viewingGroups)
    {
        var parts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        viewingGroups = [];
        foreach (var instruction in instructions)
        {
            if (!viewingGroups.Contains(instruction.ViewingGroup)) viewingGroups.Add(instruction.ViewingGroup);
            var part = instruction switch
            {
                PointInstruction { SymbolReference: { Length: > 0 } symbol } => "symbol:" + symbol,
                LineInstruction { LineStyleReference: { Length: > 0 } style } => "line:" + style,
                LineInstruction line => string.Create(
                    CultureInfo.InvariantCulture,
                    $"pen:{line.LineColor},{Math.Round(line.LineWidth, 3)},{(line.Dashes is { Count: > 0 } ? "dash" : "solid")}"),
                AreaInstruction { AreaFillReference: { Length: > 0 } fill } => "fill:" + fill,
                AreaInstruction { FillColor: { Length: > 0 } colour } => "colour:" + colour,
                TextInstruction text => "text:" + text.Text,
                _ => null,
            };
            if (part is not null && seen.Add(part)) parts.Add(part);
        }

        return parts;
    }

    /// <summary>
    /// One feature type's inputs, their values and the combinations they
    /// make, and the outcomes found.
    /// </summary>
    internal sealed class ProbePlan
    {
        private readonly List<ProbeInput> _inputs;
        private readonly ProbeVocabulary _vocabulary;
        private readonly HashSet<string> _multiValued;
        private readonly List<(ProbeConditionLine Line, int Evidence)> _conditions;
        private readonly Dictionary<string, int> _outcomeIndex = new(StringComparer.Ordinal);

        private ProbePlan(
            string code,
            bool partial,
            List<ProbeInput> inputs,
            HashSet<string> multiValued,
            List<(ProbeConditionLine, int)> conditions,
            ProbeVocabulary vocabulary)
        {
            Code = code;
            IsPartial = partial;
            _inputs = inputs;
            _multiValued = multiValued;
            _conditions = conditions;
            _vocabulary = vocabulary;
            Count = inputs.Aggregate(1, (n, i) => n * i.Values.Count);
        }

        public string Code { get; }

        public bool IsPartial { get; }

        public IReadOnlyList<ProbeInput> Inputs => _inputs;

        /// <summary>The number of combinations.</summary>
        public int Count { get; }

        public static ProbePlan? Create(FeatureTypeEntry entry, ProbeVocabulary vocabulary, int cap)
        {
            if (!vocabulary.FeatureTypes.TryGetValue(entry.Code, out var type)) return null;
            var bound = type.AttributeBindings
                .GroupBy(b => b.AttributeRef, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var multiValued = bound.Values
                .Where(b => b.Multiplicity.Upper is null or > 1)
                .Select(b => b.AttributeRef)
                .ToHashSet(StringComparer.Ordinal);

            var uses = entry.Uses.Cast<RuleUse>().Concat(entry.ColorFills).Concat(entry.Pens).Concat(entry.ViewingGroups).ToList();
            var dims = new Dictionary<string, Dimension>(StringComparer.Ordinal);
            var order = 0;

            Dimension Dim(ProbeInputKind kind, string code)
            {
                var key = (kind == ProbeInputKind.ContextParameter ? "c:" : "a:") + code;
                if (!dims.TryGetValue(key, out var dim))
                {
                    dim = new Dimension(kind, code, order++);
                    dims[key] = dim;
                }

                return dim;
            }

            // What the conditions test.
            var conditionLines = new SortedSet<ProbeConditionLine>(Comparer<ProbeConditionLine>.Create(
                (a, b) => string.CompareOrdinal(a.File, b.File) is var c && c != 0 ? c : a.Line.CompareTo(b.Line)));
            foreach (var use in uses)
            {
                var indexed = new Dictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
                foreach (var condition in use.Conditions)
                {
                    var line = new ProbeConditionLine(use.RuleFile, condition.Line);
                    if (condition.Line > 0) conditionLines.Add(line);
                    switch (condition.Kind)
                    {
                        case RuleConditionKind.Attribute when condition.Subject is { } subject && bound.ContainsKey(subject)
                            && vocabulary.Attributes.ContainsKey(subject):
                            {
                                var dim = Dim(ProbeInputKind.Attribute, subject);
                                dim.Lines.Add(line);
                                if (condition.Operator is ConditionOperator.Present or ConditionOperator.Absent) dim.PresenceTested = true;
                                if (condition.Value is not { } value) break;
                                if (condition.Index is { } i && multiValued.Contains(subject))
                                {
                                    if (!indexed.TryGetValue(subject, out var map)) indexed[subject] = map = [];
                                    map[i] = value;
                                }
                                else
                                {
                                    dim.Tested.Add(value);
                                }

                                break;
                            }

                        case RuleConditionKind.ContextParameter when condition.Subject is { } id && vocabulary.IsBoolean(id):
                            Dim(ProbeInputKind.ContextParameter, id).Lines.Add(line);
                            break;

                        case RuleConditionKind.Expression when condition.Text is { } text:
                            foreach (var name in Names(text, "contextParameters"))
                            {
                                if (vocabulary.IsBoolean(name)) Dim(ProbeInputKind.ContextParameter, name).Lines.Add(line);
                            }

                            foreach (var name in Names(text, "feature"))
                            {
                                if (bound.ContainsKey(name) && vocabulary.Attributes.ContainsKey(name))
                                    Dim(ProbeInputKind.Attribute, name).Lines.Add(line);
                            }

                            break;
                    }
                }

                // The values a branch tests together, as one list: colour 3,4,3.
                foreach (var (subject, map) in indexed)
                {
                    var dim = Dim(ProbeInputKind.Attribute, subject);
                    var filler = vocabulary.Attributes[subject].ListedValues.FirstOrDefault()?.Code ?? "1";
                    var list = Enumerable.Range(1, map.Keys.Max()).Select(i => map.TryGetValue(i, out var v) ? v : filler);
                    dim.Tested.Add(string.Join(',', list));
                }
            }

            // What the rule reads: its own file and the files it requires.
            var reads = Reads(entry.RuleFile!, vocabulary);
            var thresholds = reads.ContextParameters
                .Select(vocabulary.NumericDefault)
                .OfType<double>()
                .Distinct()
                .Order()
                .ToList();
            foreach (var name in reads.Attributes.Order(StringComparer.Ordinal))
            {
                if (bound.ContainsKey(name) && vocabulary.Attributes.ContainsKey(name) && !dims.ContainsKey("a:" + name))
                {
                    var dim = Dim(ProbeInputKind.Attribute, name);
                    dim.ReadOnly = true;
                }
            }

            // Each input's values.
            var inputs = new List<Dimension>();
            foreach (var dim in dims.Values)
            {
                if (dim.Kind == ProbeInputKind.ContextParameter)
                {
                    dim.Values.AddRange(["true", "false"]);
                    dim.Neutral = vocabulary.BooleanDefault(dim.Code);
                    inputs.Add(dim);
                }
                else if (ChooseValues(dim, vocabulary.Attributes[dim.Code], multiValued.Contains(dim.Code), thresholds))
                {
                    inputs.Add(dim);
                }
            }

            // Most important first: tested most, then tested first; then read only.
            inputs = inputs
                .OrderBy(d => d.ReadOnly)
                .ThenByDescending(d => d.Lines.Count)
                .ThenBy(d => d.ReadOnly ? 0 : d.Order)
                .ThenBy(d => d.Code, StringComparer.Ordinal)
                .ToList();

            var primitives = type.PermittedPrimitives
                .Select(PrimitiveName)
                .Where(p => p is not null)
                .Select(p => (string?)p)
                .Distinct()
                .ToList();
            if (primitives.Count == 0) primitives.Add("None");
            var primitive = new Dimension(ProbeInputKind.Primitive, PrimitiveCode, -1);
            primitive.Values.AddRange(primitives);
            primitive.Neutral = primitives[0];
            inputs.Insert(0, primitive);

            // The cap: cut the least important inputs first.
            var partial = Product(inputs) > cap;
            for (var i = inputs.Count - 1; i >= 0 && Product(inputs) > cap; i--)
            {
                var dim = inputs[i];
                var rest = Product(inputs) / dim.Values.Count;
                var allowed = (int)Math.Max(1, cap / Math.Max(1, rest));
                if (allowed < dim.Values.Count) dim.Cut(allowed);
            }

            // Within the cap: the other listed values, most important input first.
            if (!partial)
            {
                foreach (var dim in inputs)
                {
                    if (dim.Extras.Count == 0) continue;
                    var grown = Product(inputs) / dim.Values.Count * (dim.Values.Count + dim.Extras.Count);
                    if (grown <= cap) dim.Grow();
                }
            }

            var evidence = conditionLines
                .Select(l => (l, vocabulary.LinesOf(l.File) is { } lines ? EvidenceLine(lines, l.Line) : 0))
                .Where(c => c.Item2 > 0)
                .ToList();

            return new ProbePlan(
                entry.Code,
                partial,
                inputs.Select(d => new ProbeInput(d.Kind, d.Code, [.. d.Values])).ToList(),
                multiValued,
                evidence,
                vocabulary);
        }

        private static long Product(List<Dimension> dims) => dims.Aggregate(1L, (n, d) => n * Math.Max(1, d.Values.Count));

        private static string? PrimitiveName(string permitted) => permitted.ToLowerInvariant() switch
        {
            "point" => "Point",
            "pointset" or "multipoint" => "MultiPoint",
            "curve" => "Curve",
            "surface" => "Surface",
            "nogeometry" => "None",
            _ => null,
        };

        /// <summary>Picks an attribute's values; <see langword="false"/> when it isn't worth varying.</summary>
        private static bool ChooseValues(Dimension dim, SimpleAttribute attribute, bool multiValued, List<double> displayThresholds)
        {
            var listed = attribute.ListedValues.Select(v => v.Code).Distinct(StringComparer.Ordinal).ToList();
            switch (attribute.ValueType.ToLowerInvariant())
            {
                case "enumeration":
                    {
                        var tested = dim.Tested.Distinct(StringComparer.Ordinal).ToList();
                        dim.Values.AddRange(tested);
                        if (!dim.ReadOnly && (dim.PresenceTested || tested.Count == 0))
                        {
                            // A value the rule doesn't test, for "has a value" branches.
                            var other = listed.FirstOrDefault(v => !tested.Contains(v));
                            if (other is not null) dim.Values.Add(other);
                        }

                        dim.Values.Add(null);
                        dim.Extras.AddRange(listed.Where(v => !dim.Values.Contains(v)));
                        return dim.Values.Count > 1 || dim.Extras.Count > 0;
                    }

                case "boolean":
                    dim.Values.Add(null);
                    if (dim.ReadOnly) dim.Extras.AddRange(["true", "false"]);
                    else dim.Values.InsertRange(0, ["true", "false"]);
                    return true;

                case "integer":
                case "real":
                    {
                        var integer = attribute.ValueType.Equals("integer", StringComparison.OrdinalIgnoreCase);
                        var step = integer ? 1.0 : 0.1;
                        var points = dim.Tested
                            .Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : (double?)null)
                            .OfType<double>()
                            .Concat(displayThresholds)
                            .Distinct()
                            .ToList();
                        if (points.Count == 0)
                        {
                            if (dim.ReadOnly || !dim.PresenceTested && dim.Lines.Count == 0) return false;
                            dim.Values.Add("1");
                        }

                        foreach (var value in points
                            .SelectMany(t => new[] { t - step, t, t + step })
                            .Select(v => Math.Round(v, 6))
                            .Distinct()
                            .Order())
                        {
                            dim.Values.Add(value.ToString("R", CultureInfo.InvariantCulture));
                        }

                        dim.Values.Add(null);
                        return true;
                    }

                default:
                    {
                        if (dim.ReadOnly) return false;
                        dim.Values.AddRange(dim.Tested.Distinct(StringComparer.Ordinal));
                        if (dim.PresenceTested || dim.Values.Count == 0) dim.Values.Add("x");
                        dim.Values.Add(null);
                        return true;
                    }
            }
        }

        /// <summary>The attributes and context parameters a rule file and the files it requires name.</summary>
        private static (HashSet<string> Attributes, HashSet<string> ContextParameters) Reads(string ruleFile, ProbeVocabulary vocabulary)
        {
            var attributes = new HashSet<string>(StringComparer.Ordinal);
            var parameters = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>([ruleFile]);
            while (queue.Count > 0)
            {
                var file = queue.Dequeue();
                if (!visited.Add(file) || !vocabulary.Rules.TryGetValue(file, out var source)) continue;
                attributes.UnionWith(Names(source, "feature"));
                parameters.UnionWith(Names(source, "contextParameters"));
                foreach (var required in Required(source)) queue.Enqueue(required);
            }

            return (attributes, parameters);
        }

        /// <summary>The names <c>target.name</c> or <c>target['name']</c> picks out of <paramref name="text"/>.</summary>
        private static IEnumerable<string> Names(string text, string target)
        {
            var from = 0;
            while ((from = IndexOfTarget(text, target, from)) >= 0)
            {
                var i = from + target.Length;
                from = i;
                string? name = null;
                if (i < text.Length && text[i] == '.')
                {
                    name = Identifier(text, i + 1);
                }
                else if (i + 1 < text.Length && text[i] == '[' && text[i + 1] is '\'' or '"')
                {
                    var end = text.IndexOf(text[i + 1], i + 2);
                    if (end > i + 2) name = text[(i + 2)..end];
                }

                if (!string.IsNullOrEmpty(name)) yield return name;
            }
        }

        private static int IndexOfTarget(string text, string target, int from)
        {
            while (true)
            {
                var at = text.IndexOf(target, from, StringComparison.Ordinal);
                if (at < 0) return -1;
                if (at == 0 || !(char.IsLetterOrDigit(text[at - 1]) || text[at - 1] is '_' or '.' or ':')) return at;
                from = at + 1;
            }
        }

        private static string Identifier(string text, int start)
        {
            var end = start;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
            return text[start..end];
        }

        private static IEnumerable<string> Required(string source)
        {
            var from = 0;
            while ((from = IndexOfTarget(source, "require", from)) >= 0)
            {
                var i = from + "require".Length;
                from = i;
                while (i < source.Length && (source[i] is ' ' or '(' or '\t')) i++;
                if (i >= source.Length || source[i] is not ('\'' or '"')) continue;
                var end = source.IndexOf(source[i], i + 1);
                if (end > i + 1) yield return source[(i + 1)..end];
            }
        }

        /// <summary>The values of each input for combination <paramref name="combination"/>.</summary>
        private string?[] ValuesOf(int combination)
        {
            var values = new string?[_inputs.Count];
            var rest = combination;
            for (var i = _inputs.Count - 1; i >= 0; i--)
            {
                var count = _inputs[i].Values.Count;
                values[i] = _inputs[i].Values[rest % count];
                rest /= count;
            }

            return values;
        }

        /// <summary>The display settings combination <paramref name="combination"/> sets that differ from the catalogue's defaults.</summary>
        public Dictionary<string, string> ContextOf(int combination)
        {
            var values = ValuesOf(combination);
            var context = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < _inputs.Count; i++)
            {
                if (_inputs[i].Kind == ProbeInputKind.ContextParameter
                    && values[i] is { } value
                    && value != _vocabulary.BooleanDefault(_inputs[i].Code))
                {
                    context[_inputs[i].Code] = value;
                }
            }

            return context;
        }

        /// <summary>The synthetic feature of combination <paramref name="combination"/>.</summary>
        public ProbeFeature FeatureOf(int combination)
        {
            var values = ValuesOf(combination);
            var primitive = "None";
            var attributes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            for (var i = 0; i < _inputs.Count; i++)
            {
                if (values[i] is not { } value) continue;
                switch (_inputs[i].Kind)
                {
                    case ProbeInputKind.Primitive:
                        primitive = value;
                        break;
                    case ProbeInputKind.Attribute:
                        attributes[_inputs[i].Code] = _multiValued.Contains(_inputs[i].Code) ? value.Split(',') : [value];
                        break;
                }
            }

            return new ProbeFeature(Code, primitive, attributes);
        }

        /// <summary>The outcome of a traced feature, shared with the same outcome of other combinations.</summary>
        public ProbeOutcome Outcome(LuaFeatureTrace trace, bool error)
        {
            var parts = Parts(trace.Instructions, out var groups);
            var ran = trace.Lines.ToHashSet();
            var held = new List<int>();
            for (var c = 0; c < _conditions.Count; c++)
            {
                var (line, evidence) = _conditions[c];
                if (ran.Contains(new LuaTraceLine(line.File, evidence))) held.Add(c);
            }

            return new ProbeOutcome { Parts = parts, ViewingGroups = groups, Conditions = held, IsError = error };
        }

        public FeatureTypeProbe ToProbe(ProbeOutcome?[] outcomes)
        {
            var distinct = new List<ProbeOutcome>();
            var variants = new List<int>(outcomes.Length);
            foreach (var outcome in outcomes)
            {
                var o = outcome ?? new ProbeOutcome { IsError = true };
                var key = Key(o);
                if (!_outcomeIndex.TryGetValue(key, out var at))
                {
                    at = distinct.Count;
                    _outcomeIndex[key] = at;
                    distinct.Add(o);
                }

                variants.Add(at);
            }

            return new FeatureTypeProbe
            {
                Code = Code,
                IsPartial = IsPartial,
                Inputs = _inputs,
                ConditionLines = _conditions.Select(c => c.Line).ToList(),
                Outcomes = distinct,
                Variants = variants,
            };

            static string Key(ProbeOutcome o) => new StringBuilder()
                .AppendJoin('\u001f', o.Parts).Append('\u001e')
                .AppendJoin(',', o.ViewingGroups).Append('\u001e')
                .AppendJoin(',', o.Conditions).Append('\u001e')
                .Append(o.IsError)
                .ToString();
        }
    }

    /// <summary>An input while a plan is made.</summary>
    private sealed class Dimension(ProbeInputKind kind, string code, int order)
    {
        public ProbeInputKind Kind { get; } = kind;

        public string Code { get; } = code;

        public int Order { get; } = order;

        public HashSet<ProbeConditionLine> Lines { get; } = [];

        public List<string> Tested { get; } = [];

        public bool PresenceTested { get; set; }

        public bool ReadOnly { get; set; }

        public List<string?> Values { get; } = [];

        public List<string?> Extras { get; } = [];

        /// <summary>The value an input is held at when the cap cuts it to one: no value, or a display setting's default.</summary>
        public string? Neutral { get; set; }

        /// <summary>Keeps <paramref name="count"/> values: the first ones and the neutral one.</summary>
        public void Cut(int count)
        {
            var neutral = Neutral;
            var hasNeutral = Values.Contains(neutral);
            var kept = Values.Where(v => v != neutral).Take(hasNeutral ? count - 1 : count).ToList();
            if (hasNeutral)
            {
                if (Kind == ProbeInputKind.Attribute) kept.Add(neutral);
                else kept.Insert(0, neutral);
            }

            Values.Clear();
            Values.AddRange(kept);
            Extras.Clear();
        }

        /// <summary>Adds <see cref="Extras"/> before the trailing no-value.</summary>
        public void Grow()
        {
            var at = Values.Count > 0 && Values[^1] is null ? Values.Count - 1 : Values.Count;
            Values.InsertRange(at, Extras);
            Extras.Clear();
        }
    }
}
