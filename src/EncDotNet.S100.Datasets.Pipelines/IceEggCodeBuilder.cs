using System.Collections.Immutable;
using System.Globalization;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Builds a render-ready <see cref="IceEggCode"/> from the raw S-411 sea-ice /
/// lake-ice WMO attributes (<c>iceact</c>, <c>iceapc</c>, <c>icesod</c>,
/// <c>iceflz</c>, <c>snowDepth</c>).
/// </summary>
/// <remarks>
/// <para>
/// Real-world JCOMM producers (e.g. Canadian Ice Service feeds) serialise the
/// list-valued attributes as Python-list-style strings such as
/// <c>"[30, 30, 10, 4, 4]"</c>; this builder tolerates that shape as well as
/// bare comma- or space-separated tokens and single scalars. Token text is
/// preserved verbatim so undetermined values (<c>"9+"</c>, <c>"X"</c>, ranges
/// like <c>"4-6"</c>) survive into the diagram unchanged.
/// </para>
/// <para>
/// The oval carries at most three ice types (ordered by decreasing thickness as
/// supplied); thinner fourth and fifth classes surface as trailing values
/// rendered outside the oval, to the right of their row
/// (<see cref="IceEggCode.TrailingPartialConcentrations"/>,
/// <see cref="IceEggCode.TrailingStagesOfDevelopment"/>,
/// <see cref="IceEggCode.TrailingFormsOfIce"/>) with WMO subscripts
/// <c>d</c>/<c>e</c>, matching WMO / SIGRID-3 egg-code conventions
/// (S-411 Edition 1.2.1 Annex A).
/// </para>
/// </remarks>
public static class IceEggCodeBuilder
{
    private const int MaxOvalTypes = 3;

    /// <summary>
    /// Projects the raw S-411 egg-code attributes into an <see cref="IceEggCode"/>,
    /// or <c>null</c> when no drawable value is present.
    /// </summary>
    /// <param name="totalConcentrationRaw">Raw <c>iceact</c> / <c>totalConcentration</c> value (e.g. <c>"70"</c>, <c>"9+"</c>, <c>"0"</c>).</param>
    /// <param name="partialConcentrationsRaw">Raw <c>iceapc</c> value (list-style or scalar).</param>
    /// <param name="stagesOfDevelopmentRaw">Raw <c>icesod</c> value (list-style or scalar).</param>
    /// <param name="formsOfIceRaw">Raw <c>iceflz</c> value (list-style or scalar).</param>
    /// <param name="snowDepthCm">Optional snow depth in centimetres (<c>snowDepth</c>).</param>
    /// <returns>The projected egg code, or <c>null</c> when every component is empty.</returns>
    public static IceEggCode? Build(
        string? totalConcentrationRaw,
        string? partialConcentrationsRaw,
        string? stagesOfDevelopmentRaw,
        string? formsOfIceRaw,
        double? snowDepthCm = null)
    {
        var total = Clean(totalConcentrationRaw);
        var partials = ParseList(partialConcentrationsRaw);
        var stages = ParseList(stagesOfDevelopmentRaw);
        var forms = ParseList(formsOfIceRaw);

        var hasAnyIce = total is not null || partials.Count > 0 || stages.Count > 0
            || forms.Count > 0 || snowDepthCm is not null;
        if (!hasAnyIce)
            return null;

        var totalValue = total is null
            ? null
            : new IceEggValue { Text = total, Role = IceEggValueRole.TotalConcentration, SourceCode = "iceact", Symbol = "Ct" };

        // Open water / no ice: total is zero and nothing else is present. By
        // convention the oval is omitted and only Ct (0) is shown.
        if (IsZero(total) && partials.Count == 0 && stages.Count == 0
            && forms.Count == 0 && snowDepthCm is null)
        {
            return new IceEggCode
            {
                HasOval = false,
                TotalConcentration = totalValue,
            };
        }

        var typeCount = Math.Max(partials.Count, Math.Max(stages.Count, forms.Count));
        var singleType = typeCount == 1;

        var annotations = ImmutableArray.CreateBuilder<IceEggValue>();

        // A single ice type folds the partial-concentration row away (it would
        // merely repeat Ct); otherwise carry up to three partials in the oval.
        var partialRow = singleType
            ? ImmutableArray<IceEggValue>.Empty
            : TakeRow(partials, IceEggValueRole.PartialConcentration, "iceapc", 'C');

        var stageRow = TakeRow(stages, IceEggValueRole.StageOfDevelopment, "icesod", 'S');
        var formRow = TakeRow(forms, IceEggValueRole.FormOfIce, "iceflz", 'F');

        // The thinner fourth / fifth classes cannot ride in the oval; surface
        // them to the right of their respective rows (Cd/Ce, Sd/Se, Fd/Fe).
        var partialTrailing = singleType
            ? ImmutableArray<IceEggValue>.Empty
            : TakeTrailing(partials, IceEggValueRole.PartialConcentration, "iceapc", 'C');
        var stageTrailing = TakeTrailing(stages, IceEggValueRole.StageOfDevelopment, "icesod", 'S');
        var formTrailing = TakeTrailing(forms, IceEggValueRole.FormOfIce, "iceflz", 'F');

        if (snowDepthCm is { } snow)
            annotations.Add(new IceEggValue
            {
                Text = snow.ToString("0.###", CultureInfo.InvariantCulture),
                Role = IceEggValueRole.SnowDepth,
                SourceCode = "snowDepth",
            });

        return new IceEggCode
        {
            HasOval = true,
            TotalConcentration = totalValue,
            PartialConcentrations = partialRow,
            StagesOfDevelopment = stageRow,
            FormsOfIce = formRow,
            TrailingPartialConcentrations = partialTrailing,
            TrailingStagesOfDevelopment = stageTrailing,
            TrailingFormsOfIce = formTrailing,
            Annotations = annotations.ToImmutable(),
            ConcentrationRowFolded = singleType,
        };
    }

    private static ImmutableArray<IceEggValue> TakeRow(
        IReadOnlyList<string> tokens, IceEggValueRole role, string sourceCode, char symbolPrefix)
    {
        if (tokens.Count == 0)
            return ImmutableArray<IceEggValue>.Empty;

        var take = Math.Min(MaxOvalTypes, tokens.Count);
        var builder = ImmutableArray.CreateBuilder<IceEggValue>(take);
        for (var i = 0; i < take; i++)
            builder.Add(new IceEggValue
            {
                Text = tokens[i],
                Role = role,
                SourceCode = sourceCode,
                // Positional WMO subscript: a, b, c for the first three types.
                Symbol = $"{symbolPrefix}{(char)('a' + i)}",
            });
        return builder.ToImmutable();
    }

    /// <summary>
    /// Collects the thinner classes beyond the three that fit the oval (indices
    /// <see cref="MaxOvalTypes"/> and up), assigning positional WMO subscripts
    /// (<c>d</c>, <c>e</c>, …). These render outside the oval to the right of
    /// their row.
    /// </summary>
    private static ImmutableArray<IceEggValue> TakeTrailing(
        IReadOnlyList<string> tokens, IceEggValueRole role, string sourceCode, char symbolPrefix)
    {
        if (tokens.Count <= MaxOvalTypes)
            return ImmutableArray<IceEggValue>.Empty;

        var builder = ImmutableArray.CreateBuilder<IceEggValue>(tokens.Count - MaxOvalTypes);
        for (var i = MaxOvalTypes; i < tokens.Count; i++)
            builder.Add(new IceEggValue
            {
                Text = tokens[i],
                Role = role,
                SourceCode = sourceCode,
                // Positional WMO subscript: d, e, … for classes 4, 5, …
                Symbol = $"{symbolPrefix}{(char)('a' + i)}",
            });
        return builder.ToImmutable();
    }

    private static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool IsZero(string? token) =>
        token is not null
        && int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
        && n == 0;

    /// <summary>
    /// Parses a raw list-valued attribute into ordered tokens. Accepts
    /// Python-list-style strings (<c>"[30, 30, 10]"</c>), bare comma- or
    /// semicolon-separated lists, space-separated lists, and single scalars.
    /// </summary>
    private static IReadOnlyList<string> ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('['))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith(']'))
            trimmed = trimmed[..^1];
        trimmed = trimmed.Trim();
        if (trimmed.Length == 0)
            return Array.Empty<string>();

        var parts = trimmed.Split(
            new[] { ',', ';' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Fall back to whitespace separation when the producer used spaces
        // (WMO cells are conventionally space-separated) rather than commas.
        if (parts.Length <= 1 && trimmed.IndexOf(' ') >= 0)
            parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return parts
            .Select(StripQuotes)
            .Where(static p => p.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Strips a single pair of surrounding straight quotes from a token.
    /// Python-list-style producers quote non-numeric SIGRID-3 tokens (e.g.
    /// <c>'9+'</c>, <c>'X'</c>, <c>'4-6'</c>); the quotes are serialisation
    /// artefacts and must not appear in the egg diagram.
    /// </summary>
    private static string StripQuotes(string token)
    {
        if (token.Length >= 2
            && (token[0] == '\'' || token[0] == '"')
            && token[^1] == token[0])
        {
            return token[1..^1].Trim();
        }
        return token;
    }
}
